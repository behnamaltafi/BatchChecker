using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.WageApi
{
    public class WageInquiryRequestDto
    {
        public string AccountNumber { get; set; }
        public CurrencyDto amount { get; set; }
        public int recordCount { get; set; }
        public string requesterBranchCode { get; set; }
        public bool retrieveBalanceData { get; set; }
        public string serviceTypeCode { get; set; }
    }
}
