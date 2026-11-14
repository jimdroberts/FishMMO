using System;

namespace FishMMO.Client
{
	/// <summary>
	/// Decides when the guild's message of the day is written into chat.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The server sends the guild's text to a member every time they connect to a scene server,
	/// which is every zone change as well as the login, and again to the whole guild whenever an
	/// officer edits it. Printing every copy would repeat the same line at each zone line. So the
	/// rule is: once per login, and again only when the text is different from what was last
	/// shown — an edit, a new guild, or a different character.
	/// </para>
	/// <para>
	/// An empty message is remembered but never printed, so a guild that sets one later while the
	/// player is online still has it announced.
	/// </para>
	/// </remarks>
	public sealed class GuildMessageOfTheDayGate
	{
		private long characterID;
		private long guildID;
		private string text = string.Empty;
		private bool hasSeen;

		/// <summary>
		/// Records a received message of the day and reports whether it should be printed.
		/// </summary>
		/// <param name="characterID">The local character the text was sent to.</param>
		/// <param name="guildID">The guild the text belongs to.</param>
		/// <param name="messageOfTheDay">The text as received; null is treated as empty.</param>
		/// <param name="announceText">The trimmed text to print, when the result is true.</param>
		/// <returns>True when the line should be written into chat.</returns>
		public bool ShouldAnnounce(long characterID, long guildID, string messageOfTheDay, out string announceText)
		{
			string trimmed = messageOfTheDay?.Trim() ?? string.Empty;

			bool same = this.hasSeen &&
						this.characterID == characterID &&
						this.guildID == guildID &&
						string.Equals(this.text, trimmed, StringComparison.Ordinal);

			this.hasSeen = true;
			this.characterID = characterID;
			this.guildID = guildID;
			this.text = trimmed;

			announceText = trimmed;
			return !same && guildID > 0 && trimmed.Length > 0;
		}

		/// <summary>
		/// Forgets what was shown, so the next login announces the message again.
		/// </summary>
		public void Reset()
		{
			this.hasSeen = false;
			this.characterID = 0;
			this.guildID = 0;
			this.text = string.Empty;
		}
	}
}
