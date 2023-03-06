using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BatchOP.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOp.Infrastructure.Shared.Services.Utilities;
using System.Transactions;
using BatchOP.Domain.Entities.Customer;
using System.Linq;
using BatchOP.Domain.Enums;
using BatchChecker.Checkers;
using BatchChecker.Dto;
using Serilog;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchChecker.Dto.Share;
using BatchChecker.Extensions;

namespace BatchChecker.Services.Customer
{

    public class CustomerPublisherService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly ISubscriber _subscriber;
        private readonly IPublisher _publisher;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICheckerFactory _checkerFactory;


        public CustomerPublisherService(
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

                    var checker = _checkerFactory.CreateChecker<BatchProcessQueueDetailRequestDto>(CheckType.CustomerChecker);
                    var queryResponse = await checker.SenderAsync(stoppingToken);
                    if (queryResponse.Count() != 0)
                    {
                        IList<BatchProcessQueue> lstBatchProcessQueue = new List<BatchProcessQueue>();
                        foreach (var response in queryResponse)
                        {
                            var messageModel = new QueueMessageModel<BatchProcessQueueDetailRequestDto>()
                            {
                                Item = new List<BatchProcessQueueDetailRequestDto>() { response },
                                checkType = CheckType.CustomerChecker
                            };

                            var message = JsonConvert.SerializeObject(messageModel);
                            _logger.Information($"Send CustomerPublisherService.Message To RabbitMQ : {message}");
                            _publisher.Publish(message, queueName.CustomerQueueName);

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
                        _logger.Information($"BulkUpdate CustomerBatchProcessQueueDetail Count:{lstBatchProcessQueue.Count}");
                    }
                    _logger.Information("End CustomerPublisherService.BackgroundProcessing");
                }
                catch (Exception ex)
                {
                    _logger.ErrorLog($"CustomerPublisherService.BackgroundProcessing Exception", ex);
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
                var checker = _checkerFactory.CreateChecker<BatchProcessQueueDetailRequestDto>(CheckType.CustomerChecker);
                var queryResponse = await checker.GetMissedSenderAsync(stoppingToken);
                if (queryResponse.Count() != 0)
                {
                    IList<BatchProcessQueue> lstBatchProcessQueue = new List<BatchProcessQueue>();
                    foreach (var response in queryResponse)
                    {
                        var messageModel = new QueueMessageModel<BatchProcessQueueDetailRequestDto>()
                        {
                            Item = new List<BatchProcessQueueDetailRequestDto>() { response },
                            checkType = CheckType.CustomerChecker
                        };

                        var message = JsonConvert.SerializeObject(messageModel);
                        _logger.Information($"Send Missed CustomerPublisherService.Message To RabbitMQ : {message}");
                        _publisher.Publish(message, queueName.CustomerQueueName);

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
                    _logger.Information($"Missed: BulkUpdate CustomerBatchProcessQueueDetail Count:{lstBatchProcessQueue.Count}");
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorLog($"CustomerPublisherService.Missed.BackgroundProcessing Exception:", ex);
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.Information("Queued Hosted CustomerPublisherService is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }

}
