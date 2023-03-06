using BatchChecker.Dto;
using BatchChecker.Dto.FetchCDC;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.AccountCustomer
{
    public class AccuracyHandler : AbstractAccountCustomerHandler
    {
        private readonly Dictionary<string, CurrencyTypeStatus> CurrencyTypes =
    new Dictionary<string, CurrencyTypeStatus>() {
                { "00", CurrencyTypeStatus.Riali },
                { "01", CurrencyTypeStatus.Arzi }
                  };
        public AccuracyHandler(CustomerSourceDetailResult _get) : base(_get)
        {
        }
        public override ResponseContext execute(AccountResponseCdcDto accountResponse)
        {
            if (accountResponse == null || !CurrencyTypes.ContainsKey(accountResponse.ACCURRCY))
            {
                _customerSourceDetailResult.CurrencyType = CurrencyTypeStatus.Undefined;
            }
            else
            {
                _customerSourceDetailResult.CurrencyType = CurrencyTypes[accountResponse.ACCURRCY];
            }

            return _next == null ? null : _next.execute(accountResponse);
        }
    }
}
