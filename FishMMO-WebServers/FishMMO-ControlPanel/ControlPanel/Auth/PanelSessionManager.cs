using System.Security.Cryptography;
using FishMMO.Auth.Core;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Auth
{
	/// <summary>
	/// Issues, validates and revokes Control Panel browser sessions.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The browser holds an opaque 32-byte identifier; the database holds only its SHA-256, so a
	/// database read does not yield usable sessions. The identifier is delivered as a cookie that
	/// is <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Strict</c> and host-scoped to the panel.
	/// </para>
	/// <para>
	/// Elevated sessions get shorter lifetimes than player sessions, on the principle that the
	/// blast radius of a stolen operator session is the whole shard.
	/// </para>
	/// </remarks>
	public sealed class PanelSessionManager
	{
		/// <summary>Name of the session cookie.</summary>
		public const string CookieName = "fishmmo_panel_session";

		private readonly IWebSessionService sessions;

		/// <summary>Idle timeout for a player-level session.</summary>
		private static readonly TimeSpan PlayerIdle = TimeSpan.FromMinutes(30);

		/// <summary>Idle timeout for a game master or administrator session.</summary>
		private static readonly TimeSpan ElevatedIdle = TimeSpan.FromMinutes(15);

		/// <summary>Absolute lifetime for a player-level session.</summary>
		private static readonly TimeSpan PlayerLifetime = TimeSpan.FromHours(12);

		/// <summary>Absolute lifetime for a game master or administrator session.</summary>
		private static readonly TimeSpan ElevatedLifetime = TimeSpan.FromHours(4);

		/// <summary>
		/// How long a session that has passed the password stage but not the second factor may sit
		/// waiting for a code. Short: it is not a session, it is a half-finished login.
		/// </summary>
		private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(5);

		public PanelSessionManager(IWebSessionService sessions)
		{
			this.sessions = sessions;
		}

		/// <summary>Hashes a session identifier the way the store expects.</summary>
		public static string Hash(string sessionId)
		{
			byte[] digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sessionId));
			return Convert.ToHexString(digest).ToLowerInvariant();
		}

		/// <summary>Idle timeout that applies at a given access level.</summary>
		public static TimeSpan IdleTimeoutFor(byte accessLevel) =>
			accessLevel >= (byte)AccessLevel.GameMaster ? ElevatedIdle : PlayerIdle;

		/// <summary>Absolute lifetime that applies at a given access level.</summary>
		public static TimeSpan LifetimeFor(byte accessLevel) =>
			accessLevel >= (byte)AccessLevel.GameMaster ? ElevatedLifetime : PlayerLifetime;

		/// <summary>
		/// Issues a session and returns the raw identifier for the cookie. The identifier is
		/// returned exactly once and never stored.
		/// </summary>
		public async Task<(bool Ok, string SessionId, WebSessionData Session, string Error)> IssueAsync(
			string accountName,
			byte accessLevel,
			bool twoFactorSatisfied,
			string ipAddress,
			string userAgent,
			CancellationToken cancellationToken = default)
		{
			string sessionId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
			string hash = Hash(sessionId);

			// A half-finished login gets minutes, not hours, regardless of the account's level.
			DateTime expiresUtc = DateTime.UtcNow +
				(twoFactorSatisfied ? LifetimeFor(accessLevel) : PendingLifetime);

			var result = await sessions.IssueAsync(
				hash, accountName, accessLevel, twoFactorSatisfied, expiresUtc,
				ipAddress, userAgent, cancellationToken);

			if (!result.IsSuccess)
			{
				return (false, null, default, result.ErrorMessage ?? "Could not create a session.");
			}
			return (true, sessionId, result.Data, null);
		}

		/// <summary>
		/// Loads a session by its raw identifier and applies the revoked, expiry and idle checks.
		/// </summary>
		/// <remarks>
		/// A session past its idle timeout is revoked here rather than merely refused, so an
		/// abandoned browser cannot be resurrected by a stolen cookie inside the absolute window.
		/// </remarks>
		public async Task<(bool Ok, WebSessionData Session, string Hash)> ValidateAsync(
			string sessionId,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sessionId))
			{
				return (false, default, null);
			}

			string hash = Hash(sessionId);
			var result = await sessions.FetchByHashAsync(hash, cancellationToken);
			if (!result.IsSuccess)
			{
				return (false, default, hash);
			}

			WebSessionData session = result.Data;
			DateTime now = DateTime.UtcNow;

			if (session.Revoked || session.ExpiresUtc <= now)
			{
				return (false, default, hash);
			}

			TimeSpan idle = session.TwoFactorSatisfied
				? IdleTimeoutFor(session.AccessLevelAtIssue)
				: PendingLifetime;

			if (now - session.LastSeenUtc > idle)
			{
				await sessions.RevokeByHashAsync(hash, cancellationToken);
				return (false, default, hash);
			}

			return (true, session, hash);
		}

		/// <summary>Stamps activity on a live session.</summary>
		public Task TouchAsync(string hash, CancellationToken cancellationToken = default) =>
			sessions.TouchAsync(hash, cancellationToken);

		/// <summary>
		/// Promotes a pending session once its second factor is proven, extending it to the
		/// account's real lifetime.
		/// </summary>
		/// <returns><c>false</c> when the promotion did not persist.</returns>
		/// <remarks>
		/// The result is returned rather than discarded. Swallowing it meant a promotion could
		/// fail while the endpoint still answered "signed in", leaving the operator with a session
		/// that every subsequent policy check refused.
		/// </remarks>
		public async Task<bool> PromoteAsync(string hash, byte accessLevel, CancellationToken cancellationToken = default)
		{
			var result = await sessions.PersistTwoFactorSatisfiedAsync(
				hash, DateTime.UtcNow + LifetimeFor(accessLevel), cancellationToken);
			return result.IsSuccess;
		}

		/// <summary>Records a fresh step-up.</summary>
		/// <returns><c>false</c> when the step-up did not persist.</returns>
		public async Task<bool> StepUpAsync(string hash, CancellationToken cancellationToken = default)
		{
			var result = await sessions.PersistStepUpAsync(hash, cancellationToken);
			return result.IsSuccess;
		}

		/// <summary>Revokes one session.</summary>
		public Task RevokeAsync(string hash, CancellationToken cancellationToken = default) =>
			sessions.RevokeByHashAsync(hash, cancellationToken);

		/// <summary>Revokes every session for an account, returning how many rows changed.</summary>
		public Task<FishMMO.Database.DatabaseResult<int>> RevokeAllAsync(string accountName, CancellationToken cancellationToken = default) =>
			sessions.RevokeAllForAccountAsync(accountName, cancellationToken);

		/// <summary>Cookie options for the session cookie.</summary>
		public static CookieOptions BuildCookieOptions(DateTime expiresUtc, bool isDevelopment)
		{
			return new CookieOptions
			{
				HttpOnly = true,
				// Development runs over plain HTTP on localhost, where a Secure cookie is
				// dropped and nobody can sign in. Production always terminates TLS at NGINX.
				Secure = !isDevelopment,
				SameSite = SameSiteMode.Strict,
				Path = "/",
				Expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero),
				IsEssential = true,
			};
		}
	}
}
