using System.Reflection;
using FishMMO.ControlPanel.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Wraps authorization: turns a step-up refusal into a 428, and records refusals the
	/// action filter never sees.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>It must be registered before <c>UseAuthorization</c>.</b> Authorization short-circuits
	/// on failure and never calls its <c>next</c>, so anything registered downstream of it is
	/// simply not entered when it refuses. This middleware used to sit after it, which meant the
	/// 403-to-428 rewrite below never ran: an operator whose step-up window had lapsed got a
	/// dead-end "Forbidden" on every moderation action instead of being asked for a code, since
	/// the browser keys its step-up prompt on the 428.
	/// </para>
	/// <para>
	/// The second job exists because <see cref="AuditActionFilter"/> is an action filter, and an
	/// action filter does not run when authorization refuses the request — so a refusal decided
	/// by policy left no trace at all. That is the gap this closes: an account probing endpoints
	/// it may not reach is precisely the pattern the audit log exists to make visible, and it is
	/// already what the in-game command gate records.
	/// </para>
	/// <para>
	/// Writes only, matching the rule elsewhere that reads are not recorded. A refused GET is a
	/// failed look, it is frequent, and recording it would bury the refusals that matter.
	/// </para>
	/// </remarks>
	public sealed class PrivilegedRequestMiddleware
	{
		/// <summary>Marks a request whose action ran, so it is not recorded twice.</summary>
		public const string HandledKey = "fishmmo:audit-handled";

		/// <summary>The marker the access-level handler leaves when only a step-up was missing.</summary>
		private const string NeedsStepUpKey = "fishmmo:needs-step-up";

		private readonly RequestDelegate next;

		public PrivilegedRequestMiddleware(RequestDelegate next) => this.next = next;

		/// <summary>Runs the rest of the pipeline, then inspects how it ended.</summary>
		public async Task InvokeAsync(HttpContext context, AuditWriter audit)
		{
			await next(context);

			if (context.Response.StatusCode == StatusCodes.Status403Forbidden &&
				context.Items.TryGetValue(NeedsStepUpKey, out object flag) &&
				flag is true &&
				!context.Response.HasStarted)
			{
				context.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
			}

			await RecordAuthorizationRefusalAsync(context, audit);
		}

		/// <summary>
		/// Records a privileged write that authorization refused before the action could run.
		/// </summary>
		private static async Task RecordAuthorizationRefusalAsync(HttpContext context, AuditWriter audit)
		{
			// The action ran, so the filter already recorded it.
			if (context.Items.ContainsKey(HandledKey))
			{
				return;
			}

			int status = context.Response.StatusCode;
			if (status != StatusCodes.Status401Unauthorized &&
				status != StatusCodes.Status403Forbidden &&
				status != StatusCodes.Status428PreconditionRequired)
			{
				return;
			}

			string method = context.Request.Method;
			if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
			{
				return;
			}

			var descriptor = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
			if (descriptor == null || !IsPrivileged(descriptor))
			{
				return;
			}

			/* An unauthenticated 401 names no actor, and recording a row per anonymous POST
			 * would let anybody fill the table from outside. Those are the rate limiter's and
			 * the web server's business; this log is about accounts. */
			if (context.User?.Identity?.IsAuthenticated != true)
			{
				return;
			}

			string action = descriptor.MethodInfo.GetCustomAttribute<AuditedAttribute>()?.Action
				?? $"{descriptor.ControllerName.ToLowerInvariant()}.{descriptor.ActionName.ToLowerInvariant()}";

			string target = null;
			foreach (string key in new[] { "id", "username", "name" })
			{
				if (context.Request.RouteValues.TryGetValue(key, out object value) && value != null)
				{
					target = value.ToString();
					break;
				}
			}

			await audit.FailedAsync(
				action,
				descriptor.MethodInfo.GetCustomAttribute<AuditedAttribute>()?.TargetType,
				target,
				null,
				// No reason: the body was never bound, because the action never ran.
				null,
				status == StatusCodes.Status428PreconditionRequired
					? "Refused before the action: a two-factor step-up was required."
					: "Refused before the action: not permitted at this access level.",
				new { status });
		}

		private static bool IsPrivileged(ControllerActionDescriptor descriptor)
		{
			if (descriptor.MethodInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null ||
				descriptor.ControllerTypeInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null)
			{
				return false;
			}

			var policies = descriptor.MethodInfo.GetCustomAttributes<AuthorizeAttribute>()
				.Concat(descriptor.ControllerTypeInfo.GetCustomAttributes<AuthorizeAttribute>())
				.Select(a => a.Policy)
				.Where(p => p != null)
				.ToList();

			if (policies.Any(p => p == PanelPolicies.Support || p == PanelPolicies.SupportStepUp ||
								  p == PanelPolicies.Operator || p == PanelPolicies.OperatorStepUp))
			{
				return true;
			}
			return !policies.Any(p => p == PanelPolicies.Self || p == PanelPolicies.TwoFactorPending);
		}
	}
}
