namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Interface for the scene channel system that handles channel selection for open world scenes.
	/// Provides channel listing (aggregating instances across all scene servers on the same world server)
	/// and same-server or cross-server channel switching.
	/// </summary>
	public interface ISceneChannelSystem : IServerBehaviour
	{
	}
}