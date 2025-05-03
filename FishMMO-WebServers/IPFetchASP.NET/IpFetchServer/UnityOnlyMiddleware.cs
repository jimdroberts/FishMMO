public class UnityOnlyMiddleware
{
	private readonly RequestDelegate next;
	private readonly ILogger<UnityOnlyMiddleware> logger;

	public UnityOnlyMiddleware(RequestDelegate next, ILogger<UnityOnlyMiddleware> logger)
	{
		this.next = next;
		this.logger = logger;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var userAgent = context.Request.Headers["X-FishMMO"].ToString();

		logger.LogWarning($"UserAgent: {userAgent}");

		if (!userAgent.Equals("Client"))
		{
			logger.LogWarning($"Rejected Non-FishMMO Client");
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			await context.Response.WriteAsync("Access denied.");
			return;
		}

		await next(context); // Pass control to the next middleware
	}
}