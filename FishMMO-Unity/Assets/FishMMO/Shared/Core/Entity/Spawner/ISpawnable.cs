using FishNet.Object;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// An entity a server-side spawner creates and recycles: NPCs, world items, gathering nodes,
	/// containers.
	/// </summary>
	/// <remarks>
	/// The spawner itself lives in the server assembly, so the entity holds it only as an
	/// <see cref="ISpawnOwner"/>. What the entity was spawned from is the spawner's bookkeeping,
	/// not the entity's, and is kept there.
	/// </remarks>
	public interface ISpawnable
	{
		/// <summary>
		/// The spawner that created this entity and schedules its respawn, or null when it was
		/// placed any other way.
		/// </summary>
		ISpawnOwner Spawner { get; set; }

		/// <summary>
		/// The network object associated with this entity for network synchronization.
		/// </summary>
		NetworkObject NetworkObject { get; }

		/// <summary>
		/// The unique identifier for this spawnable entity.
		/// </summary>
		long ID { get; }

		/// <summary>
		/// Despawns the entity, removing it from the game world and network.
		/// </summary>
		void Despawn();
	}
}
