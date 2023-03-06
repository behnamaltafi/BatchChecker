using AutoMapper;
using BatchChecker.Dto.FinDoc;
using BatchOp.Application.DTOs.Files;
using BatchOP.Domain.Entities.Subsidy;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Mapping
{
    public class GeneralProfile : Profile
    {
        public GeneralProfile()
        {
            CreateMap<ProcessFileDto, FinDocSourceHeaderTemp>()
                 .ForMember(dest => dest.NumberOfRecordsCredit,
                        opt => opt.MapFrom(src => src.ViewModel.NumberOfRecordsCredit))
                 .ForMember(dest => dest.NumberOfRecordsDebit,
                        opt => opt.MapFrom(src => src.ViewModel.NumberOfRecordsDebit))
                 .ForMember(dest => dest.TotalAmountCredit,
                        opt => opt.MapFrom(src => src.ViewModel.TotalAmountCredit))
                 .ForMember(dest => dest.TotalAmountDebit,
                        opt => opt.MapFrom(src => src.ViewModel.TotalAmountDebit))
                .ReverseMap();

            CreateMap<FinDocSourceDetailTemp, SourceDetailTempListDto>()
                .ForMember(dest => dest.FinDocSourceHeaderTemp_ID,
                        opt => opt.MapFrom(src => src.FinDocSourceHeaderTempId))
               .ReverseMap();
        }
    }
}
