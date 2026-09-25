using System.Security.Claims;
using System.Text.Encodings.Web;
using FishMMO.Auth.Core;
using FishMMO.Database;
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

		/// <summary>
		/// Marks a request whose session could not be checked because the database did not answer.
		/// </summary>
		/// <remarks>
		/// Such a request is unauthenticated for this request only: its cookie is kept and its session
		/// row untouched. The challenge answers 503 instead of 401 when this is set, so the page says
		/// "try again" rather than "sign in", and <c>api/auth/session</c> does the same.
		/// </remarks>
		public const string UnavailableKey = "fishmmo:auth-unavailable";

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

			var (validity, session, hash) = await sessionManager.ValidateAsync(sessionId, Context.RequestAborted);
			if (validity == PanelSessionManager.Validity.Unavailable)
			{
				return Unavailable("the session could not be read");
			}
			if (validity != PanelSessionManager.Validity.Valid)
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
				/* FetchForLoginAsync fails for a banned account (FORBIDDEN) as well as a missing one
				 * (NOT_FOUND), and deliberately does not distinguish them. Either way the session is
				 * over. Any OTHER failure is the database not answering, which says nothing about the
				 * account: the request is refused, fail-closed, but the session is left alone. Revoking
				 * it on a database blip signed every operator out at their next click, during exactly
				 * the kind of incident they had the panel open for. */
				if (accountResult.ErrorCode is DatabaseErrorCodes.NotFound or DatabaseErrorCodes.Forbidden or DatabaseErrorCodes.ValidationError)
				{
					await sessionManager.RevokeAsync(hash, Context.RequestAborted);
					Response.Cookies.Delete(PanelSessionManager.CookieName);
					return AuthenticateResult.NoResult();
				}
				return Unavailable($"the account could not be read [{accountResult.ErrorCode}] {accountResult.ErrorMessage}");
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

		/// <summary>Refuses this request without touching its session or its cookie. See <see cref="UnavailableKey"/>.</summary>
		private AuthenticateResult Unavailable(string why)
		{
			// A browser that went away mid-request cancels the read too; that is not worth a line.
			if (!Context.RequestAborted.IsCancellationRequested)
			{
				Logger.LogWarning("Panel request could not be authenticated because {Why}; its session was left intact.", why);
			}
			Context.Items[UnavailableKey] = true;
			return AuthenticateResult.NoResult();
		}

		/// <inheritdoc/>
		protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
		{
			if (Context.Items.ContainsKey(UnavailableKey))
			{
				/* Not 401: nothing is wrong with the session, and a 401 sends the page to the sign-in
				 * form. The same body shape as every other refusal the panel writes. */
				Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				Response.ContentType = "application/json";
				await Response.WriteAsync("{\"error\":\"Try again shortly.\"}");
				return;
			}

			// An API, not a login redirect: the single-page app decides what to show.
			Response.StatusCode = StatusCodes.Status401Unauthorized;
		}

		/// <inheritdoc/>
		protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
		{
			Response.StatusCode = StatusCodes.Status403Forbidden;
			return Task.CompletedTask;
		}
	}
}
