using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using spyglass_backend.Configuration;
using spyglass_backend.Features.Links;
using spyglass_backend.Features.Search;
using spyglass_backend.Features.WebUtils;
using Xunit;

namespace spyglass_backend.Tests
{
    public class SearchServiceTests
    {
        private readonly Mock<ILogger<SearchService>> _loggerMock;
        private readonly Mock<WebService> _webServiceMock;
        private readonly IOptions<ScraperRules> _scraperRules;
        private readonly IOptions<SearchSettings> _searchSettings;

        public SearchServiceTests()
        {
            _loggerMock = new Mock<ILogger<SearchService>>();
            _webServiceMock = new Mock<WebService>(new Mock<IHttpClientFactory>().Object);
            _scraperRules = Options.Create(
                new ScraperRules { SearchSkipKeywords = new List<string> { "ad" } }
            );
            _searchSettings = Options.Create(new SearchSettings { MaxParallelism = 1 });
        }

        private async Task<(AngleSharp.Dom.IDocument, string)> CreateDocument(string html)
        {
            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            var doc = await context.OpenAsync(req => req.Content(html));
            return (doc, "https://search.com");
        }

        [Fact]
        public async Task SearchLinksAsync_CorrectlyExtractsUniqueLinksFromCards()
        {
            // Arrange
            var html =
                @"
                <div class='result-card'>
                    <h2><a href='/movie-123'>The Batman</a></h2>
                    <p>Some description</p>
                    <a href='/movie-123' class='btn'>Download</a>
                </div>
                <div class='result-card'>
                    <h2><a href='/movie-456'>Inception</a></h2>
                </div>";

            var (doc, url) = await CreateDocument(html);
            _webServiceMock
                .Setup(w =>
                    w.GetHtmlDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())
                )
                .ReturnsAsync((doc, url));

            var service = new SearchService(
                _loggerMock.Object,
                _scraperRules,
                _searchSettings,
                _webServiceMock.Object
            );
            var link = new Link
            {
                Url = "https://search.com",
                CardSelector = ".result-card",
                SearchUrl = "https://search.com/s?q={0}",
            };

            // Act
            var results = await service
                .SearchLinksAsync("batman", new List<Link> { link })
                .ToListAsync();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains(
                results,
                r => r.Title == "The Batman" && r.ResultUrl.Contains("/movie-123")
            );
            Assert.Contains(
                results,
                r => r.Title == "Inception" && r.ResultUrl.Contains("/movie-456")
            );
        }

        [Fact]
        public async Task SearchLinksAsync_DisambiguatesBetweenMultipleLinksInCard()
        {
            // Arrange
            // Card has 3 links, but only one is unique (movie-789).
            // The others (category-action and uploader-bob) appear in multiple cards.
            var html =
                @"
                <div class='item'>
                    <a href='/category-action'>Action</a>
                    <a href='/movie-789'>The Matrix</a>
                    <a href='/uploader-bob'>Bob</a>
                </div>
                <div class='item'>
                    <a href='/category-action'>Action</a>
                    <a href='/movie-999'>John Wick</a>
                    <a href='/uploader-bob'>Bob</a>
                </div>";

            var (doc, url) = await CreateDocument(html);
            _webServiceMock
                .Setup(w =>
                    w.GetHtmlDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())
                )
                .ReturnsAsync((doc, url));

            var service = new SearchService(
                _loggerMock.Object,
                _scraperRules,
                _searchSettings,
                _webServiceMock.Object
            );
            var link = new Link
            {
                Url = "https://search.com",
                CardSelector = ".item",
                SearchUrl = "https://search.com/s?q={0}",
            };

            // Act
            var results = await service
                .SearchLinksAsync("matrix", new List<Link> { link })
                .ToListAsync();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.ResultUrl.Contains("/movie-789")); // Should pick the UNIQUE one
            Assert.Contains(results, r => r.ResultUrl.Contains("/movie-999")); // Should pick the UNIQUE one
            Assert.DoesNotContain(results, r => r.ResultUrl.Contains("/category-action"));
        }
    }
}
