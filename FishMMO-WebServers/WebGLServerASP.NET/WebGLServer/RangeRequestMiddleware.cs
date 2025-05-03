using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;

public class RangeRequestMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<RangeRequestMiddleware> logger;

    public RangeRequestMiddleware(RequestDelegate next, ILogger<RangeRequestMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;

        logger.LogInformation($"RangeRequestMiddleware path: {path}");

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