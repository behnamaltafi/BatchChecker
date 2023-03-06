using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.SerilogEnricher
{
    public class ActivityEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var activity = Activity.Current;

            logEvent.AddPropertyIfAbsent(new LogEventProperty("SpanId", new ScalarValue(activity.GetSpanId())));
            logEvent.AddPropertyIfAbsent(new LogEventProperty("TraceId", new ScalarValue(activity.GetTraceId())));
            logEvent.AddPropertyIfAbsent(new LogEventProperty("ParentId", new ScalarValue(activity.GetParentId())));
        }
    }
}
