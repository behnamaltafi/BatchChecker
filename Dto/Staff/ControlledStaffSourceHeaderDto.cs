using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.Staff
{
    public class ControlledStaffSourceHeaderDto
    {
        public long TotalControlledCorrectAmountCredit { get; set; }
        public long TotalControlledCorrectAmountDebit { get; set; }
        public long TotalControlledWrongAmountCredit { get; set; }
        public long TotalControlledWrongAmountDebit { get; set; }
        public int ControlledCorrectCountCredit { get; set; }
        public int ControlledCorrectCountDebit { get; set; }
        public int ControlledWrongCountCredit { get; set; }
        public int ControlledWrongCountDebit { get; set; }
    }
}
