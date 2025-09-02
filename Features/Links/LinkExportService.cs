// In LinkExportService.cs
using System.Text.Json;

namespace spyglass_backend.Features.Links;

// Note: I'm assuming you will register this as ILinkExportService in Program.cs
public class LinkExportService(ILogger<LinkExportService> logger, IWebHostEnvironment environment)
{
	private readonly ILogger<LinkExportService> _logger = logger;
	private readonly IWebHostEnvironment _environment = environment;
	private readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true
	};

	public async Task<string> SaveJsonFileAsync(StoredLinks links, string fileName)
	{
		// Use the more performant method that serializes directly to bytes.
		byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(links, _jsonOptions);

		// Create file at root
		var filePath = Path.Combine(_environment.ContentRootPath, fileName);

		// Write the file to the disk asynchronously. This is the actual save operation.
		await File.WriteAllBytesAsync(filePath, jsonBytes);

		_logger.LogInformation("Successfully saved file to: {FilePath}", filePath);

		// Return the full path for confirmation.
		return filePath;
	}
}
