using FishMMO.Logging;

/// <summary>
/// Middleware that handles HTTP range requests for files served from the
/// application's `wwwroot` directory. Supports partial content responses
/// (HTTP 206) and full-file responses depending on the presence of the
/// `Range` request header.
/// </summary>
public class RangeRequestMiddleware
{
	/// <summary>
	/// The next middleware in the pipeline.
	/// </summary>
	private readonly RequestDelegate next;

	/// <summary>
	/// Initializes a new instance of the <see cref="RangeRequestMiddleware"/> class.
	/// </summary>
	/// <param name="next">The next <see cref="RequestDelegate"/> in the ASP.NET Core pipeline.</param>
	public RangeRequestMiddleware(RequestDelegate next)
	{
		this.next = next;
	}

	/// <summary>
	/// Invokes the middleware to handle an incoming <see cref="HttpContext"/>.
	/// If the request path maps to an existing file under `wwwroot` this method
	/// will either return the requested byte range (when the `Range` header is
	/// present and valid) or the full file contents. If the file is not found,
	/// a 404 status code is returned. If a requested range is unsatisfiable,
	/// a 416 status code is returned.
	/// </summary>
	/// <param name="context">The <see cref="HttpContext"/> for the current request.</param>
	/// <returns>A <see cref="Task"/> that completes when the response has been written.</returns>
	public async Task InvokeAsync(HttpContext context)
	{
		var path = context.Request.Path.Value;

		await Log.Info("RangeRequestMiddleare", $"RangeRequestMiddleware path: {path}");

		if (string.IsNullOrEmpty(path))
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		var filePath = Path.Combine("wwwroot", path.TrimStart('/'));

		if (!File.Exists(filePath))
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		var fileInfo = new FileInfo(filePath);
		using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		var response = context.Response;
		var request = context.Request;

		response.Headers["Accept-Ranges"] = "bytes";
		long totalLength = fileStream.Length;

		if (request.Headers.TryGetValue("Range", out var rangeHeader))
		{
			var rangeHeaderString = rangeHeader.ToString();

			if (rangeHeaderString.StartsWith("bytes="))
			{
				var range = rangeHeaderString.Replace("bytes=", "").Split('-');
				long start = long.Parse(range[0]);
				long end = range.Length > 1 && !string.IsNullOrEmpty(range[1]) ? long.Parse(range[1]) : totalLength - 1;

				if (start >= totalLength || end >= totalLength)
				{
					response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
					return;
				}

				long length = end - start + 1;
				fileStream.Seek(start, SeekOrigin.Begin);
				response.StatusCode = StatusCodes.Status206PartialContent;
				response.ContentType = GetContentType(fileInfo.Extension);
				response.ContentLength = length;
				response.Headers["Content-Range"] = $"bytes {start}-{end}/{totalLength}";

				await fileStream.CopyToAsync(response.Body, (int)length);
				return;
			}
		}

		response.ContentType = GetContentType(fileInfo.Extension);
		response.ContentLength = totalLength;
		await fileStream.CopyToAsync(response.Body);

		await next(context); // Pass control to the next middleware
	}

	/// <summary>
	/// Maps a file extension to a MIME content type string used in the
	/// HTTP response <c>Content-Type</c> header.
	/// </summary>
	/// <param name="extension">The file extension, including the leading dot (e.g. ".html").</param>
	/// <returns>A MIME type string suitable for the <c>Content-Type</c> header.</returns>
	private string GetContentType(string extension)
	{
		return extension.ToLower() switch
		{
			".html" => "text/html",
			".js" => "application/javascript",
			".json" => "application/json",
			".wasm" => "application/wasm",
			".css" => "text/css",
			".png" => "image/png",
			".jpg" => "image/jpeg",
			".gif" => "image/gif",
			".webmanifest" => "application/manifest+json",
			".unityweb" => "application/octet-stream", // or a more specific type if known
			".bin" => "application/octet-stream", // or a more specific type if known
			".hash" => "text/plain", // assuming it's a text file
			".bundle" => "application/octet-stream", // or a more specific type if known
			_ => "application/octet-stream",
		};
	}
}