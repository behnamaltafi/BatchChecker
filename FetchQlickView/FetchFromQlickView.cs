using BatchChecker.Dto.FetchGalaxy;
using BatchChecker.Dto.FetchQlickView;
using BatchChecker.Extensions;
using BatchOp.Application.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.FetchQlickView
{
    public class FetchFromQlickView : IFetchFromQlickView
    {
        private readonly ILogger _logger;
        private readonly IQlickViewRepository _qlickViewRepository;
        public FetchFromQlickView(ILogger logger,
            IQlickViewRepository qlickViewRepository)
        {
            _logger = logger;
            _qlickViewRepository = qlickViewRepository;
        }
        public async Task<IEnumerable<NacAccountResponseDto>> GetListNacAccount(IEnumerable<AccountMappingRequestDto> requestDto)
        {
            try
            {
                if (!requestDto.Any())
                    return await Task.FromResult(Enumerable.Empty<NacAccountResponseDto>());
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

                var script = $"SELECT CONCAT(RPAD('0',10 - LENGTH(ACNT_NO), 0),ACNT_NO) ACNT_NO, GRP "
                    + "FROM SGB.NAC "
                    + $"WHERE ACNT_NO IN ({st})";
                IEnumerable<NacAccountResponseDto> result;
                result = _qlickViewRepository.Query<NacAccountResponseDto>(script);
                _logger.Information($"FetchFromQlickView.GetListNacAccount End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<NacAccountResponseDto>());
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"FetchFromQlickView.GetListNacAccount Exception",ex);
                return await Task.FromResult(Enumerable.Empty<NacAccountResponseDto>());
            }
        }
    }
}
