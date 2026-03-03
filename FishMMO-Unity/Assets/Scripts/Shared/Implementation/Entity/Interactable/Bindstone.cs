using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Bindstone interactable, used for setting player respawn points. Inherits from Interactable.
	/// Typically displays no title in the UI.
	/// </summary>
	public class Bindstone : Interactable, IBindstone
	{
		/// <summary>
		/// Achievement to increment when a player activates this bindstone.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		AchievementTemplate IBindstone.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// The display title for the bindstone. Returns an empty string, as bindstones do not show a title in the UI.
		/// </summary>
		public override string Title { get { return ""; } }
	}
}