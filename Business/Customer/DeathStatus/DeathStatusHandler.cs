using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.FetchCDC;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.DeathStatus
{
    public class DeathStatusHandler : AbstractDeathStatusHandler
    {
        private readonly Dictionary<string, BatchOP.Domain.Enums.DeathStatus> _DeathStatus =
    new Dictionary<string, BatchOP.Domain.Enums.DeathStatus>() {
                { "0", BatchOP.Domain.Enums.DeathStatus.Dead },
                { "1", BatchOP.Domain.Enums.DeathStatus.Alive }
                  };
        public DeathStatusHandler(CustomerSourceDetailResult _get) : base(_get)
        {
        }
        public override ResponseContext execute(CustomerResponseCdcDto customerResponses)
        {
            if (customerResponses == null || !_DeathStatus.ContainsKey(customerResponses.DEATH_STATUS))
            {
                _customerSourceDetailResult.DeathStatus = BatchOP.Domain.Enums.DeathStatus.Undefined;
            }
            else
            {
                _customerSourceDetailResult.DeathStatus = _DeathStatus[customerResponses.DEATH_STATUS];
            }

            return _next == null ? null : _next.execute(customerResponses);
        }
    }
}
