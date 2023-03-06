using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.OpenAcc;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.OpenAcc.HasError
{
    public abstract class AbstractHasErrorHandler : IHandler
    {
        protected AbstractHasErrorHandler _next;
        protected OpenAccSourceDetailResult _openAccSourceDetailResult;

        public AbstractHasErrorHandler(OpenAccSourceDetailResult openAccSourceDetailResult)
        {
            _openAccSourceDetailResult = openAccSourceDetailResult;
        }

        public void SetSuccessor(AbstractHasErrorHandler next)
        {
            _next = next;
        }
        public abstract ResponseContext execute(OpenAccSourceDetailResult openAccSourceDetailResult);
    }
}
