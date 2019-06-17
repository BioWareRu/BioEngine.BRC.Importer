using BioEngine.BRC.Common;
using BioEngine.Core.Seo;
using BioEngine.Core.Tests;
using BioEngine.Extra.Facebook;
using BioEngine.Extra.Twitter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace BioEngine.BRC.Importer.Tests
{
    public class ImporterTestScope : BaseTestScope
    {
        protected override Core.BioEngine ConfigureBioEngine(Core.BioEngine bioEngine)
        {
            return base.ConfigureBioEngine(bioEngine).ConfigureServices(collection =>
                {
                    collection.AddScoped<FilesUploader>();
                    collection.AddHttpClient();
                    collection.AddScoped<HtmlParser>();
                }).AddBrcDomain().AddS3Storage().AddModule<SeoModule>()
                .AddModule<TwitterModule>()
                .AddElasticSearch()
                .AddModule<FacebookModule>().ConfigureAppConfiguration(builder =>
                {
                    builder.AddUserSecrets<Importer>();
                    builder.AddEnvironmentVariables();
                });
        }
    }

    public abstract class ImporterTest : BaseTest<ImporterTestScope>
    {
        protected ImporterTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }
    }
}
