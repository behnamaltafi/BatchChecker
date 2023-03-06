using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BatchChecker.Dto.FetchCDC
{
    public class OrganResponseCdcDto
    {
        [Display(Name = "کد شعبه / باجه (بانک تجارت)")]
        public string ORGAN_CODE { get; set; }
        [Display(Name = "کد شعبه / باجه (خدمات انفورماتیک)")]
        public string ISC_ORGAN_CODE { get; set; }
    }
}
