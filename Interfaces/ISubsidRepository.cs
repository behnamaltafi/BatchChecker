using BatchChecker.Dto.Share;
using BatchChecker.Dto.Subsidy;
using BatchOp.Application.AOP;
using BatchOp.Application.DTOs.Subsidy;
using BatchOP.Domain.Entities.Customer;
using BatchOP.Domain.Entities.Subsidy;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Interfaces
{
    public interface ISubsidRepository
    {
        [LogDef(LogMode =LogMode.Input)]
        Task<IEnumerable<SubsidySourceDetail>> GetSourceDetail(BatchProcessQueueDetailRequestDto batchProcess, CancellationToken stoppingToken);
        [LogDef(LogMode = LogMode.OnlyMethodName)]
        Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken);
        [LogDef(LogMode = LogMode.OnlyMethodName)]
        Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetBatches(CancellationToken stoppingToken);
        [LogDef(LogMode = LogMode.OnlyMethodName)]

        Task<bool> InsertToBatchProcessQueueDetail(IEnumerable<SubsidySourceHeaderDto> subsidyResponses, CancellationToken stoppingToken);
        [LogDef(LogMode = LogMode.OnlyMethodName)]

        Task<IEnumerable<SubsidySourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken);
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<BatchProcessQueue>> CheckInSubsidyBatchProcessQueue(BatchProcessQueueDetailRequestDto batchProcess);
        [LogDef(LogMode = LogMode.Input)]
        Task<bool> ProcessInSubsidySourceDetail(BatchProcessQueueDetailRequestDto batchProcess, CancellationToken cancellationToken);
        [LogDef(LogMode = LogMode.Input)]
        Task<bool> IsDone(long sourceHeaderId);
        [LogDef(LogMode = LogMode.Input)]
        Task<long> GetCurrentAccountingYear();
        [LogDef(LogMode = LogMode.Input)]
        Task<long> GetRequestBySourceId(long id);
        [LogDef(LogMode = LogMode.Input)]
        Task<int> GetMaxVoucherNumber();
        [LogDef]
        Task InsertSubsidControlls(IEnumerable<NationalCodelistDto> nationalCodelistDto);
        [LogDef(LogMode = LogMode.Input)]
        Task InsertVoucherItems(long voucherId, long sourceheaderId);
        [LogDef(LogMode = LogMode.Input)]
        Task UpdateSubsidyDeatailPaymentAccount(long sourceheaderId);
        [LogDef(LogMode = LogMode.Input)]
        Task<bool> UpdateSubsidySourceHeader(long Id, int processIndex, CancellationToken stoppingToken);
        [LogDef(LogMode = LogMode.Input)]
        Task<bool> UpdateStatusSubsidySourceHeader(long Id, GenericEnum status);
        [LogDef(LogMode = LogMode.Input)]
        Task<bool> UpdateProcessEndDateDateQueue(long Id, GenericEnum genericEnum);
        [LogDef(LogMode = LogMode.Input)]
        Task<bool> UpdateProcessStartDateQueue(long Id, GenericEnum genericEnum);
        [LogDef(LogMode = LogMode.Input)]
        Task Validate(IEnumerable<SubsidySourceDetail> subsidySourceDetails, CancellationToken cancellationToken);
        [LogDef(LogMode = LogMode.Input)]
        Task<bool> CreateVoucherItems();
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<SubsidySourceHeader>> GetAllReadyForIssueVoucherHeaders();
    }
}
