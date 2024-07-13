using FishNet.Object;

namespace FishMMO.Shared
{
	public interface ISpawnable
	{
		ObjectSpawner ObjectSpawner { get; set; }
		Spawnable SpawnTemplate { get; set; }
		NetworkObject NetworkObject { get; }
		long ID { get; }
		void Despawn();
	}
}