using AutoMapper;
using BatchChecker.Checkers;
using BatchChecker.Dto.Share;
using BatchChecker.Extensions;
using BatchChecker.FetchCDC;
using BatchChecker.FetchGalaxy;
using BatchChecker.FetchQlickView;
using BatchChecker.Interfaces;
using BatchChecker.Mapping;
using BatchChecker.Repository;
using BatchChecker.RequestCheckers;
using BatchChecker.RequestCheckers.FinDoc;
using BatchChecker.Services.Customer;
using BatchChecker.Services.NocrInquiry;
using BatchChecker.Services.OpenAcc;
using BatchChecker.Services.Signature;
using BatchChecker.Services.Staff;
using BatchOp.Application;
using BatchOp.Application.AOP;
using BatchOp.Application.Interfaces;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Application.Interfaces.Repositories.Subsidy;
using BatchOp.Infrastructure.Persistence;
using BatchOp.Infrastructure.Persistence.DapperRepositories;
using BatchOp.Infrastructure.Persistence.DapperRepository;
using BatchOp.Infrastructure.Persistence.MySqlConnectionProvider;
using BatchOp.Infrastructure.Persistence.Repositories;
using BatchOp.Infrastructure.Persistence.Services;
using BatchOp.Infrastructure.Shared.ApiCaller;
using BatchOp.Infrastructure.Shared.ApiCaller.Galaxy;
using BatchOp.Infrastructure.Shared.ApiCaller.Model;
using BatchOp.Infrastructure.Shared.ApiCaller.TataGateway;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOP.Domain.Entities;
using BatchOP.Domain.Entities.Customer;
using Castle.DynamicProxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace BatchChecker
{
    public class Program
    {
        private static IConfiguration _configuration;
        public static async Task Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");

            //Read Configuration from appSettings
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            //Initialize Logger
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();

            DapperConfig();

            _configuration = builder.Build();
            //  string rabbitmqconnection = $"amqp://{HttpUtility.UrlEncode(_configuration["RabbitMQ:Username"])}:{HttpUtility.UrlEncode(_configuration["RabbitMQ:Password"])}@{_configuration["RabbitMQ:Host"]}";
            using var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddElasticSearch();

                    services.AddSingleton(new ProxyGenerator());
                    services.AddTransient<IInterceptor, LogInterceptor>();
                    services.AddProxiedTransient<ISubsidRepository, SubsidyRepository>();


                    services.AddHostedService<CustomerPublisherService>();
                    services.AddHostedService<CustomerSubscriberService>();
                    services.AddHostedService<StaffPublisherService>();
                    services.AddHostedService<StaffSubscriberService>();
                    services.AddHostedService<OpenAccPublisherService>();
                    services.AddHostedService<OpenAccSubscriberService>();
                    services.AddHostedService<SignaturePublisherService>();
                    services.AddHostedService<SignatureSubscriberService>();
                    services.AddHostedService<NocrInquiryPublisherService>();
                    services.AddHostedService<NocrInquirySubscriberService>();

                    //services.AddHostedService<FinDocPublisherService>();
                    //services.AddHostedService<FinDocSubscriberService>();
                    //services.AddHostedService<PayaPublisherService>();
                    //services.AddHostedService<PayaSubscriberService>();

                    //services.AddHostedService<SamatPublisherService>();
                    //services.AddHostedService<SamatSubscriberService>();
                    //services.AddHostedService<SamaChequePublisherService>();
                    //services.AddHostedService<SamaChequeSubscriberService>();

                    services.AddTransient((_) => new SqlConnectionProvider(config.GetConnectionString("DefaultConnection")));
                    //services.AddTransient<SqlConnectionProvider>();
                    services.AddScoped<ICheckerFactory, CheckerFactory>();

                    services.AddTransient<StaffChecker>()
                       .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, StaffChecker>(s => s.GetService<StaffChecker>());

                    services.AddTransient<NocrInquiryChecker>()
                    .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, NocrInquiryChecker>(s => s.GetService<NocrInquiryChecker>());

                    services.AddTransient<SamatChecker>()
                       .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, SamatChecker>(s => s.GetService<SamatChecker>());

                    services.AddTransient<SamaChequeChecker>()
                      .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, SamaChequeChecker>(s => s.GetService<SamaChequeChecker>());



                    services.AddTransient<CustomerChecker>()
                      .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, CustomerChecker>(s => s.GetService<CustomerChecker>());

                    //services.AddTransient<FinDocCheker>()
                    //  .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, FinDocCheker>(s => s.GetService<FinDocCheker>());

                    services.AddTransient<OpenAccChecker>()
                  .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, OpenAccChecker>(s => s.GetService<OpenAccChecker>());

                    services.AddTransient<SignatureChecker>()
                    .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, SignatureChecker>(s => s.GetService<SignatureChecker>());

                    services.AddTransient<PayaChecker>()
                   .AddTransient<IRequestChecker<BatchProcessQueueDetailRequestDto>, PayaChecker>(s => s.GetService<PayaChecker>());

                    services.AddTransient<IConnectionProvider, ConnectionProvider>();
                    services.AddTransient<IPublisher, Publisher>();
                    services.AddTransient<ISubscriber, Subscriber>();
                    services.AddTransient<IDapperRepository, ZDapperPlusRepository>();
                    services.AddTransient<ICDCRepository, CDCRepository>();
                    services.AddTransient<IDapperUnitOfWork, DapperUnitOfWork>();

                    var mock = config.GetValue<bool>("IsMock");
                    if(mock)
                        services.AddProxiedTransient<IApiCaller<ResponseTokenDto>, MockApiCaller>();
                    else
                        services.AddProxiedTransient<IApiCaller<ResponseTokenDto>, ApiCaller>();
                    services.AddProxiedTransient<IFetchFromCDC, FetchFromCDC>();
                    services.AddTransient<IGalaxyApi, GalaxyApi>();
                    services.AddTransient<ITataGatewayApi, TataGatewayApi>();
                    services.AddAutoMapper(typeof(GeneralProfile));
                    services.AddTransient<IBaseInfoSettingRepository, BaseInfoSettingRepository>();
                    services.AddTransient<IGalaxyRepository, GalaxyRepository>();
                    services.AddTransient<IFetchFromGalaxy, FetchFromGalaxy>();
                    services.AddTransient<IQlickViewRepository, QlickViewRepository>();
                    services.AddTransient<IFetchFromQlickView, FetchFromQlickView>();
                })
                .Build();

            await host.StartAsync();

            await host.WaitForShutdownAsync();
        }

        private static void DapperConfig()
        {
            BulkDapperConfig.MappForBulkDapper(new CustomerSourceDetail());
            BulkDapperConfig.MappForBulkDapper(new CustomerDetailControl());
            BulkDapperConfig.MappForBulkDapper(new BatchProcessQueue());
            BulkDapperConfig.MappForBulkDapper(new CustomerSourceHeader());
        }
    }
}
