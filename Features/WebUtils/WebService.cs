using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using spyglass_backend.Features.Links;

namespace spyglass_backend.Features.WebUtils
{
    public partial class WebService(IHttpClientFactory httpClientFactory) : IWebService
    {
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
        private static bool? _proxyAvailable;
        private static readonly SemaphoreSlim _proxySemaphore = new(1, 1);

        public async Task<(IDocument, long)> GetHtmlDocumentAsync(
            string url,
            Uri? referer = null,
            bool useProxy = false
        )
        {
            var client = _httpClientFactory.CreateClient();
            var requestUrl = url;

            if (useProxy)
            {
                await CheckProxyHealthAsync(client);

                requestUrl =
                    $"http://proxy-spyglass.cyc.fyi/fetch?url={System.Web.HttpUtility.UrlEncode(url)}";
            }

            // Add anti-bot headers
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
            );
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8"
            );
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept-Language",
                "en-US,en;q=0.9"
            );
            client.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("DNT", "1");
            // Add the Referer header if provided
            if (referer != null)
            {
                client.DefaultRequestHeaders.Referrer = referer;
            }

            var stopwatch = Stopwatch.StartNew();
            using var response = await client.GetAsync(requestUrl);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);
            }
            response.EnsureSuccessStatusCode();
            var htmlContent = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();
            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(htmlContent));
            return (document, stopwatch.ElapsedMilliseconds);
        }

        private static async Task CheckProxyHealthAsync(HttpClient client)
        {
            if (_proxyAvailable == false)
            {
                throw new HttpRequestException(
                    "Proxy is marked as unavailable.",
                    null,
                    HttpStatusCode.ServiceUnavailable
                );
            }

            if (_proxyAvailable == null)
            {
                await _proxySemaphore.WaitAsync();
                try
                {
                    if (_proxyAvailable == null)
                    {
                        try
                        {
                            // Probe the relay with a very short timeout
                            const string probeUrl = "http://proxy-spyglass.cyc.fyi/health";
                            using var cts = new CancellationTokenSource(
                                TimeSpan.FromMilliseconds(400)
                            );
                            using var probeResponse = await client.GetAsync(
                                probeUrl,
                                HttpCompletionOption.ResponseHeadersRead,
                                cts.Token
                            );
                            _proxyAvailable = true;
                        }
                        catch
                        {
                            _proxyAvailable = false;
                            throw new HttpRequestException(
                                "Proxy probe failed or timed out.",
                                null,
                                HttpStatusCode.ServiceUnavailable
                            );
                        }
                    }
                }
                finally
                {
                    _proxySemaphore.Release();
                }
            }

            if (_proxyAvailable == false)
            {
                throw new HttpRequestException(
                    "Proxy is unavailable.",
                    null,
                    HttpStatusCode.ServiceUnavailable
                );
            }
        }

        public static ElementSelector GetElementSelector(IElement element)
        {
            // Build the selector part for the current element.
            var elementSelector = BuildClassSelector(element);

            // Get the parent element.
            var parent = element.ParentElement;

            // If there's no parent (e.g., for the <html> tag), the selector is just the element itself.
            if (parent == null)
            {
                return new ElementSelector { Parent = string.Empty, Element = elementSelector };
            }

            // Build the selector part for the parent.
            var parentSelector = BuildParentSelector(parent);

            // Combine them using the direct child combinator ">".
            return new ElementSelector { Parent = parentSelector, Element = elementSelector };
        }

        // Creates a generalized CSS selector for a group of elements by finding their common classes.
        public static ElementSelector GetCommonSelector(
            string parent,
            IEnumerable<IElement> elements
        )
        {
            var first = elements.First();
            var tagName = first.TagName.ToLowerInvariant();

            // Find the intersection of all class lists to get the classes they all share.
            var commonClasses = elements
                .Select(e => e.ClassList as IEnumerable<string>) // Cast to IEnumerable for Aggregate
                .Aggregate((current, next) => current.Intersect(next))
                .Where(IsValidCssClass)
                .Order()
                .ToList();

            var builder = new StringBuilder(tagName);
            if (commonClasses.Count > 0)
            {
                var escapedCommonClasses = commonClasses.Select(EscapeCssIdentifier);
                builder.Append('.').AppendJoin(".", escapedCommonClasses);
            }
            return new ElementSelector { Parent = parent, Element = builder.ToString() };
        }

        // Helper function to build a consistent "tag.class1.class2" selector for a single element.
        public static string BuildClassSelector(IElement element)
        {
            if (element == null)
                return string.Empty;

            var tag = element.TagName.ToLowerInvariant();
            var builder = new StringBuilder(tag);

            // --- MODIFIED LOGIC ---
            var classes = element
                .ClassList
                .Where(IsValidCssClass)
                .Select(EscapeCssIdentifier) // Escape each class name
                .Order()
                .ToList();

            if (classes.Count > 0)
            {
                // Priority 1: If classes exist, use them. They are more likely to be semantic.
                builder.Append('.').AppendJoin(".", classes);
            }
            else if (!string.IsNullOrEmpty(element.Id))
            {
                // Priority 2: If NO classes exist, fall back to using the ID.
                builder.Append('#').Append(element.Id);
            }

            // If neither classes nor ID exist, it will just return the tag name.
            return builder.ToString();
        }

        // Prioritise ID over classes
        public static string BuildParentSelector(IElement element)
        {
            if (element == null)
                return string.Empty;
            // Handle the priority case: if a non-empty ID exists, use it immediately.
            if (!string.IsNullOrEmpty(element.Id))
            {
                return $"{element.TagName.ToLowerInvariant()}#{element.Id}";
            }
            // If there's no ID, fall back to the original class-based logic.
            // This avoids repeating the class-building code.
            return BuildClassSelector(element);
        }

        [GeneratedRegex(@"[^a-zA-Z0-9_-]")]
        private static partial Regex InvalidCssCharRegex();

        private static string EscapeCssIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return string.Empty;
            }

            // This regex matches any character that is NOT a-z, A-Z, 0-9, underscore, or hyphen.
            // The replacement pattern "\\$&" inserts a literal backslash before the matched character.
            return InvalidCssCharRegex().Replace(identifier, @"\$&");
        }

        private static bool IsValidCssClass(string className)
        {
            return !string.IsNullOrEmpty(className)
                && !char.IsDigit(className[0])
                && !className.Contains(':')
                && !className.Contains('[')
                && !className.Contains(']');
        }

        // Generates a CSS selector path from a parent element to a child element, excluding the parent itself.
        public static string? GetTagPath(IElement parent, IElement child)
        {
            // If the child is the parent, there's no path.
            if (parent == child)
            {
                return null;
            }

            var pathSegments = new List<string>();
            var currentNode = child;

            // Walk up the tree from the child until we hit the parent.
            // The loop must also check for null in case the child is not a descendant of the parent.
            while (currentNode != null && currentNode != parent)
            {
                // Prepend the tag name to our list of segments.
                pathSegments.Add(currentNode.TagName.ToLowerInvariant());
                currentNode = currentNode.ParentElement;
            }

            // After the loop, if currentNode is not the parent, it means we reached the top of the document
            // without finding the parent, so the child is not a descendant.
            if (currentNode != parent)
            {
                throw new InvalidOperationException(
                    "The specified child is not a descendant of the specified parent."
                );
            }

            // The path was built from child-to-parent, so we need to reverse it.
            pathSegments.Reverse();

            // Join the segments with the direct child combinator.
            return string.Join(" > ", pathSegments);
        }
    }
}
