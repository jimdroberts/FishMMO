#if !UNITY_SERVER
using UnityEngine.UI;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Interactable representing a dungeon entrance. Displays a title and optional image in the UI.
	/// </summary>
	public class DungeonEntrance : Interactable
	{
		/// <summary>
		/// The display title for the dungeon entrance, shown in the UI.
		/// </summary>
		private string title = "Dungeon";

#if !UNITY_SERVER
		/// <summary>
		/// The image representing the dungeon entrance in the UI (client only).
		/// </summary>
		public Image DungeonImage;
#endif

		/// <summary>
		/// The name of the dungeon associated with this entrance.
		/// </summary>
		public string DungeonName;

		/// <summary>
		/// Gets the display title for the dungeon entrance.
		/// </summary>
		public override string Title { get { return title; } }
	}
}