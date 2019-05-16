using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace BioEngine.BRC.Importer
{
    public class ImporterService : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;

        public ImporterService(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var dataJson = await File.ReadAllTextAsync("export.json", cancellationToken);
            var data = JsonConvert.DeserializeObject<Export>(dataJson);
            var siteId = Guid.Parse(_configuration["BE_SITE_ID"]);
            using (var scope = _serviceProvider.CreateScope())
            {
                var importer = scope.ServiceProvider.GetRequiredService<Importer>();
                await importer.ImportAsync(siteId, data);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
