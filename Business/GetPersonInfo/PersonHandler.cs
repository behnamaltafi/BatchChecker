using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.PersonInfo;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Business.GetPersonInfo
{
    public  abstract class PersonHandler : IpersonHandler
    {
        public abstract Task<PersonInfoDto> Execute(string nationalCode, CancellationToken ct)     ;

        public IpersonHandler SetSuccessor(IpersonHandler handler)
        {
            throw new NotImplementedException();
        }
    }
}
