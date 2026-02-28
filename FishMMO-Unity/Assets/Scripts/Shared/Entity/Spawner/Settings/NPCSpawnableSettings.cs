using FishNet.Object;
using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Spawnable settings for NPCs. Optionally overrides the NPC's <see cref="NPCAttributeDatabase"/>
	/// with a spawner-specific attribute database on spawn.
	/// </summary>
	[Serializable]
	public class NPCSpawnableSettings : SpawnableSettings
	{
		/// <summary>
		/// Optional attribute database override. When assigned, replaces the NPC prefab's default
		/// <see cref="NPC.AttributeBonuses"/> at spawn time, allowing per-spawner attribute variation.
		/// </summary>
		public NPCAttributeDatabase AttributeBonusOverride;

		/// <summary>
		/// Injects the attribute database override into the spawned NPC.
		/// Attribute initialization is deferred to <see cref="NPC.OnStartServer"/>,
		/// which runs after this override has been applied.
		/// </summary>
		/// <param name="nob">The instantiated network object to configure.</param>
		/// <param name="spawner">The spawner that created this object.</param>
		public override void OnSpawned(NetworkObject nob, ObjectSpawner spawner)
		{
			if (AttributeBonusOverride == null)
			{
				return;
			}

			NPC npc = nob.GetComponent<NPC>();
			if (npc != null)
			{
				npc.AttributeBonuses = AttributeBonusOverride;
			}
		}
	}
}