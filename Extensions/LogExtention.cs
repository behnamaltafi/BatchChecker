using Newtonsoft.Json;
using Serilog;
using Serilog.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Extensions
{
    public static class LogExtention
    {
        public static void ErrorLog(this ILogger log,string message,Exception e)
        {
            if (e.InnerException != null) LogContext.PushProperty("InnerExeption:", JsonConvert.SerializeObject(e.InnerException));
            if (e.StackTrace != null) LogContext.PushProperty("StackTrace:", JsonConvert.SerializeObject(e.StackTrace));
            log.Error($"{message}:{e.Message}");

        }
    }
}
