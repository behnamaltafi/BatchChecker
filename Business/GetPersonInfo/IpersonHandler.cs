using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.PersonInfo;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Business.GetPersonInfo
{
   

    public interface IpersonHandler
    {

        IpersonHandler SetSuccessor(IpersonHandler handler);
        Task<PersonInfoDto> Execute(string nationalCode, CancellationToken ct);
    }
}
