using System.Globalization;
using System.Text.RegularExpressions;
using FuzzySharp;

namespace spyglass_backend.Features.WebUtils
{
	public partial class ResultATagService
	{
		// Find the most likely title
		public static int GetRankingScore(string normalisedQuery, string normalisedTitle)
		{
			int score = Fuzz.TokenSetRatio(normalisedQuery, normalisedTitle);

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

		public static string ExtractUrlPath(string resultUrl)
		{
			if (string.IsNullOrWhiteSpace(resultUrl)) return string.Empty;

			if (!Uri.TryCreate(resultUrl, UriKind.Absolute, out Uri? uri))
			{
				return string.Empty; // Not a valid URL
			}

			string fullPath = uri.AbsolutePath.TrimEnd('/');

			// Get the last segment of the path
			// Path.GetFileName handles trimming leading/trailing slashes implicitly for the segment it returns.
			string lastSegment = Path.GetFileName(fullPath);

			if (string.IsNullOrWhiteSpace(lastSegment))
			{
				return string.Empty; // No meaningful last segment
			}

			// Replace common URL separators with spaces to treat them as word boundaries
			// Example: "batman-arkham-knight" -> "batman arkham knight"
			lastSegment = lastSegment.Replace('-', ' ').Replace('_', ' ');

			// Now apply the general string normalization which handles lowercasing,
			// removes any remaining non-alphanumeric/non-space characters, and standardizes spaces.
			return lastSegment;
		}

		// REGEX 2: Matches one or MORE whitespace characters in a row.
		[GeneratedRegex(@"\s+")]
		private static partial Regex WhitespaceRegex();
		// Cleans up title by removing extra spaces created by phrase removal
		public static string CleanTitle(string title)
		{
			if (string.IsNullOrWhiteSpace(title)) return string.Empty;

			// 1. Initial cleanup of whitespace.
			title = WhitespaceRegex().Replace(title, " ").Trim();

			// 2. Split the title into words, process each one conditionally, and then rejoin.
			return string.Join(" ", title.Split(' ').Select(word =>
			{
				// If a word is empty/null or already contains ANY uppercase letters,
				// leave it exactly as it is. This preserves acronyms (GTA) and mixed case (McLovin).
				if (string.IsNullOrEmpty(word) || word.Any(char.IsUpper))
				{
					return word;
				}
				else
				{
					if (word.Length > 1)
					{
						return char.ToUpper(word[0]) + word[1..].ToLower();
					}
					// For single-letter words like "a" or "i".
					return word.ToUpper();
				}
			}));
		}
		// Converts a possibly relative URL to an absolute URL based on the provided base URL.
		public static string? ToAbsoluteUrl(string baseUrl, string? url)
		{
			// Basic input validation.
			if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(url))
			{
				return null;
			}

			// Safely create the base Uri. This is required for resolving relative URLs.
			if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
			{
				// The provided base URL was invalid.
				return null;
			}


			// Let the Uri class do all the intelligent work.
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
