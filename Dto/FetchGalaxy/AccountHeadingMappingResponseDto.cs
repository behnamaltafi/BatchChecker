using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BatchChecker.Dto.FetchGalaxy
{
    public class AccountHeadingMappingResponseDto
    {
        [Display(Name = "شماره حساب داده پردازی")]
        public string LEGACY_ACCOUNT_NUMBER { get; set; }
        [Display(Name = "GL NickName")]
        public string ISC_GL_NICK_NAME { get; set; }
    }
}
