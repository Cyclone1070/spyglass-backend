using Xunit;
using spyglass_backend.Features.WebUtils;

namespace spyglass_backend.Tests
{
    public class ResultATagServiceTests
    {
        [Theory]
        [InlineData("The Batman (2022)", "the batman 2022")]
        [InlineData("Spider-Man: No Way Home!!!", "spider-man no way home")]
        [InlineData("It's Always Sunny", "it's always sunny")]
        [InlineData("   extra   spaces   ", "extra spaces")]
        public void NormaliseString_CleansInputCorrectly(string input, string expected)
        {
            var result = ResultATagService.NormaliseString(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetRankingScore_ExactMatch_HighResult()
        {
            var score = ResultATagService.GetRankingScore("batman arkham", "batman arkham");
            Assert.True(score >= 100);
        }

        [Fact]
        public void GetRankingScore_TitleShorterThanQuery_HeavyPenalty()
        {
            // Query is long, title is short
            var score = ResultATagService.GetRankingScore("the batman arkham knight", "batman");
            Assert.True(score < 70); // Should have received a -30 penalty
        }

        [Theory]
        [InlineData("https://site.com/movies/the-batman-2022", "the batman 2022")]
        [InlineData("https://site.com/downloads/gta_v_v1.5", "gta v v1.5")]
        public void ExtractUrlPath_ExtractsCleanSegment(string url, string expected)
        {
            var result = ResultATagService.ExtractUrlPath(url);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("the batman", "The Batman")]
        [InlineData("GTA five", "GTA Five")] // Preserves uppercase acronym
        [InlineData("a simple title", "A Simple Title")]
        public void CleanTitle_ProperlyCapitalizes(string input, string expected)
        {
            var result = ResultATagService.CleanTitle(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToAbsoluteUrl_ResolvesCorrectly()
        {
            var result = ResultATagService.ToAbsoluteUrl("https://base.com/path/", "/new-page");
            Assert.Equal("https://base.com/new-page", result);
        }
    }
}
