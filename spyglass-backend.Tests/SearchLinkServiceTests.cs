using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
        private readonly Mock<IWebService> _webServiceMock;

        public SearchLinkServiceTests()
        {
            _loggerMock = new Mock<ILogger<SearchLinkService>>();
            _webServiceMock = new Mock<IWebService>();
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
                .Setup(w => w.GetHtmlDocumentAsync(It.IsAny<string>(), It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((doc, 0L));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink
            {
                Url = "https://test.com",
                Title = "Test",
                Category = "General",
                Starred = false
            };

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
                .Setup(w => w.GetHtmlDocumentAsync(It.IsAny<string>(), It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((doc, 0L));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink
            {
                Url = "https://test.com",
                Title = "Test",
                Category = "General",
                Starred = false
            };

            // Act
            var result = await service.ScrapeSearchLinksAsync(link);

            // Assert
            Assert.Contains("/search?q={0}", result.SearchUrl);
            Assert.DoesNotContain("subscribe", result.SearchUrl);
        }

        [Fact]
        public async Task ScrapeSearchLinksAsync_ProbesSearchSlashQuery_WhenNoFormFound()
        {
            // Arrange - main page has no forms
            var mainHtml = @"<html><body>No forms here</body></html>";
            var (mainDoc, _) = await CreateDocument(mainHtml);

            // /search/batman returns content with batman
            var probeHtml = @"<html><body>Showing results for batman</body></html>";
            var (probeDoc, _) = await CreateDocument(probeHtml);

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((mainDoc, 0L));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search/batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((probeDoc, 0L));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink
            {
                Url = "https://test.com",
                Title = "Test",
                Category = "General",
                Starred = false
            };

            // Act
            var result = await service.ScrapeSearchLinksAsync(link);

            // Assert
            Assert.Equal("https://test.com/search/{0}", result.SearchUrl);
        }

        [Fact]
        public async Task ScrapeSearchLinksAsync_ProbesSearchQueryPattern_WhenSearchSlashFails()
        {
            // Arrange
            var mainHtml = @"<html><body>No forms here</body></html>";
            var (mainDoc, _) = await CreateDocument(mainHtml);

            var probeHtml = @"<html><body>Showing results for batman</body></html>";
            var (probeDoc, _) = await CreateDocument(probeHtml);

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((mainDoc, 0L));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search/batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ThrowsAsync(new HttpRequestException("Not found"));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search?q=batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((probeDoc, 0L));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink
            {
                Url = "https://test.com",
                Title = "Test",
                Category = "General",
                Starred = false
            };

            // Act
            var result = await service.ScrapeSearchLinksAsync(link);

            // Assert
            Assert.Equal("https://test.com/search?q={0}", result.SearchUrl);
        }

        [Fact]
        public async Task ScrapeSearchLinksAsync_ProbesSPattern_WhenSearchPatternsFail()
        {
            // Arrange
            var mainHtml = @"<html><body>No forms here</body></html>";
            var (mainDoc, _) = await CreateDocument(mainHtml);

            var probeHtml = @"<html><body>Showing results for batman</body></html>";
            var (probeDoc, _) = await CreateDocument(probeHtml);

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((mainDoc, 0L));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search/batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ThrowsAsync(new HttpRequestException("Not found"));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search?q=batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ThrowsAsync(new HttpRequestException("Not found"));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/?s=batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((probeDoc, 0L));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink
            {
                Url = "https://test.com",
                Title = "Test",
                Category = "General",
                Starred = false
            };

            // Act
            var result = await service.ScrapeSearchLinksAsync(link);

            // Assert
            Assert.Equal("https://test.com/?s={0}", result.SearchUrl);
        }

        [Fact]
        public async Task ScrapeSearchLinksAsync_ProbesNextPattern_WhenBodyDoesNotContainQuery()
        {
            // Arrange
            var mainHtml = @"<html><body>No forms here</body></html>";
            var (mainDoc, _) = await CreateDocument(mainHtml);

            // /search/batman returns content but doesn't contain "batman"
            var emptyProbeHtml = @"<html><body>No results</body></html>";
            var (emptyProbeDoc, _) = await CreateDocument(emptyProbeHtml);

            var probeHtml = @"<html><body>Showing results for batman</body></html>";
            var (probeDoc, _) = await CreateDocument(probeHtml);

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((mainDoc, 0L));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search/batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((emptyProbeDoc, 0L));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search?q=batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((probeDoc, 0L));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink
            {
                Url = "https://test.com",
                Title = "Test",
                Category = "General",
                Starred = false
            };

            // Act
            var result = await service.ScrapeSearchLinksAsync(link);

            // Assert
            Assert.Equal("https://test.com/search?q={0}", result.SearchUrl);
        }

        [Fact]
        public async Task ScrapeSearchLinksAsync_ProbesSucceeds_WhenLaterQueryMatches()
        {
            // Arrange
            var mainHtml = @"<html><body>No forms here</body></html>";
            var (mainDoc, _) = await CreateDocument(mainHtml);

            // /search/batman returns content without "batman"
            var emptyBatmanHtml = @"<html><body>No results here</body></html>";
            var (emptyBatmanDoc, _) = await CreateDocument(emptyBatmanHtml);

            // /search/mario returns content WITH "mario"
            var marioHtml = @"<html><body>Showing results for mario</body></html>";
            var (marioDoc, _) = await CreateDocument(marioHtml);

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((mainDoc, 0L));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search/batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((emptyBatmanDoc, 0L));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search/mario", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((marioDoc, 0L));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink
            {
                Url = "https://test.com",
                Title = "Test",
                Category = "General",
                Starred = false
            };

            // Act
            var result = await service.ScrapeSearchLinksAsync(link);

            // Assert
            Assert.Equal("https://test.com/search/{0}", result.SearchUrl);
        }

        [Fact]
        public async Task ScrapeSearchLinksAsync_Throws_WhenNoFormAndAllProbesFail()
        {
            // Arrange
            var mainHtml = @"<html><body>No forms here</body></html>";
            var (mainDoc, _) = await CreateDocument(mainHtml);

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ReturnsAsync((mainDoc, 0L));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search/batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ThrowsAsync(new HttpRequestException("Not found"));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/search?q=batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ThrowsAsync(new HttpRequestException("Not found"));

            _webServiceMock
                .Setup(w => w.GetHtmlDocumentAsync("https://test.com/?s=batman", It.IsAny<Uri?>(), It.IsAny<bool>()))
                .ThrowsAsync(new HttpRequestException("Not found"));

            var service = new SearchLinkService(_loggerMock.Object, _webServiceMock.Object);
            var link = new WebsiteLink
            {
                Url = "https://test.com",
                Title = "Test",
                Category = "General",
                Starred = false
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ScrapeSearchLinksAsync(link));
            Assert.Contains("search", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
