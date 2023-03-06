using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOp.Infrastructure.Shared.Services.Utility;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto
{
    public class FinDocSourceDetailResult
    {
        public string AccountHeading { get; set; }
        public string AccountNumber { get; set; }
        public AccountDetection AccountDetection { get; set; }
        public GenericEnum Status { get; set; }
        public HostStatus HostStatus { get; set; }
        public bool HasError { get; set; }
        public long FinDocSourceDetail_ID { get; set; }
        public string CreatedBy { get; set; }
        public DateTime Created { get; set; }
        public string LastModifiedBy { get; set; }
        public DateTime? LastModified { get; set; }
    }
}
