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
	/// from <see cref="PointOfInterestNpcTable"/> by the site's race (2026-10-10: auto spawners).
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
	/// The world boss's spawner, EMPTY (2026-10-10: a designer assigns the boss), standing at the centre of an arena:
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
				pending.Reason = $"The world boss of {PointOfInterestFeatureKit.SiteName(context)}, empty by design (2026-10-10): assign the boss NPC, then remove this.";
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
	/// Gathering nodes at the site: generated resources of the site's families (<see cref="GatheringResourceCatalogue"/>),
	/// drawn by its biome, each kept alive on its own spot by a spawner so a depleted node comes back where it stood.
	/// </summary>
	/// <remarks>
	/// <para>Needs <see cref="GatheringContentGenerator.Generate"/> to have run; a resource whose node prefab does not exist
	/// yet is noted and skipped. A node is a NetworkObject spawned by its spawner rather than placed in the scene, because a
	/// placed node that depletes despawns for good (<see cref="Interactable.Despawn"/> only respawns through a spawner).</para>
	/// <para><b>The site's own family.</b> Most nodes are of the kind's first family (a herb grove's herbs); from three nodes
	/// on, the last is of one of its others (a mushroom cluster among the herbs). The family used to be one coin toss for the
	/// whole site, so half of all herb groves grew nothing but mushrooms.</para>
	/// <para><b>One spawner per node, on the ground.</b> A spawner that scatters (<c>RandomSpawnPosition</c>) raises what it
	/// spawns by its collider's whole height (SpawnableSettings.YOffset, sized for things that fall to the ground); a node's
	/// pivot is its base and it never falls, so every node hovered its own height in the air (Flo Monolith's spawn table,
	/// 2026-10-12: Mirewort 0.8 m, Iron Vein 1.2 m, the Ancient Oak 27 m). Each node therefore has a spawner of its own
	/// standing on its spot (spawned there exactly, no offset), drawn from the site's seed apart from the others, off the
	/// props the site laid, out of the water (sea-floor nodes aside) and off steep ground.</para>
	/// </remarks>
	[Serializable]
	public class GatheringFeature : PointOfInterestFeature
	{
		/// <summary>The steepest ground a node is put on while a gentler spot can be found, degrees.</summary>
		private const float MaxSlopeDegrees = 40.0f;
		/// <summary>Draws per node before the best of them is taken.</summary>
		private const int Attempts = 16;

		[Tooltip("The resource families to draw from, the main one first; empty takes the kind's own (GatheringResourceCatalogue.FamiliesFor).")]
		public List<GatheringFamily> Families = new List<GatheringFamily>();

		[Tooltip("Nodes kept alive; 0 picks by the site's size class (2 / 3 / 5).")]
		[Min(0)]
		public int Count;

		[Tooltip("Respawn delay range for a depleted node, seconds.")]
		public Vector2 RespawnSeconds = new Vector2(300.0f, 600.0f);

		[Tooltip("The area nodes stand in, as a share of the site's radius.")]
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

			// What the biome offers: its template's Gathering Resources (2026-10-16; the catalogue's rows named biomes before).
			Biomes.BiomeTemplate template = context.BiomeAt != null ? context.BiomeAt(context.Record.Position.x, context.Record.Position.z) : null;
			IReadOnlyList<string> offered = BiomeTemplateArt.Gathering(template);

			int size = Mathf.Clamp(context.Record.SizeClass, 0, 2);
			int count = Count > 0 ? Count : size == 0 ? 2 : size == 1 ? 3 : 5;
			GatheringResource main = GatheringResourceCatalogue.Pick(families[0], offered, context.Random);
			GatheringResource other = families.Count > 1 && count >= 3
				? GatheringResourceCatalogue.Pick(families[1 + context.Random.Next(families.Count - 1)], offered, context.Random)
				: null;
			NetworkObject mainNode = NodeObject(context, main, families[0]);
			NetworkObject otherNode = other != null ? NodeObject(context, other, other.Family) : null;
			if (mainNode == null && otherNode == null)
			{
				return;
			}

			bool underwater = families[0] == GatheringFamily.Marine;
			List<Vector2> spots = Spots(context, count, underwater);
			for (int i = 0; i < spots.Count; ++i)
			{
				bool isOther = otherNode != null && (i == spots.Count - 1 || mainNode == null);
				GatheringResource resource = isOther ? other : main;
				NetworkObject networkObject = isOther ? otherNode : mainNode;
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
				// Area 0: the node appears on the spawner's own spot (the ground there), never raised by its height.
				ObjectSpawner spawner = PointOfInterestFeatureKit.AddSpawner(context, PointOfInterestFeatureKit.UniqueName(context, $"{resource.Node} Spawner {i + 1}"),
					spots[i], spawnables, 1, 0.0f);
				// Turned its own way, so a bed of one plant does not stand in step.
				spawner.transform.rotation = Quaternion.Euler(0.0f, context.Random.NextFloat() * 360.0f, 0.0f);
				EditorUtility.SetDirty(spawner);
			}
		}

		/// <summary>A resource's node prefab's NetworkObject, or null with a note when Generate Gathering Content has not made it.</summary>
		private static NetworkObject NodeObject(PointOfInterestSiteContext context, GatheringResource resource, GatheringFamily family)
		{
			GameObject prefab = GatheringContentGenerator.LoadPrefab(resource);
			NetworkObject networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
			if (networkObject == null)
			{
				PointOfInterestFeatureKit.Note(context, $"no node prefab for '{resource?.Node ?? family.ToString()}': run Generate Gathering Content.");
			}
			return networkObject;
		}

		/// <summary>
		/// Where the nodes stand, in the site's frame: seeded spots within <see cref="AreaShare"/> of its radius. Each is the
		/// first of a few draws that is clear of the spots before it and of the site's props, on dry (unless
		/// <paramref name="underwater"/>) and not too steep ground; the clearest draw when none is.
		/// </summary>
		private List<Vector2> Spots(PointOfInterestSiteContext context, int count, bool underwater)
		{
			PointOfInterestRecord record = context.Record;
			float reach = Mathf.Max(3.0f, record.Radius * AreaShare);
			float gap = Mathf.Clamp(reach * 0.35f, 1.5f, 4.0f);
			List<Vector3> props = PropsNear(context, reach + 25.0f);
			Quaternion turn = Quaternion.Euler(0.0f, record.Yaw, 0.0f);
			var spots = new List<Vector2>(count);
			var placed = new List<Vector2>(count);
			for (int n = 0; n < count; ++n)
			{
				Vector2 best = Vector2.zero;
				Vector2 bestWorld = new Vector2(record.Position.x, record.Position.z);
				float bestScore = float.NegativeInfinity;
				for (int attempt = 0; attempt < Attempts; ++attempt)
				{
					float r = reach * Mathf.Sqrt(context.Random.NextFloat());
					float a = context.Random.NextFloat() * Mathf.PI * 2.0f;
					var local = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
					Vector3 offset = turn * new Vector3(local.x, 0.0f, local.y);
					var world = new Vector2(record.Position.x + offset.x, record.Position.z + offset.z);

					// How far clear of everything it must keep away from (negative: inside it), less what the ground costs.
					float score = gap * 4.0f;
					foreach (Vector2 other in placed)
					{
						score = Mathf.Min(score, Vector2.Distance(world, other) - gap);
					}
					foreach (Vector3 prop in props)
					{
						score = Mathf.Min(score, Vector2.Distance(world, new Vector2(prop.x, prop.y)) - prop.z);
					}
					if (!underwater && context.Water != null && context.Water.KindAt(world.x, world.y) != WaterKind.None)
					{
						score -= 100.0f;
					}
					float slope = context.SlopeAt != null ? context.SlopeAt(world.x, world.y) : 0.0f;
					if (slope > MaxSlopeDegrees)
					{
						score -= slope - MaxSlopeDegrees;
					}
					if (score > bestScore)
					{
						bestScore = score;
						best = local;
						bestWorld = world;
					}
					if (score >= 0.0f)
					{
						break;
					}
				}
				spots.Add(best);
				placed.Add(bestWorld);
			}
			return spots;
		}

		/// <summary>
		/// The props laid so far within <paramref name="reach"/> of the site, as (east, north, keep-clear radius): the
		/// footprint of each prefab's meshes at the prop's scale and half a metre more, so a node never grows out of a stone.
		/// </summary>
		private static List<Vector3> PropsNear(PointOfInterestSiteContext context, float reach)
		{
			var near = new List<Vector3>();
			PointOfInterestPropSink sink = context.Sink;
			if (sink == null)
			{
				return near;
			}
			var centre = new Vector2(context.Record.Position.x, context.Record.Position.z);
			var radii = new Dictionary<int, float>();
			foreach (ScenePropSet.Prop prop in sink.Props)
			{
				var at = new Vector2(prop.Position.x, prop.Position.z);
				if ((at - centre).sqrMagnitude > reach * reach || prop.Prototype < 0 || prop.Prototype >= sink.Prototypes.Count)
				{
					continue;
				}
				if (!radii.TryGetValue(prop.Prototype, out float radius))
				{
					radius = FootprintRadius(sink.Prototypes[prop.Prototype].Prefab);
					radii[prop.Prototype] = radius;
				}
				float scale = Mathf.Max(Mathf.Abs(prop.Scale.x), Mathf.Abs(prop.Scale.z));
				near.Add(new Vector3(at.x, at.y, radius * scale + 0.5f));
			}
			return near;
		}

		/// <summary>
		/// The radius round a prefab's origin that holds its meshes' footprint, metres. From the meshes' own bounds placed
		/// under the root (a prefab asset's renderers report no bounds outside a scene).
		/// </summary>
		private static float FootprintRadius(GameObject prefab)
		{
			if (prefab == null)
			{
				return 0.5f;
			}
			float radius = 0.0f;
			Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
			foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
			{
				Mesh mesh = filter.sharedMesh;
				if (mesh == null)
				{
					continue;
				}
				Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
				Vector3 min = mesh.bounds.min, max = mesh.bounds.max;
				for (int corner = 0; corner < 8; ++corner)
				{
					Vector3 p = matrix.MultiplyPoint3x4(new Vector3((corner & 1) != 0 ? max.x : min.x, (corner & 2) != 0 ? max.y : min.y, (corner & 4) != 0 ? max.z : min.z));
					radius = Mathf.Max(radius, new Vector2(p.x, p.z).magnitude);
				}
			}
			return radius > 0.0f ? radius : 0.5f;
		}
	}
}
#endif
