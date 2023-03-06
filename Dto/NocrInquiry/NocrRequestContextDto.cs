using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.NocrInquiry
{
    public class NocrRequestContextDto
    {


        
        public long NocrInquirySourceHeaderId { get; set; }
        public int RowIdentifier { get; set; }
        public string UniqueIdentifier { get; set; }
        public string BirthDate { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FatherName { get; set; }
        public string DisMatchfields { get; set; }
        public string Descriptions { get; set; }
        public bool IsDead { get; set; }
        public DateTime Date { get; set; }
        //public GenericEnum Status { get; set; }
        //public GenericEnum MappingStatus { get; set; }
    }
}
