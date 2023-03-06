using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Ident;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.NocrInquiry.HasError
{
    public abstract class AbstractHasErrorHandler : NocrInquiryHandeler
    {
        protected AbstractHasErrorHandler _next;
       

        public void SetSuccessor(AbstractHasErrorHandler next)
        {
            _next = next;
        }
    }
}
