using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOp.Infrastructure.Shared.Services.Utility;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto
{
    public class NocrSourceDetailResult
    {
        public GenericEnum Status { get; set; }
        public bool NationalCodeVerified { get; set; }
        public bool FirstNameVerified { get; set; }
        public bool LastNameVerified { get; set; }
        public bool BirthDateIsValid { get; set; }
        public bool BirthDateVerified { get; set; }
        public bool FatherNameVerified { get; set; }
        public bool HasError { get; set; }
        public long NocrInquirySourceDetail_ID { get; set; }
        public string CreatedBy { get; set; }
        public DateTime Created { get; set; }
        public string LastModifiedBy { get; set; }
        public DateTime? LastModified { get; set; }
    }
}
