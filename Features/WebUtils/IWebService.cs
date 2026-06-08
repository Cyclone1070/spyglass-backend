using AngleSharp.Dom;

namespace spyglass_backend.Features.WebUtils;

public interface IWebService
{
    Task<(IDocument, long)> GetHtmlDocumentAsync(string url, Uri? referer = null, bool useProxy = false);
}
