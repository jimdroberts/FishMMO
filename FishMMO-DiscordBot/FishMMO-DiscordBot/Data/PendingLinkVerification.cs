using System;

namespace FishMMO.DiscordBot.Data
{
	/// <summary>
	/// Represents an in-progress account-link verification request.
	/// Stored in memory only; expires after a timeout.
	/// </summary>
	public class PendingLinkVerification
	{
		/// <summary>Discord user ID requesting the link.</summary>
		public ulong DiscordUserId { get; set; }

		/// <summary>Character name the user claims to own.</summary>
		public string CharacterName { get; set; } = string.Empty;

		/// <summary>The 6-character verification code the user must type in-game.</summary>
		public string VerificationCode { get; set; } = string.Empty;

		/// <summary>When this verification request expires.</summary>
		public DateTime ExpiresAtUtc { get; set; }
	}
}
