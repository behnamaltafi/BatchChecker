using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Formatting.Json;
using Serilog.Sinks.Elasticsearch;
using Serilog.Sinks.File;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace BatchChecker.Extensions
{
    public static class ServiceExtensions
    {
        public static void AddElasticSearch(this IServiceCollection services)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile(
                    $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json",
                    optional: true)
                .Build();
                 Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .WriteTo.Debug()
                .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(configuration["ElasticConfiguration:Uri"]))
                {
                    AutoRegisterTemplate = true,
                    OverwriteTemplate = true,
                    DetectElasticsearchVersion = true,
                    AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
                    NumberOfReplicas = 1,
                    NumberOfShards = 2,
                    FailureCallback = e => Console.WriteLine("Unable to submit event " + e.MessageTemplate),
                    EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                                       EmitEventFailureHandling.WriteToFailureSink |
                                       EmitEventFailureHandling.RaiseCallback,
                    FailureSink = new FileSink($"./logs/log-{DateTime.Now.ToString("yyyyMMdd")}.txt", new JsonFormatter(), null, null),
                    IndexFormat = $"{Assembly.GetExecutingAssembly().GetName().Name.ToLower().Replace(".", "-")}-{environment?.ToLower().Replace(".", "-")}-{DateTime.UtcNow:yyyy-MM}"
                })
                .Enrich.WithProperty("Environment", environment)
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            services.AddSingleton<ILogger>(Log.Logger);
        }
    }
}
