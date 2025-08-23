namespace FishMMO.Shared
{
	/// <summary>
	/// Defines the types of scenes available in the FishMMO server.
	/// </summary>
	public enum SceneType : int
	{
		/// <summary>
		/// Unknown or undefined scene type.
		/// </summary>
		Unknown = 0,
		/// <summary>
		/// Normal open world scene instance.
		/// </summary>
		OpenWorld,
		/// <summary>
		/// Group specific scene instance.
		/// </summary>
		Group,
		/// <summary>
		/// Player versus Player scene instance.
		/// </summary>
		PvP,
	}
}