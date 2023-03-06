using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.Customer;
using BatchChecker.Dto.OpenAcc;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.OpenAcc.HasError
{
    public interface IHandler
    {
        ResponseContext execute(OpenAccSourceDetailResult customerSourceDetailResult);
    }
}
