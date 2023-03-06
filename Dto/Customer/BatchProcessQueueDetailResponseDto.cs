using BatchOp.Domain.Common;
using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOp.Infrastructure.Shared.Services.Utility;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Customer
{
    public class BatchProcessQueueDetailResponseDto 
    {
        public long ID { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public DateTime? ProcessStartDate { get; set; }
        public DateTime? ProcessEndDate { get; set; }
        public GenericEnum Status { get; set; }
        public long CustomerSourceHeader_ID { get; set; }
    }
}
