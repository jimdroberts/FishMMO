using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an item that exists in the world and can be interacted with or picked up by players.
	/// </summary>
	public class WorldItem : Interactable
	{
		/// <summary>
		/// The item template for this world item, defining its type and properties.
		/// Set in the inspector or at runtime.
		/// </summary>
		[SerializeField]
		private BaseItemTemplate template;

		/// <summary>
		/// The amount of items represented by this world item (e.g., stack size).
		/// </summary>
		public uint Amount;

		/// <summary>
		/// Gets the item template for this world item, used to access item data and display info.
		/// </summary>
		public BaseItemTemplate Template { get { return template; } }

		/// <summary>
		/// Gets the display title for the world item. Returns an empty string, meaning no title is shown in UI by default.
		/// Override this property in derived classes to provide a custom title.
		/// </summary>
		public override string Title { get { return ""; } }
	}
}