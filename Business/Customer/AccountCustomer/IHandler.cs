
using BatchChecker.Dto.FetchCDC;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Business.AccountCustomer
{
    public interface IHandler
    {
        ResponseContext execute(AccountResponseCdcDto accountResponses);
    }
}
