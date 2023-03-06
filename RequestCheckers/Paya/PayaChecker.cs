using BatchChecker.Dto;
using BatchChecker.Dto.Customer;
using BatchChecker.Dto.FetchCDC;
using BatchChecker.Dto.Share;
using BatchChecker.Extensions;
using BatchChecker.FetchCDC;
using BatchOp.Application.Interfaces;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Infrastructure.Shared.ApiCaller.Galaxy;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOP.Domain.Entities.Customer;
using BatchOP.Domain.Entities.Paya;
using BatchOP.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.RequestCheckers
{
    public class PayaChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICDCRepository _cDCRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IFetchFromCDC _fetchFromCDC;
        private readonly IGalaxyApi _galaxyApi;

        public PayaChecker(
             ILogger logger,
            IDapperRepository dapperRepository,
            ICDCRepository cDCRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration,
            IFetchFromCDC fetchFromCDC,
            IGalaxyApi galaxyApi)
        {
            _logger = logger;
            _dapperRepository = dapperRepository;
            _cDCRepository = cDCRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
            _fetchFromCDC = fetchFromCDC;
            _galaxyApi = galaxyApi;
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
                               $" from TB_BatchProcessQueue Q join TB_PayaSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                               $" (Q.Status = {(int)GenericEnum.Unasign}) and RequestType = {(int)RequestType.Paya} " +
                               $"and  (C.Status != {(int)GenericEnum.Deleted} or C.Status != {(int)GenericEnum.Cancel}) and TryCount < 11 ";

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
        private async Task<bool> UpdatePayaSourceHeaderAsync(long Id, int processIndex, CancellationToken stoppingToken)
        {
            var script = $"Update C set Status = {(int)GenericEnum.Pending} , ProcessIndex = {processIndex} , ProcessDate = '{DateTime.Now}' " +
                $"from TB_PayaSourceHeader C where ID={Id}";
            lock (this)
            {
                var payaSourceHeader = _dapperRepository.Query<PayaSourceHeader>(script);
            }
            return await Task.FromResult(true);
        }
        /// <summary>
        /// ثبت درخواست ها با ایندکس در تیبل صف
        /// </summary>
        /// <param name="PayaResponses"></param>
        /// <returns></returns>
        private async Task<bool> InsertToBatchProcessQueueDetailAsync(IEnumerable<SourceHeaderDto> PayaResponses, CancellationToken stoppingToken)
        {
            foreach (var item in PayaResponses)
            {
                var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                var batchCount = rabbitMQConfiguration.PayaBatchCount;
                int startIndex = 0;
                int endIndex = 0;

                for (int i = 1; endIndex < item.RequestCount; i++)
                {
                    startIndex = (i - 1) * batchCount + 1;
                    endIndex = item.RequestCount <= i * batchCount ? endIndex = item.RequestCount : endIndex = i * batchCount;

                    BatchProcessQueue batchProcessQueueDetail = new BatchProcessQueue()
                    {
                        SourceHeaderId = item.SourceHeaderId,
                        StartIndex = 1,
                        EndIndex = item.RequestCount,
                        Status = GenericEnum.Unasign,
                        RequestType = RequestType.Paya,
                        TryCount = 0,
                    };
                    if (FindBatchProccessQueue(batchProcessQueueDetail) == 0)
                    {
                        _dapperRepository.Add(batchProcessQueueDetail);
                        _logger.Information($"InsertToBatchProcessQueueDetail : {JsonConvert.SerializeObject(batchProcessQueueDetail)}");
                        await UpdatePayaSourceHeaderAsync(item.SourceHeaderId, item.RequestCount, stoppingToken);
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
                                    $"and RequestType = {(int)RequestType.Paya} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);

            return queryResponse.Count();
        }

        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderPayas");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R " +
                $"INNER JOIN TB_PayaSourceHeader H On R.ID = H.Request_ID " +
                $"join TB_PayaSourceDetail det on h.id = det.PayaSourceHeader_ID " +
                $"WHERE H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderPayas Count:{queryResponse.Count()}");

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
                IEnumerable<BatchProcessQueue> responseQueue = CheckInPayaBatchProcessQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"select C.PayaSourceHeader_ID as PayaSourceHeaderId , C.* from TB_PayaSourceDetail C " +
                                           $" join TB_PayaSourceHeader H on H.ID = C.PayaSourceHeader_ID " +
                                           $" where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                                           $" and C.PayaSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                                           $" and C.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
                // $"and C.Status != {(int)GenericEnum.Success}";

                IEnumerable<PayaSourceDetail> responseDetails;
                responseDetails = _dapperRepository.Query<PayaSourceDetail>(scriptDetail);
                _logger.Information($"GetListPayaSourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;
                result = await ProcessInPayaSourceDetail(responseDetails);
                if (result)
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Success, 2);
                else
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);

                var header = _dapperRepository.Query<PayaSourceHeader>($"select * from TB_PayaSourceHeader where ID = {batchProcess.SourceHeaderId}").FirstOrDefault();
                if (await IsDone(batchProcess.SourceHeaderId) && header.Status == GenericEnum.Pending)
                    await UpdateStatusSourceHeader(batchProcess.SourceHeaderId, GenericEnum.Success);

                // await Task.Delay(100);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"PayaChecker.BatchProcess Exception",ex);
                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }

        }
        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private IEnumerable<BatchProcessQueue> CheckInPayaBatchProcessQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or  Status = {(int)GenericEnum.InQueue} or  Status = {(int)GenericEnum.Unasign})";
            var responseQueue = _dapperRepository.Query<BatchProcessQueue>(scriptQueue);
            if (responseQueue.Any())
            {
                var scriptControl = $"delete FROM TB_PayaSourceControl where PayaSourceDetail_ID in " +
                    $"(select ID from TB_PayaSourceDetail where PayaSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                    $"and RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex})";
                _dapperRepository.ExecQuery(scriptControl);
            }

            return responseQueue;
        }

        private async Task<bool> IsDone(long sourceHeaderId)
        {
            var count = await _dapperRepository.QueryAsync<int>($"SELECT COUNT(ID)  FROM TB_BatchProcessQueue where SourceHeader_ID = {sourceHeaderId} and Status!= 0 and RequestType = {(int)RequestType.Paya}");
            return count.FirstOrDefault() == 0;
        }

        private async Task<bool> UpdateStatusSourceHeader(long Id, GenericEnum status)
        {
            var script = $"Update H set Status = {(int)status} " +
                 $"from TB_PayaSourceHeader H where ID={Id}";
            await _dapperRepository.QueryAsync<PayaSourceHeader>(script);
            return true;
        }

        /// <summary>
        /// پردازش بر روی لیست دریافتی مشتریان
        /// </summary>
        /// <param name="PayaResponses"></param>
        /// <returns></returns>
        private async Task<bool> ProcessInPayaSourceDetail(IEnumerable<PayaSourceDetail> PayaResponses)
        {
            try
            {
                IList<PayaSourceDetail> lstPayaSourceDetail = new List<PayaSourceDetail>();
                IList<PayaSourceControl> lstPayaSourceControl = new List<PayaSourceControl>();

                long totalControlledCorrectAmount = 0;
                long totalControlledWrongAmount = 0;

                foreach (var response in PayaResponses)
                {
                    var result = await Validate(response);
                    PayaSourceDetail PayaSourceDetail = new PayaSourceDetail()
                    {
                        Id = response.Id,
                        Amount = response.Amount,
                        PayaSourceHeaderId = response.PayaSourceHeaderId,
                        BeneficiaryName = response.BeneficiaryName,
                        DestinationIban = response.DestinationIban,
                        Created = response.Created,
                        CreatedBy = response.CreatedBy,
                        ErrorDescription = null,
                        LastModified = DateTime.Now,
                        LastModifiedBy = "PayaChecker",
                        RowIdentifier = response.RowIdentifier,
                        Status = GenericEnum.Success,
                        MappingStatus = response.MappingStatus,
                    };
                    lstPayaSourceDetail.Add(PayaSourceDetail);

                    PayaSourceControl payaSourceControl = new PayaSourceControl()
                    {
                        AmountVerified = result.AmountVerified,
                        PayaSourceDetailId = response.Id,
                        DestinationAccountStatus = result.DestinationAccountStatus,
                        DestinationBankCode = result.DestinationBankCode,
                        DestinationBankName = result.DestinationBankName,
                        DestinationAccountStatusDesc = result.DestinationAccountStatusDesc,
                        DestinationIbanVerified = result.DestinationIbanVerified,
                        OwnerFirstName = result.OwnerFirstName,
                        OwnerLastName = result.OwnerFirstName,
                        Created = DateTime.Now,
                        CreatedBy = "PayaChecker",
                        HasError = result.HasError,
                    };
                    lstPayaSourceControl.Add(payaSourceControl);

                    if (result.HasError == false)
                        totalControlledCorrectAmount = totalControlledCorrectAmount + response.Amount;
                    else
                        totalControlledWrongAmount = totalControlledWrongAmount + response.Amount;
                }

                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                _dapperRepository.BulkUpdate<PayaSourceDetail>(lstPayaSourceDetail);
                _logger.Information($"PayaSourceDetail BulkUpdateCount: {lstPayaSourceDetail.Count()}");

                _dapperRepository.BulkInsert<PayaSourceControl>(lstPayaSourceControl);
                _logger.Information($"PayaSourceControl BulkInsertCount: {lstPayaSourceControl.Count()}");

                await UpdatePayaSourceHeader(PayaResponses, lstPayaSourceControl, totalControlledCorrectAmount, totalControlledWrongAmount);
                _dapperUnitOfWork.Commit();

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                var message = ex.Message;
                _logger.ErrorLog($"PayaChecker.ProcessInPayaSourceDetail Exception",ex);
                return await Task.FromResult(false);
            }
        }

        private async Task UpdatePayaSourceHeader(IEnumerable<PayaSourceDetail> PayaResponses, IList<PayaSourceControl> lstPayaSourceControl, long totalControlledCorrectAmount, long totalControlledWrongAmount)
        {
            int controlledCorrectCount = lstPayaSourceControl.Where(c => c.HasError == false).Count();
            int controlledWrongCount = lstPayaSourceControl.Where(c => c.HasError == true).Count(); ;
            var PayaSourceHeaderId = PayaResponses.Select(c => c.PayaSourceHeaderId).FirstOrDefault();
            var script = $"Update C set controlledCorrectCount = controlledCorrectCount + {controlledCorrectCount} ," +
                         $"controlledWrongCount = controlledWrongCount + {controlledWrongCount} ," +
                         $"totalControlledCorrectAmount = totalControlledCorrectAmount + {totalControlledCorrectAmount} ," +
                         $"totalControlledWrongAmount = totalControlledWrongAmount + {totalControlledWrongAmount} " +
                         $"from TB_PayaSourceHeader C where ID={PayaSourceHeaderId} ";
            lock (this)
            {
                var PayaSourceHeader = _dapperRepository.Query<PayaSourceHeader>(script);
            }
            await Task.Delay(0);
        }

        /// <summary>
        /// صحت سنجی اطلاعات مشتری
        /// </summary>
        /// <param name="PayaSourceDetail"></param>
        /// <param name="accountResponseCdc"></param>
        /// <param name="PayaResponseCdc"></param>
        /// <returns></returns>
        private async Task<PayaSourceDetailResult> Validate(PayaSourceDetail payaSourceDetail)
        {
            try
            {
                PayaSourceDetailResult payaSourceDetailResult = new PayaSourceDetailResult();
                
                var ibanInquiryResponse = await _galaxyApi.GetIbanInquiry(payaSourceDetail.DestinationIban);
                if (ibanInquiryResponse.resultCode != "0")
                    return NotValidate(payaSourceDetail);

                payaSourceDetailResult.AmountVerified = payaSourceDetail.Amount.IsValidateAmount();
                payaSourceDetailResult.DestinationIbanVerified = true;
                payaSourceDetailResult.OwnerFirstName = ibanInquiryResponse.result.owners.FirstOrDefault().firstName;
                payaSourceDetailResult.OwnerLastName = ibanInquiryResponse.result.owners.FirstOrDefault().lastName;
                payaSourceDetailResult.PayaSourceDetailId = payaSourceDetail.Id;
                payaSourceDetailResult.DestinationAccountStatus = ibanInquiryResponse.result.accountStatus;
                payaSourceDetailResult.DestinationAccountStatusDesc = ibanInquiryResponse.result.accountStatusDescription;
                payaSourceDetailResult.DestinationBankName = ibanInquiryResponse.result.iban.bank.name;
                payaSourceDetailResult.DestinationBankCode = ibanInquiryResponse.result.iban.bank.code;
                payaSourceDetailResult.Status = GenericEnum.Success;
                payaSourceDetailResult.HasError = false;
                return payaSourceDetailResult;
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"PayaChecker.Validate Exception ",ex);
                return NotValidate(payaSourceDetail);
            }
        }

        private static PayaSourceDetailResult NotValidate(PayaSourceDetail payaSourceDetail)
        {
            return new PayaSourceDetailResult
            {
                AmountVerified = false,
                DestinationIbanVerified = false,
                OwnerFirstName = null,
                OwnerLastName = null,
                PayaSourceDetailId = payaSourceDetail.Id,
                DestinationAccountStatus = null,
                DestinationAccountStatusDesc = null,
                DestinationBankName = null,
                DestinationBankCode = null,
                Status = GenericEnum.Error,
                HasError = true,
            };
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
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1) TryCount " +
                 $" from TB_BatchProcessQueue Q join TB_PayaSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                 $" (Q.Status = {(int)GenericEnum.Pending} or  Q.Status = {(int)GenericEnum.InQueue}) and RequestType = {(int)RequestType.Paya} " +
                 $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"Missed: GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        #endregion
    }
}
