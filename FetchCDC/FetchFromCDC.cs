using BatchChecker.Dto.FetchCDC;
using BatchChecker.Extensions;
using BatchOp.Application.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.FetchCDC
{
    public class FetchFromCDC : IFetchFromCDC
    {
        private readonly ILogger _logger;
        private readonly ICDCRepository _cDCRepository;

        public FetchFromCDC(ILogger logger,
                      ICDCRepository cDCRepository)
        {
            _logger = logger;
            _cDCRepository = cDCRepository;
        }
        public async Task<IEnumerable<AccountResponseCdcDto>> GetListCDCAccount(IEnumerable<RequestCdcDto> requestCdcDtos)
        {
            try
            {
                _logger.Information($"FetchCDC.GetListCDCAccount Start");
                if (!requestCdcDtos.Any())
                    return await Task.FromResult(Enumerable.Empty<AccountResponseCdcDto>());
                var st = new StringBuilder();
                int i = 0;
                int count = requestCdcDtos.Count();
                foreach (var item in requestCdcDtos)
                {
                    if (i == count - 1)
                        st.Append($"'{item.LEGACY_ACCOUNT_NUMBER}'");
                    else
                        st.Append($"'{item.LEGACY_ACCOUNT_NUMBER}',");
                    i++;
                }

                var script = $"select A.ACCURRCY, A.ACCTTYPE, A.STATCD, A.ACNO , AN.LEGACY_ACCOUNT_NUMBER, A.RETCUSNO " +
                    $"from CDC.ACCOUNT A INNER JOIN  CDC.ACCOUNT_NUMBER AN ON AN.ACCOUNT_NUMBER = A.ACNO " +
                    $"WHERE AN.LEGACY_ACCOUNT_NUMBER in ({st})";
                IEnumerable<AccountResponseCdcDto> result;
                //  lock (this)
                // {
                result = _cDCRepository.Query<AccountResponseCdcDto>(script);
                //  }
                _logger.Information($"FetchCDC.GetListCDCAccount End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<AccountResponseCdcDto>());
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"FetchCDC.GetListCDCAccount Exception",ex);
                return await Task.FromResult(Enumerable.Empty<AccountResponseCdcDto>());
            }
        }

        public async Task<IEnumerable<AccountResponseCdcDto>> GetListCDCAccountByAcno(IEnumerable<RequestCdcDto> requestCdcDtos)
        {
            try
            {
                _logger.Information($"FetchCDC.GetListCDCAccountByAcno Start");
                if (!requestCdcDtos.Any())
                    return await Task.FromResult(Enumerable.Empty<AccountResponseCdcDto>());
                var st = new StringBuilder();
                int i = 0;
                int count = requestCdcDtos.Count();
                foreach (var item in requestCdcDtos)
                {
                    if (i == count - 1)
                        st.Append($"'{item.ACNO}'");
                    else
                        st.Append($"'{item.ACNO}',");
                    i++;
                }

                var script = $"select A.ACCURRCY, A.ACCTTYPE, A.STATCD, A.ACNO , AN.LEGACY_ACCOUNT_NUMBER, A.RETCUSNO " +
                    $"FROM CDC.ACCOUNT A " +
                    $"INNER JOIN CDC.ACCOUNT_NUMBER AN ON AN.ACCOUNT_NUMBER = A.ACNO " +
                    $"WHERE A.ACNO in ({st})";
                IEnumerable<AccountResponseCdcDto> result;
                result = _cDCRepository.Query<AccountResponseCdcDto>(script);
   
                _logger.Information($"FetchCDC.GetListCDCAccountByAcno End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<AccountResponseCdcDto>());
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"FetchCDC.GetListCDCAccountByAcno Exception",ex);
                return await Task.FromResult(Enumerable.Empty<AccountResponseCdcDto>());
            }
        }

        public async Task<IEnumerable<AccountNumberResponseCdcDto>> GetListCDCAccountNumber(IEnumerable<RequestCdcDto> requestCdcDtos)
        {
            try
            {
                _logger.Information($"FetchCDC.GetListCDCAccountNumber Start");
                if (!requestCdcDtos.Any())
                    return await Task.FromResult(Enumerable.Empty<AccountNumberResponseCdcDto>());
                var st = new StringBuilder();
                int i = 0;
                int count = requestCdcDtos.Count();
                foreach (var item in requestCdcDtos)
                {
                    if (i == count - 1)
                        st.Append($"'{item.LEGACY_ACCOUNT_NUMBER}'");
                    else
                        st.Append($"'{item.LEGACY_ACCOUNT_NUMBER}',");
                    i++;
                }

                var script = $"select ACCOUNT_NUMBER, LEGACY_ACCOUNT_NUMBER from CDC.ACCOUNT_NUMBER " +
                    $"WHERE LEGACY_ACCOUNT_NUMBER in ({st})";
                IEnumerable<AccountNumberResponseCdcDto> result;
                //  lock (this)
                // {
                result = _cDCRepository.Query<AccountNumberResponseCdcDto>(script);
                //  }
                _logger.Information($"FetchCDC.GetListCDCAccountNumber End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<AccountNumberResponseCdcDto>());
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"GetListCDCAccountNumber Exception" ,ex);
                return await Task.FromResult(Enumerable.Empty<AccountNumberResponseCdcDto>());
            }
        }

        public async Task<IEnumerable<CustomerResponseCdcDto>> GetListCDCCustomerByNationalCode(IEnumerable<RequestCdcDto> requestCdcDtos)
        {
            try
            {
                _logger.Information($"FetchCDC.GetListCDCCustomerByNationalCode Start");
                if (!requestCdcDtos.Any())
                    return await Task.FromResult(Enumerable.Empty<CustomerResponseCdcDto>());
                var st = new StringBuilder();
                int i = 0;
                int count = requestCdcDtos.Count();
                foreach (var item in requestCdcDtos)
                {
                    if (i == count - 1)
                        st.Append($"'{item.NATIONAL_CODE}'");
                    else
                        st.Append($"'{item.NATIONAL_CODE}',");
                    i++;
                }

                var script = $"SELECT DEATH_STATUS,CUSNO,NATIONAL_CODE,CUSTYPE,CUSTYPE_DSC FROM CDC.CUSTOMER WHERE NATIONAL_CODE in ({st})";
                IEnumerable<CustomerResponseCdcDto> result;
                //lock (this)
                // {
                result = _cDCRepository.Query<CustomerResponseCdcDto>(script);
                // }
                _logger.Information($"FetchCDC.GetListCDCCustomerByNationalCode End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<CustomerResponseCdcDto>());

            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"FetchCDC.GetListCDCCustomerByNationalCode Exception",ex);
                return await Task.FromResult(Enumerable.Empty<CustomerResponseCdcDto>());
            }
        }

        public async Task<IEnumerable<CustomerResponseCdcDto>> GetListCDCCustomerByRETCUSNO(IEnumerable<RequestCdcDto> requestCdcDtos)
        {
            try
            {
                _logger.Information($"FetchCDC.GetListCDCCustomerByRETCUSNO Start");
                if (!requestCdcDtos.Any())
                    return await Task.FromResult(Enumerable.Empty<CustomerResponseCdcDto>());
                var st = new StringBuilder();
                int i = 0;
                int count = requestCdcDtos.Count();
                foreach (var item in requestCdcDtos)
                {
                    if (i == count - 1)
                        st.Append($"'{item.RETCUSNO}'");
                    else
                        st.Append($"'{item.RETCUSNO}',");
                    i++;
                }

                var script = $"SELECT DEATH_STATUS,CUSNO,CUSTYPE,CUSTYPE_DSC FROM CDC.CUSTOMER WHERE CUSNO in ({st})";
                IEnumerable<CustomerResponseCdcDto> result;
                //lock (this)
                // {
                result = _cDCRepository.Query<CustomerResponseCdcDto>(script);
                // }
                _logger.Information($"FetchCDC.GetListCDCCustomerByRETCUSNO End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<CustomerResponseCdcDto>());

            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"FetchCDC.GetListCDCCustomerByRETCUSNO Exception",ex);
                return await Task.FromResult(Enumerable.Empty<CustomerResponseCdcDto>());
            }
        }

        public Task<IEnumerable<LegacyAccountResponseCdcDto>> GetListCDCLegacyAccount(IEnumerable<RequestCdcDto> requestCdcDtos)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<OrganResponseCdcDto>> GetListCDCOrgan(IEnumerable<RequestCdcDto> requestCdcDtos)
        {
            try
            {
                _logger.Information($"FetchCDC.GetListCDCOrgan Start");
                if (!requestCdcDtos.Any())
                    return await Task.FromResult(Enumerable.Empty<OrganResponseCdcDto>());
                var st = new StringBuilder();
                int i = 0;
                int count = requestCdcDtos.Count();
                foreach (var item in requestCdcDtos)
                {
                    if (i == count - 1)
                        st.Append($"'{item.ISSUERBRANCHCODE}'");
                    else
                        st.Append($"'{item.ISSUERBRANCHCODE}',");
                    i++;
                }

                var Script = $"SELECT ORGAN_CODE,ISC_ORGAN_CODE FROM CDC.ORGAN WHERE ORGAN_CODE in ({st})";
                IEnumerable<OrganResponseCdcDto> result;
                //lock (this)
                // {
               result = _cDCRepository.Query<OrganResponseCdcDto>(Script);
                // }
                _logger.Information($"FetchCDC.GetListCDCOrgan End");
                if (result.Any())
                    return await Task.FromResult(result);
                return await Task.FromResult(Enumerable.Empty<OrganResponseCdcDto>());

            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"FetchCDC.GetListCDCOrgan Exception",ex);
                return await Task.FromResult(Enumerable.Empty<OrganResponseCdcDto>());
            }
        }
    }
}
