using System.Reflection;
using FishMMO.ControlPanel.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Records every privileged write, whether or not its author remembered to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every Game Master and Admin action must appear in the audit log. The in-game commands get
	/// that for free because they are audited at the single access gate where a command cannot
	/// be registered without declaring a level. HTTP endpoints had no equivalent: coverage
	/// depended on each author adding a call, which is exactly the kind of discipline that holds
	/// until the day somebody is in a hurry.
	/// </para>
	/// <para>
	/// This filter is that equivalent. It runs on every action, decides from the endpoint's own
	/// authorization policy and HTTP method whether the action is a privileged write, and writes
	/// a row if it is. An endpoint added tomorrow with no audit code in it is still recorded.
	/// </para>
	/// <para>
	/// It records refusals as well as successes, including the request that never reached the
	/// action because model binding rejected it — an operator repeatedly failing to reach
	/// something is the more interesting record.
	/// </para>
	/// </remarks>
	public sealed class AuditActionFilter : IAsyncActionFilter
	{
		private readonly AuditScope scope;
		private readonly AuditWriter writer;
		private readonly ILogger<AuditActionFilter> log;

		public AuditActionFilter(AuditScope scope, AuditWriter writer, ILogger<AuditActionFilter> log)
		{
			this.scope = scope;
			this.writer = writer;
			this.log = log;
		}

		/// <inheritdoc/>
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
			var audited = descriptor?.MethodInfo.GetCustomAttribute<AuditedAttribute>();

			if (!IsPrivilegedWrite(context, descriptor))
			{
				await next();
				return;
			}

			/* No attribute is still recorded, with an action name derived from the route. The
			 * rule is that the action appears in the log; the attribute only makes the name
			 * stable. AuditCoverage refuses to start in development when this happens, so the
			 * derived name should never be seen in practice — if it is, something shipped
			 * without being run. */
			if (audited == null)
			{
				log.LogError(
					"Endpoint '{Endpoint}' is a privileged write with no [Audited] attribute. " +
					"It is being recorded under a derived name; add the attribute.",
					descriptor?.DisplayName);
			}

			scope.Action ??= audited?.Action ?? DeriveAction(context, descriptor);
			scope.TargetType ??= audited?.TargetType;
			scope.TargetID ??= ResolveTarget(context, audited);
			scope.Reason ??= ResolveReason(context);

			ActionExecutedContext executed = await next();

			/* Tells PrivilegedRequestMiddleware that the action ran and this request is already
			 * accounted for. Without it, an action that itself returns 403 would be recorded
			 * twice — once here and once by the middleware watching the status code. */
			context.HttpContext.Items[PrivilegedRequestMiddleware.HandledKey] = true;

			if (!scope.Record)
			{
				return;
			}

			int status = executed.Result is Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult coded && coded.StatusCode.HasValue
				? coded.StatusCode.Value
				: context.HttpContext.Response.StatusCode;

			// An unhandled exception is a failure even though no status code was set yet, and it
			// is the case most worth having in the log.
			bool succeeded = executed.Exception == null && status >= 200 && status < 300;
			string outcome = scope.Outcome ?? DescribeOutcome(executed, status, succeeded);

			if (succeeded)
			{
				await writer.SucceededAsync(scope.Action, scope.TargetType, scope.TargetID, scope.TargetName, scope.Reason, scope.Details);
			}
			else
			{
				await writer.FailedAsync(scope.Action, scope.TargetType, scope.TargetID, scope.TargetName, scope.Reason, outcome, scope.Details);
			}
		}

		/// <summary>
		/// Whether this endpoint is a privileged write, and so must be recorded.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two tests, both read off the endpoint rather than configured in a list. A list would
		/// be a second place to remember, which is the problem being solved.
		/// </para>
		/// <para>
		/// The method test excludes GET and HEAD: a read changes nothing, and recording reads
		/// would fill the table with the act of looking at it. The policy test excludes
		/// <see cref="PanelPolicies.Self"/> and anonymous endpoints: a player changing their own
		/// password is not an operator action, and putting it here would mix the two populations
		/// in one log.
		/// </para>
		/// </remarks>
		private static bool IsPrivilegedWrite(ActionExecutingContext context, ControllerActionDescriptor descriptor)
		{
			string method = context.HttpContext.Request.Method;
			if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
			{
				return false;
			}
			if (descriptor == null)
			{
				return false;
			}

			// AllowAnonymous wins over everything, so registration and sign-in are not operator
			// actions however their controller is decorated.
			if (descriptor.MethodInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null ||
				descriptor.ControllerTypeInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null)
			{
				return false;
			}

			foreach (string policy in Policies(descriptor))
			{
				if (policy == PanelPolicies.Support ||
					policy == PanelPolicies.SupportStepUp ||
					policy == PanelPolicies.Operator ||
					policy == PanelPolicies.OperatorStepUp)
				{
					return true;
				}
			}

			/* An endpoint with no explicit policy falls to the fallback policy, which requires
			 * Admin with two-factor. That is a privileged write by definition, so it is recorded
			 * — a controller that forgot its [Authorize] is still an operator surface. */
			return !HasAnyPolicy(descriptor);
		}

		private static IEnumerable<string> Policies(ControllerActionDescriptor descriptor)
		{
			foreach (var attribute in descriptor.MethodInfo.GetCustomAttributes<AuthorizeAttribute>())
			{
				if (attribute.Policy != null) yield return attribute.Policy;
			}
			foreach (var attribute in descriptor.ControllerTypeInfo.GetCustomAttributes<AuthorizeAttribute>())
			{
				if (attribute.Policy != null) yield return attribute.Policy;
			}
		}

		private static bool HasAnyPolicy(ControllerActionDescriptor descriptor)
		{
			foreach (string policy in Policies(descriptor))
			{
				// Self is a policy, and a Self-scoped write is a player acting on themselves.
				if (policy == PanelPolicies.Self || policy == PanelPolicies.TwoFactorPending)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A fallback name, used only when the attribute is missing.</summary>
		private static string DeriveAction(ActionExecutingContext context, ControllerActionDescriptor descriptor)
		{
			string controller = descriptor?.ControllerName?.ToLowerInvariant() ?? "unknown";
			string action = descriptor?.ActionName?.ToLowerInvariant() ?? context.HttpContext.Request.Method.ToLowerInvariant();
			return $"{controller}.{action}";
		}

		/// <summary>The target, from the route value the attribute names, or the first plausible one.</summary>
		private static string ResolveTarget(ActionExecutingContext context, AuditedAttribute audited)
		{
			var route = context.RouteData.Values;

			if (audited?.TargetRouteValue != null &&
				route.TryGetValue(audited.TargetRouteValue, out object named) && named != null)
			{
				return named.ToString();
			}

			// The two conventions this API uses for a target in the path.
			foreach (string key in new[] { "id", "username", "name" })
			{
				if (route.TryGetValue(key, out object value) && value != null)
				{
					return value.ToString();
				}
			}
			return null;
		}

		/// <summary>
		/// The reason, read off whichever bound argument carries one.
		/// </summary>
		/// <remarks>
		/// By convention rather than by interface: every request model in this API that needs a
		/// reason names the property <c>Reason</c>, and requiring them all to implement an
		/// interface would be one more thing to remember for no gain. A model that carries no
		/// reason simply records none.
		/// </remarks>
		private static string ResolveReason(ActionExecutingContext context)
		{
			foreach (object argument in context.ActionArguments.Values)
			{
				if (argument == null)
				{
					continue;
				}
				if (argument is string)
				{
					continue;
				}
				PropertyInfo property = argument.GetType().GetProperty("Reason", BindingFlags.Public | BindingFlags.Instance);
				if (property != null && property.PropertyType == typeof(string))
				{
					string value = property.GetValue(argument) as string;
					if (!string.IsNullOrWhiteSpace(value))
					{
						return value;
					}
				}
			}
			return null;
		}

		private static string DescribeOutcome(ActionExecutedContext executed, int status, bool succeeded)
		{
			if (succeeded)
			{
				return null;
			}
			if (executed.Exception != null)
			{
				// The type, not the message: a message can carry data the log should not hold,
				// and the stack trace is already in the application log.
				return $"Unhandled {executed.Exception.GetType().Name}.";
			}
			return status switch
			{
				400 => "Refused: the request was rejected.",
				401 => "Refused: not authenticated.",
				403 => "Refused: not permitted.",
				404 => "Refused: no such target.",
				409 => "Refused: conflict.",
				428 => "Refused: a two-factor step-up was required.",
				429 => "Refused: rate limited.",
				_ => $"Refused: status {status}.",
			};
		}
	}
}
