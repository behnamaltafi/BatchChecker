using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.FetchCDC;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.CustomerType
{
    public abstract class AbstractCustomerTypeStatusHandler : IHandler
    {
        protected AbstractCustomerTypeStatusHandler _next;
        protected CustomerSourceDetailResult _customerSourceDetailResult;

        public AbstractCustomerTypeStatusHandler(CustomerSourceDetailResult customerSourceDetailResult)
        {
            _customerSourceDetailResult = customerSourceDetailResult;
        }

        public void SetSuccessor(AbstractCustomerTypeStatusHandler next)
        {
            _next = next;
        }
        public abstract ResponseContext execute(CustomerResponseCdcDto customerResponses);
    }
}
