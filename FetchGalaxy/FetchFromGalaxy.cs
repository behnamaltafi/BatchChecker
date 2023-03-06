using BatchChecker.Dto.FetchGalaxy;
using BatchChecker.Extensions;
using BatchOp.Application.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.FetchGalaxy
{
    public class FetchFromGalaxy : IFetchFromGalaxy
    {
        private readonly ILogger _logger;
        private readonly IGalaxyRepository _galaxyRepository;
        public FetchFromGalaxy(ILogger logger,
            IGalaxyRepository galaxyRepository)
        {
            _logger = logger;
            _galaxyRepository = galaxyRepository;
        }
        public async Task<IEnumerable<AccountMappingResponseDto>> GetListMappedAccounts(IEnumerable<AccountMappingRequestDto> requestDto)
        {
            try
            {
                if (!requestDto.Any())
                    return await Task.FromResult(Enumerable.Empty<AccountMappingResponseDto>());
                var st = new StringBuilder();
                int i = 0;
                int count = requestDto.Count();
                foreach (var item in requestDto)
                {
                    if (i == count - 1)
                        st.Append($"'{item.LagacyAccountNumber}'");
                    else
                        st.Append($"'{item.LagacyAccountNumber}',");
                    i++;
                }

                var script = $"SELECT ACCOUNT_NUMBER,LEGACY_ACCOUNT_NUMBER, ACCOUNT_HEADING, IS_HEADING "
                    + "FROM G_SUPPORT.VW_DETECT_ACCOUNT_HOST "
                    + $"WHERE legacy_account_number IN({st})";
                IEnumerable<AccountMappingResponseDto> result;
                result = _galaxyRepository.Query<AccountMappingResponseDto>(script);
                _logger.Information($"FetchFromGalaxy.GetListMappedAccounts End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<AccountMappingResponseDto>());
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"FetchFromGalaxy.GetListMappedAccounts Exception",ex);
                return await Task.FromResult(Enumerable.Empty<AccountMappingResponseDto>());
            }
        }
        public async Task<IEnumerable<AccountHeadingMappingResponseDto>> GetListNickName(IEnumerable<AccountMappingRequestDto> requestDto)
        {
            try
            {
                if (!requestDto.Any())
                    return await Task.FromResult(Enumerable.Empty<AccountHeadingMappingResponseDto>());
                var st = new StringBuilder();
                int i = 0;
                int count = requestDto.Count();
                foreach (var item in requestDto)
                {
                    if (i == count - 1)
                        st.Append($"'{item.LagacyAccountNumber}'");
                    else
                        st.Append($"'{item.LagacyAccountNumber}',");
                    i++;
                }

                var script = $"SELECT LEGACY_ACCOUNT_NUMBER, ISC_GL_NICK_NAME "
                    + "FROM G_SUPPORT.VW_ACCOUNT_HEADER_MAPPING "
                    + $"WHERE LEGACY_ACCOUNT_NUMBER IN({st}) ";
                IEnumerable<AccountHeadingMappingResponseDto> result;
                result = _galaxyRepository.Query<AccountHeadingMappingResponseDto>(script);
                _logger.Information($"FetchFromGalaxy.GetListNickName End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<AccountHeadingMappingResponseDto>());
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"FetchFromGalaxy.GetListNickName Exception",ex);
                return await Task.FromResult(Enumerable.Empty<AccountHeadingMappingResponseDto>());
            }
        }
    }
}
