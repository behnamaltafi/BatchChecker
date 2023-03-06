using BatchChecker.Business.AccountCustomer;
using BatchChecker.Business.CustomerType;
using BatchChecker.Business.DeathStatus;
using BatchChecker.Business.HasError;
using BatchChecker.Dto;
using BatchChecker.Dto.Customer;
using BatchChecker.Dto.FetchCDC;
using BatchChecker.Dto.Share;
using BatchChecker.Dto.Staff;
using BatchChecker.Extensions;
using BatchChecker.FetchCDC;
using BatchOp.Application.Interfaces;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOP.Domain.Entities.Customer;
using BatchOP.Domain.Entities.Staff;
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
using System.Transactions;

namespace BatchChecker.RequestCheckers
{
    public class StaffChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICDCRepository _cDCRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IFetchFromCDC _fetchFromCDC;

        public StaffChecker(
             ILogger logger,
            IDapperRepository dapperRepository,
            ICDCRepository cDCRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration,
            IFetchFromCDC fetchFromCDC)
        {
            _logger = logger;
            _dapperRepository = dapperRepository;
            _cDCRepository = cDCRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
            _fetchFromCDC = fetchFromCDC;
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
                $" from TB_BatchProcessQueue Q join TB_StaffSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                $" (Q.Status = {(int)GenericEnum.Unasign}) and RequestType = {(int)RequestType.Staff} " +
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
                $"from TB_StaffSourceHeader S where ID={Id}";
            lock (this)
            {
                var staffSourceHeader = _dapperRepository.Query<StaffSourceHeader>(script);
            }
            return await Task.FromResult(true);
        }
        /// <summary>
        /// ثبت درخواست ها با ایندکس در تیبل صف
        /// </summary>
        /// <param name="staffResponses"></param>
        /// <returns></returns>
        private async Task<bool> InsertToBatchProcessQueueDetailAsync(IEnumerable<SourceHeaderDto> staffResponses, CancellationToken stoppingToken)
        {
            foreach (var item in staffResponses)
            {
                var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                var batchCount = rabbitMQConfiguration.StaffBatchCount;

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
                        RequestType = RequestType.Staff,
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
                                    $"and RequestType = {(int)RequestType.Staff} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);

            return queryResponse.Count();
        }

        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderStaff");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R " +
                $"INNER JOIN TB_StaffSourceHeader H On R.ID = H.Request_ID " +
                $"join TB_StaffSourceDetail det on h.id = det.StaffSourceHeader_ID " +
                $"WHERE R.Status = 2 and H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderStaff Count:{queryResponse.Count()}");

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
                IEnumerable<BatchProcessQueue> responseQueue = CheckInStaffBatchMappingQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"select C.StaffSourceHeader_ID as StaffSourceHeaderId , C.* from TB_StaffSourceDetail C " +
                           $" join TB_StaffSourceHeader H on H.ID = C.StaffSourceHeader_ID " +
                           $"where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                           $" and C.StaffSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                           $" and C.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
                //$" and C.Status = {(int)GenericEnum.Unasign} ";

                IEnumerable<StaffSourceDetail> responseDetails;
                responseDetails = _dapperRepository.Query<StaffSourceDetail>(scriptDetail);
                _logger.Information($"GetListStaffSourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;
                result = await ProcessInSourceDetail(responseDetails);
                if (result)
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Success, 2);
                else
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"StaffChecker.CheckAsync Exception",ex);
                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }

        }
        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private IEnumerable<BatchProcessQueue> CheckInStaffBatchMappingQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or  Status = {(int)GenericEnum.InQueue} or  Status = {(int)GenericEnum.Unasign})";
            var responseQueue = _dapperRepository.Query<BatchProcessQueue>(scriptQueue);

            if (responseQueue.Any())
            {
                var scriptControl = $"delete FROM TB_StaffDetailControl where StaffSourceDetail_ID in " +
                    $"(select ID from TB_StaffSourceDetail where StaffSourceHeader_ID = {batchProcess.SourceHeaderId} " +
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
        private async Task<bool> ProcessInSourceDetail(IEnumerable<StaffSourceDetail> staffResponses)
        {
            try
            {
                IList<StaffSourceDetail> lstSourceDetail = new List<StaffSourceDetail>();
                IList<StaffDetailControl> lstDetailControl = new List<StaffDetailControl>();
                var sourceHeaderId = staffResponses.Select(c => c.StaffSourceHeaderId).FirstOrDefault();

                var lstCDCAccount = await GetListCDCAccount(staffResponses);
                var lstCDCCustomer = await GetListCDCCUSTOMER(lstCDCAccount);

                long totalControlledCorrectAmountCredit = 0;
                long totalControlledCorrectAmountDebit = 0;
                long totalControlledWrongAmountCredit = 0;
                long totalControlledWrongAmountDebit = 0;
                int controlledCorrectCountCredit = 0;
                int controlledCorrectCountDebit = 0;
                int controlledWrongCountCredit = 0;
                int controlledWrongCountDebit = 0;

                foreach (var response in staffResponses)
                {
                    var result = await Validate(response, lstCDCAccount, lstCDCCustomer);
                    StaffSourceDetail sourceDetail = new StaffSourceDetail()
                    {
                        Id = response.Id,
                        AccountNumber = response.AccountNumber,
                        Amount = response.Amount,
                        StaffSourceHeaderId = response.StaffSourceHeaderId,
                        RowIdentifier = response.RowIdentifier,
                        Status = result.Status,
                        MappingStatus = response.MappingStatus,
                        ArticleDesc = response.ArticleDesc,
                        ArticleNumber = response.ArticleNumber,
                        Date = response.Date,
                        DebitCreditType = response.DebitCreditType,
                        OperationCode = response.OperationCode,
                    };
                    lstSourceDetail.Add(sourceDetail);

                    StaffDetailControl detailControl = new StaffDetailControl()
                    {
                        AccStatus = result.AccStatus,
                        AccType = result.AccType,
                        AccVerified = result.AccVerified,
                        AmountVerified = result.AmountVerified,
                        CurrencyType = result.CurrencyType,
                        StaffSourceDetailId = response.Id,
                        CustomerType = result.CustomerType,
                        DeathStatus = result.DeathStatus,
                        DepositeVerified = result.DepositeVerified,
                        HasError = result.HasError,
                        Created = DateTime.Now,
                        CreatedBy = "System",
                    };
                    lstDetailControl.Add(detailControl);

                    if (result.HasError == false && response.DebitCreditType == 1)
                    {
                        totalControlledCorrectAmountCredit += response.Amount;
                        controlledCorrectCountCredit += 1;
                    }
                    else if (result.HasError == false && response.DebitCreditType == 0)
                    {
                        totalControlledCorrectAmountDebit += response.Amount;
                        controlledCorrectCountDebit += 1;
                    }
                    else if (result.HasError == true && response.DebitCreditType == 1)
                    {
                        totalControlledWrongAmountCredit += response.Amount;
                        controlledWrongCountCredit += 1;
                    }
                    else if (result.HasError == true && response.DebitCreditType == 0)
                    {
                        totalControlledWrongAmountDebit += response.Amount;
                        controlledWrongCountDebit += 1;
                    }

                }
                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                _dapperRepository.BulkUpdate<StaffSourceDetail>(lstSourceDetail);
                _logger.Information($"StaffSourceDetail BulkUpdateCount: {lstSourceDetail.Count()}");

                _dapperRepository.BulkInsert<StaffDetailControl>(lstDetailControl);
                _logger.Information($"StaffDetailControl BulkInsertCount: {lstDetailControl.Count()}");

                ControlledStaffSourceHeaderDto controlledStaffSourceHeaderDto = new ControlledStaffSourceHeaderDto()
                {
                    ControlledCorrectCountCredit = controlledCorrectCountCredit,
                    ControlledCorrectCountDebit = controlledCorrectCountDebit,
                    ControlledWrongCountCredit = controlledWrongCountCredit,
                    ControlledWrongCountDebit = controlledWrongCountDebit,
                    TotalControlledCorrectAmountCredit = totalControlledCorrectAmountCredit,
                    TotalControlledCorrectAmountDebit = totalControlledCorrectAmountDebit,
                    TotalControlledWrongAmountCredit = totalControlledWrongAmountCredit,
                    TotalControlledWrongAmountDebit = totalControlledWrongAmountDebit
                };

                await UpdateCustomerSourceHeader(sourceHeaderId, controlledStaffSourceHeaderDto);
                _dapperUnitOfWork.Commit();

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                var message = ex.Message;
                _logger.ErrorLog($"StaffChecker.ProcessInSourceDetail Exception",ex);
                return await Task.FromResult(false);
            }
        }

        private async Task UpdateCustomerSourceHeader(long sourceHeaderId, ControlledStaffSourceHeaderDto controlledStaffSourceHeaderDto)
        {
            var script = $"Update C set TotalControlledCorrectAmountCredit = TotalControlledCorrectAmountCredit + {controlledStaffSourceHeaderDto.TotalControlledCorrectAmountCredit} ," +
             $"TotalControlledCorrectAmountDebit = TotalControlledCorrectAmountDebit + {controlledStaffSourceHeaderDto.TotalControlledCorrectAmountDebit} ," +
             $"TotalControlledWrongAmountCredit = TotalControlledWrongAmountCredit + {controlledStaffSourceHeaderDto.TotalControlledWrongAmountCredit} ," +
             $"TotalControlledWrongAmountDebit = TotalControlledWrongAmountDebit + {controlledStaffSourceHeaderDto.TotalControlledWrongAmountDebit} ," +
             $"ControlledCorrectCountCredit = ControlledCorrectCountCredit + {controlledStaffSourceHeaderDto.ControlledCorrectCountCredit} ," +
             $"ControlledCorrectCountDebit = ControlledCorrectCountDebit + {controlledStaffSourceHeaderDto.ControlledCorrectCountDebit} ," +
             $"ControlledWrongCountCredit = ControlledWrongCountCredit + {controlledStaffSourceHeaderDto.ControlledWrongCountCredit} ," +
             $"ControlledWrongCountDebit = ControlledWrongCountDebit + {controlledStaffSourceHeaderDto.ControlledWrongCountDebit} " +
             $" from TB_StaffSourceHeader C where ID={sourceHeaderId} ";
            lock (this)
            {
                var customerSourceHeader = _dapperRepository.Query<StaffSourceHeader>(script);
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
        private async Task<CustomerSourceDetailResult> Validate(StaffSourceDetail staffSourceDetail, IEnumerable<AccountResponseCdcDto> accountResponseCdc, IEnumerable<CustomerResponseCdcDto> customerResponseCdc)
        {
            try
            {
                CustomerSourceDetailResult customerSourceDetailResult = new CustomerSourceDetailResult();
                #region صحت حساب بانک تجارت
                if (await IsLegacyAccount(staffSourceDetail.AccountNumber))
                    customerSourceDetailResult.AccVerified = staffSourceDetail.AccountNumber.IsValidateTjbAccDigit();
                else
                    customerSourceDetailResult.AccVerified = staffSourceDetail.AccountNumber.IsValidateAccDigit();
                #endregion

                #region صحت تعداد ارقام مبلغ
                customerSourceDetailResult.AmountVerified = staffSourceDetail.Amount.IsValidateAmount();
                #endregion

                #region صحت شناسه واریز
                customerSourceDetailResult.DepositeVerified = true;
                #endregion

                #region نوع ارز- نوع حساب - وضعیت حساب
                var accountResponse = accountResponseCdc.Where(a => a.LEGACY_ACCOUNT_NUMBER == staffSourceDetail.AccountNumber || a.ACNO == staffSourceDetail.AccountNumber).FirstOrDefault();
                var checkAccountCustomer = ConfigValidateChain(customerSourceDetailResult);
                checkAccountCustomer.execute(accountResponse);
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
                customerSourceDetailResult.BlackListStatus = BlackListStatus.NotInBlackList;
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
                _logger.ErrorLog($"StaffChecker.Validate Exception ",ex);
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
        private async Task<IEnumerable<AccountResponseCdcDto>> GetListCDCAccount(IEnumerable<StaffSourceDetail> staffResponses)
        {

            IList<RequestCdcDto> requestCdcDto = new List<RequestCdcDto>();
            IList<RequestCdcDto> requestCdcDtoByAcno = new List<RequestCdcDto>();

            foreach (var item in staffResponses)
            {
                if (await IsLegacyAccount(item.AccountNumber))
                    requestCdcDto.Add(new RequestCdcDto() { LEGACY_ACCOUNT_NUMBER = item.AccountNumber });
                else
                    requestCdcDtoByAcno.Add(new RequestCdcDto() { ACNO = item.AccountNumber });

            }
            var lstCDCAccounts = await _fetchFromCDC.GetListCDCAccount(requestCdcDto);
            var lstCDCAccountsByAcno = await _fetchFromCDC.GetListCDCAccountByAcno(requestCdcDtoByAcno);

            lstCDCAccounts.ToList().AddRange(lstCDCAccountsByAcno.ToList());
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
                requestCdcDto.Add(new RequestCdcDto() { RETCUSNO = item.RETCUSNO });
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
        private async Task<bool> RollBackUpdateSourceDetail(IEnumerable<StaffSourceDetail> staffResponses)
        {
            IList<StaffSourceDetail> lstSourceDetail = new List<StaffSourceDetail>();
            foreach (var response in staffResponses)
            {
                StaffSourceDetail sourceDetail = new StaffSourceDetail()
                {
                    Id = response.Id,
                    AccountNumber = response.AccountNumber,
                    Amount = response.Amount,
                    StaffSourceHeaderId = response.StaffSourceHeaderId,
                    RowIdentifier = response.RowIdentifier,
                    Status = GenericEnum.Unasign,
                    ArticleDesc = response.ArticleDesc,
                    ArticleNumber = response.ArticleNumber,
                    DebitCreditType = response.DebitCreditType,
                    Date = response.Date,
                    MappingStatus = response.MappingStatus,
                    OperationCode = response.OperationCode,
                };
                lstSourceDetail.Add(sourceDetail);
            }
            _dapperRepository.BulkUpdate<StaffSourceDetail>(lstSourceDetail);
            _logger.Information($"RollBack StaffSourceDetail BulkUpdateCount: {lstSourceDetail.Count()}");
            return await Task.FromResult(true);
        }

        /// <summary>
        /// تشخیص حساب خدمات یا تجارت از روی تعداد ارقام
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        private async Task<bool> IsLegacyAccount(string accountNo)
        {
            bool result = true;
            if (accountNo.Length == 13)
                result = false;
            return await Task.FromResult(result);
        }
        #endregion

        #region MissedSender
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken)
        {
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount, RequestType " +
            $" from TB_BatchProcessQueue Q join TB_StaffSourceHeader C on C.ID = Q.SourceHeader_ID where " +
            $" (Q.Status = {(int)GenericEnum.Pending} or Q.Status = {(int)GenericEnum.InQueue})  and RequestType = {(int)RequestType.Staff} " +
            $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        #endregion
    }
}
