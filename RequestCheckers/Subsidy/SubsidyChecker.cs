using BatchChecker.Dto.Share;
using BatchChecker.Extensions;
using BatchChecker.Interfaces;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOP.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.RequestCheckers
{
    public class SubsidyChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly ISubsidRepository _subsidRepository;

        public SubsidyChecker(
             ILogger logger,
            IDapperRepository dapperRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration,
            ISubsidRepository subsidRepository
            )
        {
            _logger = logger;
            _dapperRepository = dapperRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
            _subsidRepository = subsidRepository;
        }




        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> SenderAsync(CancellationToken stoppingToken)
        {
            
            _logger.Information($"Subsidy _ Run SenderAsync");
            var queryResponse = await _subsidRepository.GetListSourceHeaderAsync(stoppingToken);
            if (queryResponse.Any())
            {
                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                await _subsidRepository.InsertToBatchProcessQueueDetail(queryResponse, stoppingToken);
                _dapperUnitOfWork.Commit();
            }
            var responseDetail = await _subsidRepository.GetBatches(stoppingToken);
            _logger.Information($"Subsidy _ GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");
            return responseDetail;
        }

        public async Task<bool> CheckAsync(BatchProcessQueueDetailRequestDto batchProcess, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Information($"Subsidy.SubsidyChecker.CheckAsync Start");
                
                bool result = false;
                result = await _subsidRepository.ProcessInSubsidySourceDetail(batchProcess, cancellationToken);
                if (result)
                {
                    await _subsidRepository.UpdateProcessEndDateDateQueue(batchProcess.ID, GenericEnum.Success);
                }
                else
                    await _subsidRepository.UpdateProcessEndDateDateQueue(batchProcess.ID, GenericEnum.Unasign);

                _logger.Information($"Subsidy.SubsidyChecker.CheckAsync End");

                return true;
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"Subsidy.SubsidyChecker.BatchProcess Exception",ex);
                await _subsidRepository.UpdateProcessEndDateDateQueue(batchProcess.ID, GenericEnum.Unasign);
                return true;
            }

        }
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken)
        {
            return await _subsidRepository.GetMissedSenderAsync(stoppingToken); ;
        }

    }
}
