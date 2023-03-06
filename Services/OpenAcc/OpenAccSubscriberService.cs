using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchChecker.Checkers;
using BatchChecker.Dto;
using System.Linq;
using Serilog;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces;
using BatchChecker.Dto.Share;
using BatchChecker.Extensions;

namespace BatchChecker.Services.OpenAcc
{

    public class OpenAccSubscriberService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly ICheckerFactory _checkerFactory;
        private readonly IConfiguration _configuration;
        private readonly ISubscriber _subscriber;
        private readonly IPublisher _publisher;
        private readonly IDapperRepository _dapperRepository;
        private readonly ICDCRepository _cDCRepository;

        public OpenAccSubscriberService(
            ILogger logger,
            ICheckerFactory checkerFactory,
            IConfiguration configuration,
            ISubscriber subscriber,
            IPublisher publisher,
            IDapperRepository dapperRepository,
            ICDCRepository cDCRepository)
        {
            _logger = logger;
            _checkerFactory = checkerFactory;
            _configuration = configuration;
            _subscriber = subscriber;
            _publisher = publisher;
            _dapperRepository = dapperRepository;
            _cDCRepository = cDCRepository;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await BackgroundProcessing(stoppingToken);
        }
        private async Task BackgroundProcessing(CancellationToken stoppingToken)
        {
            try
            {
                _logger.Information($"OpenAccSubscriberService.BackgroundProcessing Start");

                var queueName = _configuration.GetSection("RabbitQueueName").Get<RabbitMqCheckerQueueName>();
                _logger.Information($"OpenAccSubscriberService.BackgroundProcessing Queuenames {JsonConvert.SerializeObject(queueName)}");

                _subscriber.Subscribe(queueName.OpenAccQueueName, ProcessMessage);
                await Task.Delay(10000, stoppingToken);
                await Task.FromResult(0);

                _logger.Information($"OpenAccSubscriberService.BackgroundProcessing End");
            }
            catch (Exception ex)
            {
                _logger.ErrorLog($"OpenAccSubscriberService.BackgroundProcessing Exception:", ex);
            }
        }
        //private async Task<bool> ProcessMessageAsync(string message, CancellationToken stoppingToken)
        //{
        //    //var msgModel = JsonConvert.DeserializeObject<QueueMessageModel>(message);
        //    var customerResponse = JsonConvert.DeserializeObject<BatchProcessQueueDetailDto>(message);
        //    //var checker = _checkerFactory.CreatChecker(msgModel.CheckType);
        //    //checker.CheckAsync(stoppingToken).Wait();
        //    return await Task.FromResult(true);
        //}

        /// <summary>
        /// پردازش پیام های دریافتی از RabbitMQ
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private bool ProcessMessage(string message)
        {
            _logger.Information($"OpenAccSubscriberService.ProcessMessage Start:{message}");

            CancellationToken cancellationToken = new CancellationToken();
            var msgModel = JsonConvert.DeserializeObject<QueueMessageModel<BatchProcessQueueDetailRequestDto>>(message);

            var checker = _checkerFactory.CreateChecker<BatchProcessQueueDetailRequestDto>(msgModel.checkType);
            var task = Task.Run(() => checker.CheckAsync(msgModel.Item.First(), cancellationToken));
            task.Wait();

            _logger.Information($"OpenAccSubscriberService.ProcessMessage Result:{task.Result.ToString()}");
            return true;
        }
    }
}
