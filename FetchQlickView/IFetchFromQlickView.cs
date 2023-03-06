using BatchChecker.Dto.FetchGalaxy;
using BatchChecker.Dto.FetchQlickView;
using BatchOp.Application.AOP;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.FetchQlickView
{
    public interface IFetchFromQlickView
    {
        [LogDef(LogMode = LogMode.Input)]
        Task<IEnumerable<NacAccountResponseDto>> GetListNacAccount(IEnumerable<AccountMappingRequestDto> requestDto);
    }
}
