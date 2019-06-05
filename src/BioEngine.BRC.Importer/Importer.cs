using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BioEngine.BRC.Domain.Entities;
using BioEngine.BRC.Domain.Entities.Blocks;
using BioEngine.Core.Abstractions;
using BioEngine.Core.DB;
using BioEngine.Core.Entities;
using BioEngine.Core.Entities.Blocks;
using BioEngine.Posts.Entities;
using BioEngine.Core.Properties;
using BioEngine.Core.Seo;
using BioEngine.Core.Storage;
using BioEngine.Extra.Facebook.Entities;
using BioEngine.Extra.IPB.Publishing;
using BioEngine.Extra.Twitter;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace BioEngine.BRC.Importer
{
    public class Importer
    {
        private readonly BioContext _dbContext;
        private readonly ILogger<Importer> _logger;
        private readonly IStorage _storage;
        private readonly PropertiesProvider _propertiesProvider;
        private readonly HttpClient _httpClient = new HttpClient();
        private Dictionary<int, Guid> _developersMap;
        private Dictionary<int, Guid> _gamesMap;
        private Dictionary<int, Guid> _topicsMap;
        private List<Tag> _tags;

        public Importer(BioContext dbContext, ILogger<Importer> logger, IStorage storage,
            PropertiesProvider propertiesProvider)
        {
            _dbContext = dbContext;
            _logger = logger;
            _storage = storage;
            _propertiesProvider = propertiesProvider;
        }

        public async Task ImportAsync(Guid siteId, Export data)
        {
            _logger.LogCritical("Begin import");
            await PrintStatsAsync();

            var transaction = await _dbContext.Database.BeginTransactionAsync();
            var site = await _dbContext.Sites.FirstOrDefaultAsync(s => s.Id == siteId);
            if (site == null)
            {
                throw new Exception($"Site with id {siteId.ToString()} not found");
            }

            _tags = await _dbContext.Tags.ToListAsync();

            _storage.BeginBatch();

            var emptyLogo = await UploadFromUrlAsync("https://dummyimage.com/200x200/000/fff", "tmp", "dummy.png");
            try
            {
                _logger.LogInformation($"Developers: {data.Developers.Count.ToString()}");

                _developersMap = new Dictionary<int, Guid>();
                await ImportDevelopersAsync(data, site, emptyLogo);

                _logger.LogInformation($"Games: {data.Games.Count.ToString()}");
                _gamesMap = new Dictionary<int, Guid>();
                await ImportGamesAsync(data, site, emptyLogo);

                _logger.LogInformation($"Topics: {data.Topics.Count.ToString()}");
                _topicsMap = new Dictionary<int, Guid>();
                await ImportTopicsAsync(data, site, emptyLogo);

                var posts = new List<Post>();
                _logger.LogWarning("News");
                var newsMap = new Dictionary<NewsExport, Post>();
                await ImportNewsAsync(data, site, posts, newsMap);

                //articles

                _logger.LogWarning("Articles");
                await ImportArticlesAsync(data, site, posts);

                // files
                _logger.LogWarning("Files");
                await ImportFilesAsync(data, site, posts);
                // pictures
                _logger.LogWarning("Gallery");
                await ImportGalleryAsync(data, site, posts);

                await _dbContext.AddRangeAsync(posts.OrderBy(p => p.DateAdded));
                foreach (var post in posts)
                {
                    var version = new ContentVersion {Id = Guid.NewGuid(), ContentId = post.Id,};
                    version.SetContent(post);
                    version.ChangeAuthorId = post.AuthorId;

                    await _dbContext.AddAsync(version);
                }

                _logger.LogWarning("Properties");
                _propertiesProvider.BeginBatch();
                _propertiesProvider.DisableChecks();
                foreach (var (news, post) in newsMap)
                {
                    if (news.TwitterId > 0)
                    {
                        _dbContext.Add(new TwitterPublishRecord
                        {
                            Type = post.GetType().FullName,
                            ContentId = post.Id,
                            TweetId = news.TwitterId.Value,
                            SiteIds = new[] {site.Id}
                        });
                    }

                    if (!string.IsNullOrEmpty(news.FacebookId))
                    {
                        _dbContext.Add(new FacebookPublishRecord
                        {
                            Type = post.GetType().FullName,
                            ContentId = post.Id,
                            PostId = news.FacebookId,
                            SiteIds = new[] {site.Id}
                        });
                    }

                    if (news.ForumTopicId > 0 && news.ForumPostId > 0)
                    {
                        _dbContext.Add(new IPBPublishRecord
                        {
                            Type = post.GetType().FullName,
                            ContentId = post.Id,
                            TopicId = news.ForumTopicId.Value,
                            PostId = news.ForumPostId.Value,
                            SiteIds = new[] {site.Id}
                        });
                    }
                }

                _propertiesProvider.EnableChecks();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                Rollback(transaction);
                return;
            }

            await _storage.FinishBatchAsync();
            await _dbContext.SaveChangesAsync();
            await PrintStatsAsync();

            transaction.Commit(); // TODO: Uncomment when done =)
            //await RollbackAsync(transaction);

            _logger.LogCritical("Done!");
        }

        private Tag GetTag(string title)
        {
            var tag = _tags.FirstOrDefault(t => t.Title == title);
            if (tag == null)
            {
                tag = new Tag
                {
                    Id = Guid.NewGuid(),
                    Title = title,
                    DateAdded = DateTimeOffset.UtcNow,
                    DateUpdated = DateTimeOffset.UtcNow,
                };
                _dbContext.Add(tag);
                _tags.Add(tag);
            }

            return tag;
        }

        private async Task ImportFilesAsync(Export data, Site site, List<Post> posts)
        {
            var fileCatsMap = new Dictionary<FileCatExport, List<Tag>>();
            foreach (var cat in data.FilesCats)

            {
                var textParts = new List<string>();
                var current = cat;
                while (current != null)
                {
                    textParts.Add(current.Title);
                    if (current.CatId > 0)
                    {
                        current = data.FilesCats.FirstOrDefault(c => c.Id == current.CatId);
                    }
                    else
                    {
                        current = null;
                    }
                }

                textParts.Reverse();
                var tags = new List<Tag>();
                foreach (var text in textParts)
                {
                    var tag = GetTag(text);
                    tags.Add(tag);
                }

                fileCatsMap.Add(cat, tags);
            }

            foreach (var fileExport in data.Files)
            {
                var url = $"{fileExport.Url}_{fileExport.Id.ToString()}";
                if (!await _dbContext.ContentItems.AnyAsync(p => p.Url == url))
                {
                    var post = new Post
                    {
                        Id = Guid.NewGuid(),
                        Url = url,
                        Title = fileExport.Title,
                        SiteIds = new[] {site.Id},
                        DateAdded = fileExport.Date,
                        DateUpdated = fileExport.Date,
                        DatePublished = fileExport.Date,
                        IsPublished = true,
                        AuthorId = fileExport.AuthorId,
                        Blocks = new List<ContentBlock>()
                    };
                    if (!string.IsNullOrEmpty(fileExport.Desc))
                    {
                        post.Blocks.Add(new TextBlock
                        {
                            Id = Guid.NewGuid(), Position = 0, Data = new TextBlockData {Text = fileExport.Desc}
                        });
                    }

                    if (!string.IsNullOrEmpty(fileExport.YtId))
                    {
                        post.Blocks.Add(new YoutubeBlock
                        {
                            Id = Guid.NewGuid(),
                            Position = 0,
                            Data = new YoutubeBlockData {YoutubeId = fileExport.YtId}
                        });
                    }
                    else
                    {
                        var file = UploadByPath(fileExport.Link, fileExport.Size, fileExport.Date);

                        post.Blocks.Add(
                            new FileBlock {Id = Guid.NewGuid(), Position = 0, Data = new FileBlockData {File = file}});
                    }

                    var cat = data.FilesCats.First(c => c.Id == fileExport.CatId);
                    var tags = fileCatsMap[cat];
                    post.TagIds = tags.Select(t => t.Id).ToArray();

                    if (cat.GameId > 0)
                    {
                        var game = data.Games.FirstOrDefault(g => g.Id == cat.GameId);
                        if (game != null)
                        {
                            post.SectionIds = new[] {_gamesMap[game.Id]};
                        }
                    }

                    if (cat.DeveloperId > 0)
                    {
                        var developer = data.Developers.FirstOrDefault(g => g.Id == cat.DeveloperId);
                        if (developer != null)
                        {
                            post.SectionIds = new[] {_developersMap[developer.Id]};
                        }
                    }

                    if (cat.TopicId > 0)
                    {
                        var topic = data.Topics.FirstOrDefault(g => g.Id == cat.TopicId);
                        if (topic != null)
                        {
                            post.SectionIds = new[] {_developersMap[topic.Id]};
                        }
                    }

                    posts.Add(post);
                }
            }
        }

        private async Task ImportGalleryAsync(Export data, Site site, List<Post> posts)
        {
            foreach (var cat in data.GalleryCats)
            {
                var textParts = new List<string>();
                var current = cat;
                while (current != null)
                {
                    textParts.Add(current.Title);
                    if (current.CatId > 0)
                    {
                        current = data.GalleryCats.FirstOrDefault(c => c.Id == current.CatId);
                    }
                    else
                    {
                        current = null;
                    }
                }

                textParts.Reverse();
                var tags = new List<Tag>();
                foreach (var text in textParts)
                {
                    var tag = GetTag(text);
                    tags.Add(tag);
                }

                var pics = data.GalleryPics.Where(p => p.CatId == cat.Id);
                foreach (var picsGroup in pics.GroupBy(p => p.Date.Date))
                {
                    var url = $"gallery_{picsGroup.First().Id.ToString()}";
                    if (!await _dbContext.ContentItems.AnyAsync(p => p.Url == url))
                    {
                        var post = new Post
                        {
                            Id = Guid.NewGuid(),
                            Url = url,
                            Title = cat.Title,
                            SiteIds = new[] {site.Id},
                            DateAdded = picsGroup.First().Date,
                            DateUpdated = picsGroup.First().Date,
                            DatePublished = picsGroup.First().Date,
                            IsPublished = true,
                            AuthorId = picsGroup.First().AuthorId,
                            Blocks = new List<ContentBlock>()
                        };

                        foreach (var galleryPicExport in picsGroup)
                        {
                            var pictures = new List<StorageItem>();
                            foreach (var picFile in galleryPicExport.Files)
                            {
                                var pic = await UploadFromUrlAsync(picFile.Url,
                                    $"posts/{post.DateAdded.Year.ToString()}/{post.DateAdded.Month.ToString()}",
                                    picFile.FileName);
                                if (pic != null)
                                {
                                    pictures.Add(pic);
                                }
                            }

                            if (pictures.Count == 1)
                            {
                                var block = new PictureBlock
                                {
                                    Id = Guid.NewGuid(), Position = 1, Data = new PictureBlockData()
                                };
                                var picFile = pictures[0];
                                var pic = await UploadFromUrlAsync(picFile.Url,
                                    $"posts/{post.DateAdded.Year.ToString()}/{post.DateAdded.Month.ToString()}",
                                    picFile.FileName);
                                if (pic != null)
                                {
                                    block.Data.Picture = pic;
                                    post.Blocks.Add(block);
                                }
                            }
                            else
                            {
                                var block = new GalleryBlock
                                {
                                    Id = Guid.NewGuid(), Position = 1, Data = new GalleryBlockData()
                                };

                                block.Data.Pictures = pictures.ToArray();
                                post.Blocks.Add(block);
                            }

                            if (!string.IsNullOrEmpty(galleryPicExport.Desc))
                            {
                                post.Blocks.Add(new TextBlock
                                {
                                    Id = Guid.NewGuid(),
                                    Position = 0,
                                    Data = new TextBlockData {Text = galleryPicExport.Desc}
                                });
                            }
                        }

                        post.TagIds = tags.Select(t => t.Id).ToArray();

                        if (cat.GameId > 0)
                        {
                            var game = data.Games.FirstOrDefault(g => g.Id == cat.GameId);
                            if (game != null)
                            {
                                post.SectionIds = new[] {_gamesMap[game.Id]};
                            }
                        }

                        if (cat.DeveloperId > 0)
                        {
                            var developer = data.Developers.FirstOrDefault(g => g.Id == cat.DeveloperId);
                            if (developer != null)
                            {
                                post.SectionIds = new[] {_developersMap[developer.Id]};
                            }
                        }

                        if (cat.TopicId > 0)
                        {
                            var topic = data.Topics.FirstOrDefault(g => g.Id == cat.TopicId);
                            if (topic != null)
                            {
                                post.SectionIds = new[] {_developersMap[topic.Id]};
                            }
                        }

                        posts.Add(post);
                    }
                }
            }
        }

        private async Task ImportArticlesAsync(Export data, Site site, List<Post> posts)
        {
            var articleCatsMap = new Dictionary<ArticleCatExport, List<Tag>>();
            foreach (var cat in data.ArticlesCats)
            {
                var textParts = new List<string>();
                var current = cat;
                while (current != null)
                {
                    textParts.Add(current.Title);
                    if (current.CatId > 0)
                    {
                        current = data.ArticlesCats.FirstOrDefault(c => c.Id == current.CatId.Value);
                    }
                    else
                    {
                        current = null;
                    }
                }

                textParts.Reverse();
                var tags = new List<Tag>();
                foreach (var text in textParts)
                {
                    var tag = GetTag(text);
                    tags.Add(tag);
                }

                articleCatsMap.Add(cat, tags);
            }

            foreach (var articleExport in data.Articles)
            {
                var url = $"{articleExport.Url}_{articleExport.Id.ToString()}";
                if (!await _dbContext.ContentItems.AnyAsync(p => p.Url == url))
                {
                    var post = new Post
                    {
                        Id = Guid.NewGuid(),
                        Url = url,
                        Title = articleExport.Title,
                        SiteIds = new[] {site.Id},
                        DateAdded = articleExport.Date,
                        DateUpdated = articleExport.Date,
                        DatePublished = articleExport.Date,
                        IsPublished = articleExport.Pub == 1,
                        AuthorId = articleExport.AuthorId,
                        Blocks = new List<ContentBlock>()
                    };

                    await AddTextAsync(post, articleExport.Text, data);

                    var cat = data.ArticlesCats.First(c => c.Id == articleExport.CatId);
                    var tags = articleCatsMap[cat];
                    post.TagIds = tags.Select(t => t.Id).ToArray();

                    if (cat.GameId > 0)
                    {
                        var game = data.Games.FirstOrDefault(g => g.Id == cat.GameId);
                        if (game != null)
                        {
                            post.SectionIds = new[] {_gamesMap[game.Id]};
                        }
                    }

                    if (cat.DeveloperId > 0)
                    {
                        var developer = data.Developers.FirstOrDefault(g => g.Id == cat.DeveloperId);
                        if (developer != null)
                        {
                            post.SectionIds = new[] {_developersMap[developer.Id]};
                        }
                    }

                    if (cat.TopicId > 0)
                    {
                        var topic = data.Topics.FirstOrDefault(g => g.Id == cat.TopicId);
                        if (topic != null)
                        {
                            post.SectionIds = new[] {_developersMap[topic.Id]};
                        }
                    }

                    posts.Add(post);
                }
            }
        }

        private async Task ImportNewsAsync(Export data, Site site, List<Post> posts,
            Dictionary<NewsExport, Post> newsMap)
        {
            foreach (var newsExport in data.News.OrderByDescending(n => n.Id))
            {
                var url = $"{newsExport.Url}_{newsExport.Id.ToString()}";
                if (!await _dbContext.ContentItems.AnyAsync(p => p.Url == url))
                {
                    var post = new Post
                    {
                        Id = Guid.NewGuid(),
                        Url = url,
                        Title = newsExport.Title,
                        SiteIds = new[] {site.Id},
                        DateAdded = newsExport.Date,
                        DateUpdated = newsExport.LastChangeDate,
                        DatePublished = newsExport.LastChangeDate,
                        IsPublished = newsExport.Pub == 1,
                        AuthorId = newsExport.AuthorId,
                        Blocks = new List<ContentBlock>()
                    };

                    await AddTextAsync(post, newsExport.ShortText, data);

                    if (newsExport.DeveloperId > 0)
                    {
                        post.SectionIds = new[] {_developersMap[newsExport.DeveloperId.Value]};
                    }

                    if (newsExport.GameId > 0)
                    {
                        post.SectionIds = new[] {_gamesMap[newsExport.GameId.Value]};
                    }

                    if (newsExport.TopicId > 0)
                    {
                        post.SectionIds = new[] {_topicsMap[newsExport.TopicId.Value]};
                    }

                    if (!string.IsNullOrEmpty(newsExport.AddText))
                    {
                        post.Blocks.Add(new CutBlock
                        {
                            Position = 1,
                            Id = Guid.NewGuid(),
                            Data = new CutBlockData {ButtonText = "Читать дальше"}
                        });
                        await AddTextAsync(post, newsExport.AddText, data);
                    }

                    posts.Add(post);
                    newsMap.Add(newsExport, post);
                }
            }
        }

        private readonly Regex _iframeRegex =
            new Regex("<iframe.*src=\"(.*?)\".*>.*</iframe>", RegexOptions.Multiline & RegexOptions.IgnoreCase);

        private readonly Regex _iframeWithPRegex =
            new Regex("<p[^>]+><iframe.*src=\"(.*?)\".*>.*</iframe></p>",
                RegexOptions.Multiline & RegexOptions.IgnoreCase);

        private readonly Regex _imageRegex = new Regex("<p.*><img.*src=\"(.*?)\".*></p>",
            RegexOptions.Multiline & RegexOptions.IgnoreCase);

        private async Task AddTextAsync(IContentEntity entity, string text, Export data)
        {
            // iframe
            var extractedBlocks = new List<ContentBlock>();
            var currentText = ExtractFrameBlocks(text, extractedBlocks, _iframeWithPRegex);
            currentText = ExtractFrameBlocks(currentText, extractedBlocks, _iframeRegex);
            currentText = ExtractBlockQuotes(currentText, extractedBlocks);
            currentText = await ExtractImageBlocksAsync(entity, currentText, extractedBlocks, data);

            if (extractedBlocks.Count > 0)
            {
                var matches = new Regex("\\[block-([0-9]+)\\]", RegexOptions.Multiline & RegexOptions.IgnoreCase)
                    .Matches(currentText);
                foreach (Match match in matches)
                {
                    var replace = match.Value;
                    var blockId = int.Parse(match.Groups[1].Value);
                    var textParts = currentText.Split(replace);
                    if (!string.IsNullOrWhiteSpace(textParts[0].Trim()))
                    {
                        entity.Blocks.Add(new TextBlock
                        {
                            Id = Guid.NewGuid(),
                            Position = entity.Blocks.Count,
                            Data = new TextBlockData {Text = textParts[0].Trim()}
                        });
                    }

                    var block = extractedBlocks[blockId - 1];
                    block.Position = entity.Blocks.Count;
                    entity.Blocks.Add(block);
                    currentText = textParts[1].Trim();
                    if (entity.Blocks.Count == 3 && extractedBlocks.Count > 3)
                    {
                        entity.Blocks.Add(new CutBlock
                        {
                            Id = Guid.NewGuid(), Position = entity.Blocks.Count, Data = new CutBlockData()
                        });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(currentText))
            {
                entity.Blocks.Add(new TextBlock
                {
                    Id = Guid.NewGuid(),
                    Position = entity.Blocks.Count,
                    Data = new TextBlockData {Text = currentText}
                });
            }
        }

        private string ExtractBlockQuotes(string currentText, List<ContentBlock> extractedBlocks)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(currentText);
            var quotes = doc.DocumentNode.Descendants("blockquote").ToArray();
            if (!quotes.Any()) return currentText;
            foreach (var quote in quotes)
            {
                extractedBlocks.Add(new QuoteBlock
                {
                    Id = Guid.NewGuid(), Data = new QuoteBlockData {Text = quote.InnerHtml}
                });

                var newNodeStr = $"[block-{extractedBlocks.Count.ToString()}]";
                var newNode = HtmlNode.CreateNode(newNodeStr);
                quote.ParentNode.ReplaceChild(newNode, quote);
            }

            return doc.DocumentNode.InnerHtml;
        }

        private string ExtractFrameBlocks(string text, List<ContentBlock> extractedBlocks, Regex regex)
        {
            var matches = regex.Matches(text);
            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    var iframeHtml = match.Value;

                    var srcUrl = match.Groups[1].Value;
                    if (srcUrl.Contains("www.youtube.com/embed"))
                    {
                        var ytId = srcUrl.Substring(srcUrl.LastIndexOf('/') + 1);
                        extractedBlocks.Add(new YoutubeBlock
                        {
                            Id = Guid.NewGuid(), Data = new YoutubeBlockData {YoutubeId = ytId}
                        });
                    }
                    else if (srcUrl.Contains("player.twitch.tv"))
                    {
                        var block = new TwitchBlock {Id = Guid.NewGuid(), Data = new TwitchBlockData()};
                        var uri = new Uri(srcUrl);
                        var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
                        if (queryParams.ContainsKey("video"))
                        {
                            block.Data.VideoId = queryParams["video"].First();
                        }

                        if (queryParams.ContainsKey("channel"))
                        {
                            block.Data.ChannelId = queryParams["channel"].First();
                        }

                        if (queryParams.ContainsKey("collection"))
                        {
                            block.Data.CollectionId = queryParams["collection"].First();
                        }

                        extractedBlocks.Add(block);
                    }
                    else
                    {
                        extractedBlocks.Add(new IframeBlock
                        {
                            Id = Guid.NewGuid(), Data = new IframeBlockData {Src = srcUrl}
                        });
                    }

                    text = text.Replace(iframeHtml, $"[block-{extractedBlocks.Count.ToString()}]");
                }
            }

            return text;
        }

        private async Task<string> ExtractImageBlocksAsync(IContentEntity post, string text,
            ICollection<ContentBlock> extractedBlocks,
            Export data)
        {
            var matches = _imageRegex.Matches(text);
            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    var imgHtml = match.Value;

                    var imgUrl = match.Groups[1].Value;

                    var thumbMatch = _thumbUrlRegex.Match(imgUrl);
                    if (thumbMatch.Success)
                    {
                        int.TryParse(thumbMatch.Groups[1].Value, out var picId);
                        var indexId = 0;
                        if (thumbMatch.Groups.Count > 1)
                        {
                            int.TryParse(thumbMatch.Groups[2].Value, out indexId);
                        }

                        if (picId > 0)
                        {
                            var pic = data.GalleryPics.FirstOrDefault(p => p.Id == picId);
                            if (pic != null && pic.Files.Count > indexId)
                            {
                                var file = pic.Files[indexId];
                                imgUrl = file.Url;
                            }
                        }
                    }

                    var item = await UploadFromUrlAsync(imgUrl,
                        $"posts/{post.DateAdded.Year.ToString()}/{post.DateAdded.Month.ToString()}");
                    if (item != null)
                    {
                        extractedBlocks.Add(new PictureBlock
                        {
                            Id = Guid.NewGuid(), Data = new PictureBlockData {Picture = item}
                        });

                        text = text.Replace(imgHtml, $"[block-{extractedBlocks.Count.ToString()}]");
                    }
                }
            }

            return text;
        }

        private async Task ImportTopicsAsync(Export data, Site site, StorageItem emptyLogo)
        {
            foreach (var topicExport in data.Topics)
            {
                var topic = await _dbContext.Set<Topic>().FirstOrDefaultAsync(d => d.Title == topicExport.Title);
                if (topic == null)
                {
                    topic = new Topic
                    {
                        Id = Guid.NewGuid(),
                        Url = topicExport.Url,
                        Title = topicExport.Title,
                        SiteIds = new[] {site.Id},
                        IsPublished = true,
                        DateAdded = DateTimeOffset.UtcNow,
                        DateUpdated = DateTimeOffset.UtcNow,
                        DatePublished = DateTimeOffset.UtcNow,
                        Data = new TopicData {Hashtag = string.Empty,},
                        Properties = new List<PropertiesEntry>(),
                        Blocks = new List<ContentBlock>()
                    };

                    await AddTextAsync(topic, topicExport.Desc, data);
                    var logo = await UploadFromUrlAsync(topicExport.Logo, Path.Combine("sections", "topics")) ??
                               emptyLogo;
                    topic.Data.Logo = logo;
                    topic.Data.LogoSmall = logo;

                    await _dbContext.AddAsync(topic);
                }

                _topicsMap.Add(topicExport.Id, topic.Id);
            }
        }

        private async Task ImportGamesAsync(Export data, Site site,
            StorageItem emptyLogo)
        {
            foreach (var gameExport in data.Games)
            {
                var game = await _dbContext.Set<Game>().FirstOrDefaultAsync(d => d.Title == gameExport.Title);
                if (game == null)
                {
                    game = new Game
                    {
                        Id = Guid.NewGuid(),
                        Url = gameExport.Url,
                        Title = gameExport.Title,
                        SiteIds = new[] {site.Id},
                        IsPublished = true,
                        DateAdded = gameExport.Date,
                        DateUpdated = gameExport.Date,
                        DatePublished = gameExport.Date,
                        Data = new GameData {Platforms = new Platform[0], Hashtag = gameExport.TweetTag},
                        Properties = new List<PropertiesEntry>(),
                        Blocks = new List<ContentBlock>()
                    };
                    await AddTextAsync(game, gameExport.Desc, data);
                    if (gameExport.DeveloperId > 0 && _developersMap.ContainsKey(gameExport.DeveloperId))
                    {
                        game.ParentId = _developersMap[gameExport.DeveloperId];
                    }

                    game.Data.Logo = await UploadFromUrlAsync(gameExport.Logo, Path.Combine("sections", "games")) ??
                                     emptyLogo;
                    game.Data.LogoSmall =
                        await UploadFromUrlAsync(gameExport.SmallLogo, Path.Combine("sections", "games")) ??
                        emptyLogo;

                    await _dbContext.AddAsync(game);

                    if (!string.IsNullOrEmpty(gameExport.Keywords))
                    {
                        await _propertiesProvider.SetAsync(
                            new SeoPropertiesSet {Keywords = gameExport.Keywords, Description = gameExport.Desc}, game);
                    }
                }

                _gamesMap.Add(gameExport.Id, game.Id);
            }
        }

        private async Task ImportDevelopersAsync(Export data, Site site, StorageItem emptyLogo)
        {
            foreach (var dev in data.Developers)
            {
                var developer = await _dbContext.Set<Developer>().FirstOrDefaultAsync(d => d.Title == dev.Name);
                if (developer == null)
                {
                    developer = new Developer
                    {
                        Id = Guid.NewGuid(),
                        Url = dev.Url,
                        Title = dev.Name,
                        SiteIds = new[] {site.Id},
                        DateAdded = DateTimeOffset.UtcNow,
                        DatePublished = DateTimeOffset.UtcNow,
                        IsPublished = true,
                        DateUpdated = DateTimeOffset.UtcNow,
                        Data = new DeveloperData {Persons = new Person[0], Hashtag = string.Empty},
                        Blocks = new List<ContentBlock>()
                    };
                    await AddTextAsync(developer, dev.Desc, data);
                    var logo = await UploadFromUrlAsync(dev.Logo, Path.Combine("sections", "developers")) ?? emptyLogo;

                    developer.Data.Logo = logo;
                    developer.Data.LogoSmall = logo;

                    await _dbContext.AddAsync(developer);
                }

                _developersMap.Add(dev.Id, developer.Id);
            }
        }

        private async Task PrintStatsAsync()
        {
            _logger.LogCritical($"Developers: {(await _dbContext.Set<Developer>().CountAsync()).ToString()}");
            _logger.LogCritical($"Games: {(await _dbContext.Set<Game>().CountAsync()).ToString()}");
            _logger.LogCritical(message: $"Topics: {(await _dbContext.Set<Topic>().CountAsync()).ToString()}");
            _logger.LogCritical($"Storage items: {(await _dbContext.Set<StorageItem>().CountAsync()).ToString()}");
            _logger.LogCritical($"Tags: {(await _dbContext.Set<Tag>().CountAsync()).ToString()}");
            _logger.LogCritical($"Properties: {(await _dbContext.Set<PropertiesRecord>().CountAsync()).ToString()}");
            _logger.LogCritical($"Posts: {(await _dbContext.Set<Post>().CountAsync()).ToString()}");
            _logger.LogCritical($"Blocks: {(await _dbContext.Set<ContentBlock>().CountAsync()).ToString()}");
        }

        private readonly Regex _thumbUrlRegex = new Regex("gallery\\/thumb\\/([0-9]+)\\/[0-9]+\\/[0-9]+\\/?([0-9]+)?");

        private async Task<StorageItem> UploadFromUrlAsync(string url, string path, string fileName = null)
        {
            fileName ??= Path.GetFileName(url);
            if (!string.IsNullOrEmpty(fileName))
            {
                try
                {
                    _logger.LogInformation($"Downloading file from url {url}");
                    var fileData = await _httpClient.GetByteArrayAsync(url);
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

        private StorageItem UploadByPath(string path, int size, DateTimeOffset date)
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

        private void Rollback(IDbContextTransaction transaction)
        {
            transaction.Rollback();
        }
    }
}
