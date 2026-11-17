namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>The ability crafting system's runtime state: its request guard.</summary>
	public interface IAbilityCraftingSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>Debounces and serialises each connection's ability crafting requests.</summary>
		IngressGuard IngressGuard { get; }
	}
}
