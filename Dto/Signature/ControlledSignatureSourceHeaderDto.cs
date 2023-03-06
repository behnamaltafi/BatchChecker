using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Signature
{
    public class ControlledSignatureSourceHeaderDto
    {
        public int ControlledCorrectCount { get; set; }
        public int ControlledWrongCount { get; set; }
    }
}
