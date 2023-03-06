using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.WageApi
{
    public class BalanceDataDto
    {
        public CurrencyDto available { get; set; }
        public CurrencyDto current { get; set; }
        public CurrencyDto effective { get; set; }
    }
}
