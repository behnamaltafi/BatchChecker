using BatchChecker.Dto.NocrInquiry;
using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Ident;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Business.NocrInquiry
{
    public class CheckUserIsNotDead : NocrInquiryHandeler
    {
        public CheckUserIsNotDead()
        {
        }
        public override Task<IdentReponseDto> Execute(IdentReponseDto request, CancellationToken ct)
        {
            if (request != null && !string.IsNullOrEmpty(request.FatherName) && request.DeathStatus == 1)
                return Task.FromResult(new IdentReponseDto { DeathStatus = 1, ErrorMessage = "کاربر فوت شده است ", HasError = true });

            else if (successor != null && !string.IsNullOrEmpty(request.FatherName))
                return successor.Execute(request, ct);
            else
                return Task.FromResult(new IdentReponseDto { ErrorMessage = "خطا  ", HasError = true });
        }
    }
}
