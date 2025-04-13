using System.Text.RegularExpressions;

public class UnityOnlyMiddleware
{
	private readonly RequestDelegate next;
	private readonly ILogger<UnityOnlyMiddleware> logger;
	private static readonly Regex UnityRegex = new Regex(@"UnityPlayer/\d+\.\d+\.\d+.*\(UnityWebRequest/\d+\.\d+.*\)", RegexOptions.Compiled);

	public UnityOnlyMiddleware(RequestDelegate next, ILogger<UnityOnlyMiddleware> logger)
	{
		this.next = next;
		this.logger = logger;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var userAgent = context.Request.Headers["User-Agent"].ToString();

		if (!UnityRegex.IsMatch(userAgent))
		{
			logger.LogWarning($"Rejected non-Unity request: {userAgent}");
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			await context.Response.WriteAsync("Access denied.");
			return;
		}

		await next(context); // Pass control to the next middleware
	}
}