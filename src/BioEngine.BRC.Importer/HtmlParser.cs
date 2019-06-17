using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BioEngine.BRC.Domain.Entities.Blocks;
using BioEngine.Core.Entities;
using BioEngine.Core.Entities.Blocks;
using HtmlAgilityPack;

namespace BioEngine.BRC.Importer
{
    public class HtmlParser
    {
        private readonly FilesUploader _filesUploader;

        private readonly Regex _thumbUrlRegex = new Regex("gallery\\/thumb\\/([0-9]+)\\/[0-9]+\\/[0-9]+\\/?([0-9]+)?");

        public HtmlParser(FilesUploader filesUploader)
        {
            _filesUploader = filesUploader;
        }

        private ContentBlock ParseIframe(HtmlNode node)
        {
            var srcUrl = node.Attributes["src"].Value;
            if (srcUrl.Contains("www.youtube.com/embed"))
            {
                var ytId = srcUrl.Substring(srcUrl.LastIndexOf('/') + 1);
                return new YoutubeBlock {Id = Guid.NewGuid(), Data = new YoutubeBlockData {YoutubeId = ytId}};
            }

            if (srcUrl.Contains("player.twitch.tv"))
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
            var blocks = new List<ContentBlock>();
            var document = new HtmlDocument();
            document.LoadHtml(html);

            foreach (var childNode in document.DocumentNode.ChildNodes)
            {
                switch (childNode.Name)
                {
                    case "p":
                    case "div":
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
                    case "img":
                        var imgBlock = await ParseImgAsync(childNode, pics, uploadPath);
                        if (imgBlock != null)
                        {
                            blocks.Add(imgBlock);
                        }

                        break;
                }
            }

            return blocks;
        }
    }
}
