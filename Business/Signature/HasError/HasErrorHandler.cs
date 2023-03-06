using BatchOP.Domain.Entities.Signature;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.Signature.HasError
{
    public class HasErrorHandler : AbstractHasErrorHandler
    {
        public HasErrorHandler(SignatureDetailControl _get) : base(_get)
        {
        }
        public override ResponseContext execute(SignatureDetailControl signatureDetailControl)
        {
            if (signatureDetailControl == null)
                signatureDetailControl.HasError = true;
            if (signatureDetailControl.TejaratCustomerVerified == true)
                signatureDetailControl.HasError = true;
            else if (signatureDetailControl.NationalCodeVerified == false)
                signatureDetailControl.HasError = true;
            return _next == null ? null : _next.execute(signatureDetailControl);
        }
    }
}
