using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RequestConsumer.Dto.Customer
{
    public class CustomerResponseDto
    {
        [Display(Name = "کد وضعیت حیات")]
        public string DEATH_STATUS { get; set; }
    }
}
