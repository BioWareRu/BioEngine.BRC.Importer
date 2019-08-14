using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BioEngine.BRC.Domain.Entities.Blocks;
using BioEngine.Core.Entities;
using BioEngine.Core.Entities.Blocks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace BioEngine.BRC.Importer
{
    public class HtmlParser
    {
        private readonly FilesUploader _filesUploader;
        private readonly ILogger<HtmlParser> _logger;

        private readonly Regex _thumbUrlRegex = new Regex("gallery\\/thumb\\/([0-9]+)\\/[0-9]+\\/[0-9]+\\/?([0-9]+)?");

        public HtmlParser(FilesUploader filesUploader, ILogger<HtmlParser> logger)
        {
            _filesUploader = filesUploader;
            _logger = logger;
        }

        private ContentBlock ParseIframe(HtmlNode node)
        {
            var srcUrl = node.Attributes["src"].Value;
            if (srcUrl.Contains("youtube.com"))
            {
                if (srcUrl.StartsWith("//"))
                {
                    srcUrl = "https:" + srcUrl;
                }

                var uri = new Uri(srcUrl);

                var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);

                var videoId = queryParams.ContainsKey("v") ? queryParams["v"][0] : uri.Segments.Last();
                return new YoutubeBlock {Id = Guid.NewGuid(), Data = new YoutubeBlockData {YoutubeId = videoId}};
            }

            if (srcUrl.Contains("player.twitch.tv"))
            {
                var block = new TwitchBlock {Id = Guid.NewGuid(), Data = new TwitchBlockData()};
                var uri = new Uri(srcUrl);
                var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
                if (queryParams.ContainsKey("video"))
                {
                    block.Data.VideoId = queryParams["video"][0];
                }

                if (queryParams.ContainsKey("channel"))
                {
                    block.Data.ChannelId = queryParams["channel"][0];
                }

                if (queryParams.ContainsKey("collection"))
                {
                    block.Data.CollectionId = queryParams["collection"][0];
                }

                return block;
            }

            return new IframeBlock {Id = Guid.NewGuid(), Data = new IframeBlockData {Src = srcUrl}};
        }

        private async Task<ContentBlock> ParseImgAsync(HtmlNode node, List<GalleryExport> pics, string uploadPath)
        {
            var imgUrl = node.Attributes["src"].Value;
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
                    var pic = pics.FirstOrDefault(p => p.Id == picId);
                    if (pic != null && pic.Files.Count > indexId)
                    {
                        var file = pic.Files[indexId];
                        imgUrl = file.Url;
                    }
                }
            }

            var item = await _filesUploader.UploadFromUrlAsync(imgUrl, uploadPath);
            if (item != null)
            {
                return new PictureBlock {Id = Guid.NewGuid(), Data = new PictureBlockData {Picture = item}};
            }

            return null;
        }

        private async Task<List<ContentBlock>> ParseTextAsync(HtmlNode node, string uploadPath,
            List<GalleryExport> pics)
        {
            var blocks = new List<ContentBlock>();
            var extractedBlocks = new List<ContentBlock>();
            foreach (var childNode in node.ChildNodes.ToArray())
            {
                switch (childNode.Name)
                {
                    case "img":
                        var imgBlock = await ParseImgAsync(childNode, pics, uploadPath);
                        if (imgBlock != null)
                        {
                            extractedBlocks.Add(imgBlock);
                            var newNodeStr = $"<block id=\"{extractedBlocks.Count.ToString()}\" />";
                            var newNode = HtmlNode.CreateNode(newNodeStr);
                            childNode.ParentNode.ReplaceChild(newNode, childNode);
                        }

                        break;
                    case "iframe":
                        var frameBlock = ParseIframe(childNode);
                        if (frameBlock != null)
                        {
                            extractedBlocks.Add(frameBlock);
                            var newNodeStr = $"<block id=\"{extractedBlocks.Count.ToString()}\" />";
                            var newNode = HtmlNode.CreateNode(newNodeStr);
                            childNode.ParentNode.ReplaceChild(newNode, childNode);
                        }

                        break;
                }
            }

            var currentHtml = "";
            foreach (var childNode in node.ChildNodes)
            {
                switch (childNode.Name)
                {
                    case "block":
                        if (!string.IsNullOrEmpty(currentHtml))
                        {
                            blocks.Add(new TextBlock
                            {
                                Id = Guid.NewGuid(),
                                Position = blocks.Count,
                                Data = new TextBlockData {Text = currentHtml}
                            });
                            currentHtml = "";
                        }

                        var id = int.Parse(childNode.Attributes["id"].Value);
                        blocks.Add(extractedBlocks[id - 1]);
                        break;
                    default:
                        if (!string.IsNullOrEmpty(childNode.InnerText.Replace("&nbsp;", "").Trim()))
                        {
                            currentHtml += childNode.OuterHtml;
                        }

                        break;
                }
            }

            if (!string.IsNullOrEmpty(currentHtml))
            {
                blocks.Add(new TextBlock
                {
                    Id = Guid.NewGuid(), Position = blocks.Count, Data = new TextBlockData {Text = currentHtml}
                });
            }

            return blocks;
        }

        public async Task<List<ContentBlock>> ParseAsync(string html, string uploadPath, List<GalleryExport> pics)
        {
            html = html.Replace("&amp;ndash;", "&ndash;")
                .Replace("&amp;nbsp;", "&nbsp;")
                .Replace("&amp;mdash;", "&mdash;")
                .Replace("&amp;quote;", "&quote;")
                .Replace("&amp;laquo;", "&laquo;")
                .Replace("&amp;raquo;", "&raquo;");

            var blocks = new List<ContentBlock>();
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var nodes = new List<HtmlNode>();
            string currentTextNode = null;
            foreach (var childNode in document.DocumentNode.ChildNodes)
            {
                switch (childNode.Name)
                {
                    case "a":
                    case "span":
                    case "strong":
                    case "h1":
                    case "h2":
                    case "h3":
                    case "h4":
                    case "h5":
                    case "h6":
                    case "em":
                    case "b":
                    case "i":
                    case "center":
                    case "small":
                    case "del":
                    case "dt":
                    case "s":
                    case "hr":
                    case "sup":
                    case "sub":
                    case "noindex":
                    case "style":
                    case "font":
                    case "nobr":
                    case "script":
                    case "#text":
                        if (currentTextNode == null)
                        {
                            currentTextNode = childNode.OuterHtml;
                        }
                        else
                        {
                            currentTextNode += childNode.OuterHtml;
                        }

                        break;
                    case "br":
                        if (currentTextNode != null)
                        {
                            currentTextNode += childNode.OuterHtml.Trim();
                        }

                        break;
                    default:
                        if (!string.IsNullOrEmpty(currentTextNode))
                        {
                            nodes.Add(HtmlNode.CreateNode($"<div>{currentTextNode.Trim()}</div>"));
                            currentTextNode = null;
                        }

                        nodes.Add(childNode);
                        break;
                }
            }

            if (!string.IsNullOrEmpty(currentTextNode))
            {
                nodes.Add(HtmlNode.CreateNode($"<div>{currentTextNode.Trim()}</div>"));
            }

            foreach (var childNode in nodes)
            {
                switch (childNode.Name)
                {
                    case "p":
                    case "div":
                    case "#text":
                        blocks.AddRange(await ParseTextAsync(childNode, uploadPath, pics));
                        break;
                    case "blockquote":
                        blocks.Add(new QuoteBlock
                        {
                            Id = Guid.NewGuid(), Data = new QuoteBlockData {Text = childNode.InnerHtml}
                        });
                        break;
                    case "iframe":
                        blocks.Add(ParseIframe(childNode));
                        break;
                    case "ul":
                    case "ol":
                    case "table":
                        blocks.Add(new TextBlock
                        {
                            Id = Guid.NewGuid(),
                            Position = blocks.Count,
                            Data = new TextBlockData {Text = childNode.OuterHtml}
                        });
                        break;
                    case "img":
                        var imgBlock = await ParseImgAsync(childNode, pics, uploadPath);
                        if (imgBlock != null)
                        {
                            blocks.Add(imgBlock);
                        }

                        break;
                    default:
                        _logger.LogWarning("Unknown node type: {nodeType}", childNode.Name);
                        break;
                }
            }

            return blocks;
        }
    }
}
