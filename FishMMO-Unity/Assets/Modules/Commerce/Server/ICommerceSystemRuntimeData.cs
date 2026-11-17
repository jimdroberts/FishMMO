namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>The commerce system's runtime state: its request guard.</summary>
	public interface ICommerceSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>Debounces and serialises each connection's commerce requests.</summary>
		IngressGuard IngressGuard { get; }
	}
}
