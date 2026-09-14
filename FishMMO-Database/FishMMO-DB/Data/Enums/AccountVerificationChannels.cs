using System;

namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// The channels an account holder chose to prove their contact details with.
	/// </summary>
	/// <remarks>
	/// Flags, because the player may choose both. The account's single <c>verified</c> flag — the one
	/// sign-in reads — becomes true once every chosen channel has been proven, so choosing both is
	/// stricter, never looser. A server may switch a channel off for development; see the
	/// verification policy on the server side.
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
	}
}
