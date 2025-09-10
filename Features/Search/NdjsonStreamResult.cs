using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

namespace spyglass_backend.Features.Search
{
	public class NdjsonStreamResult<T>(
			IAsyncEnumerable<T> dataStream) : IActionResult
	{
		private readonly IAsyncEnumerable<T> _dataStream = dataStream;

		public async Task ExecuteResultAsync(ActionContext context)
		{
			var response = context.HttpContext.Response;
			var cancellationToken = context.HttpContext.RequestAborted;

			// Set the correct content type for Newline Delimited JSON
			response.ContentType = "application/x-ndjson";

			// Iterate over the stream of data you passed in
			await foreach (var item in _dataStream.WithCancellation(cancellationToken))
			{
				// Serialize each individual object. Your `Type` property will be handled automatically.
				var jsonString = JsonSerializer.Serialize(item);

				// Write the object's JSON string, followed by a newline.
				await response.WriteAsync(jsonString + "\n", cancellationToken);

				// Flush the stream to ensure the client gets this chunk immediately.
				await response.Body.FlushAsync(cancellationToken);
			}
		}
	}
}
