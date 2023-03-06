using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.Customer;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.HasError
{
    public interface IHandler
    {
        ResponseContext execute(CustomerSourceDetailResult customerSourceDetailResult);
    }
}
