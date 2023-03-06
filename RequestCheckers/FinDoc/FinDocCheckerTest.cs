using AutoMapper;
using BatchChecker.Dto.FinDoc;
using BatchOp.Application.DTOs.Files;
using BatchOp.Application.Exceptions;
using BatchOp.Application.Features.FileAttributes.Queries.GetFileAttributeById;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Application.Settings;
using BatchOp.Application.Wrappers;
using BatchOp.Infrastructure.Shared.Services.Utility;
using BatchOP.Domain.Common;
using BatchOP.Domain.Entities.FinDoc;
using BatchOP.Domain.Entities.Subsidy;
using BatchOP.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BatchChecker.RequestCheckers.FinDoc
{
    public class FinDocCheckerTest : IFinDocCheckerTest
    {
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;

        public FinDocCheckerTest(ILogger logger, IDapperRepository dapperRepository, IDapperUnitOfWork dapperUnitOfWork, IConfiguration configuration, IMapper mapper)
        {
            _logger = logger;
            _dapperRepository = dapperRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
            _mapper = mapper;
        }

        public async Task ReadAndWriteFinDocFile()
        {
            var scriptDetail = $"select cr.ID as RequestId,cr.Status,cr.type, cfa.FileName,cfa.RepositoryAddress,cfa.ID as FileAttrID " +
                $"from TB_Request cr " +
                $"join TB_FileAttribute cfa on cfa.Request_ID = cr.ID " +
           // $"where (cr.Status = {(int)RequestStatus.Undefined} or cr.Status is null) and cr.Type = 1";
           $"where  cfa.ID = 803";
            var responseDetail = _dapperRepository.Query<FinDocRequestFileAttrDto>(scriptDetail);

            foreach (var item in responseDetail)
            {
                string path = item.RepositoryAddress;
                using (var stream = System.IO.File.OpenRead(path))
                {
                    var File = new FormFile(stream, 0, stream.Length, null, item.FileName);
                    var resultProcessFile = await ProcessFile(File, item.RequestId);
                    var res = await AddSourceTempRecordsAsync(item, resultProcessFile);
                };

            }
        }

        private async Task<ProcessFileDto> ProcessFile(IFormFile file, long requestId)
        {
            string result = string.Empty;
            bool hasError = false;
            int lineCounter = 0;
            long totalAmountCredit = 0;
            long totalAmountDebit = 0;
            int numberOfRecordsCredit = 0;
            int numberOfRecordsDebit = 0;
            List<IBaseFileRecord> customerSourceDetailTempList = new List<IBaseFileRecord>();

            using (var reader = new StreamReader(file.OpenReadStream()))
            {

                while (reader.Peek() >= 0)
                {

                    var line = await reader.ReadLineAsync();

                    if (!hasError)
                    {

                        if (line.Substring(FinDocSourceFileLength.NatureCode, 1) == "1")
                        {
                            long amount = Convert.ToInt64(line.Substring(FinDocSourceFileLength.AmountIndex, FinDocSourceFileLength.Amount));
                            totalAmountCredit += amount;
                            numberOfRecordsCredit++;
                            customerSourceDetailTempList.Add(new FinDocSourceDetailTemp
                            {
                                AccountNumber = line.Substring(17, FinDocSourceFileLength.AccountNumber),
                                Amount = amount.ToString(),
                                ArticleDesc = line.Substring(82, FinDocSourceFileLength.ArticleDesc),
                                ArticleNumber = line.Substring(41, 7),
                                BankCode = line.Substring(0, 3),
                                ComprehensiveCode = line.Substring(65, 1),
                                DebitCreditType = line.Substring(FinDocSourceFileLength.NatureCode, 1),
                                DestinationBranchCode = line.Substring(76, 6),
                                EffectiveDate = line.Substring(27, 8),
                                GuaranteeTypeCode = line.Substring(65, 1),
                                IssuerBranchCode = line.Substring(12, 5),
                                JournalNumber = line.Substring(8, 4),
                                MappingStatus = GenericEnum.Start,
                                OperationCode = line.Substring(112, 3),
                                OwnerBranchCode = line.Substring(3, 5),
                                PKNumber = line.Substring(118, 14),
                                ProgramName = line.Substring(68, 8),
                                ReturnCode = line.Substring(64, 1),
                                RowIdentifier = lineCounter,
                                SessionId = line.Substring(132, 12),
                                Status = GenericEnum.Start,
                                SubsystemName = line.Substring(115, 3),
                                Time = line.Substring(35, 6)

                            });
                        }
                        if (line.Substring(FinDocSourceFileLength.NatureCode, 1) == "0")
                        {
                            long amount = Convert.ToInt64(line.Substring(FinDocSourceFileLength.AmountIndex, FinDocSourceFileLength.Amount));
                            totalAmountDebit += amount;
                            numberOfRecordsDebit++;
                            customerSourceDetailTempList.Add(new FinDocSourceDetailTemp
                            {
                                AccountNumber = line.Substring(17, FinDocSourceFileLength.AccountNumber),
                                Amount = amount.ToString(),
                                ArticleDesc = line.Substring(82, FinDocSourceFileLength.ArticleDesc),
                                ArticleNumber = line.Substring(41, 7),
                                BankCode = line.Substring(0, 3),
                                ComprehensiveCode = line.Substring(65, 1),
                                DebitCreditType = line.Substring(FinDocSourceFileLength.NatureCode, 1),
                                DestinationBranchCode = line.Substring(76, 6),
                                EffectiveDate = line.Substring(27, 8),
                                GuaranteeTypeCode = line.Substring(65, 1),
                                IssuerBranchCode = line.Substring(12, 5),
                                JournalNumber = line.Substring(8, 4),
                                MappingStatus = GenericEnum.Start,
                                OperationCode = line.Substring(112, 3),
                                OwnerBranchCode = line.Substring(3, 5),
                                PKNumber = line.Substring(118, 14),
                                ProgramName = line.Substring(68, 8),
                                ReturnCode = line.Substring(64, 1),
                                RowIdentifier = lineCounter,
                                SessionId = line.Substring(132, 12),
                                Status = GenericEnum.Start,
                                SubsystemName = line.Substring(115, 3),
                                Time = line.Substring(35, 6)

                            });
                        }
                    }
                    //}
                    ++lineCounter;
                }
                if (totalAmountCredit != totalAmountDebit)
                    throw new ApiException(new Message
                    {
                        English = "file content is not balance.",
                        Persian = "فایل ورودی تراز نمی باشد"
                    });
                if (hasError)
                {
                    throw new ApiException(new Message
                    {
                        English = "File content is not correct.",
                        Persian = result
                    });
                }
            }

            return new ProcessFileDto
            {

                ViewModel = new FileAttributeViewModel
                {
                    TotalAmount = totalAmountCredit,
                    NumberOfRecords = lineCounter - 1,
                    NumberOfRecordsCredit = numberOfRecordsCredit,
                    NumberOfRecordsDebit = numberOfRecordsDebit,
                    TotalAmountCredit = totalAmountCredit,
                    TotalAmountDebit = totalAmountDebit,
                },
                SourceDetailTempList = customerSourceDetailTempList,
            };

        }

        private async Task<IBaseFileRecord> AddSourceTempRecordsAsync(FinDocRequestFileAttrDto FileAttribute, ProcessFileDto processCustomerFileDto)
        {
            try
            {
                var scriptFinDocSourceHeader = $"select * from TB_FinDocSourceHeaderTemp where FileAttribute_ID = {FileAttribute.FileAttrID}";
                var responseFinDocSourceHeader = _dapperRepository.Query<FinDocSourceHeaderTemp>(scriptFinDocSourceHeader).FirstOrDefault();
                if (responseFinDocSourceHeader != null)
                {
                    var scriptFinDocSourceDetail = $"select Count(1) as RowIdentifier  from TB_FinDocSourceDetailTemp where FinDocSourceHeaderTemp_ID = {responseFinDocSourceHeader.Id}";
                    var responseFinDocSourceDetail = _dapperRepository.Query<FinDocSourceDetail>(scriptFinDocSourceDetail).FirstOrDefault();
                    if (responseFinDocSourceDetail != null)
                    {
                        var countProcessFile = processCustomerFileDto.SourceDetailTempList.Count();
                        var SourceDetailCount = responseFinDocSourceDetail.RowIdentifier;
                        if (countProcessFile != SourceDetailCount)
                        {
                            var scriptRemove = $"delete from TB_FinDocSourceDetailTemp where FinDocSourceHeaderTemp_ID = {responseFinDocSourceHeader.Id} " +
                                $"delete from TB_FinDocSourceHeaderTemp where FileAttribute_ID = {FileAttribute.FileAttrID}";
                            _dapperRepository.ExecQuery(scriptRemove);
                        }
                    }
                    else
                    {
                        var scriptRemove = $"delete from TB_FinDocSourceHeaderTemp where FileAttribute_ID = {FileAttribute.FileAttrID}";
                        _dapperRepository.ExecQuery(scriptRemove);
                    }

                }

                var customerSourceHeader = _mapper.Map<FinDocSourceHeaderTemp>(processCustomerFileDto);
                customerSourceHeader.FileAttributeId = FileAttribute.FileAttrID;
                customerSourceHeader.Status = GenericEnum.Success;
                customerSourceHeader.ProcessDate = DateTime.Now;
                customerSourceHeader.Created = DateTime.Now;
                customerSourceHeader.CreatedBy = "system";
                customerSourceHeader.FinDocSourceDetailTemps = new List<FinDocSourceDetailTemp>();

                _dapperRepository.Add(customerSourceHeader);
                List<FinDocSourceDetailTemp> sourceDetailTempList = processCustomerFileDto.SourceDetailTempList.ConvertAll(i => (FinDocSourceDetailTemp)i);

                sourceDetailTempList.ForEach(item => item.FinDocSourceHeaderTempId = customerSourceHeader.Id);


                File.WriteAllLines("D:\\ExportFinDocCSVFile.csv", sourceDetailTempList.Select(x => string
                .Join(",",
                null
                , x.RowIdentifier
                , x.BankCode
                , x.OwnerBranchCode
                , x.JournalNumber
                , x.IssuerBranchCode
                , x.AccountNumber
                , x.EffectiveDate
                , x.Time
                , x.ArticleNumber
                , x.DebitCreditType
                , x.Amount
                , x.ReturnCode
                , x.ComprehensiveCode
                , x.GuaranteeTypeCode
                , x.ProgramName
                , x.DestinationBranchCode
                , x.ArticleDesc
                , x.OperationCode
                , x.SubsystemName
                , x.PKNumber
                , x.SessionId
                , (int)x.Status
                , (int)x.MappingStatus
                , x.FinDocSourceHeaderTempId
                )));

                _dapperRepository.ExecQuery("EXEC dbo.SP_ImportToSourceDetailTempList @pathFile='E:\\ExportFinDocCSVFile.csv' ,@tableName='TB_FinDocSourceDetailTemp'");
                //var sourceDetailTempListDto = _mapper.Map<List<SourceDetailTempListDto>>(sourceDetailTempList);
                //var dataTable = new DataTable();
                //dataTable = sourceDetailTempListDto.ToDataTable();


                // _dapperRepository.BulkInsert(sourceDetailTempList);

                var scriptRequest = $"update R set Status = {(int)RequestStatus.Draft} from TB_Request R where ID= {FileAttribute.RequestId}";
                _dapperRepository.ExecQuery(scriptRequest);

                return customerSourceHeader;

            }
            catch (Exception exp)
            {
                var message = exp.Message;
                throw;
            }
            return null;
        }
    }
}
