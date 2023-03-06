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
    public class InquiryPersonDb : NocrInquiryHandeler
    {
        private readonly ITataGatewayApi _tataGatewayApi;
        public InquiryPersonDb(
     ITataGatewayApi tataGatewayApi)
        {
            _tataGatewayApi = tataGatewayApi;
        }
        public async override Task<IdentReponseDto> Execute(IdentReponseDto request, CancellationToken ct)
        {
            var person = await _tataGatewayApi.GetPersonBankDB(request.NationalCode, ct);
            if (person == null || string.IsNullOrEmpty(person.FirstName))
            {
                if (successor == null)
                    return new IdentReponseDto { DeathStatus = 1, ErrorMessage = "مشتری در دیتابیس بانک یافت نشد ", HasError = true };
                else
                    return await successor.Execute(request, ct);
            }
            else
                return person;
        }
    }
}
