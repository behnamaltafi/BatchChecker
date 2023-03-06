using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.Dto
{
    public class PayaSourceDetailResult
    {
        public bool DestinationIbanVerified { get; set; }
        public bool AmountVerified { get; set; }
        public string DestinationBankCode { get; set; }
        public string DestinationBankName { get; set; }
        public string DestinationAccountStatus { get; set; }
        public string DestinationAccountStatusDesc { get; set; }
        public string OwnerFirstName { get; set; }
        public string OwnerLastName { get; set; }
        public bool HasError { get; set; }
        public long PayaSourceDetailId { get; set; }
        public GenericEnum Status { get; set; }

        public string CreatedBy { get; set; }
        public DateTime Created { get; set; }
        public string LastModifiedBy { get; set; }
        public DateTime? LastModified { get; set; }
    }
}
