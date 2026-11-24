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
	/// Pins the gameplay half of generated points of interest: the gathering catalogue, the NPC role
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
			/* Each node is one template and one networked prefab: their names never repeat, and no prefab's address is another
			 * row's node or item. An item is its own asset type (a CachedScriptableObject's ID is its type's name and its own), so
			 * it may carry its node's name (Galerna-stone, Dulse: half the catalogue) and several nodes may yield one item
			 * (Copper Ore); the generator registers entries by GUID and the game loads them by label, never by address. */
			var nodes = new HashSet<string>();
			var prefabs = new HashSet<string>();
			foreach (GatheringResource resource in GatheringResourceCatalogue.All)
			{
				Assert.IsTrue(nodes.Add(resource.Node), $"node '{resource.Node}' repeats");
				Assert.IsTrue(prefabs.Add(resource.Node + " Node"), $"prefab of '{resource.Node}' repeats an address");
				Assert.That(resource.Tier, Is.InRange(1, 4), resource.Node);
				Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path($"{GatheringResourceCatalogue.VisualFolder}/{resource.Visual}.prefab")),
					$"'{resource.Node}' names a visual that does not exist: {resource.Visual}");
			}
			foreach (GatheringResource resource in GatheringResourceCatalogue.All)
			{
				Assert.IsFalse(prefabs.Contains(resource.Node) || prefabs.Contains(resource.Item), $"'{resource.Node}' or its item is spelled like a node prefab");
			}
			foreach (GatheringFamily family in System.Enum.GetValues(typeof(GatheringFamily)))
			{
				// Bone is a family no site gathers yet and no row stocks.
				if (family == GatheringFamily.Bone)
				{
					continue;
				}
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
			PointOfInterestNpcEntry orcs = table.Find("orc", "Humanoid", PointOfInterestNpcRole.MonsterPack);
			Assert.IsNotNull(orcs);
			Assert.AreEqual("orc", orcs.Race, "the orcs' own row wins over the raiders");
			// every role answers. Any other race's camp gets raiders copied for it; a town its own guards and folk.
			PointOfInterestNpcEntry goblins = table.Find("goblin", "Humanoid", PointOfInterestNpcRole.MonsterPack);
			Assert.IsNotNull(goblins, "a goblin camp's pack");
			Assert.IsTrue(goblins.CloneForRace, "raiders copied for the goblins");
			PointOfInterestNpcEntry guards = table.Find("human", "Humanoid", PointOfInterestNpcRole.Guard);
			Assert.IsNotNull(guards, "guards for any town");
			Assert.IsTrue(guards.CloneForRace, "of the town's race");
			Assert.IsNotNull(table.Find(null, null, PointOfInterestNpcRole.Townsfolk), "townsfolk for any town");
			// A beast race's lair is its own beast; one with no body of its own draws from the generated beasts.
			PointOfInterestNpcEntry boars = table.Find("direboar", "Beast", PointOfInterestNpcRole.MonsterPack);
			Assert.AreEqual("direboar", boars.Race);
			Assert.IsFalse(boars.CloneForRace, "a beast keeps its own kind");
			Assert.AreEqual("Beast", table.Find("giantspider", "Beast", PointOfInterestNpcRole.MonsterPack).RaceCategory);
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
			// a dungeon's entrance can be inside a cave: a large cave's great cavern holds one; a small cave, a
			// grotto or a mine none.
			foreach (POIType cave in new[] { POIType.Cave, POIType.IceCave, POIType.LavaTube })
			{
				Assert.IsTrue(PointOfInterestFeatureDefaults.For(cave, 2).Any(f => f is DungeonEntranceFeature), $"a large {cave} holds a dungeon entrance");
				Assert.IsFalse(PointOfInterestFeatureDefaults.For(cave, 0).Any(f => f is DungeonEntranceFeature), $"a small {cave} is walked in and out of");
			}
			foreach (POIType kind in new[] { POIType.Grotto, POIType.Mine })
			{
				Assert.IsFalse(PointOfInterestFeatureDefaults.For(kind, 2).Any(f => f is DungeonEntranceFeature), $"a {kind} holds no dungeon entrance");
			}
			Assert.IsTrue(PointOfInterestFeatureDefaults.For(POIType.DungeonEntrance, 1).Any(f => f is DungeonEntranceFeature), "a dungeon entrance site does");
			Assert.IsTrue(PointOfInterestFeatureDefaults.For(POIType.OreVein, 0).Any(f => f is GatheringFeature));
			Assert.IsTrue(PointOfInterestFeatureDefaults.For(POIType.Mine, 1).OfType<PointOfInterestChamberFeature>().Any(c => c.Features.Any(f => f is GatheringFeature)),
				"a mine's ore is gathered in its stope");
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
		public void GateStone_IsAWaypointOfTheGateStoneKind_AndTakesTheSiteIndex()
		{
			PointOfInterestSiteContext context = Site(POIType.StoneCircle);
			new GateStoneFeature().Build(context);

			Waypoint stone = context.Root.GetComponentInChildren<Waypoint>(true);
			Assert.IsNotNull(stone, string.Join("\n", context.Notes));
			Assert.AreEqual(FishMMO.Shared.Core.WaypointKind.GateStone, stone.Kind);
			Assert.AreEqual(3, stone.WaypointIndex);
			Assert.IsTrue(stone.GetComponent<NetworkObject>().IsSceneObject, "a generated scene NetworkObject must carry a SceneId");

			new WaypointFeature().Build(context);
			Assert.AreEqual(1, context.Root.GetComponentsInChildren<Waypoint>(true).Length, "a site has a waypoint or a Gate Stone, not both");
		}

		[Test]
		public void GateStones_AreTheOldStoneSitesDefault()
		{
			foreach (POIType kind in new[] { POIType.StoneCircle, POIType.LeyNexus, POIType.Henge, POIType.ElfWaystone, POIType.RuneStone })
			{
				Assert.AreEqual(1, PointOfInterestFeatureDefaults.For(kind, 1).Count(f => f is GateStoneFeature), kind.ToString());
			}
			Assert.IsFalse(PointOfInterestFeatureDefaults.For(POIType.Village, 1).Any(f => f is GateStoneFeature), "a village has a waypoint");
			Assert.IsTrue(PointOfInterestFeatureDefaults.NeedsUnlockIndex(PointOfInterestFeatureDefaults.For(POIType.StoneCircle, 0)), "a Gate Stone needs the site's index");
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
			// A merchant, not a guard: a guard is copied for the site's race, which would save an "Orc a guard" prefab on every
			// run (the guard rows are pinned by NpcTable_DefaultsServeEveryRaceAndOrcsFillOrcPacks).
			new NpcSpawnerFeature { Role = PointOfInterestNpcRole.Merchant }.Build(context);
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

			Assert.AreEqual(1, spawners[1].Spawnables.Count, "the merchant");

			ObjectSpawner boss = spawners[2];
			Assert.IsEmpty(boss.Spawnables, "the boss spawner is empty by design");
			var gate = boss.TrueConditions.OfType<SpawnersClearedCondition>().Single();
			CollectionAssert.AreEquivalent(new[] { spawners[0], spawners[1] }, gate.Spawners);
		}

		/// <summary>Lays a kit piece at the site as PropsFeature would record it (a stand-in prefab named as the kit names it).</summary>
		private GameObject Lay(PointOfInterestSiteContext context, string pieceId, Vector3 at, float yaw)
		{
			var prefab = new GameObject(StructurePieces.Prefix + pieceId + "_0");
			SceneManager.MoveGameObjectToScene(prefab, scene);
			loose.Add(prefab);
			context.Laid.Add(new PointOfInterestSiteContext.PlacedProp { Prefab = prefab, Position = at, Rotation = Quaternion.Euler(0f, yaw, 0f), Scale = Vector3.one });
			return prefab;
		}

		[Test]
		public void Posts_AStallsKeeperStandsBehindItsCounter_FacingTheCustomers()
		{
			PointOfInterestSiteContext context = Site(POIType.Town);
			Lay(context, PointOfInterestNpcPosts.StallPiece, new Vector3(15f, 0f, -20f), 90f);
			var posts = new PointOfInterestNpcPosts(context);

			Assert.IsTrue(posts.TryClaim(new[] { NpcPostRule.AtStall() }, out NpcPost post));
			Vector3 expected = new Vector3(15f, 0f, -20f) + Quaternion.Euler(0f, 90f, 0f) * PointOfInterestNpcPosts.StallSpot;
			Assert.Less(Vector3.Distance(expected, post.Position), 0.001f, "between the counter and the shelf");
			Assert.Less(Vector3.Angle(Quaternion.Euler(0f, 90f, 0f) * Vector3.back, post.Facing * Vector3.forward), 0.5f, "facing out over the counter");
			Assert.IsFalse(posts.TryClaim(new[] { NpcPostRule.AtStall() }, out _), "one keeper to a stall");
		}

		[Test]
		public void Posts_AKeeperStandsOutBeforeTheDoor_TwoToABuilding_SideBySide()
		{
			PointOfInterestSiteContext context = Site(POIType.Town);
			Vector3 at = new Vector3(30f, 0f, -10f);
			Lay(context, "Stables", at, 45f);
			StructureDoor door = StructurePieces.DoorsOf(StructurePieces.Find("Stables"), 0)[0];
			Quaternion turn = Quaternion.Euler(0f, 45f, 0f);
			Vector3 across = turn * door.Across, inward = turn * door.Inward;
			across.y = inward.y = 0f;
			Vector3 middle = at + turn * door.Hinge + across.normalized * (door.Width * 0.5f);
			Vector3 expected = middle - inward.normalized * PointOfInterestNpcPosts.DoorStandOff;
			expected.y = 0f;
			var rules = new[] { NpcPostRule.AtBuilding("Stables") };
			var posts = new PointOfInterestNpcPosts(context);

			Assert.IsTrue(posts.TryClaim(rules, out NpcPost first));
			Assert.Less(Vector3.Distance(expected, first.Position), 0.001f, "out on the street before the middle of its door");
			Assert.Less(Vector3.Angle(-inward, first.Facing * Vector3.forward), 0.5f, "its back to the door");
			Assert.IsTrue(posts.TryClaim(rules, out NpcPost second), "a second keeper shares the building");
			Assert.AreEqual(PointOfInterestNpcPosts.Spacing, Vector3.Distance(first.Position, second.Position), 0.001f, "beside the first");
			Assert.IsFalse(posts.TryClaim(rules, out _), "no third");
			Assert.IsFalse(new PointOfInterestNpcPosts(context).TryClaim(new[] { NpcPostRule.AtBuilding("Smithy") }, out _), "a building the site did not lay offers nothing");
		}

		[Test]
		public void Posts_TheMarketTakesAnyNumber_RoundTheMiddleOfItsStalls()
		{
			PointOfInterestSiteContext context = Site(POIType.Town);
			Lay(context, PointOfInterestNpcPosts.StallPiece, new Vector3(0f, 0f, 0f), 0f);
			Lay(context, PointOfInterestNpcPosts.StallPiece, new Vector3(10f, 0f, 0f), 0f);
			var posts = new PointOfInterestNpcPosts(context);
			Assert.IsTrue(posts.TryMarketMiddle(out Vector3 middle));
			Assert.Less(Vector3.Distance(new Vector3(5f, 0f, 0f), middle), 0.001f);
			var taken = new List<Vector3>();
			for (int i = 0; i < 8; i++)
			{
				Assert.IsTrue(posts.TryClaim(new[] { NpcPostRule.AtMarket() }, out NpcPost post));
				Assert.GreaterOrEqual(Vector3.Distance(middle, post.Position), PointOfInterestNpcPosts.MarketRing - 0.001f);
				Assert.IsTrue(taken.TrueForAll(p => Vector3.Distance(p, post.Position) > 1f), "none on another");
				taken.Add(post.Position);
			}
		}

		[Test]
		public void Seat_StandsTheTemplatesServicesAtTheirBuildings_AndACapitalKeepsOneOfEach()
		{
			PointOfInterestSiteContext context = Site(POIType.Capital, sizeClass: 2);
			Vector3 stallA = new Vector3(12f, 0f, -20f), stallB = new Vector3(16f, 0f, -20f);
			Vector3 bank = new Vector3(-20f, 0f, 0f), tower = new Vector3(40f, 0f, -40f);
			Lay(context, PointOfInterestNpcPosts.StallPiece, stallA, 0f);
			Lay(context, PointOfInterestNpcPosts.StallPiece, stallB, 0f);
			Lay(context, "CountingHouse", bank, 0f);
			Lay(context, "WizardTower", tower, 0f);
			// The capital template's service roles, as its features make them.
			foreach (PointOfInterestNpcRole role in new[] { PointOfInterestNpcRole.Merchant, PointOfInterestNpcRole.Merchant, PointOfInterestNpcRole.Banker, PointOfInterestNpcRole.Crafter })
			{
				new NpcSpawnerFeature { Role = role }.Build(context);
			}
			GameObject Of(PointOfInterestNpcRole role) => PointOfInterestNpcTable.LoadOrDefaults().Resolve(null, role).First().Prefab;
			GameObject merchant = Of(PointOfInterestNpcRole.Merchant), banker = Of(PointOfInterestNpcRole.Banker), crafter = Of(PointOfInterestNpcRole.Crafter);

			int added = PointOfInterestServiceNpcs.Seat(context);

			ObjectSpawner[] spawners = context.Root.GetComponentsInChildren<ObjectSpawner>(true);
			ObjectSpawner[] Spawning(GameObject prefab) => spawners.Where(s => s.Spawnables.Any(n => n.NetworkObject.gameObject == prefab)).ToArray();
			ObjectSpawner[] merchants = Spawning(merchant);
			Assert.AreEqual(2, merchants.Length, "the template's two merchants, no third");
			foreach (ObjectSpawner m in merchants)
			{
				float nearest = Mathf.Min(Vector3.Distance(m.transform.position, stallA), Vector3.Distance(m.transform.position, stallB));
				Assert.Less(nearest, 1f, $"{m.name} behind a market stall");
			}
			Assert.Less(Vector3.Distance(merchants[0].transform.position, merchants[1].transform.position), 5f, "the two at the two stalls");
			Assert.Greater(Vector3.Distance(merchants[0].transform.position, merchants[1].transform.position), 1f);
			Assert.Less(Vector3.Distance(Spawning(banker).Single().transform.position, bank), 20f, "the banker at the counting house");
			Assert.Less(Vector3.Distance(Spawning(crafter).Single().transform.position, tower), 20f, "the ability crafter at the wizard's tower");

			var spawned = spawners.SelectMany(s => s.Spawnables).Select(n => n.NetworkObject.gameObject).ToList();
			Assert.AreEqual(spawned.Count - 1, spawned.Distinct().Count(), "one of each service; only the template's two merchants share a prefab");
			Assert.AreEqual(spawners.Length - 4, added, "every spawner but the template's four was added");
			RaceTemplate orc = PointOfInterestNpcTable.FindRace("orc");
			foreach (ObjectSpawner spawner in spawners)
			{
				Assert.IsTrue(spawner.CompareTag(ObjectSpawner.EditorOnlyTag), spawner.name);
				Assert.IsTrue(spawner.Spawnables.All(n => n is NPCSpawnableSettings npc && npc.FactionOverride == orc), $"{spawner.name} takes the site's race");
			}

			Vector3[] before = spawners.Select(s => s.transform.position).ToArray();
			Assert.AreEqual(0, PointOfInterestServiceNpcs.Seat(context), "a second pass finds every service there");
			CollectionAssert.AreEqual(before, context.Root.GetComponentsInChildren<ObjectSpawner>(true).Select(s => s.transform.position).ToArray(), "and stands each where it stood");
		}

		[Test]
		public void Seat_AddsNothingAtASettlementWithoutTheBuildings()
		{
			PointOfInterestSiteContext city = Site(POIType.City, seed: 99);
			Assert.AreEqual(0, PointOfInterestServiceNpcs.Seat(city), "only a capital keeps every service; a city keeps what its buildings bring");
			PointOfInterestSiteContext camp = Site(POIType.BanditCamp, seed: 98);
			Lay(camp, "Smithy", new Vector3(10f, 0f, -20f), 0f);
			Assert.AreEqual(0, PointOfInterestServiceNpcs.Seat(camp), "a hostile site's smithy brings no smith");
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
			Assert.IsFalse(entrance.GetComponent<MapMarker>().ShowOnMinimap, "the baked entry marks it on the minimap too; the streamed marker would draw it twice");
		}

		[Test]
		public void ACryptsDoor_IsMadeADungeonEntrance_AndTheSiteIsMarkedOne()
		{
			// a crypt's open door over its stair stood as scenery, no entrance, no mark.
			PointOfInterestSiteContext context = Site(POIType.Crypt);
			ScenePointOfInterest point = context.Root.gameObject.AddComponent<ScenePointOfInterest>();
			point.Apply(context.Record);
			var door = new GameObject(StructurePieces.Prefix + "CryptEntrance_0");
			SceneManager.MoveGameObjectToScene(door, scene);
			context.Laid.Add(new PointOfInterestSiteContext.PlacedProp { Prefab = door, Position = context.Record.Position, Rotation = Quaternion.identity, Scale = Vector3.one });

			PointOfInterestEntrances.Mark(context);

			Assert.IsNotNull(context.Root.GetComponentInChildren<DungeonEntrance>(true), "its door is an entrance");
			Assert.IsTrue(point.HoldsDungeon);
			MapPointOfInterestDetails entry = point.ToDetails();
			Assert.AreEqual(MapMarkerType.DungeonEntrance, entry.Type, "drawn as a dungeon entrance, on the map and under its filter");
			StringAssert.StartsWith("Dungeon entrance (crypt)", entry.Description);

			PointOfInterestEntrances.Mark(context);
			Assert.AreEqual(1, context.Root.GetComponentsInChildren<DungeonEntrance>(true).Length, "never a second entrance");
		}

		[Test]
		public void ASiteWithNoDungeonDoor_GetsNoEntrance()
		{
			PointOfInterestSiteContext context = Site(POIType.Graveyard);
			ScenePointOfInterest point = context.Root.gameObject.AddComponent<ScenePointOfInterest>();
			point.Apply(context.Record);
			var tomb = new GameObject(StructurePieces.Prefix + "Mausoleum_0");
			SceneManager.MoveGameObjectToScene(tomb, scene);
			context.Laid.Add(new PointOfInterestSiteContext.PlacedProp { Prefab = tomb, Position = context.Record.Position, Rotation = Quaternion.identity, Scale = Vector3.one });

			PointOfInterestEntrances.Mark(context);

			Assert.IsNull(context.Root.GetComponentInChildren<DungeonEntrance>(true));
			Assert.IsFalse(point.HoldsDungeon);
			Assert.AreEqual(MapMarkerType.Graveyard, point.ToDetails().Type);
		}

		#endregion
	}
}
