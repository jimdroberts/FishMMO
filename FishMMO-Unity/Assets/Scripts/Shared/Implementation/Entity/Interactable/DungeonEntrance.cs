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
		/// The dungeon this entrance leads to: its description, artwork and difficulty list.
		/// </summary>
		/// <remarks>
		/// Optional. An entrance without one leads to a dungeon with a single unnamed difficulty
		/// and default rules, which is exactly how every entrance behaved before difficulties
		/// existed — so leaving it unset is a working configuration, not a broken one.
		/// </remarks>
		public DungeonTemplate Template;

		/// <summary>
		/// Achievement to increment when a player enters this dungeon.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		string IDungeonEntrance.DungeonName => DungeonName;

		/// <inheritdoc />
		int IDungeonEntrance.DungeonTemplateID => Template != null ? Template.ID : 0;

		/// <inheritdoc />
		AchievementTemplate IDungeonEntrance.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Gets the display title for the dungeon entrance.
		/// </summary>
		public override string Title { get { return title; } }
	}
}