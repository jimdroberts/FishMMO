using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using FishMMO.Logging;

public class UnityOnlyMiddleware
{
	private readonly RequestDelegate next;

	public UnityOnlyMiddleware(RequestDelegate next)
	{
		this.next = next;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var userAgent = context.Request.Headers["X-FishMMO"].ToString();

		// Directly use your custom logger
		await Log.Info("UnityOnlyMiddleware", $"UserAgent: {userAgent}"); //

		if (!userAgent.Equals("Client"))
		{
			await Log.Warning("UnityOnlyMiddleware", $"Rejected Non-FishMMO Client"); //
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			await context.Response.WriteAsync("Access denied.");
			return;
		}

		await next(context);
	}
}