using FishMMO.Server.Core.World.SceneServer;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a banker NPC that allows players to access their bank storage.
	/// </summary>
	/// <summary>
	/// Banker NPC that allows players to access their bank storage. Inherits from Interactable.
	/// Displays a custom title and color in the UI.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Banker : Interactable, IBanker
	{
		/// <summary>
		/// Achievement to increment when a player uses this banker.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		AchievementTemplate IBanker.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// The display title for the banker, shown in the UI.
		/// </summary>
		public override string Title { get { return "Banker"; } }

		/// <summary>
		/// The color of the title displayed for the banker in the UI.
		/// Uses a goldenrod color for distinction.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.goldenrod); } }
	}
}