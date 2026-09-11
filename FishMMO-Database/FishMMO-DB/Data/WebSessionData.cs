using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// A Control Panel browser session, as read back from the database.
	/// </summary>
	/// <remarks>
	/// Carries no secret: the session identifier itself is never stored, only its SHA-256.
	/// A copy of this struct is safe to hold after the database context is disposed.
	/// </remarks>
	public readonly struct WebSessionData
	{
		/// <summary>Surrogate key, used by the operator's own session list.</summary>
		public readonly long ID;

		/// <summary>Account the session authenticates.</summary>
		public readonly string AccountName;

		/// <summary>Access level recorded when the session was issued.</summary>
		public readonly byte AccessLevelAtIssue;

		/// <summary>Whether two-factor has been satisfied on this session.</summary>
		public readonly bool TwoFactorSatisfied;

		/// <summary>When the operator last re-proved their authenticator, or null.</summary>
		public readonly DateTime? LastStepUpUtc;

		/// <summary>When the session was created.</summary>
		public readonly DateTime CreatedUtc;

		/// <summary>Last activity, driving the idle timeout.</summary>
		public readonly DateTime LastSeenUtc;

		/// <summary>Absolute expiry, independent of activity.</summary>
		public readonly DateTime ExpiresUtc;

		/// <summary>Whether the session has been revoked.</summary>
		public readonly bool Revoked;

		/// <summary>Address the session was issued to.</summary>
		public readonly string IpAddress;

		/// <summary>Truncated user agent the session was issued to.</summary>
		public readonly string UserAgent;

		/// <summary>Creates a session record.</summary>
		public WebSessionData(
			long id,
			string accountName,
			byte accessLevelAtIssue,
			bool twoFactorSatisfied,
			DateTime? lastStepUpUtc,
			DateTime createdUtc,
			DateTime lastSeenUtc,
			DateTime expiresUtc,
			bool revoked,
			string ipAddress,
			string userAgent)
		{
			ID = id;
			AccountName = accountName;
			AccessLevelAtIssue = accessLevelAtIssue;
			TwoFactorSatisfied = twoFactorSatisfied;
			LastStepUpUtc = lastStepUpUtc;
			CreatedUtc = createdUtc;
			LastSeenUtc = lastSeenUtc;
			ExpiresUtc = expiresUtc;
			Revoked = revoked;
			IpAddress = ipAddress;
			UserAgent = userAgent;
		}
	}
}
