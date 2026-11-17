namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>The matchmaking system's runtime state: its request guard.</summary>
	public interface IMatchmakingSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>Debounces and serialises each connection's matchmaking requests.</summary>
		IngressGuard IngressGuard { get; }
	}
}
