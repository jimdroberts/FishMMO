using System.Reflection;
using FishMMO.ControlPanel.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Records every privileged request — reads as well as writes — whether or not its author
	/// remembered to.
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
	/// authorization policy whether the action is privileged, and writes a row if it is. An
	/// endpoint added tomorrow with no audit code in it is still recorded.
	/// </para>
	/// <para>
	/// <b>Reads are recorded too (2026-09-14).</b> Every Game Master and Admin action is tracked,
	/// with no exception, and looking at a player's account, tickets or chat is an action. A read
	/// with no <see cref="AuditedAttribute"/> is recorded under <c>view.&lt;controller&gt;.&lt;action&gt;</c>
	/// with its query string as the details, so what was searched for is part of the record.
	/// Player self-service and anonymous endpoints are still not recorded: those are not staff.
	/// </para>
	/// <para>
	/// It records refusals as well as successes, including the request that never reached the
	/// action because model binding rejected it — an operator repeatedly failing to reach
	/// something is the more interesting record.
	/// </para>
	/// <para>
	/// <b>Automatic refreshes are not recorded (2026-09-14, the owner's decision "skip
	/// auto-refreshes").</b> The first load of a page and every refresh somebody asked for — a
	/// Refresh button, a filter change, a page click, the re-read after an action — are recorded.
	/// A page re-reading itself on a timer is not: the browser marks those requests with
	/// <c>X-Panel-Refresh: auto</c> (<see cref="RefreshHeader"/>), and a GET or HEAD carrying it is
	/// skipped here unless it was refused. A board polled every five seconds would otherwise bury
	/// every row that matters.
	/// </para>
	/// <para>
	/// <b>The header is trusted for reads only.</b> A write is recorded whatever it carries; nothing
	/// a client sends can make one skippable. For a read, the browser already decides whether to
	/// issue the request at all — the mark hides nothing the shipped page does not leave out by
	/// simply not polling — and the shipped page marks only its timers, so every page an operator
	/// opens still leaves its row. A refused read is recorded even when marked (here, and by
	/// <see cref="PrivilegedRequestMiddleware"/> for a refusal decided by policy): a refused poll is
	/// a probe, and refusals are rare. What the mark cannot defend against is an operator who edits
	/// the page's script to mark every read; those reads would go unrecorded. That is the cost of a
	/// client-side mark, accepted with the decision. Closing it would need a server-side rule, such
	/// as skipping only a repeat of a read the same session recorded recently.
	/// </para>
	/// </remarks>
	public sealed class AuditActionFilter : IAsyncActionFilter
	{
		/// <summary>The request header the panel's browser code sets on a timer-driven reload.</summary>
		public const string RefreshHeader = "X-Panel-Refresh";

		/// <summary>The one value of <see cref="RefreshHeader"/> that marks an automatic refresh.</summary>
		public const string AutomaticRefresh = "auto";

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

			if (!IsPrivilegedRequest(context, descriptor))
			{
				await next();
				return;
			}

			bool isRead = IsRead(context.HttpContext.Request.Method);

			/* No attribute is still recorded, with an action name derived from the route. The
			 * rule is that the action appears in the log; the attribute only makes the name
			 * stable. AuditCoverage refuses to start in development when this happens, so the
			 * derived name should never be seen in practice — if it is, something shipped
			 * without being run. */
			if (audited == null && !isRead)
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
			if (isRead && scope.Details == null && context.HttpContext.Request.QueryString.HasValue)
			{
				scope.Details = new { query = context.HttpContext.Request.QueryString.Value };
			}

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

			/* A background poll the browser marked as one is not recorded — unless it was refused,
			 * which is always a row. Only a GET or HEAD can be marked; see IsAutomaticRefresh. The
			 * handled marker above is already set, so the middleware does not record it either. */
			if (IsAutomaticRefresh(context.HttpContext.Request) && !IsRefusal(status))
			{
				return;
			}

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
		/// Whether this endpoint is privileged, and so must be recorded.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Read off the endpoint rather than configured in a list. A list would be a second place
		/// to remember, which is the problem being solved.
		/// </para>
		/// <para>
		/// Every method except OPTIONS, which is the browser asking what it may send and is not
		/// anybody acting. The policy test excludes <see cref="PanelPolicies.Self"/> and anonymous
		/// endpoints: a player changing their own password is not an operator action, and putting
		/// it here would mix the two populations in one log.
		/// </para>
		/// </remarks>
		private static bool IsPrivilegedRequest(ActionExecutingContext context, ControllerActionDescriptor descriptor)
		{
			if (HttpMethods.IsOptions(context.HttpContext.Request.Method))
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

		/// <summary>Whether a request method only looks.</summary>
		internal static bool IsRead(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

		/// <summary>
		/// Whether a request is a read the browser marked as a background poll.
		/// </summary>
		/// <remarks>
		/// Never true for any method but GET or HEAD, whatever the header says: a write is never an
		/// automatic refresh and is never skipped. Exactly one header value, <c>auto</c>; anything
		/// else, repeated headers included, is an ordinary request and is recorded.
		/// </remarks>
		internal static bool IsAutomaticRefresh(HttpRequest request)
		{
			if (request == null || !IsRead(request.Method))
			{
				return false;
			}
			var values = request.Headers[RefreshHeader];
			return values.Count == 1 && string.Equals(values[0], AutomaticRefresh, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Whether a status is a refusal: not authenticated, not permitted, or a step-up owed.</summary>
		internal static bool IsRefusal(int status) =>
			status == StatusCodes.Status401Unauthorized ||
			status == StatusCodes.Status403Forbidden ||
			status == StatusCodes.Status428PreconditionRequired;

		/// <summary>
		/// The name used when the attribute is missing: the normal case for a read, a defect for a write.
		/// </summary>
		private static string DeriveAction(ActionExecutingContext context, ControllerActionDescriptor descriptor)
		{
			return DeriveAction(context.HttpContext.Request.Method, descriptor);
		}

		/// <summary>The derived action name for a method and endpoint. Shared with <see cref="PrivilegedRequestMiddleware"/>.</summary>
		internal static string DeriveAction(string method, ControllerActionDescriptor descriptor)
		{
			string controller = descriptor?.ControllerName?.ToLowerInvariant() ?? "unknown";
			string action = descriptor?.ActionName?.ToLowerInvariant() ?? method.ToLowerInvariant();
			return IsRead(method) ? $"view.{controller}.{action}" : $"{controller}.{action}";
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
