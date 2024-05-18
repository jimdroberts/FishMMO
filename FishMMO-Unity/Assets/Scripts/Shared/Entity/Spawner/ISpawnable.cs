using System;
using FishNet.Object;

namespace FishMMO.Shared
{
	public interface ISpawnable
	{
		Spawnable SpawnTemplate { get; set; }
		NetworkObject NetworkObject { get; }
		long ID { get; }
		event Action<ISpawnable> OnDespawn;
		void Despawn();
	}
}