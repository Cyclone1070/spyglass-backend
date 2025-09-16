using System.Text.RegularExpressions;
using FuzzySharp;

namespace spyglass_backend.Features.Results
{
	public partial class ResultATagService
	{
		// Find the most likely title
		public static int GetRankingScore(string normalisedQuery, string normalisedTitle)
		{
			int score = Fuzz.PartialTokenSetRatio(normalisedQuery, normalisedTitle);

			var queryWords = normalisedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var titleWords = new HashSet<string>(normalisedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries));

			// Slightly boost score if all words in the query are present in the title and the lengths match
			if (score > 95 && queryWords.Length == titleWords.Count)
			{
				score += 1;
			}

			// Slightly reduce score if any word in the query is missing from the title
			if (queryWords.Any(queryWord => !titleWords.Contains(queryWord)))
			{
				score -= 1;
			}

			return score;
		}
		// REGEX 1: Matches anything that ISN'T a letter, number, or space.
		[GeneratedRegex(@"[^a-z0-9\s]")]
		private static partial Regex PunctuationRegex();

		// REGEX 2: Matches one or MORE whitespace characters in a row.
		[GeneratedRegex(@"\s+")]
		private static partial Regex WhitespaceRegex();

		// Normalizes strings by lowercasing, removing punctuation, and standardizing spaces.
		public static string NormaliseString(string input)
		{
			if (string.IsNullOrWhiteSpace(input)) return string.Empty;

			var lowercased = input.ToLowerInvariant();

			// STEP 1: Use the first Regex to remove all punctuation.
			var noPunctuation = PunctuationRegex().Replace(lowercased, "");

			// STEP 2: Use the second Regex to clean up and standardize spaces.
			var singleSpaced = CleanTitle(noPunctuation);

			return singleSpaced;
		}

		// Cleans up title by removing extra spaces created by phrase removal
		public static string CleanTitle(string title)
		{
			if (string.IsNullOrWhiteSpace(title)) return string.Empty;

			// Remove extra spaces created by phrase removal
			title = WhitespaceRegex().Replace(title, " ").Trim();

			return title;
		}

		// Converts a possibly relative URL to an absolute URL based on the provided base URL.
		public static string? ToAbsoluteUrl(string baseUrl, string? url)
		{
			// 1. Basic input validation.
			if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(url))
			{
				return null;
			}

			// 2. Safely create the base Uri. This is required for resolving relative URLs.
			if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
			{
				// The provided base URL was invalid.
				return null;
			}

			// 3. Let the Uri class do all the intelligent work.
			// This constructor is designed for this exact scenario and handles all cases.
			if (Uri.TryCreate(baseUri, url, out Uri? absoluteUri))
			{
				return absoluteUri.AbsoluteUri;
			}

			// Return null if the combination failed for any reason (e.g., a malformed relative URL).
			return null;
		}

	}
}
