using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BatchChecker.Dto.FetchGalaxy
{
    public class AccountMappingResponseDto
    {
        [Display(Name = "شماره حساب")]
        public string ACCOUNT_NUMBER { get; set; }
        [Display(Name = "شماره حساب داده پردازی")]
        public string LEGACY_ACCOUNT_NUMBER { get; set; }
        [Display(Name = "شماره حساب GL")]
        public string ACCOUNT_HEADING { get; set; }
        [Display(Name = "نوع شماره حساب")]
        public int IS_HEADING { get; set; }
    }
}
