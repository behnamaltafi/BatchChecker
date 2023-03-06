using BatchChecker.Business.AccountCustomer;
using BatchChecker.Dto;
using BatchChecker.Dto.Customer;
using BatchChecker.Dto.NocrInquiry;
using BatchOp.Application.Interfaces.DapperRepository;
using BatchOp.Application.Interfaces.Repositories;
using BatchOp.Infrastructure.Shared.ApiCaller.Dto.TataGateway.Ident;
using BatchOp.Infrastructure.Shared.ApiCaller.TataGateway;
using BatchOp.Infrastructure.Shared.RabbitMQ;
using BatchOp.Infrastructure.Shared.Services.Utilities;
using BatchOP.Domain.Entities.NocrInquiry;
using BatchOP.Domain.Entities.Staff;
using BatchOP.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using AutoMapper;
using BatchChecker.Business.NocrInquiry;
using BatchChecker.Business.NocrInquiry.HasError;
using BatchChecker.Dto.Share;
using BatchOP.Domain.Entities.Customer;
using System.Diagnostics;
using System.Text.RegularExpressions;
using BatchChecker.Extensions;
using System.Xml.Linq;

namespace BatchChecker.RequestCheckers
{
    public class NocrInquiryChecker : IRequestChecker<BatchProcessQueueDetailRequestDto>
    {
        private readonly ITataGatewayApi _tataGatewayApi;
        private readonly ILogger _logger;
        private readonly IDapperRepository _dapperRepository;
        private readonly IDapperUnitOfWork _dapperUnitOfWork;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<int, string> uselessStrings = new Dictionary<int, string>() {
               { 1 , "سیده"},
               { 2 ,"سید"},
               { 3 , "السادات"},
               { 4 , "سادات"},
            };
        public NocrInquiryChecker(
            IMapper mapper,
             ILogger logger,
             ITataGatewayApi tataGatewayApi,
            IDapperRepository dapperRepository,
            IDapperUnitOfWork dapperUnitOfWork,
            IConfiguration configuration
            )
        {
            _tataGatewayApi = tataGatewayApi;
            _logger = logger;
            _dapperRepository = dapperRepository;
            _dapperUnitOfWork = dapperUnitOfWork;
            _configuration = configuration;
        }



        #region Publish
        /// <summary>
        /// واکشی لیست درخواست ها جهت ثبت در تیبل صف و سپس ارسال به ربیت
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> SenderAsync(CancellationToken stoppingToken)
        {
            var queryResponse = await GetListSourceHeaderAsync(stoppingToken);
            //using (var transactionScope = new TransactionScope())
            //{
            //    if (queryResponse.Any())

            //        _dapperUnitOfWork.BeginTransaction(_dapperRepository);
            //    await InsertToBatchProcessQueueDetailAsync(queryResponse, stoppingToken);
            //    transactionScope.Complete();
            //}


            if (queryResponse.Any())
            {
                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                await InsertToBatchProcessQueueDetailAsync(queryResponse, stoppingToken);
                _dapperUnitOfWork.Commit();
            }
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount, RequestType " +
                $" from TB_BatchProcessQueue Q join TB_NocrInquirySourceHeader C on C.ID = Q.SourceHeader_ID where " +
                $" (Q.Status = {(int)GenericEnum.Unasign}) and RequestType = {(int)RequestType.NocrInquiry} " +
                $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"GetListBatchProcessQueueDetail Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }

        /// <summary>
        /// آپدیت درخواست بعد ثبت در تیبل صف و ربیت
        /// </summary>
        /// <param name="Id"></param>
        /// <param name="processIndex"></param>
        /// <returns></returns>
        private async Task<bool> UpdateSourceHeaderAsync(long Id, int processIndex, CancellationToken stoppingToken)
        {
            var script = $"Update S set Status = {(int)GenericEnum.Pending} , ProcessIndex = {processIndex} , ProcessDate = '{DateTime.Now}' " +
                $"from TB_NocrInquirySourceHeader S where ID={Id}";
            lock (this)
            {
                var NocrInquirySourceHeader = _dapperRepository.Query<NocrInquirySourceHeader>(script);
            }
            return await Task.FromResult(true);
        }

        private async Task<bool> UpdateStatusSourceHeader(long Id, GenericEnum status)
        {
            var script = $"Update H set Status = {(int)status} " +
                 $"from TB_NocrInquirySourceHeader H where ID={Id}";
            await _dapperRepository.QueryAsync<NocrInquirySourceHeader>(script);
            return true;
        }

        private async Task<bool> IsDone(long sourceHeaderId)
        {
            var count = await _dapperRepository.QueryAsync<int>($"SELECT COUNT(ID)  FROM TB_BatchProcessQueue where SourceHeader_ID = {sourceHeaderId} and Status!= 0 and RequestType = {(int)RequestType.NocrInquiry}");
            return count.FirstOrDefault() == 0;
        }


        /// <summary>
        /// ثبت درخواست ها با ایندکس در تیبل صف
        /// </summary>
        /// <param name="nocrInquiryResponses"></param>
        /// <returns></returns>
        private async Task<bool> InsertToBatchProcessQueueDetailAsync(IEnumerable<SourceHeaderDto> nocrInquiryResponses, CancellationToken stoppingToken)
        {
            foreach (var item in nocrInquiryResponses)
            {
                var rabbitMQConfiguration = _configuration.GetSection("RabbitMQ").Get<RabbitMQConfiguration>();
                var batchCount = rabbitMQConfiguration.NocrInquiryBatchCount;

                int startIndex = 0;
                int endIndex = 0;

                for (int i = 1; endIndex < item.RequestCount; i++)
                {
                    startIndex = (i - 1) * batchCount + 1;
                    endIndex = item.RequestCount <= i * batchCount ? endIndex = item.RequestCount : endIndex = i * batchCount;

                    BatchProcessQueue batchProcessQueueDetail = new BatchProcessQueue()
                    {
                        SourceHeaderId = item.SourceHeaderId,
                        StartIndex = startIndex,
                        EndIndex = endIndex,
                        Status = GenericEnum.Unasign,
                        RequestType = RequestType.NocrInquiry,
                        TryCount = 0,
                    };

                    if (FindBatchProccessQueue(batchProcessQueueDetail) == 0)
                    {
                        _dapperRepository.Add(batchProcessQueueDetail);
                        _logger.Information($"InsertToBatchProcessQueueDetail : {JsonConvert.SerializeObject(batchProcessQueueDetail)}");
                        await UpdateSourceHeaderAsync(item.SourceHeaderId, item.RequestCount, stoppingToken);
                    }
                }
            }
            return true;
        }

        private int FindBatchProccessQueue(BatchProcessQueue batchProcessQueueDetail)
        {
            var scriptBatchProcessQueue = $"SELECT * FROM TB_BatchProcessQueue where SourceHeader_ID = {batchProcessQueueDetail.SourceHeaderId} and " +
                                    $"StartIndex = {batchProcessQueueDetail.StartIndex} and " +
                                    $"EndIndex = {batchProcessQueueDetail.EndIndex} " +
                                    $"and RequestType = {(int)RequestType.NocrInquiry} ";
            var queryResponse = _dapperRepository.Query<BatchProcessQueue>(scriptBatchProcessQueue);

            return queryResponse.Count();
        }
        /// <summary>
        /// واکشی لیست درخواست ها
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<SourceHeaderDto>> GetListSourceHeaderAsync(CancellationToken stoppingToken)
        {
            _logger.Information($"Run GetListHeaderNocrInquiry");
            var scriptHeader = $"SELECT count(*) RequestCount ,R.ID RequestId, H.ID SourceHeaderId " +
                $"FROM TB_Request R " +
                $"INNER JOIN TB_NocrInquirySourceHeader H On R.ID = H.Request_ID " +
                $"join TB_NocrInquirySourceDetail det on h.id = det.NocrInquirySourceHeader_ID " +
                $"WHERE R.Status = 2 and H.Status = {(int)GenericEnum.Unasign} " +
                $"group by R.ID,H.ID";

            var queryResponse = _dapperRepository.Query<SourceHeaderDto>(scriptHeader);
            _logger.Information($"GetListHeaderNocrInquiryCount Count:{queryResponse.Count()}");

            return await Task.FromResult(queryResponse);
        }
        #endregion

        #region Subscribe
        /// <summary>
        /// دریافت لیست جزییات مشتریان براساس پارامترهای دریافتی از RabbitMQ
        /// </summary>
        /// <param name="Request"></param>
        /// <returns></returns>
        public async Task<bool> CheckAsync(BatchProcessQueueDetailRequestDto batchProcess, CancellationToken cancellationToken)
        {
            try
            {
                IEnumerable<BatchProcessQueue> responseQueue = await CheckInNocrInquiryBatchMappingQueue(batchProcess);
                if (!responseQueue.Any())
                    return await Task.FromResult(true);

                var scriptDetail = $"select C.NocrInquirySourceHeader_ID as NocrInquirySourceHeaderId , C.* from TB_NocrInquirySourceDetail C " +
                           $" join TB_NocrInquirySourceHeader H on H.ID = C.NocrInquirySourceHeader_ID " +
                           $"where (H.Status != {(int)GenericEnum.Deleted} or H.Status != {(int)GenericEnum.Cancel}) " +
                           $" and C.NocrInquirySourceHeader_ID = {batchProcess.SourceHeaderId} " +
                           $" and C.RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex} ";
                //$" and C.Status = {(int)GenericEnum.Unasign} ";

                IEnumerable<NocrInquirySourceDetail> responseDetails;
                responseDetails = await _dapperRepository.QueryAsync<NocrInquirySourceDetail>(scriptDetail);
                _logger.Information($"GetListNocrInquirySourceDetail Count: {responseDetails.Count()}");

                if (!responseDetails.Any())
                    return await Task.FromResult(true);

                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Pending, 1);

                bool result = false;


                result = await ProcessInCustomerSourceDetail(responseDetails, cancellationToken);
                if (result)
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Success, 2);
                else
                    await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);

                var header = (await _dapperRepository.QueryAsync<NocrInquirySourceHeader>($"select * from " +
                    $"TB_NocrInquirySourceHeader where ID = {batchProcess.SourceHeaderId}")).FirstOrDefault();

                if (await IsDone(batchProcess.SourceHeaderId) && header.Status == GenericEnum.Pending)
                    await UpdateStatusSourceHeader(batchProcess.SourceHeaderId, GenericEnum.Success);

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"NocrInquiryChecker.CheckAsync Exception", ex);
                await UpdateBatchProcessQueueDetail(batchProcess.ID, GenericEnum.Unasign, 2);
                return await Task.FromResult(true);
            }

        }

        /// <summary>
        /// چک وجود پیام در تیبل صف
        /// </summary>
        /// <param name="batchProcess"></param>
        /// <returns></returns>
        private async Task<IEnumerable<BatchProcessQueue>> CheckInNocrInquiryBatchMappingQueue(BatchProcessQueueDetailRequestDto batchProcess)
        {
            var scriptQueue = $"select * from TB_BatchProcessQueue where ID ={batchProcess.ID} " +
                $"and (Status = {(int)GenericEnum.Pending} or  Status = {(int)GenericEnum.InQueue} or  Status = {(int)GenericEnum.Unasign}) " +
                $"and RequestType = {(int)RequestType.NocrInquiry}";
            var responseQueue = await _dapperRepository.QueryAsync<BatchProcessQueue>(scriptQueue);

            if (responseQueue.Any())
            {
                var scriptControl = $"delete FROM TB_NocrInquiryDetailControl where NoCrInquirySourceDetail_ID in " +
                    $"(select ID from TB_NocrInquirySourceDetail where NocrInquirySourceHeader_ID = {batchProcess.SourceHeaderId} " +
                    $"and RowIdentifier between {batchProcess.StartIndex} and {batchProcess.EndIndex})";
                _dapperRepository.ExecQuery(scriptControl);
            }

            return responseQueue;
        }




        /// <summary>
        /// پردازش بر روی لیست دریافتی 
        /// </summary>
        /// <param name="customerResponses"></param>
        /// <returns></returns>
        private async Task<bool> ProcessInCustomerSourceDetail(IEnumerable<NocrInquirySourceDetail> nocrReponse, CancellationToken cancellationToken)
        {
            IList<NocrInquirySourceDetail> lstNocrInquirySourceDetail = new List<NocrInquirySourceDetail>();
            IList<NocrInquiryDetailControl> lstNocrInquiryDetailControl = new List<NocrInquiryDetailControl>();
            var sucessCount = 0;
            var WrongCount = 0;
            var deadCount = 0;
            var sourceHeaderId = nocrReponse.Select(c => c.NocrInquirySourceHeaderId).FirstOrDefault();

            try
            {
                foreach (var request in nocrReponse)
                {
                    var validateresult = await Validate(request, cancellationToken);
                    validateresult.NationalCode = request.UniqueIdentifier;
                    validateresult.BirthDate = (request.BirthDate != null && Int64.TryParse(request.BirthDate.Replace("/", "").Replace("-", ""), out var parsresult)) ? Int64.Parse(request.BirthDate.Replace("/", "").Replace("-", "")) : 0;

                    if (validateresult.NationalCodeIsValid == true //&& validateresult.BirthDateIsValid == true
                                                                   )
                    {

                        #region استعلام ثبت احوال 
                        var InquiryChecker = await ConfigValidateChain(validateresult, cancellationToken);
                        validateresult = await InquiryChecker.Execute(validateresult, cancellationToken);
                        #endregion
                        #region بررسی مغایرت نام 

                        if (validateresult != null && !string.IsNullOrEmpty(validateresult.FirstName))
                            validateresult = CompareHandler(request, validateresult);
                        else
                        {
                            validateresult.ErrorMessage = "مشتری  با اطلاعات دریافتی یافت نشد ";
                            validateresult.HasError = true;
                        }

                        #region نتیجه ولیدیشن
                        await AbstractHasErrorHandler(validateresult, cancellationToken);

                        #endregion
                        #endregion
                        //InquirySourceDetail.BirthDateVerified = request.BirthDate.IsValidateBirthDate();
                        //#endregion

                        NocrInquirySourceDetail sourceDetail = new NocrInquirySourceDetail()
                        {
                            Id = request.Id,
                            FirstName = request.FirstName,
                            LastName = request.LastName,
                            UniqueIdentifier = request.UniqueIdentifier,
                            BirthDate = request.BirthDate,
                            NocrInquirySourceHeaderId = request.NocrInquirySourceHeaderId,
                            RowIdentifier = request.RowIdentifier,
                            FatherName = request.FatherName,
                            DisMatchfields = request.DisMatchfields,
                            Descriptions = request.Descriptions,
                            IsDead = request.IsDead,
                            Status = request.Status,

                        };
                        lstNocrInquirySourceDetail.Add(sourceDetail);


                        if (validateresult.DeathStatus == 1)
                        {

                            NocrInquiryDetailControl detailControl = new NocrInquiryDetailControl()
                            {
                                UniqueIdentifier = validateresult != null && !string.IsNullOrEmpty(validateresult.NationalCode) ? validateresult.NationalCode : request.UniqueIdentifier,
                                BirthDate = (validateresult != null && validateresult.BirthDate > 0) ? validateresult.BirthDate.ToString() : request.BirthDate,
                                FirstName = validateresult.FirstName,
                                LastName = validateresult.LastName,
                                FatherName = validateresult.FatherName,
                                Descriptions = "فوت شده است",
                                NocrInquirySourceDetailId = request.Id,
                                NationalCodeIsValid = validateresult.NationalCodeIsValid,
                                BirthDateIsValid = validateresult.BirthDateIsValid,
                                FirstNmaeIsMatch = validateresult.FirstNameIsMatch,
                                LastNmaeIsMatch = validateresult.LastNameIsMatch,
                                BirthDateIsMatch = validateresult.BirthDateIsMatch,
                                DisMatchfields = String.Empty,
                                HasError = true,
                                IsDead = true,
                                Created = DateTime.Now,
                                CreatedBy = "System",
                            };
                            lstNocrInquiryDetailControl.Add(detailControl);
                            deadCount++;
                        }
                        else
                        {
                            NocrInquiryDetailControl detailControl = new NocrInquiryDetailControl()
                            {
                                UniqueIdentifier = validateresult != null && !string.IsNullOrEmpty(validateresult.NationalCode) ? validateresult.NationalCode : request.UniqueIdentifier,
                                BirthDate = (validateresult != null && validateresult.BirthDate > 0) ? validateresult.BirthDate.ToString() : request.BirthDate,
                                FirstName = validateresult.FirstName,
                                LastName = validateresult.LastName,
                                FatherName = validateresult.FatherName,
                                Descriptions = validateresult.HasError == true ? validateresult.ErrorMessage : null,
                                NocrInquirySourceDetailId = request.Id,
                                NationalCodeIsValid = validateresult.NationalCodeIsValid,
                                BirthDateIsValid = validateresult.BirthDateIsValid,
                                FirstNmaeIsMatch = validateresult.FirstNameIsMatch,
                                LastNmaeIsMatch = validateresult.LastNameIsMatch,
                                BirthDateIsMatch = validateresult.BirthDateIsMatch,
                                DisMatchfields = String.Empty,
                                HasError = validateresult.HasError,
                                IsDead = validateresult.DeathStatus == 1 ? true : false,
                                Created = DateTime.Now,
                                CreatedBy = "System",
                            };
                            lstNocrInquiryDetailControl.Add(detailControl);
                        }

                    }

                    WrongCount = WrongCount + (validateresult.HasError && validateresult.DeathStatus != 1 ? 1 : 0);
                    sucessCount = sucessCount + ((validateresult.HasError || validateresult.DeathStatus == 1) ? 0 : 1);
                }

                _dapperUnitOfWork.BeginTransaction(_dapperRepository);
                _dapperRepository.BulkUpdate<NocrInquirySourceDetail>(lstNocrInquirySourceDetail);
                _logger.Information($"NocrInquirySourceDetail BulkUpdateCount: {lstNocrInquirySourceDetail.Count()}");

                _dapperRepository.BulkInsert<NocrInquiryDetailControl>(lstNocrInquiryDetailControl);
                _logger.Information($"lstNocrInquiryDetailControl BulkInsertCount: {lstNocrInquiryDetailControl.Count()}");


                await UpdateSourceHeader(sourceHeaderId, deadCount, sucessCount, WrongCount);
                _dapperUnitOfWork.Commit();
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _dapperUnitOfWork.Rollback();
                _logger.ErrorLog($"NocrInquiryChecker.ProcessInCustomerSourceDetail Exception", ex);
                return await Task.FromResult(false);
            }
        }

        private async Task<IdentReponseDto> Validate(NocrInquirySourceDetail request, CancellationToken cancellationToken)
        {
            try
            {
                IdentReponseDto identReponse = new IdentReponseDto();
                #region بررسی صحت کد ملی 
                identReponse.NationalCodeIsValid = request.UniqueIdentifier.IsValidIranianNationalCode();
                //identReponse.HasError = request.UniqueIdentifier.IsValidIranianNationalCode();
                #endregion

                #region بررسی صحت تاریخ تولد 
                identReponse.BirthDateIsValid = request.BirthDate.IsValidateBirthDate();
                //identReponse.HasError = request.BirthDate.IsValidateBirthDate();
                #endregion

                return await Task.FromResult(identReponse);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                _logger.ErrorLog($"NocrInquiryCheker.Validate Exception :", ex);
                return await Task.FromResult(new IdentReponseDto
                {
                    Status = (long)GenericEnum.ServerError,
                    ErrorMessage = message,
                    NationalCode = request.UniqueIdentifier,
                    HasError = true,
                });
            }
        }

        private async Task AbstractHasErrorHandler(IdentReponseDto identReponse, CancellationToken cancellationToken)
        {
            var hasErrorHandler = new HasErrorHandler();
            await hasErrorHandler.Execute(identReponse, cancellationToken);
        }

        private async Task UpdateSourceHeader(long sourceHeaderId, long ControlledDeadCount, int ControlledCorrectCount, int ControlledWrongCount)
        {

            var script = $"Update C set ControlledDeadCount = ControlledDeadCount + {ControlledDeadCount} ," +

             $"ControlledCorrectCount = ControlledCorrectCount + {ControlledCorrectCount} ," +
                $"ControlledWrongCount = ControlledWrongCount + {ControlledWrongCount} " +

             $" from TB_NocrInquirySourceHeader C where ID={sourceHeaderId} ";
            var nocrSourceHeader = await _dapperRepository.QueryAsync<NocrInquirySourceHeader>(script);
            //await Task.Delay(0);
        }


        public IdentReponseDto CompareHandler(NocrInquirySourceDetail request, IdentReponseDto person)
        {
            var replaceChar = "";
            var requestFirstName = request.FirstName.replaceUselessStrings(replaceChar, uselessStrings).RemoveUnderLine();
            var personFirstName = person.FirstName.replaceUselessStrings(replaceChar, uselessStrings).RemoveUnderLine();

            string requestLastName = request.LastName.Trim().FixPersianChars().RemoveSymbol().RemoveUnderLine();
            string personLastName = person.LastName.Trim().FixPersianChars().RemoveSymbol().RemoveUnderLine();

            person.FirstNameIsMatch = (request.FirstName != null && person.FirstName != null) &&
                (requestFirstName.Trim() == personFirstName.Trim());
            person.LastNameIsMatch = (request.LastName != null && person.LastName != null) &&
                (requestLastName == personLastName);
            person.BirthDateIsMatch = (request.BirthDate != null && person.BirthDate > 0) &&
                (request.BirthDate.RemoveSymbol() == person.BirthDate.ToString().RemoveSymbol());

            FirstNameCompare(person, requestFirstName, personFirstName);
            LastNameCompare(person, requestLastName, personLastName);

            if (person.FirstNameIsMatch == true && person.LastNameIsMatch == true && (string.IsNullOrEmpty(request.BirthDate) || string.IsNullOrWhiteSpace(request.BirthDate) || request.BirthDate == "0"))
                person.BirthDateIsMatch = true;

            return person;
        }

        private static void LastNameCompare(IdentReponseDto person, string requestLastName, string personLastName)
        {
            int lengthLastName = requestLastName.Length - personLastName.Length;
            if (lengthLastName == 0 && person.LastNameIsMatch == false)
            {
                int j = 0;
                for (int i = 0; i < personLastName.Trim().Length; i++)
                {
                    if (requestLastName.Trim()[i] != personLastName.Trim()[i])
                    {
                        j++;
                        if (j >= 2)
                            break;
                    }
                }
                if (j == 1 || j == 0 || j == 2)
                    person.LastNameIsMatch = true;
            }
            if (lengthLastName == 1 && person.LastNameIsMatch == false)
            {
                int j = 0;
                for (int i = 0; i < personLastName.Trim().Length; i++)
                {
                    if (requestLastName.Trim()[i] != personLastName.Trim()[i - j])
                    {
                        j++;
                        if (j >= 2)
                            break;
                    }
                }
                if (j == 1 || j == 0)
                    person.LastNameIsMatch = true;
            }
            if (lengthLastName == -1 && person.LastNameIsMatch == false)
            {
                int j = 0;
                for (int i = 0; i < requestLastName.Trim().Length; i++)
                {
                    var r = requestLastName.Trim()[i - j];
                    var p = personLastName.Trim()[i];
                    if (requestLastName.Trim()[i - j] != personLastName.Trim()[i])
                    {
                        j++;
                        if (j >= 2)
                            break;
                    }
                }
                if (j == 1 || j == 0)
                    person.LastNameIsMatch = true;
            }
        }

        private static void FirstNameCompare(IdentReponseDto person, string requestFirstName, string personFirstName)
        {
            int lengthFirstName = requestFirstName.Length - personFirstName.Length;
            if (lengthFirstName == 0 && person.FirstNameIsMatch == false)
            {
                int j = 0;
                for (int i = 0; i < personFirstName.Trim().Length; i++)
                {
                    if (requestFirstName.Trim()[i] != personFirstName.Trim()[i])
                    {
                        j++;
                        if (j >= 2)
                            break;
                    }
                }
                if (j == 1 || j == 0 || j == 2)
                    person.FirstNameIsMatch = true;
            }
            if (lengthFirstName == 1 && person.FirstNameIsMatch == false)
            {
                int j = 0;
                for (int i = 0; i < personFirstName.Trim().Length; i++)
                {
                    if (requestFirstName.Trim()[i] != personFirstName.Trim()[i - j])
                    {
                        j++;
                        if (j >= 2)
                            break;
                    }
                }
                if (j == 1 || j == 0)
                    person.FirstNameIsMatch = true;
            }
            if (lengthFirstName == -1 && person.FirstNameIsMatch == false)
            {
                int j = 0;
                for (int i = 0; i < requestFirstName.Trim().Length; i++)
                {
                    if (requestFirstName.Trim()[i - j] != personFirstName.Trim()[i])
                    {
                        j++;
                        if (j >= 2)
                            break;
                    }
                }
                if (j == 1 || j == 0)
                    person.FirstNameIsMatch = true;
            }
        }

        private async Task<InquiryPersonDb> ConfigValidateChain(IdentReponseDto request, CancellationToken ct)
        {
            var InquiryPersonDbHandler = new InquiryPersonDb(_tataGatewayApi);
            var SabtAhvalInquiryHandler = new SabtAhvalInquiry(_tataGatewayApi);

            var checkUserIsNotDeadHandler = new CheckUserIsNotDead();
            InquiryPersonDbHandler.SetSuccessor(checkUserIsNotDeadHandler).SetSuccessor(SabtAhvalInquiryHandler);

            return await Task.FromResult(InquiryPersonDbHandler);
        }



        /// <summary>
        /// آپدیت تیبل BatchProcessQueueDetail
        /// </summary>
        /// <param name="Id"></param>
        /// <param name="StartDate"></param>
        /// <param name="GenericEnum"></param>
        /// <returns></returns>
        private async Task<bool> UpdateBatchProcessQueueDetail(long Id, GenericEnum genericEnum, int Result)
        {
            if (Result == 1)
            {
                var scriptBatchProcess = $"update b set ProcessStartDate='{DateTime.Now}' " +
             $",Status={(int)genericEnum}" +
                 $" from TB_BatchProcessQueue b where ID = {Id}";
                _dapperRepository.ExecQuery(scriptBatchProcess);
            }
            else
            {
                var scriptBatchProcess = $"update b set ProcessEndDate='{DateTime.Now}', ProcessStartDate = ProcessStartDate " +
             $",Status={(int)genericEnum}" +
                 $" from TB_BatchProcessQueue b where ID = {Id}";
                _dapperRepository.ExecQuery(scriptBatchProcess);
            }

            return await Task.FromResult(true);
        }
        #endregion

        #region MissedSender
        public async Task<IEnumerable<BatchProcessQueueDetailRequestDto>> GetMissedSenderAsync(CancellationToken stoppingToken)
        {
            var scriptDetail = $"select Q.ID, Q.SourceHeader_ID as SourceHeaderId, Q.StartIndex, Q.EndIndex, (Q.TryCount + 1)TryCount, RequestType " +
            $" from TB_BatchProcessQueue Q join TB_NocrInquirySourceHeader C on C.ID = Q.SourceHeader_ID where " +
            $" (Q.Status = {(int)GenericEnum.Pending} or Q.Status = {(int)GenericEnum.InQueue}) and RequestType = {(int)RequestType.NocrInquiry} " +
            $"and  C.Status not in ({(int)GenericEnum.Deleted} , {(int)GenericEnum.Cancel}) and TryCount < 11 ";

            var responseDetail = _dapperRepository.Query<BatchProcessQueueDetailRequestDto>(scriptDetail);
            _logger.Information($"GetListBatchProcessQueueDetail_Missed Count: {responseDetail.Count()}");

            return await Task.FromResult(responseDetail);
        }
        #endregion
    }
}
