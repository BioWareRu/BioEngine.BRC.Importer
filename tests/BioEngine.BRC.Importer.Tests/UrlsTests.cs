using BioEngine.Core.Posts.Entities;
using BioEngine.Core.Routing;
using Microsoft.AspNetCore.Routing;
using Xunit;
using Xunit.Abstractions;

namespace BioEngine.BRC.Importer.Tests
{
    public class UrlsTests: ImporterTest
    {
        public UrlsTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }

        [Fact]
        public void GenerateUrl()
        {
            var post = new Post
            {
                Url = "123"
            };
            var scope = GetScope();
            var linkGenerator = scope.Get<LinkGenerator>();
            var url = linkGenerator.GeneratePublicUrl(post);
            Assert.Equal($"/posts/{post.Url}.html", url.ToString());
        }
    }
}
