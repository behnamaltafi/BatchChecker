using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Signature
{
    public class SignatureDetailControlResult
    {
        public bool NationalCodeVerified { get; set; }
        public bool TejaratCustomerVerified { get; set; }
        public bool HasError { get; set; }
        public long SignatureourceDetailId { get; set; }
        public GenericEnum Status { get; set; }
        public string ErrorDescription { get; set; }
        public DateTime? ProcessDate { get; set; }
    }
}
