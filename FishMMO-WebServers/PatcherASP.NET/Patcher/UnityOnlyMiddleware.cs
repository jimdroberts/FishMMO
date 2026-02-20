using FishMMO.Logging;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Middleware that restricts access to requests containing the required
/// X-FishMMO header value. Intended to allow only the official Unity client.
/// </summary>
public class UnityOnlyMiddleware
{
	private readonly RequestDelegate next;

	/// <summary>
	/// Initializes a new instance of the <see cref="UnityOnlyMiddleware"/> class.
	/// </summary>
	/// <param name="next">The next <see cref="RequestDelegate"/> in the pipeline.</param>
	public UnityOnlyMiddleware(RequestDelegate next)
	{
		this.next = next;
	}

	/// <summary>
	/// Invokes the middleware logic: verifies the <c>X-FishMMO</c> header equals
	/// "Client" and either short-circuits with a 403 Forbidden response or calls the next delegate.
	/// </summary>
	/// <param name="context">The current <see cref="HttpContext"/>.</param>
	/// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
	public async Task InvokeAsync(HttpContext context)
	{
		var userAgent = context.Request.Headers["X-FishMMO"].ToString();

		if (!userAgent.Equals("Client"))
		{
			await Log.Warning("UnityOnlyMiddleware", "Rejected Non-FishMMO Client");
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			await context.Response.WriteAsync("Access denied.");
			return;
		}

		await next(context);
	}
}