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

		/// <summary>
		/// The requester's Discord username as Discord reports it (<c>name</c>, or <c>name#1234</c> for a
		/// real discriminator), recorded on the account when the link is made. Null when unknown.
		/// </summary>
		public string? DiscordUsername { get; set; }

		/// <summary>When this verification request expires.</summary>
		public DateTime ExpiresAtUtc { get; set; }
	}
}
