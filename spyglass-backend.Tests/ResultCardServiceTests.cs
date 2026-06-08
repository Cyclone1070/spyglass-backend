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

        [Fact]
        public async Task IsNoResultsPage_DetectsNegativeKeywords()
        {
            // Arrange
            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            var htmlWithNegative = "<html><body><h1>No Results Found</h1></body></html>";
            var htmlWithoutNegative = "<html><body><h1>Search Results</h1><div class='movie-card'>Movie A</div></body></html>";

            // Act & Assert
            var doc1 = await context.OpenAsync(req => req.Content(htmlWithNegative));
            var doc2 = await context.OpenAsync(req => req.Content(htmlWithoutNegative));

            Assert.True(ResultCardService.IsNoResultsPage(doc1));
            Assert.False(ResultCardService.IsNoResultsPage(doc2));
        }

        [Fact]
        public async Task FindResultCardSelector_FiltersPatternsByQueryMatch()
        {
            // Arrange
            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            
            // html1: query was "batman"
            var html1 = @"
                <html>
                <body>
                    <!-- Higher count, but does not match query -->
                    <div class='trending-movies'>
                        <div class='trending-card'><a href='/trending/1'>Trending Action Movie</a></div>
                        <div class='trending-card'><a href='/trending/2'>Trending Comedy Movie</a></div>
                        <div class='trending-card'><a href='/trending/3'>Trending Drama Movie</a></div>
                        <div class='trending-card'><a href='/trending/4'>Trending Sci-Fi Movie</a></div>
                        <div class='trending-card'><a href='/trending/5'>Trending Horror Movie</a></div>
                    </div>
                    <!-- Lower count, but matches query 'batman' -->
                    <div class='results-list'>
                        <div class='movie-card'>
                            <a href='/watch/batman1'>Batman Begins</a>
                        </div>
                        <div class='movie-card'>
                            <a href='/watch/batman2'>Batman Returns</a>
                        </div>
                    </div>
                </body>
                </html>";

            // html2: query was "mario"
            var html2 = @"
                <html>
                <body>
                    <!-- Higher count, but does not match query -->
                    <div class='trending-movies'>
                        <div class='trending-card'><a href='/trending/1'>Trending Action Movie</a></div>
                        <div class='trending-card'><a href='/trending/2'>Trending Comedy Movie</a></div>
                        <div class='trending-card'><a href='/trending/3'>Trending Drama Movie</a></div>
                        <div class='trending-card'><a href='/trending/4'>Trending Sci-Fi Movie</a></div>
                        <div class='trending-card'><a href='/trending/5'>Trending Horror Movie</a></div>
                    </div>
                    <!-- Lower count, but matches query 'mario' -->
                    <div class='results-list'>
                        <div class='movie-card'>
                            <a href='/watch/mario1'>Super Mario Bros</a>
                        </div>
                        <div class='movie-card'>
                            <a href='/watch/mario2'>Mario Kart</a>
                        </div>
                    </div>
                </body>
                </html>";

            var doc1 = await context.OpenAsync(req => req.Content(html1));
            var doc2 = await context.OpenAsync(req => req.Content(html2));
            var blacklist = new HashSet<ElementSelector>();

            // Act
            var selector = ResultCardService.FindResultCardSelector(blacklist, doc1, doc2, "batman", "mario");

            // Assert
            // It should ignore the trending-card (0% query match) and return the movie-card pattern
            Assert.Equal("div.results-list", selector.Parent);
            Assert.Equal("div.movie-card", selector.Element);
        }

        [Fact]
        public async Task FindResultCardSelector_SupportsJsClickNavigatingCards()
        {
            // Arrange
            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            
            // html1 and html2 have movie cards with no <a> tags, only data-href attributes on divs
            var html1 = @"
                <html>
                <body>
                    <div class=""results-list"">
                        <div class=""movie-card"" data-href=""/watch/a"">
                            <span class=""title"">Movie A</span>
                        </div>
                        <div class=""movie-card"" data-href=""/watch/b"">
                            <span class=""title"">Movie B</span>
                        </div>
                    </div>
                </body>
                </html>";

            var html2 = @"
                <html>
                <body>
                    <div class=""results-list"">
                        <div class=""movie-card"" data-href=""/watch/c"">
                            <span class=""title"">Movie C</span>
                        </div>
                        <div class=""movie-card"" data-href=""/watch/d"">
                            <span class=""title"">Movie D</span>
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
            Assert.Equal("div.results-list", selector.Parent);
            Assert.Equal("div.movie-card", selector.Element);
        }
    }
}
