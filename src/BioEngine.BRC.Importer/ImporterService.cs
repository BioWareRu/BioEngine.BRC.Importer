using System;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BioEngine.BRC.Importer
{
    public class ImporterService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ImporterService> _logger;
        private readonly ImporterOptions _options;

        public ImporterService(IServiceProvider serviceProvider,
            IOptions<ImporterOptions> options, ILogger<ImporterService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Download data from {apiUri}", _options.ApiUri);
            var data = await _options.ApiUri.WithHeader("Authorization", $"Bearer {_options.ApiToken}")
                .GetJsonAsync<Export>(cancellationToken);
            _logger.LogInformation("Data is downloaded");
            using (var scope = _serviceProvider.CreateScope())
            {
                var importer = scope.ServiceProvider.GetRequiredService<Importer>();
                _logger.LogInformation("Import for site {siteId}", _options.SiteId);
                await importer.ImportAsync(_options.SiteId, data);
            }

            _logger.LogInformation("Import done");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
