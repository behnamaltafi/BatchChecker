using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.HasError
{
    public abstract class AbstractHasErrorHandler : IHandler
    {
        protected AbstractHasErrorHandler _next;
        protected CustomerSourceDetailResult _customerSourceDetailResult;

        public AbstractHasErrorHandler(CustomerSourceDetailResult customerSourceDetailResult)
        {
            _customerSourceDetailResult = customerSourceDetailResult;
        }

        public void SetSuccessor(AbstractHasErrorHandler next)
        {
            _next = next;
        }
        public abstract ResponseContext execute(CustomerSourceDetailResult customerSourceDetailResult);
    }
}
