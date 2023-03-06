using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Customer
{
    public class SourceHeaderDto
    {
        public long RequestId { get; set; }
        public long SourceHeaderId { get; set; }
        public int RequestCount { get; set; }
    }
}
