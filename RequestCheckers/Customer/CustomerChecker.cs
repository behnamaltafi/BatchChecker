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
using BatchChecker.Business.CustomerType;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces;
using BatchOp.Application.Interfaces.Repositories;
using BatchChecker.Dto.Share;
using BatchChecker.Extensions;

namespace BatchChecker.RequestCheckers
{
    public class CustomerChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICDCRepository _cDCRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IFetchFromCDC _fetchFromCDC;
        private readonly IGalaxyApi _galaxyApi;

        public CustomerChecker(
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
                               $" from TB_BatchProcessQueue Q join TB_CustomerSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                               $" (Q.Status = {(int)GenericEnum.Unasign}) and RequestType = {(int)RequestType.Customer} " +
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
        private async Task<bool> UpdateCustomerSourceHeaderAsync(long Id, int processIndex, CancellationToken stoppingToken)
        {
            var script = $"Update C set Status = {(int)GenericEnum.Success} , ProcessIndex = {processIndex} , ProcessDate = '{DateTime.Now}' " +
                $"from TB_CustomerSourceHeader C where ID={Id}";
            lock (this)
            {
                var customerSourceHeader = _dapperRepository.Query<CustomerSourceHeader>(script);
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
                var batchCount = rabbitMQConfiguration.CustomerBatchCount;

                if (item.RequestCount < batchCount)
                {
                    BatchProcessQueue batchProcessQueueDetail = new BatchProcessQueue()
                    {
                        SourceHeaderId = item.SourceHeaderId,
                        StartIndex = 1,
                        EndIndex = item.RequestCount,
                        Status = GenericEnum.Unasign,
                        RequestType = RequestType.Customer,
                        TryCount = 0,
                    };
                    _dapperRepository.Add(batchProcessQueueDetail);
                    _logger.Information($"InsertToBatchProcessQueueDetail : {JsonConvert.SerializeObject(batchProcessQueueDetail)}");
                    await UpdateCustomerSourceHeaderAsync(item.SourceHeaderId, item.RequestCount, stoppingToken);
                }
                else
                {
                    int h = 1;
                    int Startindex = 0;
                    int Endindex = 0;

                    for (int i = batchCount; i < item.RequestCount; i = batchCount * h)
                    {
                        Startindex = Endindex + 1;
                        Endindex = i;
                        Console.WriteLine($"RequestCheckers-Startindex:{Startindex}");
                        Console.WriteLine($"RequestCheckers-Endindex:{Endindex}");
                        h++;

                        BatchProcessQueue batchProcessQueueDetail = new BatchProcessQueue()
                        {
                            SourceHeaderId = item.SourceHeaderId,
                            StartIndex = Startindex,
                            EndIndex = Endindex,
                            Status = GenericEnum.Unasign,
                            RequestType = RequestType.Customer,
                            TryCount = 0,
                        };
                        _dapperRepository.Add(batchProcessQueueDetail);
                        _logger.Information($"InsertToBatchProcessQueueDetail : {JsonConvert.SerializeObject(batchProcessQueueDetail)}");
                    }

                    BatchProcessQueue batchProcessQueueDetail2 = new BatchProcessQueue()
                    {
                        SourceHeaderId = item.SourceHeaderId,
                        StartIndex = Endindex + 1,
                        EndIndex = item.RequestCount,
                        Status = GenericEnum.Unasign,
                        RequestType = RequestType.Customer,
                        TryCount = 0,
                    };
                    _dapperRepository.Add(batchProcessQueueDetail2);
                    _logger.Information($"InsertToBatchProcessQueueDetail : {JsonConvert.SerializeObject(batchProcessQueueDetail2)}");
                    await UpdateCustomerSourceHeaderAsync(item.SourceHeaderId, item.RequestCount, stoppingToken);
                }
            }
            return await Task.FromResult(true);
        }
        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderCustomers");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R " +
                $"INNER JOIN TB_CustomerSourceHeader H On R.ID = H.Request_ID " +
                $"join TB_CustomerSourceDetail det on h.id = det.CustomerSourceHeader_ID " +
                $"WHERE H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderCustomers Count:{queryResponse.Count()}");

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
                IEnumerable<BatchProcessQueue> responseQueue = CheckInCustomerBatchProcessQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"select C.CustomerSourceHeader_ID as CustomerSourceHeaderId , C.* from TB_CustomerSourceDetail C " +
                                           $" join TB_CustomerSourceHeader H on H.ID = C.CustomerSourceHeader_ID " +
                                           $" where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                                           $" and C.CustomerSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                                           $" and C.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
                // $"and C.Status != {(int)GenericEnum.Success}";

                IEnumerable<CustomerSourceDetail> responseDetails;
                responseDetails = _dapperRepository.Query<CustomerSourceDetail>(scriptDetail);
                _logger.Information($"GetListCustomerSourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;
                result = await ProcessInCustomerSourceDetail(responseDetails);
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
                _logger.ErrorLog($"CustomerChecker.BatchProcess Exception", ex);
                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }

        }
        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private IEnumerable<BatchProcessQueue> CheckInCustomerBatchProcessQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or  Status = {(int)GenericEnum.InQueue} or  Status = {(int)GenericEnum.Unasign})";
            var responseQueue = _dapperRepository.Query<BatchProcessQueue>(scriptQueue);
            if (responseQueue.Any())
            {
                var scriptControl = $"delete FROM TB_CustomerDetailControl where CustomerSourceDetail_ID in " +
                    $"(select ID from TB_CustomerSourceDetail where CustomerSourceHeader_ID = {batchProcess.SourceHeaderId} " +
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
        private async Task<bool> ProcessInCustomerSourceDetail(IEnumerable<CustomerSourceDetail> customerResponses)
        {
            try
            {
                IList<CustomerSourceDetail> lstCustomerSourceDetail = new List<CustomerSourceDetail>();
                IList<CustomerDetailControl> lstCustomerDetailControl = new List<CustomerDetailControl>();

                var lstCDCAccount = await GetListCDCAccount(customerResponses);
                var lstCDCCustomer = await GetListCDCCUSTOMER(lstCDCAccount);

                long totalControlledCorrectAmount = 0;
                long totalControlledWrongAmount = 0;

                foreach (var response in customerResponses)
                {
                    var result = await Validate(response, lstCDCAccount, lstCDCCustomer);
                    CustomerSourceDetail customerSourceDetail = new CustomerSourceDetail()
                    {
                        Id = response.Id,
                        AccountNumber = response.AccountNumber,
                        Amount = response.Amount,
                        CustomerSourceHeaderId = response.CustomerSourceHeaderId,
                        DepositIdentifier = response.DepositIdentifier,
                        RowIdentifier = response.RowIdentifier,
                        Status = result.Status,
                        MappingStatus = response.MappingStatus,
                    };
                    lstCustomerSourceDetail.Add(customerSourceDetail);

                    CustomerDetailControl customerDetailControl = new CustomerDetailControl()
                    {
                        AccStatus = result.AccStatus,
                        AccType = result.AccType,
                        AccVerified = result.AccVerified,
                        AmountVerified = result.AmountVerified,
                        BlackListStatus = result.BlackListStatus,
                        CurrencyType = result.CurrencyType,
                        CustomerSourceDetailId = response.Id,
                        CustomerType = result.CustomerType,
                        DeathStatus = result.DeathStatus,
                        DepositeVerified = result.DepositeVerified,
                        Created = DateTime.Now,
                        CreatedBy = "System",
                        HasError = result.HasError,
                    };
                    lstCustomerDetailControl.Add(customerDetailControl);

                    if (result.HasError == false)
                        totalControlledCorrectAmount = totalControlledCorrectAmount + response.Amount;
                    else
                        totalControlledWrongAmount = totalControlledWrongAmount + response.Amount;
                }

                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                _dapperRepository.BulkUpdate<CustomerSourceDetail>(lstCustomerSourceDetail);
                _logger.Information($"CustomerSourceDetail BulkUpdateCount: {lstCustomerSourceDetail.Count()}");

                _dapperRepository.BulkInsert<CustomerDetailControl>(lstCustomerDetailControl);
                _logger.Information($"CustomerDetailControl BulkInsertCount: {lstCustomerDetailControl.Count()}");

                await UpdateCustomerSourceHeader(customerResponses, lstCustomerDetailControl, totalControlledCorrectAmount, totalControlledWrongAmount);
                _dapperUnitOfWork.Commit();

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                var message = ex.Message;
                _logger.ErrorLog($"CustomerChecker.ProcessInCustomerSourceDetail Exception", ex);
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
                _logger.ErrorLog($"CustomerChecker.Validate Exception", ex);
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
        private async Task<IEnumerable<AccountResponseCdcDto>> GetListCDCAccount(IEnumerable<CustomerSourceDetail> customerResponses)
        {

            IList<RequestCdcDto> requestCdcDto = new List<RequestCdcDto>();
            foreach (var item in customerResponses)
            {
                requestCdcDto.Add(new RequestCdcDto() { LEGACY_ACCOUNT_NUMBER = item.AccountNumber });
            }
            var lstCDCAccounts = await _fetchFromCDC.GetListCDCAccount(requestCdcDto);
            return lstCDCAccounts;
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
                _logger.ErrorLog($"CustomerChecker.ProcessWage Exception", ex);
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
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1) TryCount " +
                 $" from TB_BatchProcessQueue Q join TB_CustomerSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                 $" (Q.Status = {(int)GenericEnum.Pending} or  Q.Status = {(int)GenericEnum.InQueue}) and RequestType = {(int)RequestType.Customer} " +
                 $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"Missed: GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        #endregion
    }
}
