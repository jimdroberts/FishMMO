#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A spawner for one NPC role at the site — service NPCs, guards, townsfolk, a hostile pack or its elite — filled
	/// from <see cref="PointOfInterestNpcTable"/> by the site's race (Jim, 2026-10-10: auto spawners).
	/// </summary>
	/// <remarks>
	/// <para><b>Authoring only.</b> The spawner is an <see cref="ObjectSpawner"/> on an <c>EditorOnly</c> object: the spawn
	/// table baker copies it into the scene's table on save and builds strip it (fishmmo-issue-294-spawner-audit).</para>
	/// <para><b>Who.</b> The role resolves through the NPC table for the site's race; each NPC spawns with that race applied
	/// (<see cref="NPCSpawnableSettings.FactionOverride"/>: model, faction and name). A role nothing answers still gets its
	/// spawner, empty, and a note — a designer fills it.</para>
	/// <para><b>How many.</b> <see cref="Count"/> when set, else by the site's size class: a service NPC is one, a pack
	/// 3 / 5 / 8, guards 2 / 3 / 4, townsfolk 2 / 4 / 6.</para>
	/// </remarks>
	[Serializable]
	public class NpcSpawnerFeature : PointOfInterestFeature
	{
		public PointOfInterestNpcRole Role = PointOfInterestNpcRole.MonsterPack;

		[Tooltip("How many to keep alive; 0 picks by the site's size class.")]
		[Min(0)]
		public int Count;

		[Tooltip("Where the spawner stands in the site's frame, metres. Zero draws a seeded spot inside the footprint.")]
		public Vector2 Offset;

		[Tooltip("The spawn area's half-width as a share of the site's radius (packs, guards, townsfolk). Service NPCs stand on the spot.")]
		[Range(0.05f, 1.0f)]
		public float AreaShare = 0.5f;

		[Tooltip("Spawn each NPC as the site's race (model, faction, name). Off keeps the prefab's own race.")]
		public bool ApplySiteRace = true;

		/// <summary>The default count for a role at a size class (0 small, 1 medium, 2 large).</summary>
		public static int DefaultCount(PointOfInterestNpcRole role, int sizeClass)
		{
			int size = Mathf.Clamp(sizeClass, 0, 2);
			switch (role)
			{
				case PointOfInterestNpcRole.MonsterPack:
					return size == 0 ? 3 : size == 1 ? 5 : 8;
				case PointOfInterestNpcRole.Guard:
					return 2 + size;
				case PointOfInterestNpcRole.Townsfolk:
					return 2 + size * 2;
				default:
					return 1;
			}
		}

		/// <summary>Whether a role spawns standing on one spot (a vendor at a stall) rather than scattered.</summary>
		public static bool StandsOnSpot(PointOfInterestNpcRole role)
		{
			return role == PointOfInterestNpcRole.Banker || role == PointOfInterestNpcRole.Merchant || role == PointOfInterestNpcRole.Crafter;
		}

		public override void Build(PointOfInterestSiteContext context)
		{
			RaceTemplate race = PointOfInterestFeatureKit.SiteRace(context);
			PointOfInterestNpcTable table = PointOfInterestNpcTable.LoadOrDefaults();
			List<PointOfInterestNpcChoice> choices = table.Resolve(race, Role, context.Notes);
			List<SpawnableSettings> spawnables = PointOfInterestFeatureKit.NpcSpawnables(choices, ApplySiteRace ? race : null);
			if (spawnables.Count == 0)
			{
				PointOfInterestFeatureKit.Note(context, $"its {Role} spawner is empty: assign an NPC.");
			}

			bool onSpot = StandsOnSpot(Role);
			Vector2 local = Offset != Vector2.zero ? Offset : PointOfInterestFeatureKit.RandomLocal(context, onSpot ? 0.45f : 0.25f);
			int count = Count > 0 ? Count : DefaultCount(Role, context.Record.SizeClass);
			float area = onSpot ? 0.0f : Mathf.Max(3.0f, context.Record.Radius * AreaShare);
			ObjectSpawner spawner = PointOfInterestFeatureKit.AddSpawner(context, PointOfInterestFeatureKit.UniqueName(context, $"{Role} Spawner"), local, spawnables, count, area);
			// A list of distinct service NPCs must not stand two copies of one; a pack draws freely.
			spawner.UniqueSpawnables = onSpot && spawnables.Count > 1;
		}
	}

	/// <summary>
	/// The world boss's spawner, EMPTY (Jim, 2026-10-10: a designer assigns the boss), standing at the centre of an arena:
	/// a region over the lair's floor named "&lt;site&gt; Arena".
	/// </summary>
	/// <remarks>
	/// With <see cref="GateOnSiteSpawners"/>, the boss returns only once every other spawner the site has built is cleared
	/// (<see cref="SpawnersClearedCondition"/>); build this feature after the site's packs so it can see them.
	/// </remarks>
	[Serializable]
	public class BossSpawnerFeature : PointOfInterestFeature
	{
		[Tooltip("The arena's radius as a share of the site's footprint radius.")]
		[Range(0.2f, 1.0f)]
		public float ArenaShare = 0.6f;

		[Tooltip("The boss respawns only when the site's other spawners are all cleared.")]
		public bool GateOnSiteSpawners = true;

		public override void Build(PointOfInterestSiteContext context)
		{
			var others = new List<ObjectSpawner>(context.Root.GetComponentsInChildren<ObjectSpawner>(true));
			ObjectSpawner spawner = PointOfInterestFeatureKit.AddSpawner(context, PointOfInterestFeatureKit.UniqueName(context, "Boss Spawner"),
				Vector2.zero, new List<SpawnableSettings>(), 1, 0.0f);
			if (GateOnSiteSpawners && others.Count > 0)
			{
				spawner.TrueConditions.Add(new SpawnersClearedCondition() { Spawners = others });
			}
			if (spawner.TryGetComponent(out PendingSpawnerContent pending))
			{
				pending.Reason = $"The world boss of {PointOfInterestFeatureKit.SiteName(context)}, empty by design (Jim, 2026-10-10): assign the boss NPC, then remove this.";
			}
			EditorUtility.SetDirty(spawner);
			PointOfInterestFeatureKit.Note(context, "its boss spawner is empty by design: assign the boss.");

			// The arena: a region over the lair floor, for encounter scripts and a name on entry.
			GameObject arena = context.AddChild(PointOfInterestFeatureKit.SiteName(context) + " Arena");
			arena.transform.SetPositionAndRotation(context.Record.Position, Quaternion.Euler(0.0f, context.Record.Yaw, 0.0f));
			NetworkObject networkObject = PointOfInterestFeatureKit.AddNetworkObject(arena);
			float radius = Mathf.Max(8.0f, context.Record.Radius * ArenaShare);
			BoxCollider box = arena.AddComponent<BoxCollider>();
			box.isTrigger = true;
			box.size = new Vector3(radius * 2.0f, 30.0f, radius * 2.0f);
			box.center = new Vector3(0.0f, 7.5f, 0.0f);
			Region region = arena.AddComponent<Region>();
			region.Collider = box;
			FishNet.Component.Prediction.NetworkTrigger trigger = arena.GetComponent<FishNet.Component.Prediction.NetworkTrigger>();
			if (trigger != null)
			{
				trigger.SetLayers(RegionFeature.TriggerLayers);
				EditorUtility.SetDirty(trigger);
			}
			Trigger toast = AssetDatabase.LoadAssetAtPath<Trigger>(PointOfInterestFeatureKit.RegionNameTriggerPath);
			if (toast != null)
			{
				region.OnRegionEnter.Add(toast);
			}
			PointOfInterestFeatureKit.AssignSceneId(networkObject, context, 0xB055);
			EditorUtility.SetDirty(region);
		}
	}

	/// <summary>
	/// Gathering nodes at the site: one generated resource of the site's family (<see cref="GatheringResourceCatalogue"/>),
	/// drawn by its biome, kept alive by a spawner so a depleted node comes back.
	/// </summary>
	/// <remarks>
	/// Needs <see cref="GatheringContentGenerator.Generate"/> to have run; a resource whose node prefab does not exist yet is
	/// noted and skipped. A node is a NetworkObject spawned by its spawner rather than placed in the scene, because a placed
	/// node that depletes despawns for good (<see cref="Interactable.Despawn"/> only respawns through a spawner).
	/// </remarks>
	[Serializable]
	public class GatheringFeature : PointOfInterestFeature
	{
		[Tooltip("The resource families to draw from; empty takes the kind's own (GatheringResourceCatalogue.FamiliesFor).")]
		public List<GatheringFamily> Families = new List<GatheringFamily>();

		[Tooltip("Nodes kept alive; 0 picks by the site's size class (2 / 3 / 5).")]
		[Min(0)]
		public int Count;

		[Tooltip("Respawn delay range for a depleted node, seconds.")]
		public Vector2 RespawnSeconds = new Vector2(300.0f, 600.0f);

		[Tooltip("The area nodes scatter in, as a share of the site's radius.")]
		[Range(0.1f, 1.0f)]
		public float AreaShare = 0.7f;

		public override void Build(PointOfInterestSiteContext context)
		{
			IReadOnlyList<GatheringFamily> families = Families != null && Families.Count > 0
				? Families
				: GatheringResourceCatalogue.FamiliesFor(context.Record.Kind);
			if (families.Count == 0)
			{
				PointOfInterestFeatureKit.Note(context, $"a {context.Record.Kind} has no gathering family; set the feature's Families.");
				return;
			}

			GatheringFamily family = families[context.Random.Next(families.Count)];
			string biome = null;
			if (context.BiomeAt != null)
			{
				Biomes.BiomeTemplate template = context.BiomeAt(context.Record.Position.x, context.Record.Position.z);
				biome = template != null ? template.name : null;
			}
			GatheringResource resource = GatheringResourceCatalogue.Pick(family, biome, context.Random);
			GameObject prefab = GatheringContentGenerator.LoadPrefab(resource);
			NetworkObject networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
			if (networkObject == null)
			{
				PointOfInterestFeatureKit.Note(context, $"no node prefab for '{resource?.Node ?? family.ToString()}': run Generate Gathering Content.");
				return;
			}

			int size = Mathf.Clamp(context.Record.SizeClass, 0, 2);
			int count = Count > 0 ? Count : size == 0 ? 2 : size == 1 ? 3 : 5;
			var spawnables = new List<SpawnableSettings>
			{
				new SpawnableSettings
				{
					NetworkObject = networkObject,
					SpawnChance = 1.0f,
					MinimumRespawnTime = Mathf.Max(0.0f, RespawnSeconds.x),
					MaximumRespawnTime = Mathf.Max(RespawnSeconds.x, RespawnSeconds.y),
				},
			};
			PointOfInterestFeatureKit.AddSpawner(context, PointOfInterestFeatureKit.UniqueName(context, $"{resource.Node} Spawner"),
				PointOfInterestFeatureKit.RandomLocal(context, 0.2f), spawnables, count, Mathf.Max(3.0f, context.Record.Radius * AreaShare));
		}
	}
}
#endif
