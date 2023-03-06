using BatchChecker.Business.Signature.HasError;
using BatchChecker.Dto.Customer;
using BatchChecker.Dto.FetchCDC;
using BatchChecker.Dto.Share;
using BatchChecker.Dto.Signature;
using BatchChecker.Extensions;
using BatchChecker.FetchCDC;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Infrastructure.Persistence.DapperRepositories;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOP.Domain.Entities.Customer;
using BatchOP.Domain.Entities.Signature;
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
    public class SignatureChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IFetchFromCDC _fetchFromCDC;
        public SignatureChecker(
            ILogger logger,
            IDapperRepository dapperRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration,
            IFetchFromCDC fetchFromCDC)
        {
            _logger = logger;
            _dapperRepository = dapperRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
            _fetchFromCDC = fetchFromCDC;
        }
        public async Task<bool> CheckAsync(BatchProcessQueueDetailRequestDto batchProcess, CancellationToken cancellationToken)
        {
            try
            {
                IEnumerable<BatchProcessQueue> responseQueue = CheckInSignatureBatchProcessQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"SELECT C.ID Id, C.SignatureSourceHeader_ID as SignatureSourceHeaderId, C.RowIdentifier, C.NationalCode, C.SignatureAddress, C.Status, C.MappingStatus " +
                    $"FROM TB_SignatureSourceDetail C " +
                    $"INNER JOIN TB_SignatureSourceHeader H ON H.ID = C.SignatureSourceHeader_ID " +
                    $"WHERE (H.Status != {(int)GenericEnum.Deleted} OR H.Status != {(int)GenericEnum.Cancel}) " +
                    $"AND C.SignatureSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                    $"AND C.RowIdentifier between {batchProcess.StartIndex} AND {batchProcess.EndIndex} ";
                //$" and C.Status = {(int)GenericEnum.Unasign} ";

                IEnumerable<SignatureSourceDetail> responseDetails;
                responseDetails = _dapperRepository.Query<SignatureSourceDetail>(scriptDetail);
                _logger.Information($"GetListSignatureSourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;
                result = await ProcessInSignatureSourceDetail(responseDetails);
                if (result)
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Success, 2);
                else
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"SignatureChecker.CheckAsync Exception",ex);
                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }
        }

        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken)
        {
            var scriptDetail = $"SELECT Q.ID, Q.SourceHeader_ID AS SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1) TryCount ,RequestType " +
            $"FROM TB_BatchProcessQueue Q " +
            $"INNER JOIN TB_SignatureSourceHeader C ON C.ID = Q.SourceHeader_ID " +
            $"WHERE (Q.Status = {(int)GenericEnum.Pending} OR Q.Status = {(int)GenericEnum.InQueue}) and RequestType = {(int)RequestType.Signature} " +
            $"AND C.Status NOT IN ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) AND TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }

        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> SenderAsync(CancellationToken cancellationToken)
        {
            var queryResponse = await GetListSourceHeaderAsync(cancellationToken);
            if (queryResponse.Any())
                await InsertToBatchProcessQueueDetailAsync(queryResponse, cancellationToken);

            var scriptDetail = $"SELECT Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount, RequestType " +
                $"FROM TB_BatchProcessQueue Q " +
                $"INNER JOIN TB_SignatureSourceHeader C on C.ID = Q.SourceHeader_ID " +
                $"WHERE Q.Status = {(int)GenericEnum.Unasign} and RequestType = {(int)RequestType.Signature} " +
                $"AND C.Status NOT IN ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) AND TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderSignature");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R " +
                $"INNER JOIN TB_SignatureSourceHeader H On R.ID = H.Request_ID " +
                $"join TB_SignatureSourceDetail det on h.id = det.SignatureSourceHeader_ID " +
                $"WHERE H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderSignature Count:{queryResponse.Count()}");

            return await Task.FromResult(queryResponse);
        }

        /// <summary>
        /// ثبت درخواست ها با ایندکس در تیبل صف
        /// </summary>
        /// <param name="staffResponses"></param>
        /// <returns></returns>
        private async Task<bool> InsertToBatchProcessQueueDetailAsync(IEnumerable<SourceHeaderDto> staffResponses, CancellationToken stoppingToken)
        {
            _dapperUnitOfWork.BeginTransaction(_dapperRepository);
            foreach (var item in staffResponses)
            {
                var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                var batchCount = rabbitMQConfiguration.SignatureBatchCount;

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
                        RequestType = RequestType.Signature,
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
            _dapperUnitOfWork.Commit();
            return true;
        }

        private int FindBatchProccessQueue(BatchProcessQueue batchProcessQueueDetail)
        {
            var scriptBatchProcessQueue = $"SELECT * FROM TB_BatchProcessQueue where SourceHeader_ID = {batchProcessQueueDetail.SourceHeaderId} and " +
                                    $"StartIndex = {batchProcessQueueDetail.StartIndex} and " +
                                    $"EndIndex = {batchProcessQueueDetail.EndIndex} " +
                                    $"and RequestType = {(int)RequestType.Signature} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);

            return queryResponse.Count();
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
                $"from TB_SignatureSourceHeader S where ID={Id}";
            lock (this)
            {
                var signatureSourceHeader = _dapperRepository.Query<SignatureSourceHeader>(script);
            }
            return await Task.FromResult(true);

        }
        #region checker

        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private IEnumerable<BatchProcessQueue> CheckInSignatureBatchProcessQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            var scriptQueue = $"SELECT ID Id, SourceHeader_ID SourceHeaderdId, StartIndex, EndIndex, " +
                $"ProcessStartDate, ProcessEndDate, SentToQueueDate, Status, TryCount " +
                $"FROM TB_BatchProcessQueue " +
                $"WHERE ID = {batchProcess.ID} AND (Status = {(int)GenericEnum.Pending} OR  Status = {(int)GenericEnum.InQueue} OR  Status = {(int)GenericEnum.Unasign})";
            var responseQueue = _dapperRepository.Query<BatchProcessQueue>(scriptQueue);

            if (responseQueue.Any())
            {
                var scriptControl = $"DELETE FROM TB_SignatureDetailControl WHERE SignatureSourceDetail_ID in " +
                    $"(SELECT ID from TB_SignatureSourceDetail where SignatureSourceHeader_ID = {batchProcess.SourceHeaderId} " +
                    $"AND RowIdentifier BETWEEN {batchProcess.StartIndex} AND {batchProcess.EndIndex})";
                _dapperRepository.ExecQuery(scriptControl);
            }

            return responseQueue;
        }
        private async Task<bool> UpdateBatchProcessQueueDetail(long Id, GenericEnum genericEnum, int Result)
        {
            if (Result == 1)
            {
                var scriptBatchProcess = $"UPDATE B SET ProcessStartDate='{DateTime.Now}' ,Status={(int)genericEnum} " +
                    $"FROM TB_BatchProcessQueue B WHERE ID = {Id}";
                lock (this)
                {
                    _dapperRepository.ExecQuery(scriptBatchProcess);
                }
            }
            else
            {
                var scriptBatchProcess = $"UPDATE B set ProcessEndDate='{DateTime.Now}' ,Status={(int)genericEnum} " +
                    $"FROM TB_BatchProcessQueue B WHERE ID = {Id}";
                lock (this)
                {
                    _dapperRepository.ExecQuery(scriptBatchProcess);
                }
            }

            return await Task.FromResult(true);
        }

        /// <summary>
        /// پردازش بر روی لیست دریافتی مشتریان
        /// </summary>
        /// <param name="customerResponses"></param>
        /// <returns></returns>
        private async Task<bool> ProcessInSignatureSourceDetail(IEnumerable<SignatureSourceDetail> signatureDetails)
        {
            try
            {
                IList<SignatureSourceDetail> lstSourceDetail = new List<SignatureSourceDetail>();
                IList<SignatureDetailControl> lstDetailControl = new List<SignatureDetailControl>();
                var sourceHeaderId = signatureDetails.Select(c => c.SignatureSourceHeaderId).FirstOrDefault();

                var lstCDCCustomer = await GetListCDCCUSTOMER(signatureDetails);

                int controlledCorrectCount = 0;
                int controlledWrongCount = 0;

                foreach (var response in signatureDetails)
                {
                    var detailReault = await Validate(response, lstCDCCustomer);
                    //bebin taghirat emal mishe?
                    lstSourceDetail.Add(response);

                    detailReault.Created = DateTime.Now;
                    detailReault.CreatedBy = "System";

                    lstDetailControl.Add(detailReault);

                    if (detailReault.HasError == false)
                    {
                        controlledCorrectCount += 1;
                    }
                    else if (detailReault.HasError == true)
                    {
                        controlledWrongCount += 1;
                    }

                }
                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                _dapperRepository.BulkUpdate<SignatureSourceDetail>(lstSourceDetail);
                _logger.Information($"SignatureSourceDetail BulkUpdateCount: {lstSourceDetail.Count()}");

                _dapperRepository.BulkInsert<SignatureDetailControl>(lstDetailControl);
                _logger.Information($"SignatureDetailControl BulkInsertCount: {lstDetailControl.Count()}");

                ControlledSignatureSourceHeaderDto controlledSignatureSourceHeaderDto = new ControlledSignatureSourceHeaderDto()
                {
                    ControlledCorrectCount = controlledCorrectCount,
                    ControlledWrongCount = controlledWrongCount
                };

                await UpdateSignatureSourceHeader(sourceHeaderId, controlledSignatureSourceHeaderDto);
                _dapperUnitOfWork.Commit();

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                var message = ex.Message;
                _logger.ErrorLog($"SignatureChecker.ProcessInSignatureSourceDetail Exception",ex);
                return await Task.FromResult(false);
            }
        }

        private async Task<IEnumerable<CustomerResponseCdcDto>> GetListCDCCUSTOMER(IEnumerable<SignatureSourceDetail> signatureSourceDetails)
        {
            IList<RequestCdcDto> requestCdcDto = new List<RequestCdcDto>();
            foreach (var item in signatureSourceDetails)
            {
                requestCdcDto.Add(new RequestCdcDto() { NATIONAL_CODE = item.NationalCode });
            }
            var lstCDCCustomers = await _fetchFromCDC.GetListCDCCustomerByNationalCode(requestCdcDto);
            return lstCDCCustomers;
        }

        private async Task<SignatureDetailControl> Validate(SignatureSourceDetail signatureSourceDetail, IEnumerable<CustomerResponseCdcDto> customerResponseCdc)
        {
            try
            {
                SignatureDetailControl signatureDetailControl = new SignatureDetailControl();
                signatureDetailControl.SignatureourceDetailId = signatureSourceDetail.Id;
                #region صحت‌سنجی شماره ملی
                signatureDetailControl.NationalCodeVerified = signatureSourceDetail.NationalCode.IsValidIranianNationalCode();
                #endregion

                #region صحت‌سنجی مشتری بانک
                var bankCustomerResult = customerResponseCdc.Where(c => c.NATIONAL_CODE == signatureSourceDetail.NationalCode).Count();
                signatureDetailControl.TejaratCustomerVerified = bankCustomerResult > 0 ? true : false;
                #endregion

                #region نتیجه ولیدیشن
                await AbstractHasErrorHandler(signatureDetailControl);
                #endregion

                signatureDetailControl.SignatureourceDetailId = signatureSourceDetail.Id;
                signatureSourceDetail.Status = GenericEnum.Success;

                return await Task.FromResult(signatureDetailControl);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"SignatureChecker.Validate Exception",ex);

                signatureSourceDetail.Status = GenericEnum.ServerError;

                return await Task.FromResult(new SignatureDetailControl
                {
                    NationalCodeVerified = false,
                    TejaratCustomerVerified = false,
                    SignatureourceDetailId = signatureSourceDetail.Id,
                    HasError = true
                });
            }
        }

        private async Task AbstractHasErrorHandler(SignatureDetailControl signatureDetailControl)
        {
            var hasErrorHandler = new HasErrorHandler(signatureDetailControl);
            hasErrorHandler.execute(signatureDetailControl);
            await Task.Delay(0);
        }

        private async Task UpdateSignatureSourceHeader(long sourceHeaderId, ControlledSignatureSourceHeaderDto controlledSignatureSourceHeaderDto)
        {
            var script = $"Update C set ControlledCorrectCount = ControlledCorrectCount + {controlledSignatureSourceHeaderDto.ControlledCorrectCount} ," +
             $"ControlledWrongCount = ControlledWrongCount + {controlledSignatureSourceHeaderDto.ControlledWrongCount} " +
             $" from TB_SignatureSourceHeader C where ID={sourceHeaderId} ";
            lock (this)
            {
                var customerSourceHeader = _dapperRepository.Query<SignatureSourceHeader>(script);
            }
            await Task.Delay(0);
        }

        #endregion
    }
}
