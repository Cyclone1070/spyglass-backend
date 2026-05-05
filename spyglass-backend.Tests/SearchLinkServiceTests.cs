using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp;
using Microsoft.Extensions.Logging;
using Moq;
using spyglass_backend.Features.Links;
using spyglass_backend.Features.WebUtils;
using Xunit;

namespace spyglass_backend.Tests
{
    public class SearchLinkServiceTests
    {
        private readonly Mock<ILogger<SearchLinkService>> _loggerMock;
        private readonly Mock<WebService> _webServiceMock;

        public SearchLinkServiceTests()
        {
            _loggerMock = new Mock<ILogger<SearchLinkService>>();
            _webServiceMock = new Mock<WebService>(new Mock<IHttpClientFactory>().Object);
        }

        private async Task<(AngleSharp.Dom.IDocument, string)> CreateDocument(string html)
        {
            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            var doc = await context.OpenAsync(req => req.Content(html));
            return (doc, "https://test.com");
        }

        [Fact]
        public async Task ScrapeSearchLinksAsync_SelectsBestInputBasedOnScore()
        {
            // Arrange
            var html =
                @"
                <form action='/search' method='get'>
                    <input type='text' name='bad_q' placeholder='Footer search' /> <!-- Lower score -->
                </form>
                <header>
                    <form action='/find' method='get'>
                        <input type='search' name='q' placeholder='Search movies...' /> <!-- Higher score (type=search + header) -->
                    </form>
                </header>
                <footer>
                    <form action='/lost' method='get'>
                        <input type='text' name='ignored' /> <!-- Penalty score -->
                    </form>
                </footer>";

            var (doc, url) = await CreateDocument(html);
            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync(It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync((doc, url));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink { Url = "https://test.com", Title = "Test" };

            // Act
            var result = await service.ScrapeSearchLinksAsync(link);

            // Assert
            Assert.Contains("find?q={0}", result.SearchUrl);
        }

        [Fact]
        public async Task ScrapeSearchLinksAsync_IgnoresNewsletterAndLoginForms()
        {
            // Arrange
            // Two GET forms. One is a newsletter, one is a search.
            var html =
                @"
                <form action='/subscribe' method='get'>
                    <label>Sign up for our newsletter!</label>
                    <input type='text' name='email' />
                    <button type='submit'>Subscribe</button>
                </form>
                <div role='search'>
                    <form action='/search' method='get'>
                        <input type='text' name='q' placeholder='Search...' />
                        <button type='submit'>Find</button>
                    </form>
                </div>";

            var (doc, url) = await CreateDocument(html);
            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync(It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync((doc, url));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink { Url = "https://test.com", Title = "Test" };

            // Act
            var result = await service.ScrapeSearchLinksAsync(link);

            // Assert
            Assert.Contains("/search?q={0}", result.SearchUrl);
            Assert.DoesNotContain("subscribe", result.SearchUrl);
        }
    }
}
