using FishNet.Object;
using System;
using UnityEngine;

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
		/// Optional corpse decay duration override. When greater than zero, overrides
		/// the NPC prefab default. Set to 0 to use the prefab default.
		/// </summary>
		[Tooltip("Corpse decay duration in seconds. 0 = use prefab default.")]
		public float CorpseDecayDurationOverride;

		/// <summary>
		/// Injects spawner-specific overrides into the spawned NPC before
		/// <see cref="NPC.OnStartServer"/> runs.
		/// </summary>
		/// <param name="nob">The instantiated network object to configure.</param>
		/// <param name="spawner">The spawner that created this object.</param>
		public override void OnSpawned(NetworkObject nob, ObjectSpawner spawner)
		{
			NPC npc = nob.GetComponent<NPC>();
			if (npc == null) return;

			if (AttributeBonusOverride != null)
			{
				npc.AttributeBonuses = AttributeBonusOverride;
			}

			if (CorpseDecayDurationOverride > 0f)
			{
				npc.CorpseDecayDuration = CorpseDecayDurationOverride;
			}
		}
	}
}