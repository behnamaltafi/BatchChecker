using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using System.Linq;
using BatchOP.Domain.Enums;
using BatchChecker.Checkers;
using BatchChecker.Dto;
using Serilog;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOP.Domain.Entities.Subsidy;
using BatchChecker.Dto.Share;
using BatchOP.Domain.Entities.Customer;
using BatchChecker.Extensions;
using BatchOp.Application.Interfaces.Repositories.Subsidy;
using BatchChecker.Interfaces;

namespace BatchChecker.Services.Subsidy
{

    public class SubsidyVoucherService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly ISubsidRepository _subsidRepository;

        public SubsidyVoucherService(ILogger logger, ISubsidRepository subsidRepository)
        {
            _logger = logger;
            _subsidRepository = subsidRepository;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information(
                $"Queued Hosted Service is running.{Environment.NewLine}" +
                $"{Environment.NewLine}Tap W to add a work item to the " +
                $"background queue.{Environment.NewLine}");

            await BackgroundProcessing(stoppingToken);
        }

        private async Task BackgroundProcessing(CancellationToken stoppingToken)
        {

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.Information("Start SubsidyVoucherService.BackgroundProcessing");

                    var isCreateVoucherItems = await _subsidRepository.CreateVoucherItems();

                    _logger.Information($"End SubsidyVoucherService.BackgroundProcessing isCreateVoucherItems: {isCreateVoucherItems}");
                }
                catch (Exception ex)
                {
                    _logger.ErrorLog($"SubsidyVoucherService.BackgroundProcessing Exception", ex);
                }
                await Task.Delay(10000, stoppingToken);
            }
        }
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.Information("Queued Hosted SubsidyVoucherService is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }

}
