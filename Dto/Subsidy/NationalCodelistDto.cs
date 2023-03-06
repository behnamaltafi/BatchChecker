using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOp.Infrastructure.Shared.Services.Utility;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Subsidy
{
    public class NationalCodelistDto
    {
        public long Detail_ID { get; set; }
        public string AccountNo { get; set; }
        public string NationalCode { get; set; }
        public int? ValidationStatus { get; set; }
        public string PaymentAccount { get; set; }
        public string BranchCode { get; set; }
        public string AreaCode { get; set; }
        public string GRP { get; set; }
        public string PhoneNumber { get; set; }
        public int? Host_ID  { get; set; }
        public long Amount { get; set; }
    }
}
