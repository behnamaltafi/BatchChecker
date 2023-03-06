using BatchChecker.Dto.Share;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Checkers
{
    public interface IChecker
    {
        Task<bool> Check(BatchProcessQueueDetailRequestDto batchProcessQueueDetail);
        Task ProcessWage(long CustomerSourceHeaderId);
    }
}
