using System.Security.Claims;
using System.Text.Encodings.Web;
using FishMMO.Auth.Core;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FishMMO.ControlPanel.Auth
{
	/// <summary>
	/// Turns the panel's session cookie into a <see cref="ClaimsPrincipal"/>.
	/// </summary>
	/// <remarks>
	/// The access level is re-read from the account on every request rather than trusted from the
	/// session row. A demotion, a ban, or a level change must take effect on the operator's very
	/// next click, not when their session happens to expire.
	/// </remarks>
	public sealed class PanelAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
	{
		/// <summary>Scheme name, referenced by the authorization policies.</summary>
		public const string SchemeName = "FishMMOPanel";

		private readonly PanelSessionManager sessionManager;
		private readonly IAccountService accounts;

		public PanelAuthenticationHandler(
			IOptionsMonitor<AuthenticationSchemeOptions> options,
			ILoggerFactory logger,
			UrlEncoder encoder,
			PanelSessionManager sessionManager,
			IAccountService accounts)
			: base(options, logger, encoder)
		{
			this.sessionManager = sessionManager;
			this.accounts = accounts;
		}

		/// <inheritdoc/>
		protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Cookies.TryGetValue(PanelSessionManager.CookieName, out string sessionId) ||
				string.IsNullOrWhiteSpace(sessionId))
			{
				return AuthenticateResult.NoResult();
			}

			var (ok, session, hash) = await sessionManager.ValidateAsync(sessionId, Context.RequestAborted);
			if (!ok)
			{
				// Clear a cookie we have just refused, so the browser stops sending it.
				Response.Cookies.Delete(PanelSessionManager.CookieName);
				return AuthenticateResult.NoResult();
			}

			// Re-read the live account: the session row records the level at issue, which is a
			// record of the past, not an authorization decision.
			var accountResult = await accounts.FetchForLoginAsync(session.AccountName, false, Context.RequestAborted);
			if (!accountResult.IsSuccess)
			{
				// FetchForLoginAsync fails for a banned account as well as a missing one, and
				// deliberately does not distinguish them. Either way the session is over.
				await sessionManager.RevokeAsync(hash, Context.RequestAborted);
				Response.Cookies.Delete(PanelSessionManager.CookieName);
				return AuthenticateResult.NoResult();
			}

			byte liveLevel = accountResult.Data.AccessLevel;
			if (liveLevel == (byte)AccessLevel.Banned)
			{
				await sessionManager.RevokeAsync(hash, Context.RequestAborted);
				Response.Cookies.Delete(PanelSessionManager.CookieName);
				return AuthenticateResult.NoResult();
			}

			// A level change invalidates the session rather than silently re-scoping it. Rotating
			// identity mid-session is how privilege confusion bugs happen.
			if (liveLevel != session.AccessLevelAtIssue)
			{
				await sessionManager.RevokeAsync(hash, Context.RequestAborted);
				Response.Cookies.Delete(PanelSessionManager.CookieName);
				return AuthenticateResult.NoResult();
			}

			await sessionManager.TouchAsync(hash, Context.RequestAborted);

			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.Name, session.AccountName),
				new Claim(PanelClaims.AccessLevel, liveLevel.ToString()),
				new Claim(PanelClaims.TwoFactorSatisfied, session.TwoFactorSatisfied ? "true" : "false"),
				new Claim(PanelClaims.TotpEnrolled, accountResult.Data.TotpEnabled ? "true" : "false"),
				new Claim(PanelClaims.SessionHash, hash),
				new Claim(PanelClaims.SessionId, session.ID.ToString()),
			};
			if (session.LastStepUpUtc.HasValue)
			{
				claims.Add(new Claim(PanelClaims.LastStepUp, session.LastStepUpUtc.Value.ToString("o")));
			}

			var identity = new ClaimsIdentity(claims, SchemeName);
			var principal = new ClaimsPrincipal(identity);
			return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
		}

		/// <inheritdoc/>
		protected override Task HandleChallengeAsync(AuthenticationProperties properties)
		{
			// An API, not a login redirect: the single-page app decides what to show.
			Response.StatusCode = StatusCodes.Status401Unauthorized;
			return Task.CompletedTask;
		}

		/// <inheritdoc/>
		protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
		{
			Response.StatusCode = StatusCodes.Status403Forbidden;
			return Task.CompletedTask;
		}
	}
}
