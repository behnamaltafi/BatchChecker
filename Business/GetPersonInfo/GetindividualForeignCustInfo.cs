using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.PersonInfo;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Business.GetPersonInfo
{
    public class GetindividualForeignCustInfo : PersonHandler
    {
        public override Task<PersonInfoDto> Execute(string nationalCode, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
