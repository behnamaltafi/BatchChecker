using BatchChecker.Dto.NocrInquiry;
using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Ident;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Business.NocrInquiry
{
    public interface INocrHandler
    {
        INocrHandler SetSuccessor(INocrHandler handler);
        Task<IdentReponseDto> Execute(IdentReponseDto request, CancellationToken ct);

    }
}
