using System;
using System.IO;
using BioEngine.BRC.Common;
using BioEngine.Core.Pages.Api;
using BioEngine.Core.Seo;
using BioEngine.Core.Storage;
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
                }).AddBrcDomain().AddModule<FileStorageModule, FileStorageModuleConfig>((configuration, environment) =>
                {
                    var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                    Directory.CreateDirectory(tempDirectory);
                    return new FileStorageModuleConfig(new Uri("https://test.bioware.ru"), tempDirectory);
                }).AddModule<SeoModule>()
                .AddModule<TwitterModule>()
                .AddElasticSearch()
                .AddModule<PagesApiModule>()
                .AddModule<FacebookModule>().ConfigureAppConfiguration(builder =>
                {
                    builder.AddUserSecrets<Importer>();
                    builder.AddEnvironmentVariables();
                }).Run<ImporterStartup>();
        }
    }

    public abstract class ImporterTest : BaseTest<ImporterTestScope>
    {
        protected ImporterTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }
    }
}
