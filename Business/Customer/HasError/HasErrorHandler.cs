using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.HasError
{
    public class HasErrorHandler : AbstractHasErrorHandler
    {
        public HasErrorHandler(CustomerSourceDetailResult _get) : base(_get)
        {
        }
        public override ResponseContext execute(CustomerSourceDetailResult customerSourceDetailResult)
        {
            if (customerSourceDetailResult == null)
                _customerSourceDetailResult.HasError = true;
            else if (customerSourceDetailResult.AccVerified == false)
                _customerSourceDetailResult.HasError = true;
            else if (customerSourceDetailResult.AmountVerified == false)
                _customerSourceDetailResult.HasError = true;
            else if (customerSourceDetailResult.DepositeVerified == false)
                _customerSourceDetailResult.HasError = true;
            else if (customerSourceDetailResult.CurrencyType != CurrencyTypeStatus.Riali && customerSourceDetailResult.CurrencyType != CurrencyTypeStatus.Undefined)
                _customerSourceDetailResult.HasError = true;
            else if (customerSourceDetailResult.AccType == AccountTypeStatus.SepordeBolandModat)
                _customerSourceDetailResult.HasError = true;
            else if (customerSourceDetailResult.AccStatus != AccountStatus.Active && customerSourceDetailResult.AccStatus != AccountStatus.Undefined)
                _customerSourceDetailResult.HasError = true;
            else if (customerSourceDetailResult.DeathStatus == BatchOP.Domain.Enums.DeathStatus.Dead)
                _customerSourceDetailResult.HasError = true;
            else if (customerSourceDetailResult.BlackListStatus == BlackListStatus.InBlackList)
                _customerSourceDetailResult.HasError = true;
            return _next == null ? null : _next.execute(customerSourceDetailResult);
        }
    }
}