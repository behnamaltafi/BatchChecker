using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BatchChecker.Dto.Account
{
    public class AccountNumberCdcDto
    {
        [Display(Name = "شماره حساب")]
        public string ACCOUNT_NUMBER { get; set; }

        [Display(Name = "شماره حساب قدیمی")]
        public string LEGACY_ACCOUNT_NUMBER { get; set; }
    }
}
