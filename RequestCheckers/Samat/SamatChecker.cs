using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto.Customer;
using BatchChecker.Dto.NocrInquiry;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Ident;
using BatchOp.Infrastructure.Shared.ApiCaller.TataGateway;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOP.Domain.Entities.NocrInquiry;
using BatchOP.Domain.Entities.Staff;
using BatchOP.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using AutoMapper;
using BatchChecker.Business.NocrInquiry;
using BatchChecker.Business.NocrInquiry.HasError;
using BatchOP.Domain.Entities.Samat;
using BatchOp.Application.Enums.Excentions;
using BatchChecker.Dto.Share;
using BatchOP.Domain.Entities.Customer;
using BatchChecker.Extensions;
using BatchOp.Application.Utility.StringExtensions;
using BatchOp.Application.DTOs.TataGateway.Samat.Zamen;
using BatchOp.Application.DTOs.TataGateway.Samat;
using BatchOp.Application.DTOs.TataGateway.Vasighe;

namespace BatchChecker.RequestCheckers
{
    public class SamatChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ITataGatewayApi _tataGatewayApi;
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        public SamatChecker(
            IMapper mapper,
             ILogger logger,
             ITataGatewayApi tataGatewayApi,
            IDapperRepository dapperRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration
            )
        {
            _tataGatewayApi = tataGatewayApi;
            _logger = logger;
            _dapperRepository = dapperRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
        }




        
        #region Publish
        /// <summary>
        /// واکشی لیست درخواست ها جهت ثبت در تیبل صف و سپس ارسال به ربیت
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> SenderAsync(CancellationToken stoppingToken)
        {
            var queryResponse = await GetListSourceHeaderAsync(stoppingToken);
            if (queryResponse.Any())
            {
                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                await InsertToBatchProcessQueueDetailAsync(queryResponse, stoppingToken);
                _dapperUnitOfWork.Commit();
            }

            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount , RequestType " +
                $" from TB_BatchProcessQueue Q join TB_SamatSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                $" (Q.Status = {(int)GenericEnum.Unasign}) and RequestType = {(int)RequestType.Samat} " +
                $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }

        /// <summary>
        /// آپدیت درخواست بعد ثبت در تیبل صف و ربیت
        /// </summary>
        /// <param name="Id"></param>
        /// <param name="processIndex"></param>
        /// <returns></returns>
        private async Task<bool> UpdateSourceHeaderAsync(long Id, int processIndex, CancellationToken stoppingToken)
        {
            var script = $"Update S set Status = {(int)GenericEnum.Pending} , ProcessIndex = {processIndex} , ProcessDate = '{DateTime.Now}' " +
                $"from TB_SamatSourceHeader S where ID={Id}";
            lock (this)
            {
                var SamatSourceHeader = _dapperRepository.Query<SamatSourceHeader>(script);
            }
            return await Task.FromResult(true);
        }
        /// <summary>
        /// ثبت درخواست ها با ایندکس در تیبل صف
        /// </summary>
        /// <param name="SamatResponses"></param>
        /// <returns></returns>
        private async Task<bool> InsertToBatchProcessQueueDetailAsync(IEnumerable<SourceHeaderDto> SamatResponses, CancellationToken stoppingToken)
        {
            foreach (var item in SamatResponses)
            {
                var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                var batchCount = rabbitMQConfiguration.SamatBatchCount;

                int startIndex = 0;
                int endIndex = 0;

                for (int i = 1; endIndex < item.RequestCount ; i++)
                {
                    startIndex = (i - 1) * batchCount + 1;
                    endIndex = item.RequestCount <= i * batchCount ? endIndex = item.RequestCount : endIndex = i * batchCount;

                    BatchProcessQueue batchProcessQueueDetail = new BatchProcessQueue()
                    {
                        SourceHeaderId = item.SourceHeaderId,
                        StartIndex = startIndex,
                        EndIndex = endIndex,
                        Status = GenericEnum.Unasign,
                        RequestType = RequestType.Samat,
                        TryCount = 0,
                    };
                    if (FindBatchProccessQueue(batchProcessQueueDetail) == 0)
                    {
                        _dapperRepository.Add(batchProcessQueueDetail);
                        _logger.Information($"InsertToBatchProcessQueueDetail : {JsonConvert.SerializeObject(batchProcessQueueDetail)}");
                        await UpdateSourceHeaderAsync(item.SourceHeaderId, item.RequestCount, stoppingToken);
                    }
                }
            }
            return true;
        }

        private int FindBatchProccessQueue(BatchProcessQueue batchProcessQueueDetail)
        {
            var scriptBatchProcessQueue = $"SELECT * FROM TB_BatchProcessQueue where SourceHeader_ID = {batchProcessQueueDetail.SourceHeaderId} and " +
                                    $"StartIndex = {batchProcessQueueDetail.StartIndex} and " +
                                    $"EndIndex = {batchProcessQueueDetail.EndIndex} " +
                                    $"and RequestType = {(int)RequestType.Samat} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);

            return queryResponse.Count();
        }
        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderSamat");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R INNER JOIN TB_SamatSourceHeader H On R.ID = H.Request_ID " +
                $"join TB_SamatSourceDetail det on h.id = det.SamatSourceHeader_ID " +
                $"WHERE H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderSamat Count:{queryResponse.Count()}");

            return await Task.FromResult(queryResponse);
        }
        #endregion

        #region Subscribe
        /// <summary>
        /// دریافت لیست جزییات مشتریان براساس پارامترهای دریافتی از RabbitMQ
        /// </summary>
        /// <param name="Request"></param>
        /// <returns></returns>
        public async Task<bool> CheckAsync(BatchProcessQueueDetailRequestDto batchProcess, CancellationToken cancellationToken)
        {
            try
            {
                IEnumerable<BatchProcessQueue> responseQueue = CheckInSamatBatchMappingQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"select C.SamatSourceHeader_ID as SamatSourceHeaderId , C.* from TB_SamatSourceDetail C " +
                           $" join TB_SamatSourceHeader H on H.ID = C.SamatSourceHeader_ID " +
                           $"where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                           $" and C.SamatSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                           $" and C.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
                //$" and C.Status = {(int)GenericEnum.Unasign} ";

                IEnumerable<SamatSourceDetail> responseDetails;
                responseDetails = _dapperRepository.Query<SamatSourceDetail>(scriptDetail);
                _logger.Information($"GetListSamatSourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;


                result = await ProcessInSourceDetail(responseDetails, cancellationToken);
                if (result)
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Success, 2);
                else
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);

                var header = _dapperRepository.Query<SamatSourceHeader>($"select * from TB_SamatSourceHeader where ID = {batchProcess.SourceHeaderId}").FirstOrDefault();
                if (await IsDone(batchProcess.SourceHeaderId) && header.Status == GenericEnum.Pending)
                    await UpdateStatusSourceHeader(batchProcess.SourceHeaderId, GenericEnum.Success);

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"Check in Message Queue Exception", ex);
                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }


        }

        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private IEnumerable<BatchProcessQueue> CheckInSamatBatchMappingQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or  Status = {(int)GenericEnum.InQueue} or  Status = {(int)GenericEnum.Unasign})";
            var responseQueue = _dapperRepository.Query<BatchProcessQueue>(scriptQueue);

            if (responseQueue.Any())
            {
                var scriptControl = $"delete FROM TB_SamatDetailControl where SamatSourceDetail_ID in " +
                    $"(select ID from TB_SamatSourceDetail where SamatSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                    $"and RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex})";
                _dapperRepository.ExecQuery(scriptControl);
            }

            return responseQueue;
        }

        private async Task<bool> UpdateStatusSourceHeader(long Id, GenericEnum status)
        {
            var script = $"Update H set Status = {(int)status} " +
                 $"from TB_SamatSourceHeader H where ID={Id}";
            await _dapperRepository.QueryAsync<SamatSourceHeader>(script);
            return true;
        }

        private async Task<bool> IsDone(long sourceHeaderId)
        {
            var count = await _dapperRepository.QueryAsync<int>($"SELECT COUNT(ID)  FROM TB_BatchProcessQueue where SourceHeader_ID = {sourceHeaderId} and Status!= 0 and RequestType = {(int)RequestType.Samat}");
            return count.FirstOrDefault() == 0;
        }


        /// <summary>
        /// پردازش بر روی لیست دریافتی 
        /// </summary>
        /// <param name="customerResponses"></param>
        /// <returns></returns>
        private async Task<bool> ProcessInSourceDetail(IEnumerable<SamatSourceDetail> samatSourceDetails, CancellationToken cancellationToken)
        {
            try
            {
                IList<SamatSourceDetail> lstSamatSourceDetail = new List<SamatSourceDetail>();
                IList<SamatDetailControl> lstSamatDetailControl = new List<SamatDetailControl>();
                IList<SamatZamenControl> lstSamatZamenControl = new List<SamatZamenControl>();
                var sucessCount = 0;
                var WrongCount = 0;

                var sourceHeaderId = samatSourceDetails.Select(c => c.SamatSourceHeaderId).FirstOrDefault();
                foreach (var samatSourceDetail in samatSourceDetails)
                {
                    try
                    {
                        var errorMessage = string.Empty;
                        var getInquirySamat = await GetInquirySamat(samatSourceDetail, cancellationToken);

                        lstSamatSourceDetail.Add(getInquirySamat.SamatSourceDetail);
                        errorMessage = getInquirySamat.ErrorMessage;
                        sucessCount = getInquirySamat.SucessCount;
                        WrongCount = getInquirySamat.WrongCount;
                        if (getInquirySamat.SamatDetailControl != null)
                        {
                            foreach (var samatDetailControl in getInquirySamat.SamatDetailControl)
                                lstSamatDetailControl.Add(samatDetailControl);
                        }

                        var getSamatZammen = await GetInquirySamatZammen(samatSourceDetail, cancellationToken);

                        if (getSamatZammen.ErrorMessage != null)
                            errorMessage = getSamatZammen.ErrorMessage;
                        if (getSamatZammen.SamatZamenControl != null)
                        {
                            foreach (var samatZamenControl in getSamatZammen.SamatZamenControl)
                                lstSamatZamenControl.Add(samatZamenControl);
                        }

                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"SamatChecker.ProcessInSourceDetail Exception: {ex.Message}");
                        SamatSourceDetail sourceDetail = new SamatSourceDetail()
                        {
                            Id = samatSourceDetail.Id,
                            UniqueIdentifier = samatSourceDetail.UniqueIdentifier,
                            BirthDate = samatSourceDetail.BirthDate,
                            RowIdentifier = samatSourceDetail.RowIdentifier,
                            Status = GenericEnum.ServerError,

                        };
                        lstSamatSourceDetail.Add(sourceDetail);
                        WrongCount = WrongCount + 1;

                    }

                }
                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                _dapperRepository.BulkUpdate<SamatSourceDetail>(lstSamatSourceDetail);
                _logger.Information($"SamatSourceDetail BulkUpdateCount: {lstSamatSourceDetail.Count()}");

                _dapperRepository.BulkInsert<SamatDetailControl>(lstSamatDetailControl);
                _logger.Information($"lstSamatDetailControl BulkInsertCount: {lstSamatDetailControl.Count()}");

                _dapperRepository.BulkInsert<SamatZamenControl>(lstSamatZamenControl);
                _logger.Information($"lstSamatZamenControl BulkInsertCount: {lstSamatZamenControl.Count()}");

                await UpdateSourceHeader(sourceHeaderId, sucessCount, WrongCount);
                _dapperUnitOfWork.Commit();

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                var message = ex.Message;
                _logger.ErrorLog($"SamatChecker.ProcessInSourceDetail Exception", ex);
                return await Task.FromResult(false);
            }
        }

        private async Task<InquirySamatResponse> GetInquirySamat(SamatSourceDetail samatSourceDetail, CancellationToken cancellationToken)
        {
            decimal sumAmVasaegh = 0;
            var errorMessage = string.Empty;
            IList<SamatDetailControl> lstSamatDetailControl = new List<SamatDetailControl>();
            InquirySamatResponse inquirySamatResponse = new InquirySamatResponse();
            var samatresponse = await InquirySamatInfo(samatSourceDetail, cancellationToken);
            var sourceDetail = _dapperRepository.Query<SamatSourceDetail>($"select SamatSourceHeader_ID as SamatSourceHeaderId , ID as Id, * from TB_SamatSourceDetail where ID = {samatSourceDetail.Id}").FirstOrDefault();

            if (samatresponse.HasError)
            {
                errorMessage = $"  / خطا در استعلام تسهیلات  /  ";
            }
            else
            {
                var samatReturnValue = samatresponse.ReturnValue;
                if (samatReturnValue != null && samatReturnValue.EstelamAsliRows != null && samatReturnValue.EstelamAsliRows.EstelamAsliRow != null)
                {
                    foreach (var item in samatReturnValue.EstelamAsliRows.EstelamAsliRow)
                    {
                        SamatDetailControl detailControl = new SamatDetailControl()
                        {
                            PersonType = samatSourceDetail.PersonType,
                            RequestNum = item.RequestNum,
                            BankCode = item.BankCode,
                            ShobeCode = item.ShobeCode,
                            ShobeName = item.ShobeName,
                            RequstType = item.RequstType,
                            DateSarResid = item.DateSarResid,
                            ArzCode = item.ArzCode,
                            AmOriginal = item.AmOriginal,
                            AmBenefit = item.AmBenefit,
                            AmBedehiKol = item.AmBedehiKol,
                            AmSarResid = item.AmSarResid,
                            AmMoavagh = item.AmMoavagh,
                            AmMashkuk = item.AmMashkuk,
                            AmSukht = item.AmSukht,
                            AmEltezam = item.AmEltezam,
                            Amdirkard = item.AmDirkard,
                            AmTahod = item.AmTahod,
                            Type = item.Type,
                            RsrcTamin = item.RsrcTamin,
                            HadafAzDaryaft = item.HadafAzDaryaft,
                            PlaceCdMasraf = item.PlaceCdMasraf,
                            DasteBandi = item.DasteBandi,
                            AdamSabtSanadEntezami = item.AdamSabtSanadEntezami,
                            Created = DateTime.Now,
                            CreatedBy = "BatchChecker",
                            MainIdNo = item.MainIdNo,
                            MainLgId = item.MainLgId,
                            SamatSourceDetailId = samatSourceDetail.Id,
                        };
                        lstSamatDetailControl.Add(detailControl);
                        sumAmVasaegh += await GetSumAmountVasaegh(samatSourceDetail, item, cancellationToken);
                    }
                    inquirySamatResponse.SamatDetailControl = lstSamatDetailControl;
                }

                if (samatReturnValue != null)
                {
                    if (inquirySamatResponse.SamatDetailControl != null)
                    {
                        var lstSamatDetailControls = inquirySamatResponse.SamatDetailControl.Where(x => x.SamatSourceDetailId == samatSourceDetail.Id && (x.DasteBandi == "اصلي" || x.DasteBandi == "اصلی")).ToList();
                        sourceDetail.BadHesabiDate = samatReturnValue.BadHesabiDate;
                        sourceDetail.SumAmOriginal = lstSamatDetailControls.Where(x => x.RequstType == "3").Sum(x => Convert.ToDecimal(x.AmOriginal));
                        sourceDetail.SumAmBedehiKol = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.AmBedehiKol));
                        sourceDetail.SumAmBenefit = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.AmBenefit));
                        sourceDetail.SumAmDirkard = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.Amdirkard));
                        sourceDetail.SumAmEltezam = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.AmEltezam));
                        sourceDetail.SumAmMashkuk = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.AmMashkuk));
                        sourceDetail.SumAmMoavagh = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.AmMoavagh));
                        sourceDetail.SumAmSarResid = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.AmSarResid));
                        sourceDetail.SumAmSukht = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.AmSukht));
                        sourceDetail.SumAmTahod = lstSamatDetailControls.Sum(x => Convert.ToDecimal(x.AmTahod));
                    }
                    else
                    {
                        sourceDetail.BadHesabiDate = samatReturnValue.BadHesabiDate;
                        sourceDetail.SumAmOriginal = samatReturnValue.SumAmOriginal;
                        sourceDetail.SumAmBedehiKol = samatReturnValue.SumAmBedehiKol;
                        sourceDetail.SumAmBenefit = samatReturnValue.SumAmBenefit;
                        sourceDetail.SumAmDirkard = samatReturnValue.SumAmDirkard;
                        sourceDetail.SumAmEltezam = samatReturnValue.SumAmEltezam;
                        sourceDetail.SumAmMashkuk = samatReturnValue.SumAmMashkuk;
                        sourceDetail.SumAmMoavagh = samatReturnValue.SumAmMoavagh;
                        sourceDetail.SumAmSarResid = samatReturnValue.SumAmSarResid;
                        sourceDetail.SumAmSukht = samatReturnValue.SumAmSukht;
                        sourceDetail.SumAmTahod = samatReturnValue.SumAmTahod;
                    }
                }
            }

            var personBankDB = await _tataGatewayApi.GetPersonBankDB(samatSourceDetail.UniqueIdentifier.PadingNationalCode(samatSourceDetail.PersonType), cancellationToken);
            if (personBankDB.ErrorCode == "01")
            {
                sourceDetail.CustomerFirstName = personBankDB.FirstName;
                sourceDetail.CustomerLastName = personBankDB.LastName;
                sourceDetail.BirthDate = personBankDB.BirthDate.ToString();
                sourceDetail.SocialIdentityNumber = personBankDB.SocialIdentityNumber.ToString();
                sourceDetail.FatherName = personBankDB.FatherName;
            }
            sourceDetail.SumAmVasaegh = sumAmVasaegh;
            sourceDetail.HasError = samatresponse.HasError || samatresponse.HasError ? true : false;
            sourceDetail.Message = errorMessage;
            sourceDetail.LastModified = DateTime.Now;
            sourceDetail.LastModifiedBy = "BatchChecker";
            inquirySamatResponse.SamatSourceDetail = sourceDetail;


            inquirySamatResponse.WrongCount = (samatresponse.HasError ? 1 : 0);
            inquirySamatResponse.SucessCount = (samatresponse.HasError ? 0 : 1);

            return inquirySamatResponse;
        }

        private async Task<InquirySamatZamenResponse> GetInquirySamatZammen(SamatSourceDetail samatSourceDetail, CancellationToken cancellationToken)
        {
            #region Zamenresponse
            IList<SamatZamenControl> lstSamatZamenControl = new List<SamatZamenControl>();
            InquirySamatZamenResponse inquirySamatZamenResponse = new InquirySamatZamenResponse();
            var errorMessage = string.Empty;
            var Zamenresponse = await InquiryZamenInfo(samatSourceDetail, cancellationToken);
            if (Zamenresponse.HasError)
            {
                errorMessage = $"{errorMessage}     / خطا در استعلام ضامنین /  ";
                return new InquirySamatZamenResponse() { ErrorMessage = errorMessage, SamatZamenControl = null };
            }
            else
            {
                var zmntReturnValue = Zamenresponse.ReturnValue;
                if (zmntReturnValue != null && zmntReturnValue.EstelamZamenRows != null && zmntReturnValue.EstelamZamenRows.EstelamZamenRow != null)
                {
                    foreach (var item in zmntReturnValue.EstelamZamenRows.EstelamZamenRow)
                    {
                        SamatZamenControl samatZamenControl = new SamatZamenControl()
                        {
                            CustomerType = Zamenresponse.ReturnValue.CustomerType,
                            DateEstlm = Zamenresponse.ReturnValue.DateEstlm,
                            NationalCd = Zamenresponse.ReturnValue.NationalCd,
                            ShenaseEstlm = Zamenresponse.ReturnValue.ShenaseEstlm,
                            ShenaseRes = Zamenresponse.ReturnValue.ShenaseRes,
                            FName = Zamenresponse.ReturnValue.FName,
                            LName = Zamenresponse.ReturnValue.LName,
                            AmBedehiKol = item.AmBedehiKol,
                            AmBenefit = item.AmBenefit,
                            AmDirkard = item.AmDirkard,
                            AmEltezam = item.AmEltezam,
                            AmMashkuk = item.AmMashkuk,
                            AmMoavagh = item.AmMoavagh,
                            AmOriginal = item.AmOriginal,
                            AmSarResid = item.AmSarResid,
                            AmTahod = item.AmTahod,
                            BankCode = item.BankCode,
                            Date = item.Date,
                            DateSarResid = item.DateSarResid,
                            PrcntZemanat = item.PrcntZemanat,
                            RequestNum = item.RequestNum,
                            RequstType = item.RequstType,
                            ShobeCode = item.ShobeCode,
                            ShobeName = item.ShobeName,
                            ZmntIdNo = item.ZmntIdNo,
                            ZmntLName = item.ZmntLName,
                            ZmntLgId = item.ZmntLgId,
                            ZmntName = item.ZmntName,
                            Created = DateTime.Now,
                            CreatedBy = "BatchChecker",
                            SamatSourceDetailId = samatSourceDetail.Id,
                        };
                        lstSamatZamenControl.Add(samatZamenControl);
                    }
                    inquirySamatZamenResponse.SamatZamenControl = lstSamatZamenControl;
                }
                return inquirySamatZamenResponse;
            }
            #endregion
        }
        private async Task<decimal> GetSumAmountVasaegh(SamatSourceDetail samatSourceDetail, EstelamAsliRow item, CancellationToken cancellationToken)
        {
            decimal sumAmVasaegh = 0;
            var estelamVasighe = await _tataGatewayApi.InquirySamatVasighe(new EstelamVasigheRequest()
            {
                BankCode = item.BankCode,
                EstlmId = samatSourceDetail.SamatSourceHeaderId.ToString(),
                RequestNum = item.RequestNum,
                ShobeCode = item.ShobeCode
            }, cancellationToken);

            if (estelamVasighe.ReturnValue != null)
                if (estelamVasighe.ReturnValue.EstelamVasigheRows.EstelamVasigheRow != null)
                {
                    foreach (var estelamVasigheRow in estelamVasighe.ReturnValue.EstelamVasigheRows.EstelamVasigheRow)
                    {
                        sumAmVasaegh += Convert.ToDecimal(estelamVasigheRow.Amount);
                    }
                }

            return sumAmVasaegh;
        }

        private async Task<InquirySamatInfoResponse> InquirySamatInfo(SamatSourceDetail samatSourceDetail, CancellationToken cancellationToken)
        {
            InquirySamatInfoRequest inquirySamatInfoRequest = new InquirySamatInfoRequest()
            {
                CstmrLName = samatSourceDetail.CustomerLastName == null ? string.Empty : samatSourceDetail.CustomerLastName,
                CstmrName = samatSourceDetail.CustomerFirstName == null ? string.Empty : samatSourceDetail.CustomerFirstName,
                PersonType = samatSourceDetail.PersonType,
                RequestId = samatSourceDetail.Id,
                NationalCode = samatSourceDetail.UniqueIdentifier.PadingNationalCode(samatSourceDetail.PersonType),
            };
            var SamatResponse = await _tataGatewayApi.InquirySamatInfo(inquirySamatInfoRequest, cancellationToken);
            return SamatResponse;
        }
        private async Task<InquiryZamenInfoResponse> InquiryZamenInfo(SamatSourceDetail samatSourceDetail, CancellationToken cancellationToken)
        {
            InquiryZamenInfoRequest inquiryZamenInfoRequest = new InquiryZamenInfoRequest()
            {
                ZamenLName = samatSourceDetail.CustomerLastName == null ? string.Empty : samatSourceDetail.CustomerLastName,
                ZamenName = samatSourceDetail.CustomerFirstName == null ? string.Empty : samatSourceDetail.CustomerFirstName,
                PersonType = samatSourceDetail.PersonType,
                RequestId = samatSourceDetail.Id,
                NationalCode = samatSourceDetail.UniqueIdentifier.PadingNationalCode(samatSourceDetail.PersonType),
            };

            var SamatResponse = await _tataGatewayApi.InquiryZamenInfo(inquiryZamenInfoRequest, cancellationToken);
            return SamatResponse;
        }



        private async Task UpdateSourceHeader(long sourceHeaderId, int ControlledCorrectCount, int ControlledWrongCount)
        {

            var script = $"Update C set ControlledCorrectCount = ControlledCorrectCount + {ControlledCorrectCount} ," +
                $"ControlledWrongCount = ControlledWrongCount + {ControlledWrongCount} " +
                $" from TB_SamatSourceHeader C where ID={sourceHeaderId} ";
            lock (this)
            {
                var nocrSourceHeader = _dapperRepository.Query<SamatSourceHeader>(script);
            }
            await Task.Delay(0);
        }

        /// <summary>
        /// آپدیت تیبل BatchProcessQueueDetail
        /// </summary>
        /// <param name="Id"></param>
        /// <param name="StartDate"></param>
        /// <param name="GenericEnum"></param>
        /// <returns></returns>
        private async Task<bool> UpdateBatchProcessQueueDetail(long Id, GenericEnum genericEnum, int Result)
        {
            if (Result == 1)
            {
                var scriptBatchProcess = $"update b set ProcessStartDate='{DateTime.Now}' " +
             $",Status={(int)genericEnum}" +
                 $" from TB_BatchProcessQueue b where ID = {Id}";
                lock (this)
                {
                    _dapperRepository.ExecQuery(scriptBatchProcess);
                }
            }
            else
            {
                var scriptBatchProcess = $"update b set ProcessEndDate='{DateTime.Now}' " +
             $",Status={(int)genericEnum}" +
                 $" from TB_BatchProcessQueue b where ID = {Id}";
                lock (this)
                {
                    _dapperRepository.ExecQuery(scriptBatchProcess);
                }
            }

            return await Task.FromResult(true);
        }
        #endregion

        #region MissedSender
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken)
        {
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount ,RequestType " +
            $" from TB_BatchProcessQueue Q join TB_SamatSourceHeader C on C.ID = Q.SourceHeader_ID where " +
            $" (Q.Status = {(int)GenericEnum.Pending} or Q.Status = {(int)GenericEnum.InQueue}) and RequestType = {(int)RequestType.Samat} " +
            $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"GetListBatchProcessQueueDetail_Missed Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        #endregion
    }
}
