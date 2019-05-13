using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace BioEngine.BRC.Importer
{
    public class ImporterService : IHostedService
    {
        private readonly Importer _importer;

        public ImporterService(Importer importer)
        {
            _importer = importer;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var dataJson = await File.ReadAllTextAsync("export.json", cancellationToken);
            var data = JsonConvert.DeserializeObject<Export>(dataJson);

            await _importer.ImportAsync(data);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }
    }
}
