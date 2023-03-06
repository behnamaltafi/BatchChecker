using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.OpenAcc;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.OpenAcc.HasError
{
    public class HasErrorHandler : AbstractHasErrorHandler
    {
        public HasErrorHandler(OpenAccSourceDetailResult _get) : base(_get)
        {
        }
        public override ResponseContext execute(OpenAccSourceDetailResult openAccSourceDetailResult)
        {
            if (openAccSourceDetailResult == null)
                _openAccSourceDetailResult.HasError = true;
            if (openAccSourceDetailResult.NationalCodeDuplaicate == true)
                _openAccSourceDetailResult.HasError = true;
            else if (openAccSourceDetailResult.NationalCodeVerified == false)
                _openAccSourceDetailResult.HasError = true;
            else if (openAccSourceDetailResult.MobileNumberVerified == false)
                _openAccSourceDetailResult.HasError = true;
            else if (openAccSourceDetailResult.MobileNumberDuplicate == true)
                _openAccSourceDetailResult.HasError = true;
            else if (openAccSourceDetailResult.MobileNumberSHAHKAR == false)
                _openAccSourceDetailResult.HasError = true;
            else if (openAccSourceDetailResult.EmailVerified == false)
                _openAccSourceDetailResult.HasError = true;
            else if (openAccSourceDetailResult.CustomerIsBank == true)
                _openAccSourceDetailResult.HasError = true;
            return _next == null ? null : _next.execute(openAccSourceDetailResult);
        }
    }
}