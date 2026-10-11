using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishNet.Component.Prediction;
using FishNet.Object;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.PointsOfInterest
{
	/// <summary>
	/// Pins the gameplay half of generated points of interest (Jim, 2026-10-10): the gathering catalogue, the NPC role
	/// table, the recommended features per kind, and each feature built on a scene fixture — a waypoint with a SceneId and
	/// the site's unlock index, spawners tagged EditorOnly, a region named after the site, a dormant portal.
	/// </summary>
	[TestFixture]
	public class PointOfInterestGameplayTests
	{
		private Scene scene;
		private readonly List<GameObject> loose = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject go in loose)
			{
				if (go != null)
				{
					Object.DestroyImmediate(go);
				}
			}
			loose.Clear();
			if (scene.IsValid())
			{
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			}
		}

		private PointOfInterestSiteContext Site(POIType kind, int seed = 1234, int unlockIndex = 3, string race = "orc", byte sizeClass = 1)
		{
			if (!scene.IsValid())
			{
				scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			}
			var record = new PointOfInterestRecord
			{
				Id = 0x1234ABCD + seed,
				Kind = kind,
				Position = new Vector3(10.0f, 0.0f, -20.0f),
				Yaw = 30.0f,
				Radius = 20.0f,
				SizeClass = sizeClass,
				Name = "Testford",
				Race = race,
				SiteSeed = seed,
				UnlockIndex = unlockIndex,
			};
			var root = new GameObject("Testford");
			SceneManager.MoveGameObjectToScene(root, scene);
			return new PointOfInterestSiteContext
			{
				Scene = scene,
				Root = root.transform,
				Record = record,
				Random = new DeterministicRNG(seed),
				GroundAt = (x, z) => 0.0f,
				SlopeAt = (x, z) => 0.0f,
				Notes = new List<string>(),
			};
		}

		#region Gathering

		[Test]
		public void GatheringCatalogue_NamesAreUniqueAndNeverShareAnAddress()
		{
			var addresses = new HashSet<string>();
			foreach (GatheringResource resource in GatheringResourceCatalogue.All)
			{
				Assert.IsTrue(addresses.Add(resource.Node), $"node '{resource.Node}' repeats an address");
				Assert.IsTrue(addresses.Add(resource.Node + " Node"), $"prefab of '{resource.Node}' repeats an address");
				Assert.IsTrue(addresses.Add(resource.Item), $"item '{resource.Item}' repeats an address");
				Assert.That(resource.Tier, Is.InRange(1, 4), resource.Node);
				Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>($"{GatheringResourceCatalogue.VisualFolder}/{resource.Visual}.prefab"),
					$"'{resource.Node}' names a visual that does not exist: {resource.Visual}");
			}
			foreach (GatheringFamily family in System.Enum.GetValues(typeof(GatheringFamily)))
			{
				Assert.IsTrue(GatheringResourceCatalogue.All.Any(r => r.Family == family), $"no {family} resource");
			}
		}

		[Test]
		public void GatheringCatalogue_PicksDeterministicallyAndPrefersTheBiome()
		{
			GatheringResource a = GatheringResourceCatalogue.Pick(GatheringFamily.Crystal, "Volcanic", new DeterministicRNG(7));
			GatheringResource b = GatheringResourceCatalogue.Pick(GatheringFamily.Crystal, "Volcanic", new DeterministicRNG(7));
			Assert.AreSame(a, b);

			int volcanic = 0;
			var random = new DeterministicRNG(99);
			for (int i = 0; i < 200; ++i)
			{
				if (GatheringResourceCatalogue.Pick(GatheringFamily.Crystal, "Volcanic", random).Node == "Emberglass Cluster")
				{
					volcanic++;
				}
			}
			Assert.Greater(volcanic, 150, "a volcanic site should almost always draw volcanic glass");
			Assert.IsNotNull(GatheringResourceCatalogue.Pick(GatheringFamily.Ore, "Nowhere Biome", new DeterministicRNG(1)), "an unnamed biome still gets its family's node");

			foreach (POIType kind in new[] { POIType.OreVein, POIType.CrystalFormation, POIType.HerbGrove, POIType.AncientTree })
			{
				Assert.IsNotEmpty(GatheringResourceCatalogue.FamiliesFor(kind), kind.ToString());
			}
		}

		#endregion

		#region NPC table

		[Test]
		public void NpcTable_DefaultsServeEveryRaceAndOrcsFillOrcPacks()
		{
			PointOfInterestNpcTable table = PointOfInterestNpcTable.LoadOrDefaults();
			Assert.IsNotNull(table.Find("woodelf", "Humanoid", PointOfInterestNpcRole.Banker), "a banker for any race");
			Assert.IsNotNull(table.Find("orc", "Humanoid", PointOfInterestNpcRole.MonsterPack));
			Assert.IsNull(table.Find("goblin", "Humanoid", PointOfInterestNpcRole.MonsterPack), "unknown pack race resolves to nothing");
			Assert.IsNull(table.Find("human", "Humanoid", PointOfInterestNpcRole.Guard), "no guard prefab exists yet");

			var notes = new List<string>();
			Assert.IsEmpty(table.Resolve(null, PointOfInterestNpcRole.Townsfolk, notes));
			Assert.AreEqual(1, notes.Count, "an empty role is noted");
		}

		[Test]
		public void NpcTable_TheMostSpecificRowWins()
		{
			var prefab = new GameObject("npc");
			loose.Add(prefab);
			PointOfInterestNpcTable table = ScriptableObject.CreateInstance<PointOfInterestNpcTable>();
			try
			{
				PointOfInterestNpcEntry Row(string race, string category) => new PointOfInterestNpcEntry
				{
					Race = race, RaceCategory = category, Role = PointOfInterestNpcRole.Merchant,
					Choices = new List<PointOfInterestNpcChoice> { new PointOfInterestNpcChoice { Prefab = prefab } },
				};
				PointOfInterestNpcEntry any = Row("", ""), humanoid = Row("", "Humanoid"), elf = Row("woodelf", "");
				table.Entries = new List<PointOfInterestNpcEntry> { any, humanoid, elf };
				Assert.AreSame(elf, table.Find("woodelf", "Humanoid", PointOfInterestNpcRole.Merchant));
				Assert.AreSame(humanoid, table.Find("human", "Humanoid", PointOfInterestNpcRole.Merchant));
				Assert.AreSame(any, table.Find("slime", "Aberration", PointOfInterestNpcRole.Merchant));
				Assert.IsNull(table.Find("slime", "Aberration", PointOfInterestNpcRole.Banker));
			}
			finally
			{
				Object.DestroyImmediate(table);
			}
		}

		#endregion

		#region Defaults

		[Test]
		public void Defaults_EverySiteIsNamedAndDiscoverable_AndKindsGetTheirGameplay()
		{
			foreach (PointOfInterestKindInfo info in PointOfInterestKinds.All)
			{
				List<PointOfInterestFeature> features = PointOfInterestFeatureDefaults.For(info.Kind, 1);
				Assert.IsInstanceOf<RegionFeature>(features[0], info.Kind.ToString());
				Assert.IsTrue(features.Any(f => f is ExplorationFeature), info.Kind.ToString());
			}

			List<PointOfInterestFeature> village = PointOfInterestFeatureDefaults.For(POIType.Village, 0);
			Assert.IsTrue(village.Any(f => f is WaypointFeature) && village.Any(f => f is RespawnFeature));
			Assert.IsTrue(village.OfType<NpcSpawnerFeature>().Any(f => f.Role == PointOfInterestNpcRole.Merchant));

			int city = PointOfInterestFeatureDefaults.For(POIType.City, 2).OfType<NpcSpawnerFeature>().Count();
			int capital = PointOfInterestFeatureDefaults.For(POIType.Capital, 2).OfType<NpcSpawnerFeature>().Count();
			Assert.Greater(capital, city, "a capital gets more service NPCs than a city");

			List<PointOfInterestFeature> lair = PointOfInterestFeatureDefaults.For(POIType.BossLair, 2);
			Assert.IsInstanceOf<BossSpawnerFeature>(lair.Last(), "the boss comes after the packs it waits on");

			Assert.IsTrue(PointOfInterestFeatureDefaults.For(POIType.Portal, 1).Any(f => f is PortalFeature));
			Assert.IsTrue(PointOfInterestFeatureDefaults.For(POIType.Cave, 2).Any(f => f is DungeonEntranceFeature), "a large cave ends in a dungeon");
			Assert.IsFalse(PointOfInterestFeatureDefaults.For(POIType.Cave, 0).Any(f => f is DungeonEntranceFeature), "a small cave is a walk-in grotto");
			Assert.IsTrue(PointOfInterestFeatureDefaults.For(POIType.OreVein, 0).Any(f => f is GatheringFeature));
			Assert.IsTrue(PointOfInterestFeatureDefaults.NeedsUnlockIndex(PointOfInterestFeatureDefaults.For(POIType.Portal, 0)));
		}

		[Test]
		public void PortalScopeDraw_FollowsTheWeights()
		{
			Assert.AreEqual(PortalActivationScope.PerCharacter, PortalFeature.DrawScope(1, 1, 1, 0.0f));
			Assert.AreEqual(PortalActivationScope.WorldTimed, PortalFeature.DrawScope(1, 1, 1, 0.5f));
			Assert.AreEqual(PortalActivationScope.WorldPermanent, PortalFeature.DrawScope(1, 1, 1, 0.99f));
			Assert.AreEqual(PortalActivationScope.WorldPermanent, PortalFeature.DrawScope(0, 0, 1, 0.0f));
			Assert.AreEqual(PortalActivationScope.PerCharacter, PortalFeature.DrawScope(0, 0, 0, 0.7f), "no weights falls back to per character");
		}

		#endregion

		#region Builders

		[Test]
		public void Waypoint_GetsTheSiteIndexAndASceneId()
		{
			PointOfInterestSiteContext context = Site(POIType.Village);
			new WaypointFeature().Build(context);

			Waypoint waypoint = context.Root.GetComponentInChildren<Waypoint>(true);
			Assert.IsNotNull(waypoint, string.Join("\n", context.Notes));
			Assert.AreEqual(3, waypoint.WaypointIndex);
			Assert.AreEqual("Testford", waypoint.WaypointName);
			Assert.IsTrue(waypoint.GetComponent<NetworkObject>().IsSceneObject, "a generated scene NetworkObject must carry a SceneId");

			new WaypointFeature().Build(context);
			Assert.AreEqual(1, context.Root.GetComponentsInChildren<Waypoint>(true).Length, "one waypoint per site");
		}

		[Test]
		public void Waypoint_WithoutAnUnlockIndexBuildsNothingAndSaysSo()
		{
			PointOfInterestSiteContext context = Site(POIType.Village, unlockIndex: -1);
			new WaypointFeature().Build(context);
			Assert.IsNull(context.Root.GetComponentInChildren<Waypoint>(true));
			Assert.IsNotEmpty(context.Notes);
		}

		[Test]
		public void Spawners_AreEditorOnly_AndPacksTakeTheSiteRace()
		{
			PointOfInterestSiteContext context = Site(POIType.BanditCamp);
			new NpcSpawnerFeature { Role = PointOfInterestNpcRole.MonsterPack }.Build(context);
			new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Guard }.Build(context);
			new BossSpawnerFeature().Build(context);

			ObjectSpawner[] spawners = context.Root.GetComponentsInChildren<ObjectSpawner>(true);
			Assert.AreEqual(3, spawners.Length);
			foreach (ObjectSpawner spawner in spawners)
			{
				Assert.IsTrue(spawner.CompareTag(ObjectSpawner.EditorOnlyTag), spawner.name);
				Assert.IsNull(spawner.GetComponent<NetworkObject>(), "spawners are not networked");
			}

			ObjectSpawner pack = spawners[0];
			Assert.AreEqual(5, pack.MaxSpawnCount, "a medium pack is five");
			Assert.AreEqual(3, pack.Spawnables.Count, "the three orc prefabs");
			RaceTemplate orc = PointOfInterestNpcTable.FindRace("orc");
			Assert.IsTrue(pack.Spawnables.All(s => s is NPCSpawnableSettings npc && npc.FactionOverride == orc));

			Assert.IsEmpty(spawners[1].Spawnables, "no guard prefab: the spawner is left empty");
			Assert.IsTrue(context.Notes.Any(n => n.Contains("Guard")), "and noted");

			ObjectSpawner boss = spawners[2];
			Assert.IsEmpty(boss.Spawnables, "the boss spawner is empty by design");
			var gate = boss.TrueConditions.OfType<SpawnersClearedCondition>().Single();
			CollectionAssert.AreEquivalent(new[] { spawners[0], spawners[1] }, gate.Spawners);
		}

		[Test]
		public void Region_IsNamedAfterTheSite_AndTheDiscoveryRidesIt()
		{
			PointOfInterestSiteContext context = Site(POIType.Ruins);
			new RegionFeature().Build(context);
			new ExplorationFeature().Build(context);

			Region[] regions = context.Root.GetComponentsInChildren<Region>(true);
			Assert.AreEqual(1, regions.Length, "the exploration feature rides the region rather than making another");
			Region region = regions[0];
			Assert.AreEqual("Testford", region.Name);
			Assert.AreNotEqual(0, region.GetComponent<NetworkTrigger>().GetLayers().value, "a region with no layers detects nothing");
			Assert.IsTrue(region.GetComponent<NetworkObject>().IsSceneObject);
			Assert.AreEqual(3, region.GetComponent<PointOfInterestDiscovery>().SiteIndex);
			Assert.AreEqual(2, region.OnRegionEnter.Count, "the name toast and the discovery trigger");
		}

		[Test]
		public void Portal_IsDormantWithTheSiteIndex_AndTheSameSeedDrawsTheSamePortal()
		{
			PointOfInterestSiteContext first = Site(POIType.Portal, seed: 77);
			new PortalFeature().Build(first);
			PortalActivation a = first.Root.GetComponentInChildren<PortalActivation>(true);
			Assert.IsNotNull(a, string.Join("\n", first.Notes));
			Assert.AreEqual(3, a.PortalIndex);
			Assert.IsNull(a.Key, "the key is the designer's");
			Assert.IsFalse(a.HasRequirements, "no generated requirements");
			Assert.IsTrue(a.GetComponent<SceneTeleporter>() != null || a.GetComponent<Teleporter>() != null);

			PointOfInterestSiteContext second = Site(POIType.Portal, seed: 77);
			new PortalFeature().Build(second);
			PortalActivation b = second.Root.GetComponentInChildren<PortalActivation>(true);
			Assert.AreEqual(a.Scope, b.Scope);
			Assert.AreEqual(a.DurationSeconds, b.DurationSeconds);
			Assert.AreEqual(a.GetComponent<SceneTeleporter>() != null, b.GetComponent<SceneTeleporter>() != null);
		}

		[Test]
		public void DungeonEntrance_HasAnEmptyDungeonName()
		{
			PointOfInterestSiteContext context = Site(POIType.DungeonEntrance);
			new DungeonEntranceFeature().Build(context);
			DungeonEntrance entrance = context.Root.GetComponentInChildren<DungeonEntrance>(true);
			Assert.IsNotNull(entrance);
			Assert.IsEmpty(entrance.DungeonName);
			Assert.IsTrue(entrance.GetComponent<NetworkObject>().IsSceneObject);
			Assert.IsFalse(entrance.GetComponent<MapMarker>().ShowOnWorldMap, "the site is the map entry, behind discovery");
		}

		#endregion
	}
}
