namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Scene type enumeration for different types of game scenes.
	/// </summary>
	public enum SceneType
	{
		/// <summary>
		/// Public scene accessible by all players.
		/// </summary>
		Public = 0,

		/// <summary>
		/// Private instance for a specific character or party.
		/// </summary>
		Instance = 1,

		/// <summary>
		/// Guild-specific scene.
		/// </summary>
		Guild = 2
	}
}