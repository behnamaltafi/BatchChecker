using BatchChecker.Dto;
using BatchChecker.Dto.FetchCDC;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.AccountCustomer
{
    public abstract class AbstractAccountCustomerHandler : IHandler
    {
        protected AbstractAccountCustomerHandler _next;
        protected CustomerSourceDetailResult _customerSourceDetailResult;

        public AbstractAccountCustomerHandler(CustomerSourceDetailResult customerSourceDetailResult)
        {
            _customerSourceDetailResult = customerSourceDetailResult;
        }

        public void SetSuccessor(AbstractAccountCustomerHandler next)
        {
            _next = next;
        }
        public abstract ResponseContext execute(AccountResponseCdcDto accountResponses);
    }
}
