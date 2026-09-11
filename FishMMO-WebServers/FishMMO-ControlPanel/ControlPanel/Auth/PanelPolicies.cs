using FishMMO.Auth.Core;

namespace FishMMO.ControlPanel.Auth
{
	/// <summary>
	/// The authorization policies every Control Panel action carries.
	/// </summary>
	/// <remarks>
	/// These are policies over the existing four <see cref="AccessLevel"/> values plus a step-up
	/// window, deliberately not a fifth access level: the enum is duplicated between
	/// <c>FishMMO.Auth.Core</c> and the database layer and is baked into the wire format and the
	/// in-game commands, so the panel expresses finer distinctions without touching it.
	/// </remarks>
	public static class PanelPolicies
	{
		/// <summary>Authenticated, acting on their own account.</summary>
		public const string Self = "Self";

		/// <summary>Game master or above, with two-factor enrolled and satisfied.</summary>
		public const string Support = "Support";

		/// <summary>
		/// Game master or above, plus a two-factor code proven within the step-up window.
		/// </summary>
		/// <remarks>
		/// Moderation sits here rather than at <see cref="Operator"/>: banning an account is a
		/// game master's daily work, and requiring an administrator for it means the person
		/// handling the report cannot act. The step-up is what makes that safe — a borrowed
		/// unlocked session cannot ban anybody without the authenticator.
		/// </remarks>
		public const string SupportStepUp = "Support.StepUp";

		/// <summary>Administrator, with two-factor enrolled and satisfied.</summary>
		public const string Operator = "Operator";

		/// <summary>Administrator, plus a two-factor code proven within the step-up window.</summary>
		public const string OperatorStepUp = "Operator.StepUp";

		/// <summary>
		/// A session that has proven a password but not yet a second factor. It can reach the
		/// two-factor endpoints and nothing else.
		/// </summary>
		public const string TwoFactorPending = "TwoFactorPending";

		/// <summary>How long a step-up remains valid.</summary>
		public static readonly System.TimeSpan StepUpWindow = System.TimeSpan.FromMinutes(5);
	}

	/// <summary>Claim types the panel's authentication handler issues.</summary>
	public static class PanelClaims
	{
		/// <summary>The account's access level, as the numeric enum value.</summary>
		public const string AccessLevel = "fishmmo:access_level";

		/// <summary>Whether two-factor has been satisfied on this session ("true"/"false").</summary>
		public const string TwoFactorSatisfied = "fishmmo:2fa";

		/// <summary>Whether the account has TOTP enrolled at all ("true"/"false").</summary>
		public const string TotpEnrolled = "fishmmo:totp_enrolled";

		/// <summary>Round-trip ISO-8601 of the last step-up, when there has been one.</summary>
		public const string LastStepUp = "fishmmo:step_up";

		/// <summary>SHA-256 of the session identifier, so handlers can act on the row.</summary>
		public const string SessionHash = "fishmmo:session";

		/// <summary>
		/// The session row's surrogate id. Lets a handler say which entry in a session list is
		/// the caller's own without returning a hash to the browser.
		/// </summary>
		public const string SessionId = "fishmmo:session_id";
	}
}
