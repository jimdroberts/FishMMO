using FishNet.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for spawnable entities managed by an ObjectSpawner. Provides access to spawner, settings, network object, unique ID, and despawn logic.
	/// </summary>
	public interface ISpawnable
	{
		/// <summary>
		/// The ObjectSpawner responsible for spawning and managing this entity.
		/// </summary>
		ObjectSpawner ObjectSpawner { get; set; }

		/// <summary>
		/// The settings used to configure this spawnable entity.
		/// </summary>
		SpawnableSettings SpawnableSettings { get; set; }

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