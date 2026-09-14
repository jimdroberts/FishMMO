using System;

namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// The channels an account holder chose to prove their contact details with.
	/// </summary>
	/// <remarks>
	/// Flags, because the player may choose several. <b>Any one of them verifies the account</b>: a code
	/// goes out on each chosen channel the server has switched on, and whichever is entered first sets
	/// the single <c>verified</c> flag sign-in reads. Choosing more channels gives the player more ways
	/// to receive a code, never more codes to enter. See <c>AccountVerificationRules</c>.
	/// </remarks>
	[Flags]
	public enum AccountVerificationChannels : byte
	{
		/// <summary>No verification chosen. Only valid where the server verifies nothing.</summary>
		None = 0,
		/// <summary>A code sent by email.</summary>
		Email = 1,
		/// <summary>A code sent by SMS to the account's phone number.</summary>
		Sms = 2,
		/// <summary>
		/// A code sent once, by the Discord bot, as a direct message to the Discord username on the account.
		/// </summary>
		Discord = 4,
	}
}
