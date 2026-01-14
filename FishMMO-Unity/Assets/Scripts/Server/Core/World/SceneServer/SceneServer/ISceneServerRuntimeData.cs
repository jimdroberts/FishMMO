namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for scene server identification and state.
	/// Provides access to scene server operational state.
	/// </summary>
	public interface ISceneServerRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Database ID for this scene server instance.
		/// </summary>
		long ID { get; set; }

		/// <summary>
		/// Indicates whether the scene server is locked (not accepting new connections).
		/// </summary>
		bool IsLocked { get; set; }
	}
}
