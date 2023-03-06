using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.FetchCDC;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.DeathStatus
{
    public abstract class AbstractDeathStatusHandler : IHandler
    {
        protected AbstractDeathStatusHandler _next;
        protected CustomerSourceDetailResult _customerSourceDetailResult;

        public AbstractDeathStatusHandler(CustomerSourceDetailResult customerSourceDetailResult)
        {
            _customerSourceDetailResult = customerSourceDetailResult;
        }

        public void SetSuccessor(AbstractDeathStatusHandler next)
        {
            _next = next;
        }
        public abstract ResponseContext execute(CustomerResponseCdcDto customerResponses);
    }
}
