using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BatchChecker.Dto.FetchCDC
{
    public class AccountResponseCdcDto
    {
        [Display(Name = "نوع ارز")]
        public string ACCURRCY { get; set; }
        [Display(Name = "نوع حساب")]
        public string ACCTTYPE { get; set; }
        [Display(Name = "کد وضعیت")]
        public string STATCD { get; set; }
        [Display(Name = "شماره حساب")]
        public string ACNO { get; set; }
        [Display(Name = "شماره حساب قدیمی")]
        public string LEGACY_ACCOUNT_NUMBER { get; set; }
        [Display(Name = "شماره مشتری")]
        public string RETCUSNO { get; set; }
    }
}
