using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// A chat mute as one row stores it: on an account, or on a single character.
	/// </summary>
	/// <remarks>
	/// Expiry is computed, never stored. A mute whose instant has passed stops applying without
	/// anything clearing the row, so there is no sweep to fall behind and no window where a lapsed
	/// mute still silences a player because a job has not run yet.
	/// </remarks>
	public readonly struct ChatMuteData
	{
		/// <summary>Whether a mute is set.</summary>
		public readonly bool Muted;

		/// <summary>When the mute lifts (UTC), or null for a mute with no end.</summary>
		public readonly DateTime? MutedUntilUtc;

		/// <summary>Operator account that applied the mute.</summary>
		public readonly string? MutedBy;

		/// <summary>The reason recorded with the mute.</summary>
		public readonly string? Reason;

		/// <summary>Creates a mute record.</summary>
		public ChatMuteData(bool muted, DateTime? mutedUntilUtc, string? mutedBy, string? reason)
		{
			Muted = muted;
			MutedUntilUtc = mutedUntilUtc;
			MutedBy = mutedBy;
			Reason = reason;
		}

		/// <summary>True when the mute is set and has not yet lifted at <paramref name="utcNow"/>.</summary>
		public bool IsActiveAt(DateTime utcNow)
		{
			return Muted && (MutedUntilUtc == null || MutedUntilUtc.Value > utcNow);
		}
	}

	/// <summary>
	/// Both mutes that can silence a character: its own, and its account's.
	/// </summary>
	/// <remarks>
	/// Read together, in one statement, because either one silences the character and a reader
	/// that fetched only one of them would let a muted account talk through a fresh character.
	/// </remarks>
	public readonly struct CharacterChatMuteState
	{
		/// <summary>The account-wide mute.</summary>
		public readonly ChatMuteData Account;

		/// <summary>The mute on this character alone.</summary>
		public readonly ChatMuteData Character;

		/// <summary>Creates the pair.</summary>
		public CharacterChatMuteState(ChatMuteData account, ChatMuteData character)
		{
			Account = account;
			Character = character;
		}
	}
}
