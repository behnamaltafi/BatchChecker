using BatchChecker.Dto.FetchCDC;
using BatchOp.Application.AOP;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.FetchCDC
{
    public interface IFetchFromCDC
    {
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<AccountResponseCdcDto>> GetListCDCAccount(IEnumerable<RequestCdcDto> requestCdcDtos);
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<AccountNumberResponseCdcDto>> GetListCDCAccountNumber(IEnumerable<RequestCdcDto> requestCdcDtos);
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<CustomerResponseCdcDto>> GetListCDCCustomerByRETCUSNO(IEnumerable<RequestCdcDto> requestCdcDtos);
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<CustomerResponseCdcDto>> GetListCDCCustomerByNationalCode(IEnumerable<RequestCdcDto> requestCdcDtos);
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<OrganResponseCdcDto>> GetListCDCOrgan(IEnumerable<RequestCdcDto> requestCdcDtos);
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<LegacyAccountResponseCdcDto>> GetListCDCLegacyAccount(IEnumerable<RequestCdcDto> requestCdcDtos);
        Task<IEnumerable<AccountResponseCdcDto>> GetListCDCAccountByAcno(IEnumerable<RequestCdcDto> requestCdcDtos);
    }
}
