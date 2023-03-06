using BatchChecker.Dto;
using BatchChecker.Dto.FetchCDC;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.AccountCustomer
{
    public class AccountStatusHandler : AbstractAccountCustomerHandler
    {
        private readonly Dictionary<string, BatchOP.Domain.Enums.AccountStatus> AccountStatuses =
            new Dictionary<string, BatchOP.Domain.Enums.AccountStatus>() {
                { "02", BatchOP.Domain.Enums.AccountStatus.Active },
                { "03", BatchOP.Domain.Enums.AccountStatus.Closed },
                { "05", BatchOP.Domain.Enums.AccountStatus.Frozen }
            };

        public AccountStatusHandler(CustomerSourceDetailResult _get) : base(_get)
        {
        }
        public override ResponseContext execute(AccountResponseCdcDto accountResponse)
        {
            if (accountResponse == null || !AccountStatuses.ContainsKey(accountResponse.STATCD))
            {
                _customerSourceDetailResult.AccStatus = BatchOP.Domain.Enums.AccountStatus.Undefined;
            }
            else
            {
                _customerSourceDetailResult.AccStatus = AccountStatuses[accountResponse.STATCD];
            }

            return _next == null ? null : _next.execute(accountResponse);
        }
    }
}
