using BatchChecker.Checkers;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto
{
    public class QueueMessageModel<T>
    {
        public CheckType checkType { get; set; }
        public List<T> Item { get; set; }
    }
}
