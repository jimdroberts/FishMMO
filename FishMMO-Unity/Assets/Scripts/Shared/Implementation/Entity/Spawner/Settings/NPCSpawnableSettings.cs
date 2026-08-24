using System;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Per-spawner overrides for an NPC prefab: which attributes it rolls, which brain it runs,
	/// what it knows how to cast, and how big it is.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The point of putting these on the spawner rather than the prefab is that one prefab can then
	/// serve a whole zone's worth of variants. A single "orc" prefab becomes a weak orc in the
	/// starting valley and an elite orc at the dungeon entrance by changing an attribute database
	/// and an archetype here, instead of duplicating the prefab — which matters enormously for the
	/// pooling above, because each duplicate prefab is its own pool bucket and its own fixed slice
	/// of the map's memory budget.
	/// </para>
	/// <para>
	/// Everything is applied in <see cref="OnSpawned"/>, which runs after the object leaves the
	/// pool and before <c>ServerManager.Spawn</c> — that is, before <see cref="NPC.OnStartServer"/>
	/// rolls attributes and learns abilities, and before the spawn payload is written to clients.
	/// </para>
	/// </remarks>
	[Serializable]
	public class NPCSpawnableSettings : SpawnableSettings
	{
		/// <summary>
		/// Optional attribute database override. When assigned, replaces the NPC prefab's default
		/// <see cref="NPC.AttributeBonuses"/> at spawn time, allowing per-spawner attribute variation.
		/// </summary>
		[Header("Attributes")]
		[Tooltip("Replaces the prefab's attribute database. Leave empty to use the prefab's own.")]
		public NPCAttributeDatabase AttributeBonusOverride;

		/// <summary>
		/// Optional corpse decay duration override. When greater than zero, overrides
		/// the NPC prefab default. Set to 0 to use the prefab default.
		/// </summary>
		[Tooltip("Corpse decay duration in seconds. 0 = use prefab default.")]
		public float CorpseDecayDurationOverride;

		/// <summary>
		/// Optional loot table override, so the same prefab drops zone-appropriate loot.
		/// </summary>
		/// <remarks>
		/// The counterpart to <see cref="AttributeBonusOverride"/>: a spawner that makes one orc
		/// prefab into an elite is also the spawner that should decide the elite drops elite loot.
		/// Leave empty to use whatever the prefab carries.
		/// </remarks>
		[Tooltip("Replaces the prefab's loot table. Leave empty to use the prefab's own.")]
		public LootTableTemplate LootTableOverride;

		/// <summary>
		/// Optional AI archetype override — the NPC's whole brain in one asset.
		/// </summary>
		/// <remarks>
		/// Lets one prefab be a passive critter at one spawner and an aggressive hunter at another
		/// without touching the prefab. Applied before <see cref="AIController.InitializeOnce"/>
		/// reads its state slots.
		/// </remarks>
		[Header("Behaviour")]
		[Tooltip("Replaces the prefab's AI archetype. Leave empty to use the prefab's own.")]
		public AIArchetypeTemplate ArchetypeOverride;

		/// <summary>
		/// Abilities granted in addition to the prefab's own list.
		/// </summary>
		/// <remarks>
		/// Additive rather than replacing, so a spawner can hand an elite variant one extra
		/// signature ability without having to re-list everything the species already knows.
		/// </remarks>
		[Tooltip("Abilities granted on top of the prefab's own list.")]
		public List<AbilityTemplate> AdditionalAbilities = new List<AbilityTemplate>();

		/// <summary>
		/// When true, <see cref="AdditionalAbilities"/> replaces the prefab's list instead of
		/// extending it.
		/// </summary>
		[Tooltip("Replace the prefab's ability list rather than adding to it.")]
		public bool ReplacePrefabAbilities;

		/// <summary>
		/// Optional faction override, so the same prefab can be hostile in one zone and neutral in
		/// another.
		/// </summary>
		[Tooltip("Replaces the prefab's race template / faction source. Leave empty to use the prefab's own.")]
		public RaceTemplate FactionOverride;

		/// <summary>
		/// Minimum uniform scale applied to the spawned NPC. 0 or 1 leaves the prefab scale alone.
		/// </summary>
		/// <remarks>
		/// Visual variety within one pool bucket. Deliberately uniform: a non-uniform scale would
		/// desynchronise the NavMeshAgent's radius and height from the collider.
		/// </remarks>
		[Header("Appearance")]
		[Tooltip("Minimum uniform scale. 0 or 1 leaves the prefab scale unchanged.")]
		public float MinimumScale = 1f;

		/// <summary>
		/// Maximum uniform scale applied to the spawned NPC.
		/// </summary>
		[Tooltip("Maximum uniform scale. Must be at least the minimum to take effect.")]
		public float MaximumScale = 1f;

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

			/* Every override below resolves to "this spawner's value, or the PREFAB's" — never to
			 * whatever happens to be on the instance.
			 *
			 * Pooled instances are shared between spawners, and an override is a plain field
			 * write that outlives the spawn that made it. A spawner that set an override handed
			 * the instance back to the pool still carrying it, and the next spawner to draw that
			 * instance — one with no override of its own, expecting the prefab default — silently
			 * inherited it. That is how an elite's attribute database, corpse timer and loot table
			 * leak into the ordinary creature that reuses its slot, and it gets worse the busier
			 * the pool is. Reading the defaults back off the prefab makes each spawn independent
			 * of whoever used the object last. */
			NPC prefabNPC = NetworkObject != null ? NetworkObject.GetComponent<NPC>() : null;

			npc.AttributeBonuses = AttributeBonusOverride != null
				? AttributeBonusOverride
				: prefabNPC?.AttributeBonuses;

			npc.CorpseDecayDuration = CorpseDecayDurationOverride > 0f
				? CorpseDecayDurationOverride
				: (prefabNPC != null ? prefabNPC.CorpseDecayDuration : npc.CorpseDecayDuration);

			npc.LootTable = LootTableOverride != null
				? LootTableOverride
				: prefabNPC?.LootTable;

			ApplyAbilities(npc, prefabNPC);
			ApplyArchetype(nob);
			ApplyFaction(nob);
			ApplyScale(nob);
		}

		/// <summary>
		/// Applies the ability overrides to the NPC's inspector-facing ability list.
		/// </summary>
		/// <remarks>
		/// Mutating <see cref="NPC.Abilities"/> is safe here specifically because the list is a
		/// per-instance field on a pooled object rather than shared prefab state — but a recycled
		/// NPC still carries whatever the previous spawner wrote, so the list is rebuilt from the
		/// settings each time rather than appended to.
		/// </remarks>
		/// <param name="npc">The NPC being configured.</param>
		private void ApplyAbilities(NPC npc, NPC prefabNPC)
		{
			if (npc.Abilities == null)
			{
				npc.Abilities = new List<AbilityTemplate>();
			}

			/* Rebuilt from the prefab every spawn, for the same reason the scalar overrides above
			 * are. The list is a per-instance field on a pooled object: clearing it only when this
			 * spawner has additions of its own left the previous spawner's additions in place for
			 * any spawner that had none, so a creature could come back knowing an elite's
			 * signature ability. */
			npc.Abilities.Clear();

			if (!ReplacePrefabAbilities &&
				prefabNPC != null &&
				prefabNPC.Abilities != null)
			{
				for (int i = 0; i < prefabNPC.Abilities.Count; ++i)
				{
					AbilityTemplate template = prefabNPC.Abilities[i];
					if (template != null && !npc.Abilities.Contains(template))
					{
						npc.Abilities.Add(template);
					}
				}
			}

			if (AdditionalAbilities == null || AdditionalAbilities.Count < 1)
			{
				return;
			}

			for (int i = 0; i < AdditionalAbilities.Count; ++i)
			{
				AbilityTemplate template = AdditionalAbilities[i];
				if (template != null && !npc.Abilities.Contains(template))
				{
					npc.Abilities.Add(template);
				}
			}
		}

		/// <summary>
		/// Applies the AI archetype override.
		/// </summary>
		/// <param name="nob">The instantiated network object.</param>
		private void ApplyArchetype(NetworkObject nob)
		{
			if (ArchetypeOverride == null)
			{
				return;
			}

			AIController controller = nob.GetComponent<AIController>();
			if (controller == null)
			{
				return;
			}

			controller.Archetype = ArchetypeOverride;

			/* InitializeOnce applies the archetype, and it only runs once per instance — on the
			 * very first Awake. A recycled NPC has already been through it, so applying the
			 * archetype here as well is what makes the override take on reuse rather than only on
			 * the first spawn of that pooled object. */
			ArchetypeOverride.ApplyTo(controller);
		}

		/// <summary>
		/// Applies the faction override.
		/// </summary>
		/// <param name="nob">The instantiated network object.</param>
		private void ApplyFaction(NetworkObject nob)
		{
			if (FactionOverride == null)
			{
				return;
			}

			/* Concrete FactionController rather than the interface: the interface exposes
			 * RaceTemplate read-only on purpose, because changing it after a character has built
			 * its alliance tables would leave the two disagreeing. This runs before the spawn, so
			 * the spawn-time setter is the correct entry point. */
			FactionController factionController = nob.GetComponent<FactionController>();
			if (factionController != null)
			{
				factionController.SetRaceTemplateOnSpawn(FactionOverride);
			}
		}

		/// <summary>
		/// Applies a random uniform scale, keeping the NavMeshAgent's footprint in step with it.
		/// </summary>
		/// <param name="nob">The instantiated network object.</param>
		private void ApplyScale(NetworkObject nob)
		{
			if (MaximumScale <= 0f || MaximumScale < MinimumScale)
			{
				return;
			}

			float minimum = Mathf.Max(MinimumScale, 0.01f);
			float scale = Mathf.Approximately(minimum, MaximumScale)
				? MaximumScale
				: DeterministicRNG.Shared.Range(minimum, MaximumScale);

			// A scale of exactly 1 is the prefab's own; do not disturb it.
			if (Mathf.Approximately(scale, 1f))
			{
				nob.transform.localScale = Vector3.one;
				return;
			}

			nob.transform.localScale = Vector3.one * scale;
		}

		/// <summary>
		/// Resolves the respawn delay range, preferring this spawner's override and falling back
		/// to the NPC prefab's own cadence.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="SpawnableSettings.MinimumRespawnTime"/> and
		/// <see cref="SpawnableSettings.MaximumRespawnTime"/> are OVERRIDES here, not the values
		/// themselves. Leaving both at zero — which is what an author who has not thought about
		/// respawn timing leaves them at — means "use whatever the prefab says", so a creature
		/// keeps its own cadence at every spawner that can produce it and only differs where
		/// someone deliberately said it should.
		/// </para>
		/// <para>
		/// Zero is the unset marker rather than a separate toggle, matching
		/// <see cref="CorpseDecayDurationOverride"/>. It costs the ability to author a genuinely
		/// instant respawn, which is degenerate for an NPC and was previously the accidental
		/// default for every spawner that left these blank.
		/// </para>
		/// <para>
		/// Only the maximum is tested for "set". A minimum of zero is a legitimate override — "any
		/// time between now and thirty seconds" is a real cadence — whereas a maximum of zero
		/// cannot describe a range at all.
		/// </para>
		/// </remarks>
		/// <param name="minimum">Receives the shortest respawn delay in seconds.</param>
		/// <param name="maximum">Receives the longest respawn delay in seconds.</param>
		public override void ResolveRespawnTimeRange(out float minimum, out float maximum)
		{
			if (MaximumRespawnTime > 0f)
			{
				base.ResolveRespawnTimeRange(out minimum, out maximum);
				return;
			}

			/* Read off the PREFAB, not off any live instance. This is called both when an NPC
			 * despawns and at spawner start-up before anything has been instantiated, so the
			 * prefab is the only source available on both paths — and it is the correct one
			 * regardless, since a pooled instance carries whatever the last spawner to use it
			 * wrote. */
			NPC prefabNPC = NetworkObject != null ? NetworkObject.GetComponent<NPC>() : null;
			if (prefabNPC == null)
			{
				base.ResolveRespawnTimeRange(out minimum, out maximum);
				return;
			}

			minimum = Mathf.Max(0f, prefabNPC.MinimumRespawnTime);
			maximum = Mathf.Max(minimum, prefabNPC.MaximumRespawnTime);
		}

		/// <summary>
		/// Validates the settings, and reports scale ranges that cannot produce a value.
		/// </summary>
		public override void OnValidate()
		{
			base.OnValidate();

			if (MinimumScale < 0f) MinimumScale = 0f;
			if (MaximumScale < MinimumScale) MaximumScale = MinimumScale;
		}
	}
}
