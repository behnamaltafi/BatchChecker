using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.FinDoc
{
    public class SourceDetailTempListDto
    {
        public long Id { get; set; }
        public int RowIdentifier { get; set; }
        public string BankCode { get; set; }
        public string OwnerBranchCode { get; set; }
        public string JournalNumber { get; set; }
        public string IssuerBranchCode { get; set; }
        public string AccountNumber { get; set; }
        public string EffectiveDate { get; set; }
        public string Time { get; set; }
        public string ArticleNumber { get; set; }
        public string DebitCreditType { get; set; }
        public string Amount { get; set; }
        public string ReturnCode { get; set; }
        public string ComprehensiveCode { get; set; }
        public string GuaranteeTypeCode { get; set; }
        public string ProgramName { get; set; }
        public string DestinationBranchCode { get; set; }
        public string ArticleDesc { get; set; }
        public string OperationCode { get; set; }
        public string SubsystemName { get; set; }
        public string PKNumber { get; set; }
        public string SessionId { get; set; }
        public GenericEnum Status { get; set; }
        public GenericEnum MappingStatus { get; set; }
        public long FinDocSourceHeaderTemp_ID { get; set; }
    }
}
