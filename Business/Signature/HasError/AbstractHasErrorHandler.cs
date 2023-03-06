using BatchOP.Domain.Entities.Signature;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.Signature.HasError
{
    public abstract class AbstractHasErrorHandler : IHandler
    {
        protected AbstractHasErrorHandler _next;
        protected SignatureDetailControl _signatureDetailControl;
        public AbstractHasErrorHandler(SignatureDetailControl signatureDetailControl)
        {
            _signatureDetailControl = signatureDetailControl;
        }
        public void SetSuccessor(AbstractHasErrorHandler next)
        {
            _next = next;
        }
        public abstract ResponseContext execute(SignatureDetailControl signatureDetailControl);
    }
}
