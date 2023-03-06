using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.RequestCheckers
{
    public interface IRequestChecker<T>
    {
        Task<IEnumerable<T>> SenderAsync(CancellationToken cancellationToken);
        Task<bool> CheckAsync(T batchProcess , CancellationToken cancellationToken);
        Task<IEnumerable<T>> GetMissedSenderAsync(CancellationToken stoppingToken);
    }
}
