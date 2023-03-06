using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Staff
{
    public class StaffBatchProcessQueueDetailRequestDto
    {
        public long ID { get; set; }
        public long SourceHeaderId { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public int TryCount { get; set; }
    }
}
