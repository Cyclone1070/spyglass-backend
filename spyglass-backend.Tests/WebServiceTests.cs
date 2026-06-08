using Moq;
using Moq.Protected;
using spyglass_backend.Features.WebUtils;
using System.Net;
using System.Diagnostics;
using AngleSharp;

namespace spyglass_backend.Tests;

public class WebServiceTests
{
    [Fact]
    public async Task GetHtmlDocumentAsync_ProxyDown_ShouldFailQuickly()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        
        // Simulate a delay of 1 second for ANY proxy request
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("proxy-spyglass.cyc.fyi")),
                ItExpr.IsAny<CancellationToken>()
            )
            .Returns(async (HttpRequestMessage request, CancellationToken cancellationToken) => {
                await Task.Delay(1000, cancellationToken); // Simulate slow/unresponsive proxy
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        // First pass (normal request) returns 403
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("proxy-spyglass.cyc.fyi")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var webService = new WebService(factoryMock.Object);

        // Act
        var stopwatch = Stopwatch.StartNew();
        
        // This is what happens in MegathreadService: try normal, catch 403, try proxy
        try 
        {
            await webService.GetHtmlDocumentAsync("https://example.com", useProxy: false);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Forbidden)
        {
            // Second pass: useProxy = true
            await Assert.ThrowsAsync<HttpRequestException>(async () => 
                await webService.GetHtmlDocumentAsync("https://example.com", useProxy: true)
            );
        }
        stopwatch.Stop();

        // Assert
        // If the probe works, it should fail within ~400ms + overhead.
        Assert.True(stopwatch.ElapsedMilliseconds < 800, $"Proxy check took too long: {stopwatch.ElapsedMilliseconds}ms");

        // Verify that the health endpoint was the one probed
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("proxy-spyglass.cyc.fyi/health")),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task BuildClassSelector_FiltersOutInvalidCssClasses()
    {
        // Arrange
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content("<div class=\"grid 2xl:grid bg-[#ffffff] text-red-500\"></div>"));
        var element = document.QuerySelector("div")!;

        // Act
        var selector = WebService.BuildClassSelector(element);

        // Assert
        Assert.Equal("div.grid.text-red-500", selector);
    }

    [Fact]
    public async Task GetCommonSelector_FiltersOutInvalidCssClasses()
    {
        // Arrange
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(@"
            <div class=""item grid 2xl:grid bg-[#ffffff] text-red-500""></div>
            <div class=""item grid 2xl:grid bg-[#ffffff] text-blue-500""></div>
        "));
        var elements = document.QuerySelectorAll(".item");

        // Act
        var selector = WebService.GetCommonSelector("body", elements);

        // Assert
        Assert.Equal("body", selector.Parent);
        Assert.Equal("div.grid.item", selector.Element);
    }
}
