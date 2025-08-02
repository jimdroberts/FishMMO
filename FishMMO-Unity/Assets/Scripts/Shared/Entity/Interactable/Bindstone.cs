namespace FishMMO.Shared
{
	/// <summary>
	/// Bindstone interactable, used for setting player respawn points. Inherits from Interactable.
	/// Typically displays no title in the UI.
	/// </summary>
	public class Bindstone : Interactable
	{
		/// <summary>
		/// The display title for the bindstone. Returns an empty string, as bindstones do not show a title in the UI.
		/// </summary>
		public override string Title { get { return ""; } }
	}
}