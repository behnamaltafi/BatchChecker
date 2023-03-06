using BatchOP.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Dto.FinDoc
{
    public class FinDocRequestFileAttrDto
    {
        public long RequestId { get; set; }
        public GenericEnum Status { get; set; }
        public RequestType type { get; set; }
        public string FileName { get; set; }
        public string RepositoryAddress { get; set; }
        public long FileAttrID { get; set; }
    }
}
