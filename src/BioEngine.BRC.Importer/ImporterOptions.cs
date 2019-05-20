using System;

namespace BioEngine.BRC.Importer
{
    public class ImporterOptions
    {
        public string ApiUri { get; set; }
        public string ApiToken { get; set; }
        public Guid SiteId { get; set; }
    }
}
