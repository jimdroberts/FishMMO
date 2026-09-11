using System.Reflection;
using System.Text;
using FishMMO.ControlPanel.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Checks at startup that every privileged write endpoint declares what it records.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="AuditActionFilter"/> already guarantees that a privileged write is recorded
	/// even with no attribute on it, so nothing goes unlogged. This check is about the quality
	/// of that record, and about when the mistake is found: a derived action name changes the
	/// day somebody renames a controller method, which silently breaks every saved query and
	/// every retention rule written against it.
	/// </para>
	/// <para>
	/// It runs once, over the endpoint table, at boot. Outside production it <b>throws</b>, so
	/// the omission is a failed first run rather than a hole discovered months later. In
	/// production it logs an error and starts anyway — refusing to serve a shard because an
	/// endpoint has a weak audit name would be a worse outage than the problem.
	/// </para>
	/// </remarks>
	public static class AuditCoverage
	{
		/// <summary>Runs the check, throwing outside production when an endpoint is undeclared.</summary>
		public static void Verify(IServiceProvider services, IWebHostEnvironment environment, ILogger logger)
		{
			var provider = services.GetRequiredService<IActionDescriptorCollectionProvider>();
			var known = KnownActions();
			var undeclared = new List<string>();
			var unknown = new List<string>();

			foreach (var descriptor in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
			{
				if (!IsPrivilegedWrite(descriptor))
				{
					continue;
				}
				var audited = descriptor.MethodInfo.GetCustomAttribute<AuditedAttribute>();
				if (audited == null)
				{
					undeclared.Add($"{descriptor.ControllerName}.{descriptor.ActionName}");
					continue;
				}
				if (!known.Contains(audited.Action))
				{
					unknown.Add($"{descriptor.ControllerName}.{descriptor.ActionName} -> '{audited.Action}'");
				}
			}

			if (undeclared.Count == 0 && unknown.Count == 0)
			{
				logger.LogInformation("Audit coverage: every privileged write endpoint declares an audit action.");
				return;
			}

			var message = new StringBuilder();
			if (undeclared.Count > 0)
			{
				message.AppendLine($"{undeclared.Count} privileged write endpoint(s) have no [Audited] attribute:");
				foreach (string name in undeclared)
				{
					message.AppendLine($"  - {name}");
				}
				message.AppendLine("Add [Audited(AuditActions.<Name>, TargetType = \"...\")] to each.");
			}
			if (unknown.Count > 0)
			{
				message.AppendLine($"{unknown.Count} endpoint(s) audit under an action that is not declared in AuditActions:");
				foreach (string name in unknown)
				{
					message.AppendLine($"  - {name}");
				}
				message.AppendLine("Declare a constant for it. An undeclared name is missing from the log's own");
				message.AppendLine("filter, so those rows are written and then never seen by anyone looking.");
			}
			message.Append("Every Game Master and Admin action must be recorded under a stable action name.");

			if (environment.IsProduction())
			{
				logger.LogError("{Message}", message.ToString());
				return;
			}
			throw new InvalidOperationException(message.ToString());
		}

		/// <summary>
		/// Every action name declared in <see cref="AuditActions"/>, read off the class itself
		/// so that adding a constant is the only step needed to make a name legitimate.
		/// </summary>
		/// <remarks>
		/// The attribute takes a string, so nothing stops an endpoint auditing under a name
		/// that exists nowhere else. Such a row is written correctly and is then invisible:
		/// the log's action filter is built from these constants, so an operator looking for
		/// what was done never sees it. That is the failure this catches.
		/// </remarks>
		private static HashSet<string> KnownActions()
		{
			return typeof(AuditActions)
				.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
				.Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
				.Select(f => (string)f.GetRawConstantValue())
				.ToHashSet(StringComparer.Ordinal);
		}

		/// <summary>
		/// The same test the filter applies, deliberately duplicated in shape but kept here
		/// because this one runs over descriptors rather than over a live request.
		/// </summary>
		private static bool IsPrivilegedWrite(ControllerActionDescriptor descriptor)
		{
			bool isWrite = descriptor.EndpointMetadata
				.OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
				.SelectMany(m => m.HttpMethods)
				.Any(m => !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m) && !HttpMethods.IsOptions(m));

			if (!isWrite)
			{
				return false;
			}

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

			// No policy at all falls to the Admin fallback, which is privileged.
			return !policies.Any(p => p == PanelPolicies.Self || p == PanelPolicies.TwoFactorPending);
		}
	}
}
