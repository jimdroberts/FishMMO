using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Mailbox interactable that allows players to send, receive, and manage mail.
	/// Opens the mail UI on interaction; mail operations are handled server-side via broadcasts.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Mailbox : Interactable, IMailbox
	{
		/// <summary>
		/// Achievement to increment when a player uses this mailbox.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		AchievementTemplate IMailbox.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Display title shown above the mailbox.
		/// </summary>
		public override string Title { get { return "Mailbox"; } }

		/// <summary>
		/// Title color for the mailbox UI label.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.goldenrod); } }
	}
}