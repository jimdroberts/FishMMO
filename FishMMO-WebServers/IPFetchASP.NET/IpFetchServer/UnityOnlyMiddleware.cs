using FishMMO.Logging;

public class UnityOnlyMiddleware
{
	/// <summary>
	/// Middleware that only allows requests originating from the FishMMO Unity
	/// client. It checks for the custom request header <c>X-FishMMO</c> and
	/// rejects requests that do not identify as the expected client.
	/// </summary>
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
	/// Invokes the middleware for the given <see cref="HttpContext"/>,
	/// logging the <c>X-FishMMO</c> header and rejecting any request that does
	/// not identify as the Unity client.
	/// </summary>
	/// <param name="context">The current HTTP context.</param>
	/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
	public async Task InvokeAsync(HttpContext context)
	{
		var userAgent = context.Request.Headers["X-FishMMO"].ToString();

		await Log.Info("UnityOnlyMiddleware", $"UserAgent: {userAgent}");

		if (!userAgent.Equals("Client"))
		{
			await Log.Warning("UnityOnlyMiddleware", $"Rejected Non-FishMMO Client");
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			await context.Response.WriteAsync("Access denied.");
			return;
		}

		await next(context);
	}
}