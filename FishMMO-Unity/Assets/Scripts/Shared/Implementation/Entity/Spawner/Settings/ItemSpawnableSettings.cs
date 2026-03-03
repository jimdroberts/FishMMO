using FishNet.Object;
using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Spawnable settings for world items. Injects a <see cref="BaseItemTemplate"/> and random amount
	/// into a <see cref="WorldItem"/> component on the spawned network object.
	/// </summary>
	[Serializable]
	public class ItemSpawnableSettings : SpawnableSettings
	{
		/// <summary>
		/// The item template to inject into the spawned WorldItem.
		/// </summary>
		public BaseItemTemplate ItemTemplate;

		/// <summary>
		/// The minimum number of items to spawn in a single stack.
		/// </summary>
		public int MinimumAmount = 1;

		/// <summary>
		/// The maximum number of items to spawn in a single stack.
		/// </summary>
		public int MaximumAmount = 1;

		/// <summary>
		/// Injects the item template and a random stack amount into the spawned WorldItem component.
		/// </summary>
		/// <param name="nob">The instantiated network object to configure.</param>
		/// <param name="spawner">The spawner that created this object.</param>
		public override void OnSpawned(NetworkObject nob, ObjectSpawner spawner)
		{
			WorldItem worldItem = nob.GetComponent<WorldItem>();
			if (worldItem != null && ItemTemplate != null)
			{
				worldItem.Template = ItemTemplate;
				worldItem.Amount = (uint)UnityEngine.Random.Range(MinimumAmount, MaximumAmount + 1);
			}
		}
	}
}
