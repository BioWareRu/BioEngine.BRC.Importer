using System;

namespace BioEngine.BRC.Importer
{
    public class ImporterOptions
    {
        public string ApiUri { get; set; }
        public string ApiToken { get; set; }
        public string ImportFilePath { get; set; }
        public Guid SiteId { get; set; }
        public string OutputPath { get; set; }
        public string FilesBaseUrl { get; set; }

        public bool ImportNews { get; set; }
        public bool ImportArticles { get; set; }
        public bool ImportGallery { get; set; }
        public bool ImportFiles { get; set; }
    }
}
