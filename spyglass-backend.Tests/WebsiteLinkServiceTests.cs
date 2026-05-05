using Xunit;
using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using spyglass_backend.Features.Links;
using spyglass_backend.Configuration;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace spyglass_backend.Tests
{
    public class WebsiteLinkServiceTests
    {
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly Mock<ILogger<WebsiteLinkService>> _loggerMock;
        private readonly IOptions<ScraperRules> _rules;

        public WebsiteLinkServiceTests()
        {
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _loggerMock = new Mock<ILogger<WebsiteLinkService>>();
            
            var rules = new ScraperRules
            {
                Categories = new List<CategoryRule>
                {
                    new CategoryRule { Name = "Movies", Selector = "#movies" }
                },
                MegathreadSkipKeywords = new List<string> { "skipme" }
            };
            _rules = Options.Create(rules);
        }

        private void SetupHttpClient(string htmlContent)
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(htmlContent),
                });

            var httpClient = new HttpClient(handlerMock.Object);
            _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        }

        [Fact]
        public async Task ScrapeWebsiteLinksAsync_CorrectlyParsesStarredLinks()
        {
            // Arrange
            var html = @"
                <h2 id='movies'>Movies</h2>
                <ul>
                    <li class='starred'><a href='https://goodsite.com'>Good Site</a></li>
                    <li><a href='https://normalsite.com'>Normal Site</a></li>
                </ul>";
            SetupHttpClient(html);
            var service = new WebsiteLinkService(_loggerMock.Object, _httpClientFactoryMock.Object, _rules);

            // Act
            var result = (await service.ScrapeWebsiteLinksAsync("https://test.com")).ToList();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.True(result.First(r => r.Title == "Good Site").Starred);
            Assert.False(result.First(r => r.Title == "Normal Site").Starred);
            Assert.Equal("Movies", result[0].Category);
        }

        [Fact]
        public async Task ScrapeWebsiteLinksAsync_FiltersOutInvalidLinks()
        {
            // Arrange
            var html = @"
                <h2 id='movies'>Movies</h2>
                <ul>
                    <li><a href='https://test.com'>123</a></li> <!-- Numerical title -->
                    <li><a href='https://skipme.com'>Skip Me</a></li> <!-- Skip keyword -->
                    <li><span class='i-twemoji-globe-with-meridians'></span><a href='https://globe.com'>Globe</a></li> <!-- Globe emoji -->
                    <li><a href='https://valid.com'>Valid Site</a></li>
                </ul>";
            SetupHttpClient(html);
            var service = new WebsiteLinkService(_loggerMock.Object, _httpClientFactoryMock.Object, _rules);

            // Act
            var result = (await service.ScrapeWebsiteLinksAsync("https://test.com")).ToList();

            // Assert
            Assert.Single(result);
            Assert.Equal("Valid Site", result[0].Title);
        }

        [Fact]
        public async Task ScrapeWebsiteLinksAsync_HandlesDirtyMegathreadStructure()
        {
            // Arrange
            // This mirrors the messy reality: nested divs, tips between headers, and empty lists.
            var html = @"
                <h2 id='movies'>Movies</h2>
                <div class='tip'>Check these out!</div>
                <p>Wait, there is more...</p>
                <ul>
                    <li class='starred'>
                        <div class='flex'>
                            <a href='https://star.com'>  The Starred Movie  </a>
                            <span>(4K)</span>
                        </div>
                    </li>
                    <li><a href=''>Empty Href</a></li>
                    <li><a href='#anchor'>Anchor Link</a></li>
                    <li><span>Just text</span></li>
                </ul>";
            SetupHttpClient(html);
            var service = new WebsiteLinkService(_loggerMock.Object, _httpClientFactoryMock.Object, _rules);

            // Act
            var result = (await service.ScrapeWebsiteLinksAsync("https://test.com")).ToList();

            // Assert
            Assert.Single(result); // Only 'The Starred Movie' is valid
            Assert.Equal("The Starred Movie", result[0].Title);
            Assert.True(result[0].Starred);
        }
    }
}
