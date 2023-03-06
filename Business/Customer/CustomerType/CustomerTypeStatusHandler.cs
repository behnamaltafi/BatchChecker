using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.FetchCDC;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.CustomerType
{
    public class CustomerTypeStatusHandler : AbstractCustomerTypeStatusHandler
    {
        private readonly Dictionary<string, CustomerTypeStatus> _DeathStatus =
    new Dictionary<string, CustomerTypeStatus>() {
                { "01", CustomerTypeStatus.Haghighi },
                { "02", CustomerTypeStatus.Hoghughi }
                  };
        public CustomerTypeStatusHandler(CustomerSourceDetailResult _get) : base(_get)
        {
        }
        public override ResponseContext execute(CustomerResponseCdcDto customerResponses)
        {
            if (customerResponses == null || !_DeathStatus.ContainsKey(customerResponses.CUSTYPE))
            {
                _customerSourceDetailResult.CustomerType = CustomerTypeStatus.Undefined;
            }
            else
            {
                _customerSourceDetailResult.CustomerType = _DeathStatus[customerResponses.CUSTYPE];
            }

            return _next == null ? null : _next.execute(customerResponses);
        }
    }
}
