using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RequestConsumer.Dto.Account
{
    public class AccountResponseDto
    {
        [Display(Name = "نوع ارز")]
        public string ACCURRCY { get; set; }
        [Display(Name = "نوع حساب")]
        public string ACCTTYPE { get; set; }
        [Display(Name = "کد وضعیت")]
        public string STATCD { get; set; }
        [Display(Name = "شماره حساب")]
        public string ACNO { get; set; }
        [Display(Name = "شماره مشتری")]
        public string RETCUSNO { get; set; }
    }
}
