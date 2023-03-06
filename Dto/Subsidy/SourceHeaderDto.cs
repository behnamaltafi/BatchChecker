using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Subsidy
{
    public class SubsidySourceHeaderDto
    {
        public long RequestId { get; set; }
        public long SourceHeaderId { get; set; }
        public int RequestCount { get; set; }
    }
}
