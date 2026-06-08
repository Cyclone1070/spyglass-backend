using Xunit;
using AngleSharp;
using spyglass_backend.Features.WebUtils;
using spyglass_backend.Features.Links;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace spyglass_backend.Tests
{
    public class ResultCardServiceTests
    {
        [Fact]
        public async Task FindResultCardSelector_IgnoresLayoutAndDropdownMenus()
        {
            // Arrange
            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            
            // html1 and html2 both have a dropdown menu with 5 items, and search results with 2 items.
            // Under normal complexity and count scoring, the dropdown menu would score higher:
            // Dropdown count = 5, score = 50 + complexity
            // Results count = 2, score = 20 + complexity
            var html1 = @"
                <html>
                <body>
                    <ul class=""dropdown-menu"">
                        <li><a href=""/genre/action"">Action</a></li>
                        <li><a href=""/genre/comedy"">Comedy</a></li>
                        <li><a href=""/genre/drama"">Drama</a></li>
                        <li><a href=""/genre/sci-fi"">Sci-Fi</a></li>
                        <li><a href=""/genre/horror"">Horror</a></li>
                    </ul>
                    <div class=""results-list"">
                        <div class=""movie-card"">
                            <span class=""title"">Movie A</span>
                            <a href=""/watch/a"">Watch Now</a>
                        </div>
                        <div class=""movie-card"">
                            <span class=""title"">Movie B</span>
                            <a href=""/watch/b"">Watch Now</a>
                        </div>
                    </div>
                </body>
                </html>";

            var html2 = @"
                <html>
                <body>
                    <ul class=""dropdown-menu"">
                        <li><a href=""/genre/action"">Action</a></li>
                        <li><a href=""/genre/comedy"">Comedy</a></li>
                        <li><a href=""/genre/drama"">Drama</a></li>
                        <li><a href=""/genre/sci-fi"">Sci-Fi</a></li>
                        <li><a href=""/genre/horror"">Horror</a></li>
                    </ul>
                    <div class=""results-list"">
                        <div class=""movie-card"">
                            <span class=""title"">Movie C</span>
                            <a href=""/watch/c"">Watch Now</a>
                        </div>
                        <div class=""movie-card"">
                            <span class=""title"">Movie D</span>
                            <a href=""/watch/d"">Watch Now</a>
                        </div>
                    </div>
                </body>
                </html>";

            var doc1 = await context.OpenAsync(req => req.Content(html1));
            var doc2 = await context.OpenAsync(req => req.Content(html2));
            var blacklist = new HashSet<ElementSelector>();

            // Act
            var selector = ResultCardService.FindResultCardSelector(blacklist, doc1, doc2);

            // Assert
            // It should ignore the dropdown-menu entirely and return the movie-card pattern.
            Assert.Equal("div.results-list", selector.Parent);
            Assert.Equal("div.movie-card", selector.Element);
        }
    }
}
