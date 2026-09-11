using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A browser session for the Control Panel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Deliberately separate from <see cref="AuthTokenEntity"/>. That table's rows are game
	/// tokens issued by a LoginServer and carry a foreign key to <c>login_servers</c>; the
	/// Control Panel is not a login server, has no row to point at, and its sessions must not be
	/// accepted by a World or Scene server that validates game tokens.
	/// </para>
	/// <para>
	/// Only the SHA-256 of the session identifier is stored, so a database read does not yield
	/// usable sessions.
	/// </para>
	/// </remarks>
	public class WebSessionEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Lowercase hex SHA-256 of the opaque session identifier held by the browser cookie.
		/// The identifier itself is never stored.
		/// </summary>
		public string SessionHash { get; set; }

		/// <summary>Account this session authenticates, matching <c>accounts.name</c>.</summary>
		public string AccountName { get; set; }

		/// <summary>
		/// The account's access level when the session was issued. Compared against the account's
		/// current level on every request so a demotion takes effect immediately rather than at
		/// the session's next renewal.
		/// </summary>
		public byte AccessLevelAtIssue { get; set; }

		/// <summary>
		/// Whether two-factor has been satisfied. A session that has authenticated by password but
		/// not yet by code exists in this table with the flag clear and can do nothing else.
		/// </summary>
		public bool TwoFactorSatisfied { get; set; }

		/// <summary>
		/// When the operator last re-proved possession of their authenticator, or null if never.
		/// Drives the step-up window on destructive actions.
		/// </summary>
		public DateTime? LastStepUpUtc { get; set; }

		/// <summary>When the session was created.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>Last request on this session; drives the idle timeout.</summary>
		public DateTime LastSeenUtc { get; set; }

		/// <summary>Absolute expiry, independent of activity.</summary>
		public DateTime ExpiresUtc { get; set; }

		/// <summary>Whether the session has been explicitly revoked.</summary>
		public bool Revoked { get; set; }

		/// <summary>Client address the session was issued to, for the operator's own session list.</summary>
		public string IpAddress { get; set; }

		/// <summary>Truncated user agent, for the operator's own session list.</summary>
		public string UserAgent { get; set; }

		/// <summary>
		/// PostgreSQL <c>xmin</c> exposed as an EF concurrency token, matching every other
		/// versioned entity in this schema.
		/// </summary>
		public uint Version { get; set; }
	}
}
