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

namespace BatchChecker.Services.Subsidy
{

    public class SubsidyPublisherService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly IPublisher _publisher;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICheckerFactory _checkerFactory;


        public SubsidyPublisherService(
            ILogger logger,
            IConfiguration configuration,
            ISubscriber subscriber,
            IPublisher publisher,
            IDapperRepository dapperRepository,
            ICheckerFactory checkerFactory)
        {

            _logger = logger;
            _configuration = configuration;
            _publisher = publisher;
            _dapperRepository = dapperRepository;
            _checkerFactory = checkerFactory;
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
            await MissedBackgroundProcessing(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var queueName = _configuration.GetSection("RabbitQueueName").Get<RabbitMqCheckerQueueName>();

                    var checker = _checkerFactory.CreateChecker<BatchProcessQueueDetailRequestDto>(CheckType.SubsidyChecker);
                    var queryResponse = await checker.SenderAsync(stoppingToken);
                    if (queryResponse.Count() != 0)
                    {
                        IList<BatchProcessQueue> lstBatchProcessQueue = new List<BatchProcessQueue>();
                        foreach (var response in queryResponse)
                        {
                            var messageModel = new QueueMessageModel<BatchProcessQueueDetailRequestDto>()
                            {
                                Item = new List<BatchProcessQueueDetailRequestDto>() { response },
                                checkType = CheckType.SubsidyChecker
                            };

                            var message = JsonConvert.SerializeObject(messageModel);
                            _logger.Information($"Send SubsidyPublisherService.Message To RabbitMQ : {message}");
                            _publisher.Publish(message, queueName.SubSidyQueueName);

                            BatchProcessQueue BatchProcessQueue = new BatchProcessQueue()
                            {
                                SourceHeaderId = response.SourceHeaderId,
                                EndIndex = response.EndIndex,
                                Id = response.ID,
                                StartIndex = response.StartIndex,
                                Status = GenericEnum.InQueue,
                                RequestType = RequestType.Subsidy,
                                SentToQueueDate = DateTime.Now,
                                TryCount = response.TryCount
                            };
                            lstBatchProcessQueue.Add(BatchProcessQueue);
                        }
                        _dapperRepository.BulkUpdate(lstBatchProcessQueue);
                        _logger.Information($"BulkUpdate SubsidyBatchProcessQueueDetail Count:{lstBatchProcessQueue.Count}");
                    }
                    _logger.Information("End SubsidyPublisherService.BackgroundProcessing");
                }
                catch (Exception ex)
                {
                    _logger.ErrorLog($"SubsidyPublisherService.BackgroundProcessing Exception", ex);
                }


                await Task.Delay(5000, stoppingToken);
            }
        }
        private async Task MissedBackgroundProcessing(CancellationToken stoppingToken)
        {
            await PublishMissedMessage(stoppingToken);
        }

        private async Task PublishMissedMessage(CancellationToken stoppingToken)
        {
            try
            {
                var queueName = _configuration.GetSection("RabbitQueueName").Get<RabbitMqCheckerQueueName>();
                var checker = _checkerFactory.CreateChecker<BatchProcessQueueDetailRequestDto>(CheckType.SubsidyChecker);
                var queryResponse = await checker.GetMissedSenderAsync(stoppingToken);
                if (queryResponse.Count() != 0)
                {
                    IList<BatchProcessQueue> lstBatchProcessQueue = new List<BatchProcessQueue>();
                    foreach (var response in queryResponse)
                    {
                        var messageModel = new QueueMessageModel<BatchProcessQueueDetailRequestDto>()
                        {
                            Item = new List<BatchProcessQueueDetailRequestDto>() { response },
                            checkType = CheckType.SubsidyChecker
                        };

                        var message = JsonConvert.SerializeObject(messageModel);
                        _logger.Information($"Send Missed SubsidPublisherService.Message To RabbitMQ : {message}");
                        _publisher.Publish(message, queueName.SubSidyQueueName);

                        BatchProcessQueue BatchProcessQueue = new BatchProcessQueue()
                        {
                            SourceHeaderId = response.SourceHeaderId,
                            EndIndex = response.EndIndex,
                            Id = response.ID,
                            StartIndex = response.StartIndex,
                            Status = GenericEnum.InQueue,
                            RequestType = RequestType.Subsidy,
                            SentToQueueDate = DateTime.Now,
                            TryCount = response.TryCount
                        };
                        lstBatchProcessQueue.Add(BatchProcessQueue);
                    }
                    _dapperRepository.BulkUpdate(lstBatchProcessQueue);
                    _logger.Information($"Missed: BulkUpdate SubsidyProcessQueueDetail Count:{lstBatchProcessQueue.Count}");
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorLog($"SubsidyPublisherService.Missed.BackgroundProcessing Exception", ex);
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.Information("Queued Hosted SubsidyPublisherService is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }

}
