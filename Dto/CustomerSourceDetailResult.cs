using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOp.Infrastructure.Shared.Services.Utility;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto
{
    public class CustomerSourceDetailResult
    {
        public GenericEnum Status { get; set; }
        public bool AccVerified { get; set; }
        public bool AmountVerified { get; set; }
        public bool DepositeVerified { get; set; }
        public CurrencyTypeStatus CurrencyType { get; set; }
        public AccountTypeStatus AccType { get; set; }
        public AccountStatus AccStatus { get; set; }
        public CustomerTypeStatus CustomerType { get; set; }
        public DeathStatus DeathStatus { get; set; }
        public BlackListStatus BlackListStatus { get; set; }
        public bool HasError { get; set; }
        public long CustomerSourceDetail_ID { get; set; }
        public string CreatedBy { get; set; }
        public DateTime Created { get; set; }
        public string LastModifiedBy { get; set; }
        public DateTime? LastModified { get; set; }
    }
}
