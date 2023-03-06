using BatchOp.Application.DTOs.Files;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.RequestCheckers.FinDoc
{
    public interface IFinDocCheckerTest
    {
        Task ReadAndWriteFinDocFile();
    }
}
