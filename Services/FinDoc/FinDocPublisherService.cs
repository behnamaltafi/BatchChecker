using BatchChecker.Checkers;
using BatchChecker.Dto;
using BatchChecker.Dto.Share;
using BatchChecker.Extensions;
using BatchChecker.RequestCheckers.FinDoc;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOP.Domain.Entities.Customer;
using BatchOP.Domain.Entities.FinDoc;
using BatchOP.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Services.FinDoc
{
    public class FinDocPublisherService : BackgroundService
    {

        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly ISubscriber _subscriber;
        private readonly IPublisher _publisher;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICheckerFactory _checkerFactory;


        public FinDocPublisherService(
            ILogger logger,
            IConfiguration configuration,
            ISubscriber subscriber,
            IPublisher publisher,
            IDapperRepository dapperRepository,
            ICheckerFactory checkerFactory)
        {

            _logger = logger;
            _configuration = configuration;
            _subscriber = subscriber;
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
                    var checker = _checkerFactory.CreateChecker<BatchProcessQueueDetailRequestDto>(CheckType.FinDocCheker);
                    var queryResponse = await checker.SenderAsync(stoppingToken);
                    if (queryResponse.Count() != 0)
                    {
                        IList<BatchProcessQueue> lstBatchProcessQueue = new List<BatchProcessQueue>();
                        foreach (var response in queryResponse)
                        {
                            var messageModel = new QueueMessageModel<BatchProcessQueueDetailRequestDto>()
                            {
                                Item = new List<BatchProcessQueueDetailRequestDto>() { response },
                                checkType = CheckType.FinDocCheker
                            };

                            var message = JsonConvert.SerializeObject(messageModel);
                            _logger.Information($"Send OpenAccPublisherService.Message To RabbitMQ : {message}");
                            _publisher.Publish(message, queueName.FinDocQueueName);

                            BatchProcessQueue BatchProcessQueue = new BatchProcessQueue()
                            {
                                SourceHeaderId = response.SourceHeaderId,
                                EndIndex = response.EndIndex,
                                Id = response.ID,
                                StartIndex = response.StartIndex,
                                Status = GenericEnum.InQueue,
                                RequestType = response.RequestType,
                                SentToQueueDate = DateTime.Now,
                                TryCount = response.TryCount
                            };
                            lstBatchProcessQueue.Add(BatchProcessQueue);
                        }
                        _dapperRepository.BulkUpdate(lstBatchProcessQueue);
                        _logger.Information($"BulkUpdate OpenAccBatchProcessQueueDetail Count:{lstBatchProcessQueue.Count}");
                    }
                    _logger.Information("End OpenAccPublisherService.BackgroundProcessing");
                }
                catch (Exception ex)
                {
                    _logger.ErrorLog($"OpenAccPublisherService.BackgroundProcessing Exception:", ex);
                }
                await Task.Delay(8000, stoppingToken);
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
                var checker = _checkerFactory.CreateChecker<BatchProcessQueueDetailRequestDto>(CheckType.OpenAccChecker);
                var queryResponse = await checker.GetMissedSenderAsync(stoppingToken);
                if (queryResponse.Count() != 0)
                {
                    IList<BatchProcessQueue> lstBatchProcessQueue = new List<BatchProcessQueue>();
                    foreach (var response in queryResponse)
                    {
                        var messageModel = new QueueMessageModel<BatchProcessQueueDetailRequestDto>()
                        {
                            Item = new List<BatchProcessQueueDetailRequestDto>() { response },
                            checkType = CheckType.FinDocCheker
                        };

                        var message = JsonConvert.SerializeObject(messageModel);
                        _logger.Information($"Send Missed OpenAccPublisherService.Message To RabbitMQ : {message}");
                        _publisher.Publish(message, queueName.FinDocQueueName);

                        BatchProcessQueue BatchProcessQueue = new BatchProcessQueue()
                        {
                            SourceHeaderId = response.SourceHeaderId,
                            EndIndex = response.EndIndex,
                            Id = response.ID,
                            StartIndex = response.StartIndex,
                            Status = GenericEnum.InQueue,
                            RequestType = response.RequestType,
                            SentToQueueDate = DateTime.Now,
                            TryCount = response.TryCount
                        };
                        lstBatchProcessQueue.Add(BatchProcessQueue);
                    }
                    _dapperRepository.BulkUpdate(lstBatchProcessQueue);
                    _logger.Information($"Missed: BulkUpdate OpenAccBatchProcessQueueDetail Count:{lstBatchProcessQueue.Count}");
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorLog($"OpenAccPublisherService.Missed.BackgroundProcessing Exception:", ex);
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.Information("Queued Hosted OpenAccPublisherService is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }
}
