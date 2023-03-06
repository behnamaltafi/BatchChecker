using BatchOP.Domain.Entities.Signature;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.Signature.HasError
{
    internal interface IHandler
    {
        ResponseContext execute(SignatureDetailControl signatureDetailControl);
    }
}
