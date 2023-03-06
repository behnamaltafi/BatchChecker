using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BatchChecker.Dto.FetchCDC
{
    public class RequestCdcDto
    {
        [Display(Name = "شماره حساب قدیمی")]
        public string LEGACY_ACCOUNT_NUMBER { get; set; }
        [Display(Name = "شماره مشتری")]
        public string RETCUSNO { get; set; }
        [Display(Name = "کد شعبه صادرکننده سند")]
        public string ISSUERBRANCHCODE { get; set; }
        [Display(Name = "کد ملی")]
        public string NATIONAL_CODE { get; set; }
        [Display(Name = "شماره حساب ")]
        public string ACNO { get; set; }
    }
}
