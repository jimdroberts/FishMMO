using FishNet.Object;

namespace FishMMO.Shared
{
	public interface ISpawnable
	{
		ObjectSpawner ObjectSpawner { get; set; }
		SpawnableSettings SpawnableSettings { get; set; }
		NetworkObject NetworkObject { get; }
		long ID { get; }
		void Despawn();
	}
}