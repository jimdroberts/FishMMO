using System.Security.Claims;
using FishMMO.Auth.Core;
using Microsoft.AspNetCore.Authorization;

namespace FishMMO.ControlPanel
{
	/// <summary>
	/// Requires a minimum <see cref="AccessLevel"/>, and optionally that two-factor has been
	/// satisfied on the session and that a code was proven within the step-up window.
	/// </summary>
	public sealed class AccessLevelRequirement : IAuthorizationRequirement
	{
		/// <summary>Minimum access level.</summary>
		public byte Minimum { get; }

		/// <summary>Whether the session must have satisfied two-factor.</summary>
		public bool RequireTwoFactor { get; }

		/// <summary>Whether a code must have been proven within the step-up window.</summary>
		public bool RequireStepUp { get; }

		/// <summary>Creates a requirement.</summary>
		public AccessLevelRequirement(byte minimum, bool requireTwoFactor, bool requireStepUp)
		{
			Minimum = minimum;
			RequireTwoFactor = requireTwoFactor;
			RequireStepUp = requireStepUp;
		}
	}

	/// <summary>
	/// Evaluates <see cref="AccessLevelRequirement"/> against the claims the panel's
	/// authentication handler issues.
	/// </summary>
	public sealed class AccessLevelHandler : AuthorizationHandler<AccessLevelRequirement>
	{
		private readonly IHttpContextAccessor accessor;

		public AccessLevelHandler(IHttpContextAccessor accessor)
		{
			this.accessor = accessor;
		}

		/// <inheritdoc/>
		protected override Task HandleRequirementAsync(
			AuthorizationHandlerContext context,
			AccessLevelRequirement requirement)
		{
			if (context.User?.Identity?.IsAuthenticated != true)
			{
				return Task.CompletedTask;
			}

			if (!byte.TryParse(context.User.FindFirstValue(Auth.PanelClaims.AccessLevel), out byte level) ||
				level < requirement.Minimum)
			{
				return Task.CompletedTask;
			}

			if (requirement.RequireTwoFactor &&
				context.User.FindFirstValue(Auth.PanelClaims.TwoFactorSatisfied) != "true")
			{
				return Task.CompletedTask;
			}

			/* Two-factor is mandatory above Player. An elevated account that never enrolled
			 * cannot reach an elevated policy however its session was issued — the panel tells
			 * it so through api/auth/session rather than silently hiding half the interface. */
			if (requirement.Minimum >= (byte)AccessLevel.GameMaster &&
				context.User.FindFirstValue(Auth.PanelClaims.TotpEnrolled) != "true")
			{
				return Task.CompletedTask;
			}

			if (requirement.RequireStepUp)
			{
				string raw = context.User.FindFirstValue(Auth.PanelClaims.LastStepUp);
				if (!DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime lastStepUp) ||
					DateTime.UtcNow - lastStepUp > Auth.PanelPolicies.StepUpWindow)
				{
					/* 428 rather than 403, so the single-page app knows to prompt for a code and
					 * retry the action instead of showing a dead end. The client's withStepUp
					 * wrapper keys on exactly this status. */
					if (accessor?.HttpContext != null)
					{
						accessor.HttpContext.Items["fishmmo:needs-step-up"] = true;
					}
					return Task.CompletedTask;
				}
			}

			context.Succeed(requirement);
			return Task.CompletedTask;
		}
	}

}
