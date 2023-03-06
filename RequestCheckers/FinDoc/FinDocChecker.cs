using Microsoft.Extensions.Configuration;
using BatchChecker.Business.AccountCustomer;
using BatchChecker.Business.DeathStatus;
using BatchChecker.Business.HasError;
using BatchChecker.Dto;
using BatchChecker.Dto.Account;
using BatchChecker.Dto.Customer;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOP.Domain.Entities;
using BatchOP.Domain.Entities.Customer;
using BatchOP.Domain.Enums;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using BatchChecker.FetchCDC;
using BatchChecker.Dto.FetchCDC;
using BatchOp.Infrastructure.Shared.ApiCaller.Galaxy;
using Serilog;
using Polly;
using BatchOp.Infrastructure.Shared.ApiCaller.Dto;
using Polly.Retry;
using BatchChecker.Business.CustomerType;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces;
using BatchOp.Application.Interfaces.Repositories;
using BatchOP.Domain.Entities.FinDoc;
using BatchChecker.Dto.FetchServiceForFinDoc;
using BatchChecker.Dto.Share;
using BatchChecker.Dto.FetchGalaxy;
using BatchChecker.FetchGalaxy;
using BatchChecker.FetchQlickView;
using BatchChecker.Dto.FetchQlickView;
using BatchChecker.Extensions;

namespace BatchChecker.RequestCheckers
{
    public class FinDocCheker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICDCRepository _cDCRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IFetchFromCDC _fetchFromCDC;
        private readonly IGalaxyApi _galaxyApi;
        private readonly IFetchFromGalaxy _fetchFromGalaxy;
        private readonly IFetchFromQlickView _fetchFromQlickView;

        public FinDocCheker(
             ILogger logger,
            IDapperRepository dapperRepository,
            ICDCRepository cDCRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration,
            IFetchFromCDC fetchFromCDC,
            IGalaxyApi galaxyApi,
            IFetchFromGalaxy fetchFromGalaxy,
            IFetchFromQlickView fetchFromQlickView
            )
        {
            _logger = logger;
            _dapperRepository = dapperRepository;
            _cDCRepository = cDCRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
            _fetchFromCDC = fetchFromCDC;
            _galaxyApi = galaxyApi;
            _fetchFromGalaxy = fetchFromGalaxy;
            _fetchFromQlickView = fetchFromQlickView;
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
                               $" from TB_BatchProcessQueue Q join TB_FinDocSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                               $" (Q.Status = {(int)GenericEnum.Unasign}) and RequestType = {(int)RequestType.FinDoc} " +
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
        private async Task<bool> UpdateFinDocSourceHeaderAsync(long Id, int processIndex, CancellationToken stoppingToken)
        {
            var script = $"Update C set Status = {(int)GenericEnum.Success} , ProcessIndex = {processIndex} , ProcessDate = '{DateTime.Now}' " +
                $"from TB_FinDocSourceHeader C where ID={Id}";
            lock (this)
            {
                var finDocSourceHeader = _dapperRepository.Query<FinDocSourceHeader>(script);
            }
            return await Task.FromResult(true);
        }
        /// <summary>
        /// ثبت درخواست ها با ایندکس در تیبل صف
        /// </summary>
        /// <param name="customerResponses"></param>
        /// <returns></returns>
        private async Task<bool> InsertToBatchProcessQueueDetailAsync(IEnumerable<SourceHeaderDto> customerResponses, CancellationToken stoppingToken)
        {
            foreach (var item in customerResponses)
            {
                var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                var batchCount = rabbitMQConfiguration.FinDocBatchCount;

                int startIndex = 0;
                int endIndex = 0;

                for (int i = 1; endIndex >= batchCount * (i - 1); i++)
                {
                    startIndex = (i - 1) * batchCount + 1;
                    endIndex = item.RequestCount < i * batchCount ? endIndex = item.RequestCount : endIndex = i * batchCount;

                    BatchProcessQueue batchProcessQueueDetail = new BatchProcessQueue()
                    {
                        SourceHeaderId = item.SourceHeaderId,
                        StartIndex = startIndex,
                        EndIndex = endIndex,
                        Status = GenericEnum.Unasign,
                        RequestType = RequestType.FinDoc,
                        TryCount = 0,
                    };

                    if (FindBatchProccessQueue(batchProcessQueueDetail) == 0)
                    {
                        _dapperRepository.Add(batchProcessQueueDetail);
                        _logger.Information($"InsertToBatchProcessQueueDetail : {JsonConvert.SerializeObject(batchProcessQueueDetail)}");
                        await UpdateFinDocSourceHeaderAsync(item.SourceHeaderId, item.RequestCount, stoppingToken);
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
                                    $"and RequestType = {(int)RequestType.FinDoc} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);

            return queryResponse.Count();
        }

        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderFinDoc");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R " +
                $"INNER JOIN TB_FinDocSourceHeader H On R.ID = H.Request_ID " +
                $"join TB_FinDocSourceDetail det on h.id = det.FinDocSourceHeader_ID " +
                $"WHERE H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderFinDoc Count:{queryResponse.Count()}");

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
                IEnumerable<BatchProcessQueue> responseQueue = CheckInFinDocBatchProcessQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"select C.FinDocSourceHeader_ID as FinDocSourceHeaderId , C.* from TB_FinDocSourceDetail C " +
                                           $" join TB_FinDocSourceHeader H on H.ID = C.FinDocSourceHeader_ID " +
                                           $" where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                                           $" and C.FinDocSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                                           $" and C.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
                // $"and C.Status != {(int)GenericEnum.Success}";

                IEnumerable<FinDocSourceDetail> responseDetails;
                responseDetails = _dapperRepository.Query<FinDocSourceDetail>(scriptDetail);
                _logger.Information($"GetListFinDocSourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;
                result = await ProcessInFinDocSourceDetail(responseDetails);
                if (result)
                {
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Success, 2);
                    await ProcessWage(batchProcess.SourceHeaderId, cancellationToken);
                }
                else
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);


                // await Task.Delay(100);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"CustomerChecker.BatchProcess Exception",ex);
                return await Task.FromResult(true);
            }

        }
        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private IEnumerable<BatchProcessQueue> CheckInFinDocBatchProcessQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or Status = {(int)GenericEnum.InQueue} or Status = {(int)GenericEnum.Unasign})";
            var responseQueue = _dapperRepository.Query<BatchProcessQueue>(scriptQueue);
            if (responseQueue.Any())
            {
                var scriptControl = $"delete FROM TB_FinDocDetailControl where FinDocSourceDetail_ID in " +
                    $"(select ID from TB_FinDocSourceDetail where FinDocSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                    $"and RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex})";
                _dapperRepository.ExecQuery(scriptControl);
            }

            return responseQueue;
        }

        /// <summary>
        /// پردازش بر روی لیست دریافتی مشتریان
        /// </summary>
        /// <param name="customerResponses"></param>
        /// <returns></returns>
        private async Task<bool> ProcessInFinDocSourceDetail(IEnumerable<FinDocSourceDetail> finDocSourceDetails)
        {
            try
            {
                IList<FinDocDetailControl> lstFinDocDetailControl = new List<FinDocDetailControl>();
                var nacAccountList = await GetListNacAccount(finDocSourceDetails);
                var mappingAccountList = await GetListMappingAccounts(finDocSourceDetails);
                var mappingAccountHeadingList = await GetListMappingAccountHeadings(finDocSourceDetails);

                foreach (var itemDetail in finDocSourceDetails)
                {
                    var finDocDetailControl = await GetMappingAccount(nacAccountList, mappingAccountList, itemDetail);
                    finDocDetailControl.NickName = await GetNickName(mappingAccountHeadingList, finDocDetailControl, itemDetail);
                    finDocDetailControl.Status = GenericEnum.Success;
                    finDocDetailControl.Created = DateTime.Now;
                    finDocDetailControl.CreatedBy = "System";
                    finDocDetailControl.FinDocSourceDetailId = itemDetail.Id;

                    lstFinDocDetailControl.Add(finDocDetailControl);

                }

                _dapperRepository.BulkInsert<FinDocDetailControl>(lstFinDocDetailControl);
                _logger.Information($"FinDocDetailControl BulkInsertCount: {lstFinDocDetailControl.Count()}");
                // await UpdateCustomerSourceHeader(customerResponses, lstFinDocDetailControl);
                _dapperUnitOfWork.Commit();

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                var message = ex.Message;
                _logger.ErrorLog($"FinDocChecker.ProcessInFinDocSourceDetail Exception",ex);
                return await Task.FromResult(false);
            }
        }

        private async Task UpdateCustomerSourceHeader(IEnumerable<CustomerSourceDetail> customerResponses, IList<CustomerDetailControl> lstCustomerDetailControl, long totalControlledCorrectAmount, long totalControlledWrongAmount)
        {
            int controlledCorrectCount = lstCustomerDetailControl.Where(c => c.HasError == false).Count();
            int controlledWrongCount = lstCustomerDetailControl.Where(c => c.HasError == true).Count(); ;
            var customerSourceHeaderId = customerResponses.Select(c => c.CustomerSourceHeaderId).FirstOrDefault();
            var script = $"Update C set controlledCorrectCount = controlledCorrectCount + {controlledCorrectCount} ," +
                         $"controlledWrongCount = controlledWrongCount + {controlledWrongCount} ," +
                         $"totalControlledCorrectAmount = totalControlledCorrectAmount + {totalControlledCorrectAmount} ," +
                         $"totalControlledWrongAmount = totalControlledWrongAmount + {totalControlledWrongAmount} " +
                         $"from TB_CustomerSourceHeader C where ID={customerSourceHeaderId} ";
            lock (this)
            {
                var customerSourceHeader = _dapperRepository.Query<CustomerSourceHeader>(script);
            }
            await Task.Delay(0);
        }

        /// <summary>
        /// صحت سنجی اطلاعات مشتری
        /// </summary>
        /// <param name="customerSourceDetail"></param>
        /// <param name="accountResponseCdc"></param>
        /// <param name="customerResponseCdc"></param>
        /// <returns></returns>
        private async Task<CustomerSourceDetailResult> Validate(CustomerSourceDetail customerSourceDetail, IEnumerable<AccountResponseCdcDto> accountResponseCdc, IEnumerable<CustomerResponseCdcDto> customerResponseCdc)
        {
            try
            {
                CustomerSourceDetailResult customerSourceDetailResult = new CustomerSourceDetailResult();
                #region صحت حساب بانک تجارت
                customerSourceDetailResult.AccVerified = customerSourceDetail.AccountNumber.IsValidateTjbAccDigit();
                #endregion

                #region صحت تعداد ارقام مبلغ
                customerSourceDetailResult.AmountVerified = customerSourceDetail.Amount.IsValidateAmount();
                #endregion

                #region صحت شناسه واریز
                if (string.IsNullOrWhiteSpace(customerSourceDetail.DepositIdentifier))
                    customerSourceDetailResult.DepositeVerified = true;
                else
                    customerSourceDetailResult.DepositeVerified = false;
                #endregion

                #region نوع ارز- نوع حساب - وضعیت حساب
                var accountResponse = accountResponseCdc.Where(a => a.LEGACY_ACCOUNT_NUMBER == customerSourceDetail.AccountNumber).FirstOrDefault();
                if (accountResponse != null)
                {
                    var checkAccountCustomer = ConfigValidateChain(customerSourceDetailResult);
                    checkAccountCustomer.execute(accountResponse);
                }
                else
                {
                    customerSourceDetailResult.CurrencyType = CurrencyTypeStatus.Undefined;
                    customerSourceDetailResult.AccType = AccountTypeStatus.Undefined;
                    customerSourceDetailResult.AccStatus = BatchOP.Domain.Enums.AccountStatus.Undefined;
                }
                #endregion

                #region وضعیت حیات
                if (accountResponse != null)
                    await AbstractDeathStatusHandler(customerResponseCdc, customerSourceDetailResult, accountResponse);
                else
                    customerSourceDetailResult.DeathStatus = DeathStatus.Undefined;

                #endregion

                #region نوع مشتری حقیقی یا حقوقی
                if (accountResponse != null)
                {
                    var customerResponse = customerResponseCdc.Where(c => c.CUSNO == accountResponse.RETCUSNO).FirstOrDefault();
                    var deathStatusHandler = new CustomerTypeStatusHandler(customerSourceDetailResult);
                    deathStatusHandler.execute(customerResponse);
                }
                else
                    customerSourceDetailResult.CustomerType = CustomerTypeStatus.Undefined;
                #endregion

                #region وضعیت افراد بلک لیست
                customerSourceDetailResult.BlackListStatus = BlackListStatus.Undefined;
                #endregion

                #region نتیجه ولیدیشن
                await AbstractHasErrorHandler(customerSourceDetailResult);
                #endregion

                customerSourceDetailResult.Status = GenericEnum.Success;
                return await Task.FromResult(customerSourceDetailResult);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"CustomerChecker.Validate Exception ",ex);
                return await Task.FromResult(new CustomerSourceDetailResult
                {
                    Status = GenericEnum.ServerError,
                    AccStatus = AccountStatus.Undefined,
                    AccType = AccountTypeStatus.Undefined,
                    AccVerified = false,
                    AmountVerified = false,
                    BlackListStatus = BlackListStatus.Undefined,
                    CurrencyType = CurrencyTypeStatus.Undefined,
                    CustomerType = CustomerTypeStatus.Undefined,
                    DeathStatus = DeathStatus.Undefined,
                    DepositeVerified = false,
                    HasError = true,
                });
            }
        }

        private async Task AbstractDeathStatusHandler(IEnumerable<CustomerResponseCdcDto> customerResponseCdc, CustomerSourceDetailResult customerSourceDetailResult, AccountResponseCdcDto accountResponse)
        {
            var customerResponse = customerResponseCdc.Where(c => c.CUSNO == accountResponse.RETCUSNO).FirstOrDefault();
            var deathStatusHandler = new DeathStatusHandler(customerSourceDetailResult);
            deathStatusHandler.execute(customerResponse);
            await Task.Delay(0);
        }

        private async Task AbstractHasErrorHandler(CustomerSourceDetailResult customerSourceDetailResult)
        {
            var hasErrorHandler = new HasErrorHandler(customerSourceDetailResult);
            hasErrorHandler.execute(customerSourceDetailResult);
            await Task.Delay(0);
        }

        private AbstractAccountCustomerHandler ConfigValidateChain(CustomerSourceDetailResult customerSourceDetailResult)
        {
            var accuracyHandler = new AccuracyHandler(customerSourceDetailResult);
            var accountTypeHandler = new AccountTypeHandler(customerSourceDetailResult);
            var accountStatusHandler = new AccountStatusHandler(customerSourceDetailResult);
            accuracyHandler.SetSuccessor(accountTypeHandler);
            accountTypeHandler.SetSuccessor(accountStatusHandler);
            return accuracyHandler;
        }

        /// <summary>
        /// واکشی لیست حسابها از CDC
        /// </summary>
        /// <param name="accountNumberCdcs"></param>
        /// <returns></returns>
        private async Task<IEnumerable<FinDocSourceDetailResult>> GetListCDCAccount(IEnumerable<FinDocSourceDetail> finDocResponses)
        {

            IList<RequestFinDocDto> requestCdcDto = new List<RequestFinDocDto>();
            //foreach (var item in customerResponses)
            //{
            //    requestCdcDto.Add(new RequestCdcDto() { LEGACY_ACCOUNT_NUMBER = item.AccountNumber });
            //}
            var result = Enumerable.Empty<FinDocSourceDetailResult>(); //await _fetchFromCDC.GetListCDCAccount(requestCdcDto);
            return result;
        }

        /// <summary>
        /// واکشی لیست حسابها از کهکشان
        /// </summary>
        /// <param name="finDocResponses"></param>
        /// <returns></returns>
        private async Task<IEnumerable<AccountMappingResponseDto>> GetListMappingAccounts(IEnumerable<FinDocSourceDetail> finDocResponses)
        {

            IList<AccountMappingRequestDto> requestDtoList = new List<AccountMappingRequestDto>();
            foreach (var item in finDocResponses)
            {
                requestDtoList.Add(new AccountMappingRequestDto() { LagacyAccountNumber = item.AccountNumber });
            }
            var result = await _fetchFromGalaxy.GetListMappedAccounts(requestDtoList);
            return result;
        }
        /// <summary>
        /// واکشی AccountHeading از کهکشان
        /// </summary>
        /// <param name="finDocResponses"></param>
        /// <returns></returns>
        private async Task<IEnumerable<AccountHeadingMappingResponseDto>> GetListMappingAccountHeadings(IEnumerable<FinDocSourceDetail> finDocResponses)
        {

            IList<AccountMappingRequestDto> requestDtoList = new List<AccountMappingRequestDto>();
            foreach (var item in finDocResponses)
            {
                requestDtoList.Add(new AccountMappingRequestDto() { LagacyAccountNumber = item.AccountNumber });
            }
            var result = await _fetchFromGalaxy.GetListNickName(requestDtoList);
            return result;
        }
        /// <summary>
        /// واکشی لیست حسابها از QlickView
        /// </summary>
        /// <param name="finDocResponses"></param>
        /// <returns></returns>
        private async Task<IEnumerable<NacAccountResponseDto>> GetListNacAccount(IEnumerable<FinDocSourceDetail> finDocResponses)
        {

            IList<AccountMappingRequestDto> requestDtoList = new List<AccountMappingRequestDto>();
            foreach (var item in finDocResponses)
            {
                requestDtoList.Add(new AccountMappingRequestDto() { LagacyAccountNumber = item.AccountNumber });
            }

            var nacResponse = await _fetchFromQlickView.GetListNacAccount(requestDtoList);
            return await Task.FromResult(nacResponse);
        }
        private async Task<FinDocDetailControl> GetMappingAccount(IEnumerable<NacAccountResponseDto> NacAccountList, IEnumerable<AccountMappingResponseDto> MappingAccountList, FinDocSourceDetail sourceDetail)
        {

            FinDocDetailControl finDocDetailControl = new FinDocDetailControl();
            var controllItems = MappingAccountList.Where(x => x.LEGACY_ACCOUNT_NUMBER == sourceDetail.AccountNumber).ToList();
            var nacAccountDto = NacAccountList.Where(p => p.ACNT_NO == sourceDetail.AccountNumber).FirstOrDefault();
            if (nacAccountDto == null)
            {
                finDocDetailControl.AccountNumber = null;
                finDocDetailControl.HostStatus = HostStatus.Undefined;
                finDocDetailControl.AccountDetection = AccountDetection.Undefined;
            }
            else if (nacAccountDto.GRP < 20)
            {
                //GL ei
                var controllItem = controllItems.Where(p => p.ACCOUNT_NUMBER == null && p.IS_HEADING == 1).FirstOrDefault();
                if (controllItem != null)
                {
                    //host
                    finDocDetailControl.AccountHeading = controllItem.ACCOUNT_HEADING;
                    finDocDetailControl.HostStatus = HostStatus.ServicesHost;
                    finDocDetailControl.AccountDetection = AccountDetection.GL;
                }
                else
                {
                    finDocDetailControl.AccountHeading = null;
                    finDocDetailControl.HostStatus = HostStatus.CurrentHost;
                    finDocDetailControl.AccountDetection = AccountDetection.GL;
                }

            }
            else
            {
                //Shakhsi
                var controllItem = controllItems.Where(p => p.ACCOUNT_NUMBER != null && p.IS_HEADING == 0).FirstOrDefault();
                if (controllItem != null)
                {
                    //host
                    finDocDetailControl.AccountNumber = controllItem.ACCOUNT_NUMBER;
                    finDocDetailControl.HostStatus = HostStatus.ServicesHost;
                    finDocDetailControl.AccountDetection = AccountDetection.Space;
                }
                else
                {
                    finDocDetailControl.AccountNumber = null;
                    finDocDetailControl.HostStatus = HostStatus.CurrentHost;
                    finDocDetailControl.AccountDetection = AccountDetection.Space;
                }
            }
            return await Task.FromResult(finDocDetailControl);
        }
        private async Task<string> GetNickName(IEnumerable<AccountHeadingMappingResponseDto> MappingAccountList, FinDocDetailControl detailControl, FinDocSourceDetail itemDetail)
        {

            if (detailControl.AccountDetection == AccountDetection.GL && detailControl.HostStatus == HostStatus.ServicesHost)
            {
                var mappingAccountList = MappingAccountList.Where(p => p.LEGACY_ACCOUNT_NUMBER == itemDetail.AccountNumber).FirstOrDefault();
                if (mappingAccountList != null)
                    return await Task.FromResult(mappingAccountList.ISC_GL_NICK_NAME);
            }
            return await Task.FromResult(string.Empty);
        }
        /// <summary>
        /// واکشی لیست مشتریان از CDC
        /// </summary>
        /// <param name="accountResponseCdcs"></param>
        /// <returns></returns>
        private async Task<IEnumerable<CustomerResponseCdcDto>> GetListCDCCUSTOMER(IEnumerable<AccountResponseCdcDto> accountResponseCdcs)
        {
            IList<RequestCdcDto> requestCdcDto = new List<RequestCdcDto>();
            foreach (var item in accountResponseCdcs)
            {
                requestCdcDto.Add(new RequestCdcDto() { LEGACY_ACCOUNT_NUMBER = item.LEGACY_ACCOUNT_NUMBER, RETCUSNO = item.RETCUSNO });
            }
            var lstCDCCustomers = await _fetchFromCDC.GetListCDCCustomerByRETCUSNO(requestCdcDto);
            return lstCDCCustomers;
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
        /// <summary>
        /// CustomerSourceDetail آپدیت تیبل 
        /// </summary>
        /// <param name="customerResponses"></param>
        /// <returns></returns>
        private async Task<bool> RollBackUpdateCustomerSourceDetail(IEnumerable<CustomerSourceDetail> customerResponses)
        {
            IList<CustomerSourceDetail> lstCustomerSourceDetail = new List<CustomerSourceDetail>();
            foreach (var response in customerResponses)
            {
                CustomerSourceDetail customerSourceDetail = new CustomerSourceDetail()
                {
                    Id = response.Id,
                    AccountNumber = response.AccountNumber,
                    Amount = response.Amount,
                    CustomerSourceHeaderId = response.CustomerSourceHeaderId,
                    DepositIdentifier = response.DepositIdentifier,
                    RowIdentifier = response.RowIdentifier,
                    Status = GenericEnum.Unasign,
                    MappingStatus = response.MappingStatus,
                };
                lstCustomerSourceDetail.Add(customerSourceDetail);
            }
            _dapperRepository.BulkUpdate<CustomerSourceDetail>(lstCustomerSourceDetail);
            _logger.Information($"RollBack CustomerSourceDetail BulkUpdateCount: {lstCustomerSourceDetail.Count()}");
            return await Task.FromResult(true);
        }

        /// <summary>
        /// پردازش محاسبه‌ی کارمزد پس از اتمام هر فایل
        /// </summary>
        /// <param name="CustomerSourceHeaderId"></param>
        /// <returns></returns>
        private async Task ProcessWage(long CustomerSourceHeaderId, CancellationToken cancellationToken)
        {
            try
            {
                string script = $"select Count(Id) " +
                    $"FROM TB_CustomerSourceHeader S " +
                    $"WHERE S.ID = {CustomerSourceHeaderId} " +
                    $"And (S.ControlledCorrectCount + S.ControlledWrongCount = S.NumberOfRecords) ";
                var result = _dapperRepository.Query<string>(script);
                int finished = Convert.ToInt32(result.FirstOrDefault());
                if (finished != 0)
                {
                    Request Request = _dapperRepository.Query<Request>
                        ($"SELECT T.ID, CustomerAccountNumber, T.BranchCode " +
                        $"FROM TB_Request T " +
                        $"INNER JOIN TB_CustomerSourceHeader H on T.ID = H.Request_ID " +
                        $"WHERE H.ID = {CustomerSourceHeaderId}").FirstOrDefault();
                    long correctAmount = Convert.ToInt32(_dapperRepository.Query<string>
                        ($"SELECT TotalControlledCorrectAmount " +
                        $"FROM TB_CustomerSourceHeader " +
                        $"WHERE ID = {CustomerSourceHeaderId}").FirstOrDefault());
                    string branchCode = new string('0', 5 - Request.BranchCode.ToString().Length) + Request.BranchCode.ToString();
                    //ToDo : Daryafte ServiceTypeCode ha

                    var WageInquiryResponseDto = await CallWageApi(Request.CustomerAccountNumber, correctAmount, branchCode, cancellationToken);

                    if (WageInquiryResponseDto.ResultCode == 0)
                    {
                        var updateScript = $"Update TB_CustomerSourceHeader " +
                            $"SET TotalWageAmount ={WageInquiryResponseDto.wageAmount}, TotalWageDiscountAmount ={WageInquiryResponseDto.discountAmount} " +
                            $"WHERE Id ={CustomerSourceHeaderId} ";
                    }
                }
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"CustomerChecker.ProcessWage Exception",ex);
            }
        }
        private async Task<WageInquiryResponseDto> CallWageApi(string accountNumber, long amount, string branchCode, CancellationToken cancellationToken)
        {
            WageInquiryResponseDto wageInquiryResponseDto = new WageInquiryResponseDto();

            var retryPolicy = Policy.HandleResult<WageInquiryResponseDto>(s => s.ResultCode != 0)
                .Or<Exception>()
                .WaitAndRetryAsync(2,
                sleepDurationProvider: (retryCount) => TimeSpan.FromMilliseconds(500 * retryCount),
                onRetry: (response, delay, retryCount, ctx) =>
                {
                    _logger.Warning($"Retry calling WageApi - Recieved response {JsonConvert.SerializeObject(response.Result)} Or having Exception {response.Exception}");
                });

            var fallbackPolicy = Policy.HandleResult<WageInquiryResponseDto>(s => s.ResultCode != 0)
                .Or<Exception>()
                .FallbackAsync(
                fallbackAction: async (context, token) => await Task.FromResult(wageInquiryResponseDto),
                onFallbackAsync: async (result, context) =>
                {
                    wageInquiryResponseDto = await DoFallback(result.Result, cancellationToken);
                    _logger.Error($"fallBack calling WageApi - Recieved response {JsonConvert.SerializeObject(result.Result)} Or having Exception {result.Exception}");
                });

            var wrapPolicy = Policy.WrapAsync(fallbackPolicy, retryPolicy);
            WageInquiryResponseDto result = await wrapPolicy.ExecuteAsync(async () => await _galaxyApi.GetWage(accountNumber, amount, branchCode, "129", cancellationToken));
            return result;

        }
        private async Task<WageInquiryResponseDto> DoFallback(WageInquiryResponseDto result, CancellationToken cancellationToken)
        {
            if (result == null)
                result = new WageInquiryResponseDto { ResultCode = -1 };
            return await Task.FromResult(result);
        }
        #endregion

        #region MissedSender
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken)
        {
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount , RequestType " +
                            $" from TB_BatchProcessQueue Q join TB_FinDocSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                            $" (Q.Status = {(int)GenericEnum.Pending} or  Q.Status = {(int)GenericEnum.InQueue})  and RequestType = {(int)RequestType.FinDoc} " +
                            $"and  (C.Status != {(int)GenericEnum.Deleted} or C.Status != {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"Missed: GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        #endregion
    }
}
