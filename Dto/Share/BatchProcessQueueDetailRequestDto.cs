using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Share
{
    public class BatchProcessQueueDetailRequestDto
    {
        public long ID { get; set; }
        public long SourceHeaderId { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public int TryCount { get; set; }
        public RequestType RequestType { get; set; }
    }
}
