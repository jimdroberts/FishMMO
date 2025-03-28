namespace FishMMO.Server
{
	public enum SceneType : int
	{
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