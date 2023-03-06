using BatchChecker.Business.OpenAcc.HasError;
using BatchChecker.Dto.Customer;
using BatchChecker.Dto.FetchCDC;
using BatchChecker.Dto.OpenAcc;
using BatchChecker.Dto.Share;
using BatchChecker.Extensions;
using BatchChecker.FetchCDC;
using BatchOp.Application.Interfaces;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Shahkar;
using BatchOp.Infrastructure.Shared.ApiCaller.TataGateway;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOP.Domain.Entities.Customer;
using BatchOP.Domain.Entities.OpenAcc;
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
    public class OpenAccChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICDCRepository _cDCRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IFetchFromCDC _fetchFromCDC;
        private readonly ITataGatewayApi _tataGatewayApi;

        public OpenAccChecker(
             ILogger logger,
            IDapperRepository dapperRepository,
            ICDCRepository cDCRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration,
            IFetchFromCDC fetchFromCDC,
            ITataGatewayApi tataGatewayApi)
        {
            _logger = logger;
            _dapperRepository = dapperRepository;
            _cDCRepository = cDCRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
            _fetchFromCDC = fetchFromCDC;
            _tataGatewayApi = tataGatewayApi;
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

            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount,RequestType " +
                $" from TB_BatchProcessQueue Q join TB_OpenAccSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                $" (Q.Status = {(int)GenericEnum.Unasign}) and RequestType = {(int)RequestType.OpenAcc} " +
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
                $"from TB_OpenAccSourceHeader S where ID={Id}";
            lock (this)
            {
                var openAccSourceHeaders = _dapperRepository.Query<OpenAccSourceHeader>(script);
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
                var batchCount = rabbitMQConfiguration.OpenAccBatchCount;

                int startIndex = 0;
                int endIndex = 0;

                for (int i = 1; endIndex < item.RequestCount; i++)
                {
                    startIndex = (i - 1) * batchCount + 1;
                    endIndex = item.RequestCount <= i * batchCount ? endIndex = item.RequestCount : endIndex = i * batchCount;

                    BatchProcessQueue batchProcessQueueDetail = new BatchProcessQueue()
                    {
                        SourceHeaderId = item.SourceHeaderId,
                        StartIndex = startIndex,
                        EndIndex = endIndex,
                        Status = GenericEnum.Unasign,
                        RequestType = RequestType.OpenAcc,
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
                                    $"and RequestType = {(int)RequestType.OpenAcc} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);
           
            return queryResponse.Count();
        }

        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderOpenAcc");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R INNER JOIN TB_OpenAccSourceHeader H On R.ID = H.Request_ID " +
                $"join TB_OpenAccSourceDetail det on h.id = det.OpenAccSourceHeader_ID " +
                $"WHERE H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderOpenAcc Count:{queryResponse.Count()}");

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
                _logger.Information($"OpenAccChecker.CheckAsync: {batchProcess}");

                IEnumerable<BatchProcessQueue> responseQueue = CheckInOpenAccBatchProcessQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"select C.OpenAccSourceHeader_ID as OpenAccSourceHeaderId , C.* from TB_OpenAccSourceDetail C " +
                                           $" join TB_OpenAccSourceHeader H on H.ID = C.OpenAccSourceHeader_ID " +
                                           $" where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                                           $" and C.OpenAccSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                                           $" and C.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
                // $"and C.Status != {(int)GenericEnum.Success}";

                IEnumerable<OpenAccSourceDetail> responseDetails;
                responseDetails = _dapperRepository.Query<OpenAccSourceDetail>(scriptDetail);
                _logger.Information($"GetListOpenAccSourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueue(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;
                result = await ProcessInSourceDetail(responseDetails, cancellationToken);
                _logger.Information($"OpenAccChecker.CheckAsync.ProcessInSourceDetail Result: {JsonConvert.SerializeObject(result)}");
                if (result)
                {
                    _logger.Information($"OpenAccChecker.CheckAsync.ProcessInSourceDetail Result: True");
                    await UpdateBatchProcessQueue(batchProcess.ID, GenericEnum.Success, 2);
                }
                else
                {
                    _logger.Information($"OpenAccChecker.CheckAsync.ProcessInSourceDetail Result: False");
                    await UpdateBatchProcessQueue(batchProcess.ID, GenericEnum.Unasign, 2);
                }

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"OpenAccChecker.CheckAsync Exception:",ex);
                await UpdateBatchProcessQueue(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }

        }

        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private IEnumerable<BatchProcessQueue> CheckInOpenAccBatchProcessQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            _logger.Information($"OpenAccChecker.CheckInOpenAccBatchProcessQueue");
            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or  Status = {(int)GenericEnum.InQueue} or  Status = {(int)GenericEnum.Unasign})";
            var responseQueue = _dapperRepository.Query<BatchProcessQueue>(scriptQueue);

            if (responseQueue.Any())
            {
                var scriptControl = $"delete FROM TB_OpenAccItemsControl where OpenAccSourceDetail_ID in " +
                    $"(select ID from TB_OpenAccSourceDetail where OpenAccSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                    $"and RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex})";
                _dapperRepository.ExecQuery(scriptControl);
            }
            _logger.Information($"OpenAccChecker.CheckInOpenAccBatchProcessQueue Result:{JsonConvert.SerializeObject(responseQueue)}");
            return responseQueue;
        }

        /// <summary>
        /// آپدیت تیبل BatchProcessQueueDetail
        /// </summary>
        /// <param name="Id"></param>
        /// <param name="StartDate"></param>
        /// <param name="GenericEnum"></param>
        /// <returns></returns>
        private async Task<bool> UpdateBatchProcessQueue(long Id, GenericEnum genericEnum, int Result)
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
            _logger.Information($"OpenAccChecker.UpdateBatchProcessQueue :Id = {Id} - genericEnum = {genericEnum}");
            return await Task.FromResult(true);
        }


        /// <summary>
        /// پردازش بر روی لیست دریافتی مشتریان
        /// </summary>
        /// <param name="customerResponses"></param>
        /// <returns></returns>
        private async Task<bool> ProcessInSourceDetail(IEnumerable<OpenAccSourceDetail> sourceDetailResponse, CancellationToken cancellationToken)
        {
            try
            {

                var OpenAccSourceHeaderId = sourceDetailResponse.Select(x => x.OpenAccSourceHeaderId).FirstOrDefault();
                _logger.Information($"OpenAccChecker.ProcessInSourceDetail {OpenAccSourceHeaderId} Start");

                IList<OpenAccSourceDetail> lstSourceDetail = new List<OpenAccSourceDetail>();
                IList<OpenAccItemsControl> lstDetailControl = new List<OpenAccItemsControl>();

                var lstCDCCustomer = await GetListCDCCUSTOMER(sourceDetailResponse);

                var openAccSourceHeaderId = sourceDetailResponse.Select(s => s.OpenAccSourceHeaderId).FirstOrDefault();
                var script = $"select ID,MobileNumber,NationalCode  FROM TB_OpenAccSourceDetail where OpenAccSourceHeader_ID={openAccSourceHeaderId}";
                var queryResponse = _dapperRepository.Query<OpenAccSourceDetail>(script);
                int controlledCorrectCount = 0;
                int controlledWrongCount = 0;

                foreach (var response in sourceDetailResponse)
                {
                    _logger.Information($"OpenAccChecker.ProcessInSourceDetail{response.Id} foreach Start");
                    var result = await Validate(response, queryResponse, lstCDCCustomer, cancellationToken);
                    _logger.Information($"OpenAccChecker.ProcessInSourceDetail{response.Id} initial in OpenAccSourceDetail");
                    OpenAccSourceDetail openAccSourceDetail = new OpenAccSourceDetail()
                    {
                        Id = response.Id,
                        RowIdentifier = response.RowIdentifier,
                        Address = response.Address,
                        BirthDate = response.BirthDate,
                        Email = response.Email,
                        Gender = response.Gender,
                        IdentitySerialNumber = response.IdentitySerialNumber,
                        IdentitySeriINumber = response.IdentitySeriINumber,
                        IdentitySeriXNumber = response.IdentitySeriXNumber,
                        MobileNumber = response.MobileNumber,
                        NationalCode = response.NationalCode,
                        OpenAccSourceHeaderId = response.OpenAccSourceHeaderId,
                        OrganizationPersonalCode = response.OrganizationPersonalCode,
                        PostalCode = response.PostalCode,
                        Status = result.Status,
                        MappingStatus = GenericEnum.Unasign,
                    };
                    lstSourceDetail.Add(openAccSourceDetail);
                    _logger.Information($"OpenAccChecker.ProcessInSourceDetail{response.Id} Add into lstSourceDetail");

                    _logger.Information($"OpenAccChecker.ProcessInSourceDetail{response.Id} initial in OpenAccItemsControl");
                    OpenAccItemsControl openAccItemsControl = new OpenAccItemsControl()
                    {
                        CustomerIsBank = result.CustomerIsBank,
                        EmailVerified = result.EmailVerified,
                        MobileNumberDuplicate = result.MobileNumberDuplicate,
                        MobileNumberSHAHKAR = result.MobileNumberSHAHKAR,
                        MobileNumberVerified = result.MobileNumberVerified,
                        NationalCodeDuplaicate = result.NationalCodeDuplaicate,
                        NationalCodeVerified = result.NationalCodeVerified,
                        OpenAccSourceDetailId = response.Id,
                        HasError = result.HasError,
                        Created = DateTime.Now,
                        CreatedBy = "System",
                    };
                    lstDetailControl.Add(openAccItemsControl);
                    _logger.Information($"OpenAccChecker.ProcessInSourceDetail{response.Id} Add into lstDetailControl");

                    if (result.HasError == false)
                        controlledCorrectCount += 1;
                    else
                        controlledWrongCount += 1;
                    _logger.Information($"OpenAccChecker.ProcessInSourceDetail{response.Id} foreach End");
                }
                _logger.Information($"OpenAccChecker.ProcessInSourceDetail{OpenAccSourceHeaderId} Start BeginTransaction");

                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                _dapperRepository.BulkUpdate<OpenAccSourceDetail>(lstSourceDetail);
                _logger.Information($"OOpenAccChecker.penAccSourceDetail{OpenAccSourceHeaderId} BulkUpdateCount: {lstSourceDetail.Count()}");

                _dapperRepository.BulkInsert<OpenAccItemsControl>(lstDetailControl);
                _logger.Information($"OpenAccChecker.OpenAccItemsControl{OpenAccSourceHeaderId} BulkInsertCount: {lstDetailControl.Count()}");

                await UpdateOpenAccSourceHeader(openAccSourceHeaderId, controlledCorrectCount, controlledWrongCount);
                _dapperUnitOfWork.Commit();
                _logger.Information($"OpenAccChecker.ProcessInSourceDetail{OpenAccSourceHeaderId} Start Commit");
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                var message = ex.Message;
                _logger.ErrorLog($"OpenAccChecker.ProcessInSourceDetailException",ex);
                return await Task.FromResult(false);
            }
        }

        /// <summary>
        /// صحت سنجی اطلاعات مشتری
        /// </summary>
        /// <param name="customerSourceDetail"></param>
        /// <param name="accountResponseCdc"></param>
        /// <param name="customerResponseCdc"></param>
        /// <returns></returns>
        private async Task<OpenAccSourceDetailResult> Validate(OpenAccSourceDetail openAccSourceDetail, IEnumerable<OpenAccSourceDetail> openAccSourceDetails, IEnumerable<CustomerResponseCdcDto> customerResponseCdcDtos, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Information($"OpenAccChecker.Validate{openAccSourceDetail.Id}");
                OpenAccSourceDetailResult openAccSourceDetailResult = new OpenAccSourceDetailResult();
                var nationalCodeCount = openAccSourceDetails.Where(o => o.NationalCode == openAccSourceDetail.NationalCode).Count();
                var mobileNumberCount = openAccSourceDetails.Where(o => o.MobileNumber == openAccSourceDetail.MobileNumber).Count();

                openAccSourceDetailResult.NationalCodeVerified = openAccSourceDetail.NationalCode.IsValidIranianNationalCode();
                openAccSourceDetailResult.NationalCodeDuplaicate = nationalCodeCount > 1 ? true : false;

                openAccSourceDetailResult.MobileNumberVerified = openAccSourceDetail.MobileNumber.IsValidMobileNumber();
                openAccSourceDetailResult.MobileNumberDuplicate = mobileNumberCount > 1 ? true : false;

                _logger.Information($"OpenAccChecker.GetShahkar{openAccSourceDetail.Id} Start");
                //MockData
                openAccSourceDetailResult.MobileNumberSHAHKAR = false;
                //MockData

                //ShahkarResponseDto shahkarResult = await GetShahkar(openAccSourceDetail, cancellationToken);
                //if (shahkarResult.response == null)
                //    openAccSourceDetailResult.MobileNumberSHAHKAR = false;
                //else
                //    openAccSourceDetailResult.MobileNumberSHAHKAR = shahkarResult != null & shahkarResult.response.response == 200 ? true : false;
                _logger.Information($"OpenAccChecker.GetShahkar{openAccSourceDetail.Id} End");

                openAccSourceDetailResult.EmailVerified = openAccSourceDetail.Email.IsValidEmail();

                var customerIsBank = customerResponseCdcDtos.Where(c => c.NATIONAL_CODE == openAccSourceDetail.NationalCode).Count();
                openAccSourceDetailResult.CustomerIsBank = customerIsBank > 0 ? true : false;

                var hasErrorHandler = new HasErrorHandler(openAccSourceDetailResult);
                hasErrorHandler.execute(openAccSourceDetailResult);

                openAccSourceDetailResult.OpenAccSourceDetail_ID = openAccSourceDetail.Id;
                openAccSourceDetailResult.Status = GenericEnum.Success;

                _logger.Information($"OpenAccChecker.Validate{openAccSourceDetail.Id} Result: {JsonConvert.SerializeObject(openAccSourceDetailResult)}");
                return await Task.FromResult(openAccSourceDetailResult);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"OpenAccChecker.Validate{openAccSourceDetail.Id} Exception : ",ex);
                return await Task.FromResult(new OpenAccSourceDetailResult
                {
                    EmailVerified = false,
                    MobileNumberDuplicate = true,
                    MobileNumberVerified = false,
                    MobileNumberSHAHKAR = false,
                    NationalCodeDuplaicate = true,
                    NationalCodeVerified = false,
                    OpenAccSourceDetail_ID = openAccSourceDetail.Id,
                    CustomerIsBank = false,
                    Status = GenericEnum.ServerError,
                    HasError = true,
                });
            }
        }

        private async Task<ShahkarResponseDto> GetShahkar(OpenAccSourceDetail openAccSourceDetail, CancellationToken cancellationToken)
        {
            ShahkarRequestDto shahkarRequestDto = new ShahkarRequestDto()
            {
                identificationNo = Convert.ToInt64(openAccSourceDetail.NationalCode),
                identificationType = 0,
                serviceNumber = openAccSourceDetail.MobileNumber,
                serviceType = 2,
            };
            var shahkarResult = await _tataGatewayApi.GetShahkar(shahkarRequestDto, cancellationToken);
            return shahkarResult;
        }

        private async Task<IEnumerable<CustomerResponseCdcDto>> GetListCDCCUSTOMER(IEnumerable<OpenAccSourceDetail> openAccSourceDetails)
        {
            IList<RequestCdcDto> requestCdcDto = new List<RequestCdcDto>();
            foreach (var item in openAccSourceDetails)
            {
                requestCdcDto.Add(new RequestCdcDto() { NATIONAL_CODE = item.NationalCode });
            }
            var lstCDCCustomers = await _fetchFromCDC.GetListCDCCustomerByNationalCode(requestCdcDto);
            return lstCDCCustomers;
        }

        private async Task UpdateOpenAccSourceHeader(long openAccSourceHeaderId, int controlledCorrectCount, int controlledWrongCount)
        {
            var script = $"Update C set controlledCorrectCount = controlledCorrectCount + {controlledCorrectCount} ," +
                         $"controlledWrongCount = controlledWrongCount + {controlledWrongCount} " +
                         $"from TB_OpenAccSourceHeader C where ID={openAccSourceHeaderId} ";
            lock (this)
            {
                var customerSourceHeader = _dapperRepository.Query<OpenAccSourceHeader>(script);
            }
            await Task.Delay(0);
        }

        #endregion

        #region MissedSender
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken)
        {
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount,RequestType " +
                  $" from TB_BatchProcessQueue Q join TB_OpenAccSourceHeader C on C.ID = Q.SourceHeader_ID where " +
                  $" (Q.Status = {(int)GenericEnum.InQueue} or Q.Status = {(int)GenericEnum.Pending}) and RequestType = {(int)RequestType.OpenAcc} " +
                  $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";
            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"Missed: GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        #endregion
    }
}
