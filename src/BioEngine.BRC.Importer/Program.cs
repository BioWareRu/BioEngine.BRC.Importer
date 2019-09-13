using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using BioEngine.BRC.Common;
using BioEngine.Core.Api;
using BioEngine.Core.Pages.Api;
using BioEngine.Core.Posts.Api;
using BioEngine.Core.Seo;
using BioEngine.Extra.Facebook;
using BioEngine.Extra.IPB;
using BioEngine.Extra.Twitter;
using Flurl.Http;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog.Events;

namespace BioEngine.BRC.Importer
{
    static class Program
    {
        [SuppressMessage("ReSharper", "UseAsyncSuffix")]
        public static async Task Main(string[] args)
        {
            await new Core.BioEngine(args)
                .ConfigureServices((hostBuilder, collection) =>
                {
                    collection.AddScoped<Importer>();
                    collection.AddScoped<FilesUploader>();
                    collection.AddScoped<HtmlParser>();
                    collection.AddHttpClient();
                    collection.Configure<ImporterOptions>(options =>
                    {
                        if (!string.IsNullOrEmpty(hostBuilder.Configuration["BRC_IMPORT_FILE_PATH"]))
                        {
                            options.ImportFilePath = hostBuilder.Configuration["BRC_IMPORT_FILE_PATH"];
                        }
                        else
                        {
                            options.ApiUri = hostBuilder.Configuration["BRC_EXPORT_API_URL"];
                            options.ApiToken = hostBuilder.Configuration["BRC_EXPORT_API_TOKEN"];
                        }

                        options.SiteId = Guid.Parse(hostBuilder.Configuration["BRC_IMPORT_SITE_ID"]);
                        options.OutputPath = hostBuilder.Configuration["BRC_IMPORT_OUTPUT_PATH"];
                        if (!string.IsNullOrEmpty(hostBuilder.Configuration["BRC_REWRITES_FILE_PATH"]))
                        {
                            var json = File.ReadAllText(hostBuilder.Configuration["BRC_REWRITES_FILE_PATH"]);
                            options.FilePathRewrites = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                        }
                        else
                        {
                            options.FilePathRewrites = new Dictionary<string, string>();
                        }

                        bool.TryParse(hostBuilder.Configuration["BRC_IMPORT_NEWS"], out var importNews);
                        options.ImportNews = importNews;
                        bool.TryParse(hostBuilder.Configuration["BRC_IMPORT_ARTICLES"], out var importArticles);
                        options.ImportArticles = importArticles;
                        bool.TryParse(hostBuilder.Configuration["BRC_IMPORT_FILES"], out var importFiles);
                        options.ImportFiles = importFiles;
                        bool.TryParse(hostBuilder.Configuration["BRC_IMPORT_GALLERY"], out var importGallery);
                        options.ImportGallery = importGallery;
                    });
                })
                .AddPostgresDb()
                .AddBrcDomain()
                .AddModule<PagesApiModule>()
                .AddModule<PostsApiModule>()
                .AddElasticSearch()
                .AddS3Storage()
                .AddLogging(LogEventLevel.Information, LogEventLevel.Information, (loggerConfiguration, env) =>
                {
                    loggerConfiguration.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);
                    loggerConfiguration.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning);
                })
                .AddModule<IPBSiteModule, IPBSiteModuleConfig>((configuration, env) =>
                {
                    if (!Uri.TryCreate(configuration["BE_IPB_URL"], UriKind.Absolute, out var ipbUrl))
                    {
                        throw new ArgumentException($"Can't parse IPB url; {configuration["BE_IPB_URL"]}");
                    }

                    return new IPBSiteModuleConfig(ipbUrl) {ApiReadonlyKey = configuration["BE_IPB_API_READONLY_KEY"],};
                })
                .AddModule<SeoModule>()
                .AddModule<TwitterModule>()
                .AddModule<FacebookModule>()
                .ConfigureAppConfiguration(builder =>
                {
                    builder.AddUserSecrets<Importer>();
                    builder.AddEnvironmentVariables();
                }).ExecuteAsync<ImporterStartup>(async services =>
                {
                    var logger = services.GetRequiredService<ILogger<Importer>>();
                    var options = services.GetRequiredService<IOptions<ImporterOptions>>().Value;
                    var importer = services.GetRequiredService<Importer>();
                    Export data;
                    if (!string.IsNullOrEmpty(options.ImportFilePath))
                    {
                        logger.LogInformation("Read data from {path}", options.ImportFilePath);
                        var json = File.ReadAllText(options.ImportFilePath);
                        data = JsonConvert.DeserializeObject<Export>(json);
                    }
                    else
                    {
                        logger.LogInformation("Download data from {apiUri}", options.ApiUri);
                        data = await options.ApiUri.WithHeader("Authorization", $"Bearer {options.ApiToken}")
                            .GetJsonAsync<Export>();
                        logger.LogInformation("Data is downloaded");
                    }

                    logger.LogInformation("Import for site {siteId}", options.SiteId);
                    await importer.ImportAsync(options.SiteId, data);
                    logger.LogInformation("Import done");
                });
        }
    }

    public class ImporterStartup : BioEngineApiStartup
    {
        public ImporterStartup(IConfiguration configuration, IHostEnvironment hostEnvironment) : base(configuration,
            hostEnvironment)
        {
        }

        protected override void ConfigureEndpoints(IApplicationBuilder app, IHostEnvironment env,
            IEndpointRouteBuilder endpoints)
        {
            base.ConfigureEndpoints(app, env, endpoints);
            endpoints.AddBrcRoutes();
        }
    }


    #region dto    

    [PublicAPI]
    public class Export
    {
        public List<DeveloperExport> Developers = new List<DeveloperExport>();
        public List<GameExport> Games = new List<GameExport>();
        public List<TopicExport> Topics = new List<TopicExport>();
        public List<NewsExport> News = new List<NewsExport>();
        public List<ArticleCatExport> ArticlesCats = new List<ArticleCatExport>();
        public List<ArticleExport> Articles = new List<ArticleExport>();
        public List<FileCatExport> FilesCats = new List<FileCatExport>();
        public List<FileExport> Files = new List<FileExport>();
        public List<GalleryCatExport> GalleryCats = new List<GalleryCatExport>();
        public List<GalleryExport> GalleryPics = new List<GalleryExport>();
    }


    public class DeveloperExport
    {
        public int Id;
        public string Url;
        public string FullUrl;
        public string Name;
        public string Info;
        public string Desc;
        public string Logo;
    }

    public class GameExport
    {
        public int Id;
        public int DeveloperId;
        public string Url;
        public string FullUrl;
        public string Title;
        public string Genre;
        public string ReleaseDate;
        public string Platforms;
        public string Desc;
        public string Keywords;
        public string Publisher;
        public string Localizator;
        public string Logo;
        public string SmallLogo;
        public DateTimeOffset Date;
        public string TweetTag;
    }

    public class TopicExport
    {
        public int Id;
        public string Title;
        public string Url;
        public string Logo;
        public string Desc;
    }

    public class NewsExport
    {
        public int Id;

        public int? GameId;
        public int? DeveloperId;
        public int? TopicId;
        public string Url;
        public string FullUrl;
        public string Source;
        public string Title;
        public string ShortText;
        public string AddText;
        public int AuthorId;
        public int? ForumTopicId;
        public int? ForumPostId;
        public int Sticky;
        public DateTimeOffset Date;
        public DateTimeOffset LastChangeDate;
        public int Pub;
        public int Comments;
        public long? TwitterId;
        public string FacebookId;
    }

    public class ArticleCatExport
    {
        public int Id;
        public int? CatId;
        public int? GameId;
        public int? DeveloperId;
        public int? TopicId;
        public string Title;
        public string Url;
        public string FullUrl;
        public string Desc;
        public string Content;
    }

    public class ArticleExport
    {
        public int Id;

        public int? GameId;
        public int? DeveloperId;
        public int? TopicId;
        public string Url;
        public string FullUrl;
        public string Source;
        public int? CatId;
        public string Title;
        public string Announce;
        public string Text;
        public int AuthorId;
        public int Count;
        public DateTimeOffset Date;
        public int Pub;
    }

    public class FileCatExport
    {
        public int Id;
        public int? CatId;
        public int? GameId;
        public int? DeveloperId;
        public int? TopicId;
        public string Title;
        public string Desc;
        public string Url;
        public string FullUrl;
    }

    public class FileExport
    {
        public int Id;

        public int? GameId;
        public int? DeveloperId;
        public string Url;
        public string FullUrl;
        public int CatId;
        public string Title;
        public string Desc;
        public string Announce;
        public string Link;
        public int Size;
        public string YtId;
        public int AuthorId;
        public int Count;
        public DateTimeOffset Date;
    }

    public class GalleryCatExport
    {
        public int Id;
        public int? CatId;
        public int? GameId;
        public int? DeveloperId;
        public int? TopicId;
        public string Title;
        public string Desc;
        public string Url;
        public string FullUrl;
    }

    public class GalleryExport
    {
        public int Id;

        public int? GameId;
        public int? DeveloperId;
        public int CatId;
        public string Desc;
        public int Pub;
        public int AuthorId;
        public DateTimeOffset Date;
        public List<GalleryPicExport> Files;
        public string FullUrl;
    }

    public class GalleryPicExport
    {
        public string Url;
        public string FileName;
    }

    #endregion
}
