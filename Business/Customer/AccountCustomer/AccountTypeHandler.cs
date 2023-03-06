using BatchChecker.Dto;
using BatchChecker.Dto.FetchCDC;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.AccountCustomer
{
    public class AccountTypeHandler : AbstractAccountCustomerHandler
    {
        private readonly Dictionary<string, AccountTypeStatus> AccountTypes =
            new Dictionary<string, AccountTypeStatus>() {
                { "030", AccountTypeStatus.SepordeKootahModat },
                { "045", AccountTypeStatus.SepordeBolandModat },
                { "010", AccountTypeStatus.Jari },
                { "040", AccountTypeStatus.GharzolHasane }
            };
        public AccountTypeHandler(CustomerSourceDetailResult _get) : base(_get)
        {
        }
        public override ResponseContext execute(AccountResponseCdcDto accountResponse)
        {
            if (accountResponse == null || !AccountTypes.ContainsKey(accountResponse.ACCTTYPE))
            {
                _customerSourceDetailResult.AccType = AccountTypeStatus.Undefined;
            }
            else
            {
                _customerSourceDetailResult.AccType = AccountTypes[accountResponse.ACCTTYPE];
            }

            return _next == null ? null : _next.execute(accountResponse);
        }
    }
}
