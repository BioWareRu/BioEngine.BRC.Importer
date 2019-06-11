using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BioEngine.BRC.Common;
using BioEngine.Core.Logging.Loki;
using BioEngine.Core.Seo;
using BioEngine.Extra.Facebook;
using BioEngine.Extra.IPB;
using BioEngine.Extra.Twitter;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
                    collection.AddSingleton<IHostedService, ImporterService>();
                    collection.Configure<ImporterOptions>(options =>
                    {
                        options.ApiUri = hostBuilder.Configuration["BRC_EXPORT_API_URL"];
                        options.ApiToken = hostBuilder.Configuration["BRC_EXPORT_API_TOKEN"];
                        options.SiteId = Guid.Parse(hostBuilder.Configuration["BRC_IMPORT_SITE_ID"]);
                    });
                })
                .AddPostgresDb()
                .AddBrcDomain()
                .AddElasticSearch()
                .AddS3Storage()
                .AddModule<LokiLoggingModule, LokiLoggingConfig>((configuration, environment) =>
                    new LokiLoggingConfig(configuration["BRC_LOKI_URL"]))
                .AddModule<IPBSiteModule, IPBModuleConfig>((configuration, env) =>
                {
                    if (!Uri.TryCreate(configuration["BE_IPB_URL"], UriKind.Absolute, out var ipbUrl))
                    {
                        throw new ArgumentException($"Can't parse IPB url; {configuration["BE_IPB_URL"]}");
                    }

                    return new IPBModuleConfig(ipbUrl)
                    {
                        ApiClientId = configuration["BE_IPB_OAUTH_CLIENT_ID"],
                        ApiClientSecret = configuration["BE_IPB_OAUTH_CLIENT_SECRET"],
                        CallbackPath = "/login/ipb",
                        AuthorizationEndpoint = configuration["BE_IPB_AUTHORIZATION_ENDPOINT"],
                        TokenEndpoint = configuration["BE_IPB_TOKEN_ENDPOINT"],
                        ApiReadonlyKey = configuration["BE_IPB_API_READONLY_KEY"]
                    };
                })
                .AddModule<SeoModule>()
                .AddModule<TwitterModule>()
                .AddModule<FacebookModule>()
                .ConfigureAppConfiguration(builder =>
                {
                    builder.AddUserSecrets<Importer>();
                    builder.AddEnvironmentVariables();
                }).RunAsync();
        }
    }

    #region dto    

    [PublicAPI]
    public class Export
    {
        public List<DeveloperExport> Developers { get; set; } = new List<DeveloperExport>();
        public List<GameExport> Games { get; set; } = new List<GameExport>();
        public List<TopicExport> Topics { get; set; } = new List<TopicExport>();
        public List<NewsExport> News { get; set; } = new List<NewsExport>();
        public List<ArticleCatExport> ArticlesCats { get; set; } = new List<ArticleCatExport>();
        public List<ArticleExport> Articles { get; set; } = new List<ArticleExport>();
        public List<FileCatExport> FilesCats { get; set; } = new List<FileCatExport>();
        public List<FileExport> Files { get; set; } = new List<FileExport>();
        public List<GalleryCatExport> GalleryCats { get; set; } = new List<GalleryCatExport>();
        public List<GalleryExport> GalleryPics { get; set; } = new List<GalleryExport>();
    }


    public class DeveloperExport
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string Name { get; set; }
        public string Info { get; set; }
        public string Desc { get; set; }
        public string Logo { get; set; }
    }

    public class GameExport
    {
        public int Id { get; set; }
        public int DeveloperId { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public string Genre { get; set; }
        public string ReleaseDate { get; set; }
        public string Platforms { get; set; }
        public string Desc { get; set; }
        public string Keywords { get; set; }
        public string Publisher { get; set; }
        public string Localizator { get; set; }
        public string Logo { get; set; }
        public string SmallLogo { get; set; }
        public DateTimeOffset Date { get; set; }
        public string TweetTag { get; set; }
    }

    public class TopicExport
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string Logo { get; set; }
        public string Desc { get; set; }
    }

    public class NewsExport
    {
        public int Id { get; set; }

        public int? GameId { get; set; }
        public int? DeveloperId { get; set; }
        public int? TopicId { get; set; }
        public string Url { get; set; }
        public string Source { get; set; }
        public string Title { get; set; }
        public string ShortText { get; set; }
        public string AddText { get; set; }
        public int AuthorId { get; set; }
        public int? ForumTopicId { get; set; }
        public int? ForumPostId { get; set; }
        public int Sticky { get; set; }
        public DateTimeOffset Date { get; set; }
        public DateTimeOffset LastChangeDate { get; set; }
        public int Pub { get; set; }
        public int Comments { get; set; }
        public long? TwitterId { get; set; }
        public string FacebookId { get; set; }
    }

    public class ArticleCatExport
    {
        public int Id { get; set; }
        public int? CatId { get; set; }
        public int? GameId { get; set; }
        public int? DeveloperId { get; set; }
        public int? TopicId { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string Desc { get; set; }
        public string Content { get; set; }
    }

    public class ArticleExport
    {
        public int Id { get; set; }

        public int? GameId { get; set; }
        public int? DeveloperId { get; set; }
        public int? TopicId { get; set; }
        public string Url { get; set; }
        public string Source { get; set; }
        public int? CatId { get; set; }
        public string Title { get; set; }
        public string Announce { get; set; }
        public string Text { get; set; }
        public int AuthorId { get; set; }
        public int Count { get; set; }
        public DateTimeOffset Date { get; set; }
        public int Pub { get; set; }
    }

    public class FileCatExport
    {
        public int Id { get; set; }
        public int? CatId { get; set; }
        public int? GameId { get; set; }
        public int? DeveloperId { get; set; }
        public int? TopicId { get; set; }
        public string Title { get; set; }
        public string Desc { get; set; }
        public string Url { get; set; }
    }

    public class FileExport
    {
        public int Id { get; set; }

        public int? GameId { get; set; }
        public int? DeveloperId { get; set; }
        public string Url { get; set; }
        public int CatId { get; set; }
        public string Title { get; set; }
        public string Desc { get; set; }
        public string Announce { get; set; }
        public string Link { get; set; }
        public int Size { get; set; }
        public string YtId { get; set; }
        public int AuthorId { get; set; }
        public int Count { get; set; }
        public DateTimeOffset Date { get; set; }
    }

    public class GalleryCatExport
    {
        public int Id { get; set; }
        public int? CatId { get; set; }
        public int? GameId { get; set; }
        public int? DeveloperId { get; set; }
        public int? TopicId { get; set; }
        public string Title { get; set; }
        public string Desc { get; set; }
        public string Url { get; set; }
    }

    public class GalleryExport
    {
        public int Id { get; set; }

        public int? GameId { get; set; }
        public int? DeveloperId { get; set; }
        public int CatId { get; set; }
        public string Desc { get; set; }
        public int Pub { get; set; }
        public int AuthorId { get; set; }
        public DateTimeOffset Date { get; set; }
        public List<GalleryPicExport> Files { get; set; }
    }

    public class GalleryPicExport
    {
        public string Url { get; set; }
        public string FileName { get; set; }
    }

    #endregion
}
