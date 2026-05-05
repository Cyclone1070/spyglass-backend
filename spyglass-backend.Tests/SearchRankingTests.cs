using Xunit;
using spyglass_backend.Features.Search;
using System.Collections.Generic;
using System.Linq;

namespace spyglass_backend.Tests
{
    public class SearchRankingTests
    {
        [Fact]
        public void SortCacheByScore_WhenScoresAreEqual_PrioritizesStarred()
        {
            // Arrange
            var stream = new SearchStream();
            var result1 = new ResultDto { Title = "Normal", Score = 10, WebsiteStarred = false };
            var result2 = new ResultDto { Title = "Starred", Score = 10, WebsiteStarred = true };
            var result3 = new ResultDto { Title = "Lower Score", Score = 5, WebsiteStarred = true };

            stream.AddToCache(result1);
            stream.AddToCache(result2);
            stream.AddToCache(result3);

            // Act
            stream.SortCacheByScore();
            var results = stream.GetCachedResults();

            // Assert
            Assert.Equal("Starred", results[0].Title);     // Should be first (Score 10 + Starred)
            Assert.Equal("Normal", results[1].Title);      // Should be second (Score 10)
            Assert.Equal("Lower Score", results[2].Title); // Should be last (Score 5)
        }
    }
}
