using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto.FetchCDC;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.CustomerType
{
    public interface IHandler
    {
        ResponseContext execute(CustomerResponseCdcDto customerResponses);
    }
}
