using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.OpenAcc
{
    public class OpenAccSourceDetailResult
    {
        public GenericEnum Status { get; set; }
        public bool HasError { get; set; }
        public bool NationalCodeVerified { get; set; }
        public bool NationalCodeDuplaicate { get; set; }
        public bool MobileNumberVerified { get; set; }
        public bool MobileNumberDuplicate { get; set; }
        public bool MobileNumberSHAHKAR { get; set; }
        public bool EmailVerified { get; set; }
        public bool CustomerIsBank { get; set; }
        public long OpenAccSourceDetail_ID { get; set; }
        public string CreatedBy { get; set; }
        public DateTime Created { get; set; }
        public string LastModifiedBy { get; set; }
        public DateTime? LastModified { get; set; }
    }
}
