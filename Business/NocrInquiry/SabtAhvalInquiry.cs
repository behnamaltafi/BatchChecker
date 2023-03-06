using BatchChecker.Dto.NocrInquiry;
using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Ident;
using BatchOp.Infrastructure.Shared.ApiCaller.TataGateway;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Business.NocrInquiry
{
    public class SabtAhvalInquiry : NocrInquiryHandeler
    {

        private readonly ITataGatewayApi _tataGatewayApi;

        public SabtAhvalInquiry(
             ITataGatewayApi tataGatewayApi)
        {
            _tataGatewayApi = tataGatewayApi;
        }
        public async override Task<IdentReponseDto> Execute(IdentReponseDto request, CancellationToken ct)
        {
            return  await _tataGatewayApi.GetidentNewPersonByTwo(request.NationalCode,request.BirthDate, ct);
        }
    }
}
