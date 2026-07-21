using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interactable representing a dungeon entrance. Displays a title and optional image in the UI.
	/// </summary>
	public class DungeonEntrance : Interactable, IDungeonEntrance
	{
		/// <summary>
		/// The display title for the dungeon entrance, shown in the UI.
		/// </summary>
		private string title = "Dungeon";

		/// <summary>
		/// The image representing the dungeon entrance in the UI (client only).
		/// </summary>
		public Sprite DungeonImage;

		/// <summary>
		/// The name of the dungeon associated with this entrance.
		/// </summary>
		public string DungeonName;

		/// <summary>
		/// Achievement to increment when a player enters this dungeon.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		string IDungeonEntrance.DungeonName => DungeonName;

		/// <inheritdoc />
		AchievementTemplate IDungeonEntrance.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Gets the display title for the dungeon entrance.
		/// </summary>
		public override string Title { get { return title; } }
	}
}