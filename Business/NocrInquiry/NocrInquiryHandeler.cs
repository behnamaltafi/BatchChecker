using BatchChecker.Dto.NocrInquiry;
using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Ident;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Business.NocrInquiry
{
    public abstract class NocrInquiryHandeler : INocrHandler
    {

        protected INocrHandler successor;

        public  abstract Task<IdentReponseDto>  Execute(IdentReponseDto request, CancellationToken ct);

       

      
        public INocrHandler SetSuccessor(INocrHandler handler)
        {
            this.successor = handler;
            return successor;
        }
    }
}
