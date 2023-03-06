using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.WageApi
{
    public class WageApiResult
    {
        public int resultCode { get; set; }
        public string exceptionDetail { get; set; }
        public string requestID { get; set; }
        public string result { get; set; }
    }
}
