using System;
using System.Collections.Generic;
using System.Text;

namespace BatchChecker.Configuration.Api
{
    public class GalaxyTokenConfig
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Grant_type { get; set; }
        public string Scope { get; set; }
        public string Url { get; set; }
    }
}
