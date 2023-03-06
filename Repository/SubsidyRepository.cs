using BatchChecker.Dto.Share;
using BatchChecker.Dto.Subsidy;
using BatchChecker.Extensions;
using BatchChecker.Interfaces;
using BatchOp.Application.DTOs.Subsidy;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOp.Infrastructure.Shared.Services.Utility;
using BatchOP.Domain.Entities.Customer;
using BatchOP.Domain.Entities.Subsidy;
using BatchOP.Domain.Enums;
using Dapper;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Repository
{
    public class SubsidyRepository : ISubsidRepository
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly IConfiguration _configuration;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;


        public SubsidyRepository(
             ILogger logger,
            IDapperRepository dapperRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration


            )
        {
            _logger = logger;
            _dapperRepository = dapperRepository;
            _configuration = configuration;
            _dapperUnitOfWork = dapperUnitOfWork;
        }
        public async Task<IEnumerable<SubsidySourceHeader>> GetAllReadyForIssueVoucherHeaders()
        {
            var script = @"SELECT * from [BatchOp_db].[Subsidy].[TB_SubsidySourceHeader] where Status=5";
            return await _dapperRepository.QueryAsync<SubsidySourceHeader>(script);
        }
        public async Task<bool> CreateVoucherItems()
        {
            try
            {
                var sourceHeadersid = await GetAllReadyForIssueVoucherHeaders();
                foreach (var header in sourceHeadersid)
                {
                    if (await IsDone(header.Id) && header.Status == GenericEnum.Pending)
                    {
                        if(await CountOfValidDetails(header.Id) > 0)
                        {
                            await UpdateStatusSubsidySourceHeader(header.Id, GenericEnum.IssueVoucher);
                            _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                            var time = DateTime.Now;
                            var voucher = new Voucher();
                            voucher.AccountingYearId = await GetCurrentAccountingYear();
                            voucher.Created = time;
                            voucher.CreatedBy = header.CreatedBy;
                            voucher.VoucherDate = time;
                            voucher.VoucherNumber = await GetMaxVoucherNumber() + 1;
                            voucher.VoucherStatusId =await GetVoucherStatus(VoucherStatusCode.NotChecked);
                            voucher.VoucherTypeId = 1;
                            voucher.LastModified = time;
                            voucher.RequestId = await GetRequestBySourceId(header.Id);
                            voucher.LastModifiedBy = header.LastModifiedBy;
                            _dapperRepository.Add(voucher);
                            await UpdateSubsidyDeatailPaymentAccount(header.Id);
                            await InsertVoucherItems(voucher.Id, header.Id);
                            await UpdateDetailsVoucherId(header.Id, voucher.Id);
                            await UpdateStatusSubsidySourceHeader(header.Id, GenericEnum.Success);
                            _dapperUnitOfWork.Commit();
                        }
                       
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                _logger.ErrorLog("CreateVoucherItems Exeption:", ex);
                return false;
            }

        }

        public async Task<IEnumerable<SubsidySourceDetail>> GetSourceDetail(BatchProcessQueueDetailRequestDto batchProcess, CancellationToken stoppingToken)
        {
            //  _logger.Information($"Subsidy.GetSourceDetail Run");

            var scriptDetail = $"select d.SubsidySourceHeader_ID as SubsidySourceHeaderId , d.* from [Subsidy].[TB_SubsidySourceDetail] d " +
                                   $" join  [Subsidy].[TB_SubsidySourceHeader] H on H.ID = d.SubsidySourceHeader_ID " +
                                   $" where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                                   $" and d.SubsidySourceHeader_ID = {batchProcess.SourceHeaderId} " +
                                   $" and d.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
            var result = await _dapperRepository.QueryAsync<SubsidySourceDetail>(scriptDetail);
            // _logger.Information($"Subsidy.GetSourceDetail End");
            return result;
        }

        public async Task Validate(IEnumerable<SubsidySourceDetail> subsidySourceDetails, CancellationToken cancellationToken)
        {

            
                var nationalCodelist = subsidySourceDetails.Select(x => new NationalCodelistDto
                {
                    NationalCode = x.NationalCode,
                    AccountNo = x.AccountNo,
                    Detail_ID = x.Id,
                    Amount = x.Amount,
                    PaymentAccount = null,
                    ValidationStatus = 0,
                    AreaCode=null,
                    BranchCode=null,
                    Host_ID=null,
                    PhoneNumber=null,
                    GRP=null,
                }).ToList().ToDataTable();

                var validateParameters = new DynamicParameters();
                validateParameters.Add("@nationalCodelist", nationalCodelist.AsTableValuedParameter("[dbo].[NationalCodelist]"));
                var result = await _dapperRepository.ExecuteStoredProcedureAsync<NationalCodelistDto>($"[Subsidy].Sp_ValidateAccountNumber", validateParameters);
                await InsertSubsidControlls(result);
            
            


        }
        public async Task InsertSubsidControlls(IEnumerable<NationalCodelistDto> nationalCodelistDto)
        {

            var insertParameters = new DynamicParameters();
            insertParameters.Add("@nationalCodelist", nationalCodelistDto.ToList().ToDataTable().AsTableValuedParameter("[dbo].[NationalCodelist]"));
            await _dapperRepository.ExecuteStoredProcedureAsync<int>($"[Subsidy].Sp_InsertSubsidControlls", insertParameters);

        }

        public async Task InsertVoucherItems(long voucherId, long sourceheaderId)
        {
            var procedure = "[Subsidy].[Sp_InsertVoucherItems]";
            var insertParameters = new DynamicParameters();
            insertParameters.Add("@voucherId", voucherId);
            insertParameters.Add("@sourceheaderId", sourceheaderId);
            insertParameters.Add("@opertionCode", "660");
            await _dapperRepository.ExecuteStoredProcedureAsync<int>(procedure, insertParameters);
        }
        public async Task UpdateSubsidyDeatailPaymentAccount(long sourceheaderId)
        {
            var insertParameters = new DynamicParameters();
            insertParameters.Add("@sourceheaderId", sourceheaderId);
            var procedure = "[Subsidy].[Sp_UpdateSubsidyDeatailPaymentAccount]";
            await _dapperRepository.ExecuteStoredProcedureAsync<int>(procedure, insertParameters);
        }

        public async Task<bool> UpdateSubsidySourceHeader(long id, int processIndex, CancellationToken stoppingToken)
        {
            var script = $"Update H set Status = {(int)GenericEnum.Pending} , ProcessIndex = {processIndex} , ProcessDate = '{DateTime.Now}' " +
                $"from [Subsidy].[TB_SubsidySourceHeader] H where ID={id}";
            await _dapperRepository.QueryAsync<SubsidySourceHeader>(script);

            return true;
        }
        public async Task<bool> UpdateStatusSubsidySourceHeader(long id, GenericEnum status)
        {
            var script = $"Update H set Status = {(int)status} " +
                $"from [Subsidy].[TB_SubsidySourceHeader] H where ID={id}";
            await _dapperRepository.QueryAsync<SubsidySourceHeader>(script);

            return true;
        }
        public async Task<bool> UpdateDetailsVoucherId(long sourceId, long voucherId)
        {
            var script = $"update  D set [Voucher_ID] = {voucherId} " +
                         $"from [Subsidy].[TB_SubsidySourceDetail] D where D.SubsidySourceHeader_ID={sourceId} and D.PaymentAccountNo IS not NULL ";
            await _dapperRepository.QueryAsync<SubsidySourceHeader>(script);

            return true;
        }
        public async Task<bool> UpdateProcessEndDateDateQueue(long id, GenericEnum genericEnum)
        {

            var scriptBatchProcess = $"update b set ProcessEndDate='{DateTime.Now}' " +
                                     $",Status={(int)genericEnum}" +
                                     $" from [TB_BatchProcessQueue] b where ID = {id}";
            await _dapperRepository.QueryAsync<BatchProcessQueue>(scriptBatchProcess);

            return true;
        }

        public async Task<bool> UpdateProcessStartDateQueue(long id, GenericEnum genericEnum)
        {

            var scriptBatchProcess = $"update b set ProcessStartDate='{DateTime.Now}' " +
                                      $",Status={(int)genericEnum}" +
                                      $" from [TB_BatchProcessQueue] b where ID = {id}";
            await _dapperRepository.QueryAsync<BatchProcessQueue>(scriptBatchProcess);

            return true;
        }
        public async Task<IEnumerable<BatchProcessQueue>> CheckInSubsidyBatchProcessQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {

            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or  Status = {(int)GenericEnum.InQueue} or  Status = {(int)GenericEnum.Unasign})";
            var responseQueue = await _dapperRepository.QueryAsync<BatchProcessQueue>(scriptQueue);
            if (responseQueue.Any())
            {
                var scriptControl = $"delete	[Subsidy].[TB_SubsidyDetailControl] " +
                        $" FROM [Subsidy].[TB_SubsidyDetailControl] dc" +
                        $" LEFT JOIN [Subsidy].[TB_SubsidySourceDetail] d " +
                        $" ON dc.SubsidySourceDetail_ID= d.ID" +
                        $" WHERE d.Id IS NULL " +
                        $" and dc.SubsidySourceHeader_ID = {batchProcess.SourceHeaderId} " +
                        $" and RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex}";

                await _dapperRepository.QueryAsync<SubsidyDetailControl>(scriptControl);

            }

            return responseQueue;
        }

        public async Task<bool> ProcessInSubsidySourceDetail(BatchProcessQueueDetailRequestDto batchProcess, CancellationToken cancellationToken)
        {
            try
            {
                IEnumerable<BatchProcessQueue> responseQueue = await CheckInSubsidyBatchProcessQueue(batchProcess);
                if (!responseQueue.Any())
                    return true;

                var subsidySourceDetailList = await GetSourceDetail(batchProcess, cancellationToken);
                _logger.Information($"Subsidy.GetListCustomerSourceDetail Count: {subsidySourceDetailList.Count()}");
                if (!subsidySourceDetailList.Any())
                    return true;
                await UpdateProcessStartDateQueue(batchProcess.ID, GenericEnum.Pending);

                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                await Validate(subsidySourceDetailList, cancellationToken);
                _dapperUnitOfWork.Commit();
                return true;
            }
            catch (Exception e)
            {
                _dapperUnitOfWork.Rollback();
                _logger.ErrorLog($"SubsidyChecker.ProcessInSubsidySourceDetail Exception", e);
                return false;
            }

        }


        public async Task<bool> IsDone(long sourceHeaderId)
        {
            var count = await _dapperRepository.QueryAsync<int>($"SELECT COUNT(ID)  FROM [TB_BatchProcessQueue] where SourceHeader_ID = {sourceHeaderId} and Status!= 0 and RequestType = {(int)RequestType.Subsidy} ");
            var result = count.FirstOrDefault() == 0;
            return result;
        }

        public async Task<long> CountOfValidDetails(long sourceHeaderId)
        {
            var count = await _dapperRepository.QueryAsync<long>($"select count(*) from [Subsidy].[TB_SubsidyDetailControl] d where PaymentAccountNo is not null and d.SubsidySourceHeader_ID={sourceHeaderId} and d.Amount <> 0");
            var result = count.FirstOrDefault();
            return result;
        }
        public async Task<long> GetCurrentAccountingYear()
        {
            var id = await _dapperRepository.QueryAsync<int>(@"
                                  SELECT ID
                                  FROM [Subsidy].[TB_AccountingYear]
                                  where
                                  cast (StartDate as date) <= cast (GETDATE() as date)and 
                                  cast (EndDate as date)>= cast (GETDATE() as date)");
            var result = id.FirstOrDefault();
            return result;
        }

        public async Task<long> GetRequestBySourceId(long id)
        {
            var requestId = await _dapperRepository.QueryAsync<int>($"SELECT [Request_ID]   FROM [BatchOp_db].[Subsidy].[TB_SubsidySourceHeader] where ID={id}");
            var result = requestId.FirstOrDefault();
            return result;
        }

        public async Task<int> GetMaxVoucherNumber()
        {
            var id = await _dapperRepository.QueryAsync<int?>(@"
                                  SELECT 
                                  max([VoucherNumber]) as MaxVoucherNumber
                                  FROM [Subsidy].[TB_Voucher]"
                                   );
            if (!id.FirstOrDefault().HasValue)
            {
                _logger.Information($"Subsidy.GetMaxVoucherNumber End");
                return 0;
            }
            int result = id.FirstOrDefault().Value;

            return result;
        }
        public async Task<long> GetVoucherStatus(VoucherStatusCode status)
        {
            var id = await _dapperRepository.QueryAsync<int?>($"SELECT Id   FROM [BatchOp_db].[Subsidy].[TB_VoucherStatus] WHERE [VoucherStatusCode] ={(int)status} "
                                   );
            if (!id.FirstOrDefault().HasValue)
            {
                _logger.Information($"Subsidy.GetMaxVoucherNumber End");
                return 0;
            }
            int result = id.FirstOrDefault().Value;

            return result;
        }
        public async Task<bool> InsertToBatchProcessQueueDetail(IEnumerable<SubsidySourceHeaderDto> subsidyResponses, CancellationToken stoppingToken)
        {
            foreach (var item in subsidyResponses)
            {
                var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                var batchCount = rabbitMQConfiguration.SubsidBatchCount;
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
                        RequestType = RequestType.Subsidy,
                        TryCount = 0,
                    };
                    if (FindBatchProccessQueue(batchProcessQueueDetail) == 0)
                    {
                        _dapperRepository.Add(batchProcessQueueDetail);
                        _logger.Information($"InsertToBatchProcessQueueDetail : {JsonConvert.SerializeObject(batchProcessQueueDetail)}");
                        await UpdateSubsidySourceHeader(item.SourceHeaderId, item.RequestCount, stoppingToken);
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
                                    $"and RequestType = {(int)RequestType.Subsidy} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);

            return queryResponse.Count();
        }

        public async Task<IEnumerable<SubsidySourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R " +
                $"INNER JOIN [Subsidy].[TB_SubsidySourceHeader] H On R.ID = H.Request_ID " +
                $"join [Subsidy].[TB_SubsidySourceDetail] det on h.id = det.SubsidySourceHeader_ID " +
                $"WHERE H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = await _dapperRepository.QueryAsync<SubsidySourceHeaderDto>(scriptHeader);

            return queryResponse;
        }
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken)
        {
            //_logger.Information($"Subsidy.GetMissedSenderAsync Run");
            var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
            var tryCount = rabbitMQConfiguration.TryCount;

            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1) TryCount, Q.RequestType " +
                 $" from TB_BatchProcessQueue Q join [Subsidy].TB_SubsidySourceHeader C on C.ID = Q.SourceHeader_ID where " +
                 $" (Q.Status = {(int)GenericEnum.Pending} or  Q.Status = {(int)GenericEnum.InQueue}) and Q.RequestType = {(int)RequestType.Subsidy} " +
                 $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < {tryCount} ";

            var responseDetail = await _dapperRepository.QueryAsync<BatchProcessQueueDetailRequestDto>(scriptDetail);
            //   _logger.Information($"Subsidy.Missed: GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return responseDetail;
        }

        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetBatches(CancellationToken stoppingToken)
        {
            //   _logger.Information($"Subsidy.GetBatches Run");
            var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
            var tryCount = rabbitMQConfiguration.TryCount;
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount ,Q.RequestType " +
                              $" from [TB_BatchProcessQueue] Q join [Subsidy].[TB_SubsidySourceHeader] H on H.ID = Q.SourceHeader_ID where " +
                              $" (Q.Status = {(int)GenericEnum.Unasign}) and Q.RequestType = {(int)RequestType.Subsidy} " +
                              $"and  (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) and TryCount < {tryCount} ";
            //    _logger.Information($"Subsidy.GetBatches End");


            return await _dapperRepository.QueryAsync<BatchProcessQueueDetailRequestDto>(scriptDetail);
        }
    }
}
