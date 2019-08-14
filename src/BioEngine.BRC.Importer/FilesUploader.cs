using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using BioEngine.Core.DB;
using BioEngine.Core.Entities;
using BioEngine.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BioEngine.BRC.Importer
{
    public class FilesUploader
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IStorage _storage;
        private readonly BioContext _dbContext;
        private readonly ILogger<FilesUploader> _logger;
        private readonly ImporterOptions _options;

        public FilesUploader(IHttpClientFactory httpClientFactory, IStorage storage, BioContext dbContext,
            ILogger<FilesUploader> logger, IOptions<ImporterOptions> options)
        {
            _httpClientFactory = httpClientFactory;
            _storage = storage;
            _dbContext = dbContext;
            _logger = logger;
            _options = options.Value;
        }

        public async Task<StorageItem> UploadFromUrlAsync(string url, string path, string fileName = null)
        {
            fileName ??= Path.GetFileName(url);
            if (!string.IsNullOrEmpty(fileName))
            {
                foreach ((string from, string to) in _options.FilePathRewrites)
                {
                    if (!url.StartsWith(from)) continue;
                    url = url.Replace(from, to);
                    break;
                }

                _logger.LogInformation($"Downloading file from url {url}");
                try
                {
                    var fileData = await _httpClientFactory.CreateClient().GetByteArrayAsync(url);
                    var item = await _storage.SaveFileAsync(fileData, fileName, path);
                    return item;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error while uploading file from url: {url}: {ex}");
                }
            }

            return null;
        }

        public StorageItem UploadByPath(string path, int size, DateTimeOffset date)
        {
            var file = new StorageItem
            {
                Id = Guid.NewGuid(),
                FileName = Path.GetFileName(path),
                DateAdded = date,
                DateUpdated = date,
                FilePath = $"files{path}",
                Path = Path.GetDirectoryName($"files{path}").Replace("\\", "/"),
                FileSize = size,
                Type = StorageItemType.Other,
                PublicUri = new Uri($"https://s3.bioware.ru/files/{path}")
            };

            _dbContext.Add(file);

            return file;
        }

        public Task FinishBatchAsync()
        {
            return _storage.FinishBatchAsync();
        }

        public void BeginBatch()
        {
            _storage.BeginBatch();
        }
    }
}
