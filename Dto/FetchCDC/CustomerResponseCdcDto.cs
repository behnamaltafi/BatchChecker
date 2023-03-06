using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BatchChecker.Dto.FetchCDC
{
    public class CustomerResponseCdcDto
    {
        [Display(Name = "کد وضعیت حیات")]
        public string DEATH_STATUS { get; set; }
        [Display(Name = "شماره مشتری")]
        public string CUSNO { get; set; }
        [Display(Name = "کد ملی")]
        public string NATIONAL_CODE { get; set; }
        [Display(Name = "کد نوع مشتری")]
        public string CUSTYPE { get; set; }
        [Display(Name = "شرح نوع مشتری")]
        public string CUSTYPE_DSC { get; set; }
    }
   }
