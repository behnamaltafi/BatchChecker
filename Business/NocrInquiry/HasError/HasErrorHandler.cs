using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Ident;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Business.NocrInquiry.HasError
{
    public class HasErrorHandler : AbstractHasErrorHandler
    {
        public async override Task<IdentReponseDto> Execute(IdentReponseDto request, CancellationToken ct)
        {
            StringBuilder errorresult = new StringBuilder();


            if (request == null || request.FirstName == null)


            {
                request.HasError = true;
                errorresult.AppendLine("  مشتری  یافت نشد   ");

            }
            if (request.NationalCodeIsValid == false)

            {
                request.HasError = true;
                errorresult.AppendLine("  کد ملی نا معتبر   ");

            }
            if (request.BirthDateIsValid == false)
            {
                request.HasError = true;

                errorresult.AppendLine(" تاریخ تولد نا معتبر است  ");

            }
            if (request.FirstNameIsMatch == false)
            {
                request.HasError = true;
                errorresult.AppendLine(" مغایرت نام    ");
            }

            if (request.LastNameIsMatch == false)
            {
                request.HasError = true;
                errorresult.AppendLine("  مغایرت نام خانوادگی    ");
            }
            if (request.BirthDateIsMatch == false)
            {
                request.HasError = true;
                errorresult.AppendLine(" مغایرت تاریخ تولد    ");
            }

            request.ErrorMessage = errorresult.ToString();
            errorresult.Clear();
            return await Task.FromResult(request);
        }


    }
}
