using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BioEngine.BRC.Domain;
using BioEngine.Core.DB;
using BioEngine.Core.Logging.Loki;
using BioEngine.Core.Search.ElasticSearch;
using BioEngine.Core.Seo;
using BioEngine.Core.Storage;
using BioEngine.Extra.IPB;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BioEngine.BRC.Importer
{
    static class Program
    {
        [SuppressMessage("ReSharper", "UseAsyncSuffix")]
        public static async Task Main(string[] args)
        {
            var bioEngine = new Core.BioEngine(args);
            var host = bioEngine
                .ConfigureServices(collection =>
                {
                    collection.AddScoped<Importer>();
                    collection.AddSingleton<IHostedService, ImporterService>();
                })
                .AddModule<PostgresDatabaseModule, PostgresDatabaseModuleConfig>((config, configuration, env) =>
                {
                    config.Host = configuration["BE_POSTGRES_HOST"];
                    config.Port = int.Parse(configuration["BE_POSTGRES_PORT"]);
                    config.Username = configuration["BE_POSTGRES_USERNAME"];
                    config.Password = configuration["BE_POSTGRES_PASSWORD"];
                    config.Database = configuration["BE_POSTGRES_DATABASE"];
                    config.EnablePooling = env.IsDevelopment();
                })
                .AddModule<BrcDomainModule>()
                .AddModule<S3StorageModule, S3StorageModuleConfig>((config, configuration, env) =>
                {
                    var uri = configuration["BE_STORAGE_PUBLIC_URI"];
                    var success = Uri.TryCreate(uri, UriKind.Absolute, out var publicUri);
                    if (!success)
                    {
                        throw new ArgumentException($"URI {uri} is not proper URI");
                    }

                    var serverUriStr = configuration["BE_STORAGE_S3_SERVER_URI"];
                    success = Uri.TryCreate(serverUriStr, UriKind.Absolute, out var serverUri);
                    if (!success)
                    {
                        throw new ArgumentException($"S3 server URI {uri} is not proper URI");
                    }

                    config.PublicUri = publicUri;
                    config.ServerUri = serverUri;
                    config.Bucket = configuration["BE_STORAGE_S3_BUCKET"];
                    config.AccessKey = configuration["BE_STORAGE_S3_ACCESS_KEY"];
                    config.SecretKey = configuration["BE_STORAGE_S3_SECRET_KEY"];
                })
                .AddModule<ElasticSearchModule, ElasticSearchModuleConfig>(
                    (config, configuration, env) =>
                    {
                        config.Url = configuration["BE_ELASTICSEARCH_URI"];
                        config.Login = configuration["BE_ELASTICSEARCH_LOGIN"];
                        config.Password = configuration["BE_ELASTICSEARCH_PASSWORD"];
                    })
                .AddModule<LokiLoggingModule, LokiLoggingConfig>()
                .AddModule<SeoModule>()
                .AddModule<IPBSiteModule>()
                .GetHostBuilder().UseEnvironment("Development")
                .Build();

            await host.RunAsync();
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
