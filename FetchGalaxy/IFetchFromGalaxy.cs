using BatchChecker.Dto.FetchGalaxy;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.FetchGalaxy
{
    public interface IFetchFromGalaxy
    {
        Task<IEnumerable<AccountMappingResponseDto>> GetListMappedAccounts(IEnumerable<AccountMappingRequestDto> requestDto);
        Task<IEnumerable<AccountHeadingMappingResponseDto>> GetListNickName(IEnumerable<AccountMappingRequestDto> requestDto);
    }
}
