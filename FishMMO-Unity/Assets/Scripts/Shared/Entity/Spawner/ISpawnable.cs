using System;

namespace FishMMO.Shared
{
	public interface ISpawnable
	{
		Spawnable SpawnTemplate { get; set; }
		long ID { get; }
		event Action<ISpawnable> OnDespawn;
		void Despawn();
	}
}