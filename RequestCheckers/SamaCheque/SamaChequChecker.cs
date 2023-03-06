
using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
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
using BatchOP.Domain.Entities.samaCheque;
using BatchOp.Application.Enums;
using BatchOp.Application.Enums.Excentions;
using BatchChecker.Dto.Share;
using BatchOP.Domain.Entities.Customer;
using BatchChecker.Extensions;
using BatchOP.Domain.Entities.Samat;
using BatchOp.Application.Utility.StringExtensions;
using BatchOp.Application.DTOs.TataGateway.SamaCheque.BouncedCheque;
using BatchOp.Application.DTOs.TataGateway.SamaCheque.BouncedChequeByIdCheque;
using BatchOp.Application.DTOs.TataGateway.SamaCheque.BouncedChequeByPersonInfo;
using BatchOp.Application.DTOs.TataGateway.SamaCheque;

namespace BatchChecker.RequestCheckers
{
    public class SamaChequeChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ITataGatewayApi _tataGatewayApi;
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        public SamaChequeChecker(

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

            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount, RequestType " +
                $" from TB_BatchProcessQueue Q join TB_SamaChequeSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                $" (Q.Status = {(int)GenericEnum.Unasign}) and RequestType = {(int)RequestType.SamaCheque} " +
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
            var script = $"Update S set Status = {(int)GenericEnum.Success} , ProcessIndex = {processIndex} , ProcessDate = '{DateTime.Now}' " +
                $"from TB_SamaChequeSourceHeader S where ID={Id}";
            lock (this)
            {
                var SamaChequeSourceHeader = _dapperRepository.Query<SamaChequeSourceHeader>(script);
            }
            return await Task.FromResult(true);
        }
        /// <summary>
        /// ثبت درخواست ها با ایندکس در تیبل صف
        /// </summary>
        /// <param name="SamaChequeResponses"></param>
        /// <returns></returns>
        private async Task<bool> InsertToBatchProcessQueueDetailAsync(IEnumerable<SourceHeaderDto> SamaChequeResponses, CancellationToken stoppingToken)
        {
            foreach (var item in SamaChequeResponses)
            {
                var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                var batchCount = rabbitMQConfiguration.SamaChequeBatchCount;

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
                        RequestType = RequestType.SamaCheque,
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
                                    $"and RequestType = {(int)RequestType.SamaCheque} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);

            return queryResponse.Count();
        }

        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderSamaCheque");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R INNER JOIN TB_SamaChequeSourceHeader H On R.ID = H.Request_ID " +
                $"join TB_SamaChequeSourceDetail det on h.id = det.SamaChequeSourceHeader_ID " +
                $"WHERE H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderSamaCheque Count:{queryResponse.Count()}");

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
                IEnumerable<BatchProcessQueue> responseQueue = CheckInSamaChequeBatchMappingQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"select C.SamaChequeSourceHeader_ID as SamaChequeSourceHeaderId , C.* from TB_SamaChequeSourceDetail C " +
                           $" join TB_SamaChequeSourceHeader H on H.ID = C.SamaChequeSourceHeader_ID " +
                           $"where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                           $" and C.SamaChequeSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                           $" and C.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
                //$" and C.Status = {(int)GenericEnum.Unasign} ";

                IEnumerable<SamaChequeSourceDetail> responseDetails;
                responseDetails = _dapperRepository.Query<SamaChequeSourceDetail>(scriptDetail);
                _logger.Information($"GetListSamaChequeSourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;


                result = await ProcessInSourceDetail(responseDetails, cancellationToken);
                if (result)
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Success, 2);
                else
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);

                var header = _dapperRepository.Query<SamaChequeSourceHeader>($"select * from TB_SamaChequeSourceHeader where ID = {batchProcess.SourceHeaderId}").FirstOrDefault();
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

        private async Task<bool> UpdateStatusSourceHeader(long Id, GenericEnum status)
        {
            var script = $"Update H set Status = {(int)status} " +
                 $"from TB_SamaChequeSourceHeader H where ID={Id}";
            await _dapperRepository.QueryAsync<SamaChequeSourceHeader>(script);
            return true;
        }

        private async Task<bool> IsDone(long sourceHeaderId)
        {
            var count = await _dapperRepository.QueryAsync<int>($"SELECT COUNT(ID)  FROM TB_BatchProcessQueue where SourceHeader_ID = {sourceHeaderId} and Status!= 0 and RequestType = {(int)RequestType.SamaCheque}");
            return count.FirstOrDefault() == 0;
        }

        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private IEnumerable<BatchProcessQueue> CheckInSamaChequeBatchMappingQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or  Status = {(int)GenericEnum.InQueue} or  Status = {(int)GenericEnum.Unasign})";
            var responseQueue = _dapperRepository.Query<BatchProcessQueue>(scriptQueue);

            if (responseQueue.Any())
            {
                var scriptControl = $"delete FROM TB_SamaChequeDetailControl where SamaChequeSourceDetail_ID in " +
                    $"(select ID from TB_SamaChequeSourceDetail where SamaChequeSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                    $"and RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex})";
                _dapperRepository.ExecQuery(scriptControl);
            }

            return responseQueue;
        }

        /// <summary>
        /// پردازش بر روی لیست دریافتی 
        /// </summary>
        /// <param name="customerResponses"></param>
        /// <returns></returns>
        private async Task<bool> ProcessInSourceDetail(IEnumerable<SamaChequeSourceDetail> samaChequeSourceDetails, CancellationToken cancellationToken)
        {
            try
            {
                IList<SamaChequeSourceDetail> lstSamaChequeSourceDetail = new List<SamaChequeSourceDetail>();
                IList<SamaChequeDetailControl> lstSamaChequeDetailControl = new List<SamaChequeDetailControl>();
                var sucessCount = 0;
                var wrongCount = 0;
                var sourceHeaderId = samaChequeSourceDetails.Select(c => c.SamaChequeSourceHeaderId).FirstOrDefault();
                foreach (var samaChequeSourceDetail in samaChequeSourceDetails)
                {
                    var errorMessage = string.Empty;
                    var sourceDetail = _dapperRepository.Query<SamaChequeSourceDetail>($"select SamaChequeSourceHeader_ID as SamaChequeSourceHeaderId , ID as Id, * from TB_SamaChequeSourceDetail where ID = {samaChequeSourceDetail.Id}").FirstOrDefault();
                    try
                    {
                        var bouncedChequeResponce = await GetBouncedCheque(samaChequeSourceDetail, sourceDetail, cancellationToken);
                        errorMessage = bouncedChequeResponce.ErrorMessage;
                        sucessCount = sucessCount + 1;

                        lstSamaChequeSourceDetail.Add(bouncedChequeResponce.SamaChequeSourceDetail);
                        if (bouncedChequeResponce.SamaChequeDetailControl != null)
                        {
                            foreach (var samaChequeDetailControl in bouncedChequeResponce.SamaChequeDetailControl)
                                lstSamaChequeDetailControl.Add(samaChequeDetailControl);
                        }

                    }
                    catch (Exception exp)
                    {
                        _logger.Error($"SamaChequChecker.ProcessInSourceDetail Exception: {exp.Message}");
                        sourceDetail.LastModified = DateTime.Now;
                        sourceDetail.LastModifiedBy = "BatchChecker";
                        sourceDetail.Status = GenericEnum.Success;
                        lstSamaChequeSourceDetail.Add(sourceDetail);
                        wrongCount = wrongCount + 1;
                    }
                }
                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                _dapperRepository.BulkUpdate<SamaChequeSourceDetail>(lstSamaChequeSourceDetail);
                _logger.Information($"SamaChequeSourceDetail BulkUpdateCount: {lstSamaChequeSourceDetail.Count()}");
                _dapperRepository.BulkInsert<SamaChequeDetailControl>(lstSamaChequeDetailControl);
                _logger.Information($"lstSamaChequeDetailControl BulkInsertCount: {lstSamaChequeDetailControl.Count()}");
                await UpdateSourceHeader(sourceHeaderId, sucessCount, wrongCount);
                _dapperUnitOfWork.Commit();
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                var message = ex.Message;
                _logger.ErrorLog($"SamaChequChecker.ProcessInSourceDetail Exception", ex);
                return await Task.FromResult(false);
            }
        }

        private async Task<BouncedChequeResponce> GetBouncedCheque(SamaChequeSourceDetail samaChequeSourceDetail, SamaChequeSourceDetail sourceDetail, CancellationToken cancellationToken)
        {
            BouncedChequeResponce bouncedChequeResponce = new BouncedChequeResponce();
            IList<SamaChequeDetailControl> lstSamaChequeDetailControl = new List<SamaChequeDetailControl>();

            var bouncedChequeByPersonInfoResponse = new BouncedChequeByPersonInfoResponse();
            var bouncedChequeByIdChequeResponse = new BouncedChequeByIdChequeResponse();

            if (samaChequeSourceDetail.InquiryType == InquiryType.NationalCode)
                bouncedChequeByPersonInfoResponse = await BouncedChequeByCustInfo(samaChequeSourceDetail, cancellationToken);
            else
                bouncedChequeByIdChequeResponse = await BouncedChequeByIdCheque(samaChequeSourceDetail, cancellationToken);

            var errorMessage = string.Empty;
            #region samachequeResponse
            if (!bouncedChequeByPersonInfoResponse.IsValid)
            {
                errorMessage = $"{errorMessage}     / خطا در استعلام چک برگشتی  /  ";
            }
            else
            {
                if (samaChequeSourceDetail.InquiryType == InquiryType.NationalCode)
                {
                    var chequeInfo = bouncedChequeByPersonInfoResponse.Cheques.BouncedChequeCustomerModel;
                    var personInfo = bouncedChequeByPersonInfoResponse.Person;
                    if (chequeInfo != null)
                    {
                        foreach (var item in chequeInfo)
                        {
                            var bouncedReasons = string.Empty;
                            foreach (var bouncedReason in new List<int>(item.BouncedReason.@int))
                            {
                                bouncedReasons = String.Join(",", bouncedReason);
                            }

                            var detailControl = new SamaChequeDetailControl()
                            {

                                FirstName = personInfo.FirstName,
                                LastName = personInfo.LastName,
                                UniqueIdentifier = personInfo.NationalId,
                                CustomerType = personInfo.PersonType,
                                Amount = item.Amount,
                                BankCode = item.BankCode,
                                BouncedAmount = item.BouncedAmount,
                                BouncedBranchName = item.BouncedBranchName,
                                BouncedDate = item.BouncedDate,
                                BouncedReason = bouncedReasons,
                                BranchBounced = item.BranchBounced,
                                BranchOrigin = item.BranchOrigin,
                                ChannelKind = item.ChannelKind,
                                CurrencyCode = item.CurrencyCode,
                                CurrencyRate = item.CurrencyRate,
                                DeadlineDate = item.DeadlineDate,
                                IBAN = item.Iban,
                                idCheque = item.IdCheque.ToString(),
                                OriginBranchName = item.OriginBranchName,
                                ChequeNo = item.Serial,
                                CreatedBy = "BatchChecker",
                                Created = DateTime.Now,
                                ReturnDate = bouncedChequeByPersonInfoResponse.RequestDateTime,
                                SamaChequeSourceDetailId = samaChequeSourceDetail.Id
                            };
                            lstSamaChequeDetailControl.Add(detailControl);
                        }
                        bouncedChequeResponce.SamaChequeDetailControl = lstSamaChequeDetailControl;
                    }

                }
                else
                {
                    var chequeInfo = bouncedChequeByIdChequeResponse.Cheque;
                    var personInfo = bouncedChequeByIdChequeResponse.Customers;
                    if (personInfo != null && personInfo.CustomerModel != null)
                    {
                        foreach (var item in personInfo.CustomerModel)
                        {
                            var bouncedReasons = string.Empty;
                            foreach (var bouncedReason in new System.Collections.Generic.List<int>(chequeInfo.BouncedReason.@int))
                            {
                                bouncedReasons = String.Join(",", bouncedReason);
                            }

                            var detailControl = new SamaChequeDetailControl()
                            {
                                FirstName = item.FirstName,
                                LastName = item.LastName,
                                UniqueIdentifier = item.NationalId,
                                CustomerType = item.PersonType,
                                Amount = chequeInfo.Amount,
                                BankCode = chequeInfo.BankCode,
                                BouncedAmount = chequeInfo.BouncedAmount,
                                BouncedBranchName = chequeInfo.BouncedBranchName,
                                BouncedDate = chequeInfo.BouncedDate,
                                BouncedReason = bouncedReasons,
                                BranchBounced = chequeInfo.BranchBounced,
                                BranchOrigin = chequeInfo.BranchOrigin,
                                ChannelKind = chequeInfo.ChannelKind,
                                CurrencyCode = chequeInfo.CurrencyCode,
                                CurrencyRate = chequeInfo.CurrencyRate,
                                DeadlineDate = chequeInfo.DeadlineDate,
                                IBAN = chequeInfo.Iban,
                                idCheque = chequeInfo.IdCheque.ToString(),
                                OriginBranchName = chequeInfo.OriginBranchName,
                                ChequeNo = chequeInfo.Serial,
                                CreatedBy = "BatchChecker",
                                Created = DateTime.Now,
                                ReturnDate = bouncedChequeByIdChequeResponse.RequestDateTime,
                                SamaChequeSourceDetailId = samaChequeSourceDetail.Id
                            };
                            lstSamaChequeDetailControl.Add(detailControl);
                        }
                        bouncedChequeResponce.SamaChequeDetailControl = lstSamaChequeDetailControl;
                    }
                }
            }

            sourceDetail.LastModified = DateTime.Now;
            sourceDetail.LastModifiedBy = "BatchChecker";
            sourceDetail.Status = GenericEnum.Success;
            bouncedChequeResponce.SamaChequeSourceDetail = sourceDetail;
            bouncedChequeResponce.ErrorMessage = errorMessage;
            #endregion
            return bouncedChequeResponce;
        }
        private async Task<BouncedChequeByPersonInfoResponse> BouncedChequeByCustInfo(SamaChequeSourceDetail samaChequeSourceDetail, CancellationToken cancellationToken)
        {
            BouncedChequeByPersonInfoRequest bouncedChequeByPersonInfo = new BouncedChequeByPersonInfoRequest()
            {
                PersonInfo = new PersonInfo()
                {
                    NationalId = samaChequeSourceDetail.UniqueIdentifier.PadingNationalCode(samaChequeSourceDetail.PersonType),
                    PersonType = (int)samaChequeSourceDetail.PersonType
                },
                RequestInfo = samaChequeSourceDetail.Id.ToString()
            };
            var SamaChequeResponse = await _tataGatewayApi.InquiryBouncedChequeByPersonInfo(bouncedChequeByPersonInfo, cancellationToken);
            return SamaChequeResponse;
        }


        private async Task<BouncedChequeByIdChequeResponse> BouncedChequeByIdCheque(SamaChequeSourceDetail samaChequeSourceDetail, CancellationToken cancellationToken)
        {
            BouncedChequeByIdChequeRequest clearChequeByIdCheque = new BouncedChequeByIdChequeRequest()
            {
                IdCheque = Convert.ToInt32(samaChequeSourceDetail.IdCheque),
                RequestInfo = samaChequeSourceDetail.Id.ToString()
            };
            var SamaChequeResponse = await _tataGatewayApi.InquiryBouncedChequeByIdCheque(clearChequeByIdCheque, cancellationToken);
            return SamaChequeResponse;
        }


        private async Task UpdateSourceHeader(long sourceHeaderId, int ControlledCorrectCount, int ControlledWrongCount)
        {
            var script = $"Update C set ControlledCorrectCount = ControlledCorrectCount + {ControlledCorrectCount} ," +
                         $"ControlledWrongCount = ControlledWrongCount + {ControlledWrongCount} " +
                         $" from TB_SamaChequeSourceHeader C where ID={sourceHeaderId} ";
            lock (this)
            {
                var nocrSourceHeader = _dapperRepository.Query<SamaChequeSourceHeader>(script);
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
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount , RequestType " +
            $" from TB_BatchProcessQueue Q join TB_SamaChequeSourceHeader C on C.ID = Q.SourceHeader_ID where " +
            $" (Q.Status = {(int)GenericEnum.Pending} or Q.Status = {(int)GenericEnum.InQueue}) and RequestType = {(int)RequestType.SamaCheque} " +
            $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"GetListBatchProcessQueueDetail_Missed Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        #endregion
    }
}
