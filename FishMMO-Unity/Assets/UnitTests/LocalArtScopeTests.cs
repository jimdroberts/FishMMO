using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The LOCAL reference gate (<see cref="LocalArtScope"/>): a scene outside Assets/LOCAL is painted
	/// with committed references whatever sidecars exist, a LOCAL scene with LOCAL terrain data gets the
	/// overrides, and the rule-override keys of <see cref="BiomeLocalArt"/>. Pure: paths, in-memory
	/// objects and a fake lookup — no AssetDatabase, no scene.
	/// </summary>
	/// <remarks>
	/// The file-level guard (nothing committed names a LOCAL GUID) is
	/// <c>BiomeArtStorageTests.NoCommittedAssetReferencesAnythingUnderAssetsLocal</c>; these pin the
	/// decision that keeps that guard green when a scene is painted.
	/// </remarks>
	[TestFixture]
	public class LocalArtScopeTests
	{
		private const string CommittedScene = "Assets/Scenes/WorldScene/Earth/Harbor.unity";
		private const string CommittedTerrain = "Assets/Scenes/WorldScene/Earth/Harbor/Terrain/Harbor 0 0.asset";
		private const string LocalScene = "Assets/LOCAL/SceneCopies/Harbor/Harbor LOCAL.unity";
		private const string LocalTerrain = "Assets/LOCAL/SceneCopies/Harbor/Terrain/Harbor 0 0.asset";

		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
		}

		private T Track<T>(T o) where T : Object
		{
			created.Add(o);
			return o;
		}

		private GameObject Prefab(string name) => Track(new GameObject(name));

		private BiomeTemplate Biome(string name)
		{
			var biome = Track(ScriptableObject.CreateInstance<BiomeTemplate>());
			biome.name = name;
			return biome;
		}

		/// <summary>A LOCAL folder in memory, counting every question asked of it.</summary>
		private sealed class FakeReferences : ILocalArtReferences
		{
			public readonly Dictionary<(BiomeTemplate, string), BiomeLocalArt.SlotOverride> Slots = new Dictionary<(BiomeTemplate, string), BiomeLocalArt.SlotOverride>();
			public BiomeLocalArt Sidecar;
			public readonly Dictionary<string, TerrainLayer> Layers = new Dictionary<string, TerrainLayer>();
			public readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
			public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
			public int Asked;

			public BiomeLocalArt.SlotOverride Slot(BiomeTemplate biome, string slot)
			{
				Asked++;
				return Slots.TryGetValue((biome, slot), out var o) ? o : null;
			}

			public BiomeLocalArt.RuleOverride Rule(BiomeTemplate biome, string slot, PrefabSpawnRule rule)
			{
				Asked++;
				return Sidecar != null && Sidecar.Biome == biome ? Sidecar.FindRule(slot, rule) : null;
			}

			public TerrainLayer NamedLayer(TerrainLayer committed)
			{
				Asked++;
				return committed != null && Layers.TryGetValue(committed.name, out var l) ? l : null;
			}

			public GameObject NamedPrefab(string committedName)
			{
				Asked++;
				return committedName != null && Prefabs.TryGetValue(committedName, out var p) ? p : null;
			}

			public Material NamedMaterial(string committedName)
			{
				Asked++;
				return committedName != null && Materials.TryGetValue(committedName, out var m) ? m : null;
			}
		}

		/// <summary>A biome with one grass rule, a sidecar overriding that rule, its main layer and a cliff material.</summary>
		private (BiomeTemplate biome, PrefabSpawnRule rule, GameObject committedPrefab, GameObject localPrefab, TerrainLayer committedLayer, TerrainLayer localLayer, FakeReferences refs) Dressed()
		{
			BiomeTemplate biome = Biome("Meadow");
			GameObject committedPrefab = Prefab("Grass_Tuft_A");
			GameObject localPrefab = Prefab("Licensed Grass");
			var rule = new PrefabSpawnRule { ruleName = "Grass", prefabs = new[] { committedPrefab } };
			TerrainLayer committedLayer = Track(new TerrainLayer { name = "Meadow Grass" });
			TerrainLayer localLayer = Track(new TerrainLayer { name = "Licensed Meadow" });

			var sidecar = Track(ScriptableObject.CreateInstance<BiomeLocalArt>());
			sidecar.Biome = biome;
			sidecar.GetOrAddRule(BiomeLocalArt.SlotMain, rule).Prefabs = new[] { localPrefab };

			var refs = new FakeReferences { Sidecar = sidecar };
			refs.Slots[(biome, BiomeLocalArt.SlotMain)] = new BiomeLocalArt.SlotOverride { Slot = BiomeLocalArt.SlotMain, TerrainLayer = localLayer };
			refs.Prefabs["Grass_Tuft_A"] = Prefab("Licensed Grass By Name");
			refs.Materials["Cliff_Granite"] = Track(new Material(Shader.Find("Hidden/Internal-Colored")));
			return (biome, rule, committedPrefab, localPrefab, committedLayer, localLayer, refs);
		}

		// ── The gate ──────────────────────────────────────────────────

		[Test]
		public void ACommittedScene_WithASidecarPresent_GetsOnlyCommittedReferences()
		{
			var d = Dressed();
			Material generated = Track(new Material(Shader.Find("Hidden/Internal-Colored")));
			LocalArtScope scope = LocalArtScope.ForPaths(CommittedScene, new[] { CommittedTerrain }, d.refs);

			Assert.That(scope.AllowsLocal, Is.False);
			Assert.That(scope.ResolvePrefabs(d.biome, BiomeLocalArt.SlotMain, d.rule), Is.SameAs(d.rule.prefabs), "the rule's own array, untouched");
			Assert.That(scope.ResolveLayer(d.committedLayer, d.biome, BiomeLocalArt.SlotMain), Is.SameAs(d.committedLayer));
			Assert.That(scope.PaletteResolver(_ => d.committedLayer)(d.biome, BiomeLocalArt.SlotMain, null), Is.SameAs(d.committedLayer));
			Assert.That(scope.ResolveMaterial("Cliff_Granite", generated), Is.SameAs(generated));
			Assert.That(scope.NamedPrefab("Grass_Tuft_A"), Is.Null);
			Assert.That(scope.ScatterPrefabs, Is.Null, "the scatter keeps its default (the rule's own prefabs)");
			Assert.That(scope.CliffOptions(), Is.Null);
			Assert.That(scope.IceOptions(), Is.Null);
			Assert.That(d.refs.Asked, Is.Zero, "a committed scene never even looks at the LOCAL folder");
		}

		[Test]
		public void ALocalScene_WithLocalTerrainData_GetsTheOverrides()
		{
			var d = Dressed();
			Material generated = Track(new Material(Shader.Find("Hidden/Internal-Colored")));
			LocalArtScope scope = LocalArtScope.ForPaths(LocalScene, new[] { LocalTerrain }, d.refs);

			Assert.That(scope.AllowsLocal, Is.True);
			Assert.That(scope.ResolvePrefabs(d.biome, BiomeLocalArt.SlotMain, d.rule), Is.EqualTo(new[] { d.localPrefab }), "the sidecar's rule override wins over a name match");
			Assert.That(scope.ResolveLayer(d.committedLayer, d.biome, BiomeLocalArt.SlotMain), Is.SameAs(d.localLayer));
			Assert.That(scope.ResolveMaterial("Cliff_Granite", generated), Is.SameAs(d.refs.Materials["Cliff_Granite"]));
			Assert.That(scope.ResolveMaterial("Cliff_Basalt", generated), Is.SameAs(generated), "no LOCAL material of that name: the generated one");
			Assert.That(scope.ScatterPrefabs, Is.Not.Null);
			Assert.That(scope.CliffOptions()?.MaterialFor, Is.Not.Null);
			Assert.That(scope.IceOptions()?.Prefabs, Is.Not.Null);
		}

		[Test]
		public void ALocalScene_PaintingCommittedTerrainData_IsTreatedAsCommitted()
		{
			var d = Dressed();
			LocalArtScope scope = LocalArtScope.ForPaths(LocalScene, new[] { LocalTerrain, CommittedTerrain }, d.refs);

			Assert.That(scope.AllowsLocal, Is.False, "the terrain data carries the references, so one committed tile closes the gate");
			Assert.That(scope.Reason, Does.Contain(CommittedTerrain));
			Assert.That(scope.ResolvePrefabs(d.biome, BiomeLocalArt.SlotMain, d.rule), Is.SameAs(d.rule.prefabs));
		}

		[Test]
		public void AnUnsavedScene_IsCommitted()
		{
			var d = Dressed();
			Assert.That(LocalArtScope.ForPaths(null, null, d.refs).AllowsLocal, Is.False);
			Assert.That(LocalArtScope.ForPaths(string.Empty, new[] { LocalTerrain }, d.refs).AllowsLocal, Is.False);
		}

		[Test]
		public void TheLocalTest_MatchesTheGitignore_AnyCapitalisation()
		{
			var d = Dressed();
			Assert.That(LocalArtScope.ForPaths("Assets/Local/Scenes/X.unity", new[] { "Assets/local/Scenes/X/T.asset" }, d.refs).AllowsLocal, Is.True);
			Assert.That(LocalArtScope.ForPaths("Assets/Scenes/LOCAL/X.unity", null, d.refs).AllowsLocal, Is.False, "LOCAL deeper in the path is not Assets/LOCAL");
		}

		[Test]
		public void InMemoryTerrainData_DoesNotCloseTheGate()
		{
			var d = Dressed();
			Assert.That(LocalArtScope.ForPaths(LocalScene, new[] { LocalTerrain, string.Empty }, d.refs).AllowsLocal, Is.True);
		}

		[Test]
		public void InALocalScene_NameMatchedPrefabsSwapOneByOne_AndTheRuleIsNotMutated()
		{
			var d = Dressed();
			GameObject other = Prefab("Fern_B");
			var rule = new PrefabSpawnRule { ruleName = "Undergrowth", prefabs = new[] { d.committedPrefab, other } };
			LocalArtScope scope = LocalArtScope.ForPaths(LocalScene, new[] { LocalTerrain }, d.refs);

			GameObject[] resolved = scope.ResolvePrefabs(d.biome, BiomeLocalArt.SlotMain, rule);
			Assert.That(resolved, Is.Not.SameAs(rule.prefabs));
			Assert.That(resolved[0], Is.SameAs(d.refs.Prefabs["Grass_Tuft_A"]));
			Assert.That(resolved[1], Is.SameAs(other), "no LOCAL prefab of that name: the committed one keeps its place (and its seed index)");
			Assert.That(rule.prefabs[0], Is.SameAs(d.committedPrefab), "the committed rule is never written");
		}

		[Test]
		public void AWholeLayerByName_StandsInWhenTheSidecarHasNone()
		{
			var d = Dressed();
			TerrainLayer rock = Track(new TerrainLayer { name = "Granite" });
			TerrainLayer licensedRock = Track(new TerrainLayer { name = "Licensed Granite" });
			d.refs.Layers["Granite"] = licensedRock;
			LocalArtScope scope = LocalArtScope.ForPaths(LocalScene, new[] { LocalTerrain }, d.refs);

			Assert.That(scope.ResolveLayer(rock, d.biome, "cliff/0"), Is.SameAs(licensedRock));
			Assert.That(scope.ResolveLayer(null, d.biome, "detail/3"), Is.Null, "nothing committed and nothing LOCAL: no layer");
		}

		[Test]
		public void TheAtlasName_OfALocalCopy_IsItsSource()
		{
			Assert.That(LocalArtScope.AtlasSceneName(LocalScene, "Harbor" + LocalArtScope.LocalSceneSuffix), Is.EqualTo("Harbor"));
			Assert.That(LocalArtScope.AtlasSceneName(CommittedScene, "Harbor" + LocalArtScope.LocalSceneSuffix), Is.EqualTo("Harbor" + LocalArtScope.LocalSceneSuffix), "only a LOCAL copy is renamed back");
			Assert.That(LocalArtScope.AtlasSceneName(LocalScene, "Harbor"), Is.EqualTo("Harbor"));
		}

		// ── Rule override keys ───────────────────────────────────────

		[Test]
		public void ARuleOverride_IsFoundByGuid_ThenBySlotAndName()
		{
			var d = Dressed();
			var sidecar = Track(ScriptableObject.CreateInstance<BiomeLocalArt>());
			sidecar.Biome = d.biome;
			sidecar.GetOrAddRule("detail/1", d.rule).Prefabs = new[] { d.localPrefab };

			Assert.That(sidecar.FindRule("main", d.rule), Is.Not.Null, "the GUID finds it whatever slot is asked");
			Assert.That(sidecar.FindRule("detail/1", "a-fresh-guid", d.rule.ruleName), Is.Not.Null, "a re-authored rule keeps its override by slot and name");
			Assert.That(sidecar.FindRule("detail/2", "a-fresh-guid", d.rule.ruleName), Is.Null, "the fallback needs the slot too");
			Assert.That(sidecar.FindRule("detail/1", "a-fresh-guid", "Flowers"), Is.Null);
		}

		[Test]
		public void AnEmptyRuleOverride_CountsAsNone_AndRemovesCleanly()
		{
			var d = Dressed();
			var sidecar = Track(ScriptableObject.CreateInstance<BiomeLocalArt>());
			sidecar.Biome = d.biome;
			sidecar.GetOrAddRule("main", d.rule).Prefabs = new GameObject[] { null };

			Assert.That(sidecar.FindRule("main", d.rule), Is.Null);
			Assert.That(sidecar.RemoveRule("main", d.rule), Is.True);
			Assert.That(sidecar.Rules, Is.Empty);
		}
	}
}
