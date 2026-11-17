namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state the trade system keeps between frames: its ingress guard. Sessions and
	/// invitations are owned by the system itself, which is the only writer.
	/// </summary>
	public interface ITradeSystemRuntimeData : IRuntimeDataContainer
	{
		IngressGuard IngressGuard { get; }
	}
}
