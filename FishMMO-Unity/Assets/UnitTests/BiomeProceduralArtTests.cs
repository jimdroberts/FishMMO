using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The procedural biome art: tileable ground textures, normal maps that mean what the height
	/// says, valid and deterministic meshes within their triangle budgets, a spec table that covers
	/// every biome, an authoring pass that fills only what is empty and does so once, and the
	/// season the foliage shader reads.
	/// </summary>
	/// <remarks>
	/// Partial: the tree species' real sizes (<c>RealSizes</c>) live in BiomeProceduralArtTests.TreeSizes.cs, so the tree
	/// package adds a species' size without touching the spec-table tests here.
	/// </remarks>
	[TestFixture]
	public partial class BiomeProceduralArtTests
	{
		private const int Seed = 1234;
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

		// ── Noise and textures ────────────────────────────────────────

		[Test]
		public void PeriodicNoise_RepeatsExactlyAtTheTileEdge()
		{
			for (int i = 0; i < 50; i++)
			{
				float u = i * 0.0197f, v = i * 0.0311f;
				Assert.That(ProceduralNoise.PeriodicFbm(u + 1f, v, 4, 5, 0.5f, Seed), Is.EqualTo(ProceduralNoise.PeriodicFbm(u, v, 4, 5, 0.5f, Seed)).Within(1e-5f));
				Assert.That(ProceduralNoise.PeriodicFbm(u, v + 1f, 4, 5, 0.5f, Seed), Is.EqualTo(ProceduralNoise.PeriodicFbm(u, v, 4, 5, 0.5f, Seed)).Within(1e-5f));
				ProceduralNoise.Cell a = ProceduralNoise.PeriodicCellular(u, v, 6, 0.9f, Seed);
				ProceduralNoise.Cell b = ProceduralNoise.PeriodicCellular(u + 1f, v - 1f, 6, 0.9f, Seed);
				Assert.That(b.F1, Is.EqualTo(a.F1).Within(1e-4f));
				Assert.That(b.Id, Is.EqualTo(a.Id));
			}
		}

		/// <summary>Mean absolute height step across the wrap seam against the mean step between interior neighbours.</summary>
		private static void SeamRatios(float[] h, int size, out float acrossX, out float acrossY)
		{
			double interiorX = 0, seamX = 0, interiorY = 0, seamY = 0;
			for (int y = 0; y < size; y++)
			{
				seamX += Mathf.Abs(h[y * size] - h[y * size + size - 1]);
				for (int x = 1; x < size; x++)
				{
					interiorX += Mathf.Abs(h[y * size + x] - h[y * size + x - 1]);
				}
			}
			for (int x = 0; x < size; x++)
			{
				seamY += Mathf.Abs(h[x] - h[(size - 1) * size + x]);
				for (int y = 1; y < size; y++)
				{
					interiorY += Mathf.Abs(h[y * size + x] - h[(y - 1) * size + x]);
				}
			}
			acrossX = (float)(seamX / size / (interiorX / (size * (size - 1)) + 1e-6));
			acrossY = (float)(seamY / size / (interiorY / (size * (size - 1)) + 1e-6));
		}

		[Test]
		public void EveryGroundAndBarkSurface_TilesLeftRightAndTopBottom()
		{
			const int size = 64;
			var recipes = new List<SurfaceRecipe>(SurfaceCatalogue.GroundRecipes);
			recipes.AddRange(SurfaceCatalogue.BarkRecipes);
			foreach (SurfaceRecipe recipe in recipes)
			{
				SurfaceMaps maps = SurfaceSynth.Generate(in recipe, size, Seed);
				SeamRatios(maps.Height, size, out float acrossX, out float acrossY);
				// A seam would be a step far larger than neighbouring texels differ by; a seamless
				// tile's edge pair is just another pair of neighbours.
				Assert.That(acrossX, Is.LessThan(2.5f), $"{recipe.Name}: the left and right edges do not meet (seam step {acrossX:0.00}× an interior step)");
				Assert.That(acrossY, Is.LessThan(2.5f), $"{recipe.Name}: the top and bottom edges do not meet (seam step {acrossY:0.00}× an interior step)");
				Assert.That(maps.Albedo.Length, Is.EqualTo(size * size));
				Assert.That(maps.Mask.Length, Is.EqualTo(size * size));
			}
		}

		[Test]
		public void Surfaces_AreDeterministicPerSeed()
		{
			SurfaceRecipe recipe = SurfaceCatalogue.GroundRecipes[0];
			SurfaceMaps a = SurfaceSynth.Generate(in recipe, 32, Seed);
			SurfaceMaps b = SurfaceSynth.Generate(in recipe, 32, Seed);
			SurfaceMaps c = SurfaceSynth.Generate(in recipe, 32, Seed + 1);
			bool differs = false;
			for (int i = 0; i < a.Albedo.Length; i++)
			{
				Assert.That(b.Albedo[i], Is.EqualTo(a.Albedo[i]));
				Assert.That(b.Normal[i], Is.EqualTo(a.Normal[i]));
				differs |= !a.Albedo[i].Equals(c.Albedo[i]);
			}
			Assert.IsTrue(differs, "a different seed gave the same texture");
		}

		[Test]
		public void NormalMap_FromAFlatHeight_IsFlat()
		{
			const int size = 16;
			var height = new float[size * size];
			for (int i = 0; i < height.Length; i++)
			{
				height[i] = 0.5f;
			}
			foreach (Color32 n in SurfaceSynth.NormalsFromHeight(height, size, 10f))
			{
				Assert.That(n.r, Is.EqualTo(128));
				Assert.That(n.g, Is.EqualTo(128));
				Assert.That(n.b, Is.EqualTo(255));
			}
		}

		[Test]
		public void NormalMap_TiltsAwayFromRisingGround()
		{
			const int size = 16;
			var height = new float[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					// Rises with x and with y (rows run bottom to top); a periodic ramp so the wrap is smooth too.
					height[y * size + x] = Mathf.Sin(x * Mathf.PI * 2f / size) * 0.1f + Mathf.Sin(y * Mathf.PI * 2f / size) * 0.1f;
				}
			}
			Color32[] normals = SurfaceSynth.NormalsFromHeight(height, size, 5f);
			Color32 atOrigin = normals[0];
			Assert.That(atOrigin.r, Is.LessThan(128), "ground rising toward +U must tilt the normal toward -U");
			Assert.That(atOrigin.g, Is.LessThan(128), "ground rising toward +V must tilt the normal toward -V (OpenGL convention)");
		}

		[Test]
		public void BoxBlur_KeepsTheMean_AndWraps()
		{
			const int size = 32;
			var source = new float[size * size];
			source[0] = 1f; // A spike in the corner spreads to all four corners when the blur wraps.
			float[] blurred = SurfaceSynth.BoxBlurWrap(source, size, 2);
			float sum = 0f;
			foreach (float v in blurred)
			{
				sum += v;
			}
			Assert.That(sum, Is.EqualTo(1f).Within(1e-4f));
			Assert.That(blurred[(size - 1) * size + size - 1], Is.GreaterThan(0f), "the blur did not wrap to the opposite corner");
		}

		[Test]
		public void FoliageAtlas_HasShapesInItsCells_AndASolidCell()
		{
			const int size = 128;
			Color32[] atlas = FoliageAtlas.Generate(size, Seed);
			foreach (FoliageCell cell in System.Enum.GetValues(typeof(FoliageCell)))
			{
				Rect r = FoliageAtlas.CellRect(cell, size);
				int covered = 0, total = 0;
				for (int y = Mathf.CeilToInt(r.yMin * size); y < Mathf.FloorToInt(r.yMax * size); y++)
				{
					for (int x = Mathf.CeilToInt(r.xMin * size); x < Mathf.FloorToInt(r.xMax * size); x++)
					{
						total++;
						if (atlas[y * size + x].a > 127) covered++;
					}
				}
				Assert.That(covered, Is.GreaterThan(0), $"{cell} is empty");
				if (cell == FoliageCell.Solid || cell == FoliageCell.Blade)
				{
					Assert.That(covered, Is.EqualTo(total), $"{cell} should be opaque");
				}
			}
		}

		// ── Meshes ────────────────────────────────────────────────────

		private static void AssertSame(MeshBuilder a, MeshBuilder b, string what)
		{
			Assert.That(b.VertexCount, Is.EqualTo(a.VertexCount), what);
			for (int i = 0; i < a.VertexCount; i++)
			{
				Assert.That(b.Positions[i], Is.EqualTo(a.Positions[i]), what);
			}
		}

		[Test]
		public void Rocks_AreClosedOutwardFacingDeterministic_AndWithinBudget()
		{
			foreach (RockShape shape in ProceduralArtCatalogue.BoulderShapes)
			{
				for (int lod = 0; lod < ProceduralArtCatalogue.BoulderResolution.Length; lod++)
				{
					RockShape s = shape;
					int res = ProceduralArtCatalogue.BoulderResolution[lod];
					MeshBuilder rock = RockMeshes.Build(in s, res, Seed);
					List<string> problems = rock.Validate(true);
					Assert.That(problems, Is.Empty, $"{shape.Name} LOD{lod}: {string.Join("; ", problems)}");
					Assert.That(rock.TriangleCount, Is.EqualTo(6 * 2 * res * res));
					Assert.That(rock.TriangleCount, Is.LessThanOrEqualTo(6000));
					AssertSame(rock, RockMeshes.Build(in s, res, Seed), shape.Name);
					// Bedded in: some of it below the pivot, most of it above.
					Bounds b = rock.Bounds;
					Assert.That(b.min.y, Is.LessThan(0f), $"{shape.Name} floats");
					Assert.That(b.max.y, Is.GreaterThan(-b.min.y), $"{shape.Name} is mostly buried");
				}
			}
		}

		[Test]
		public void DetailPlants_AreValidDeterministicSingleSubmesh_AndSmall()
		{
			foreach (DetailSpec spec in ProceduralArtCatalogue.Details)
			{
				DetailPlant plant = spec.Plant;
				MeshBuilder mesh = VegetationMeshes.Build(in plant, Seed);
				List<string> problems = mesh.Validate(false);
				Assert.That(problems, Is.Empty, $"{spec.Name}: {string.Join("; ", problems)}");
				Assert.That(mesh.Submeshes.Count, Is.EqualTo(1), $"{spec.Name}: a detail prototype draws one sub-mesh");
				Assert.That(mesh.TriangleCount, Is.GreaterThan(0), spec.Name);
				// Solid sea-floor life (corals, sponges, fans, urchins) is round where a plant is flat, and sparse: twice the plants'.
				bool solidSea = SeaFloorMeshes.IsAquatic(plant.Kind) && plant.Kind != DetailKind.Kelp 
					&& plant.Kind != DetailKind.Seaweed && plant.Kind != DetailKind.Coral && plant.Kind != DetailKind.Starfish;
				int budget = plant.Kind == DetailKind.Grass || plant.Kind == DetailKind.Reeds ? 100 : solidSea ? 800 : 400;
				Assert.That(mesh.TriangleCount, Is.LessThanOrEqualTo(budget), $"{spec.Name} has {mesh.TriangleCount} triangles");
				AssertSame(mesh, VegetationMeshes.Build(in plant, Seed), spec.Name);
				Assert.That(mesh.Wind.Count, Is.EqualTo(mesh.VertexCount));
			}
		}

		[Test]
		public void Trees_AreValidDeterministic_WithinBudget_AndHaveABillboard()
		{
			foreach (TreeSpecies t in ProceduralArtCatalogue.Trees)
			{
				TreeSpecies species = t;
				MeshBuilder lod0 = TreeMeshes.Build(in species, 0, Seed);
				MeshBuilder lod1 = TreeMeshes.BuildReduced(in species, 1, Seed, lod0);
				foreach ((MeshBuilder mesh, string level) in new[] { (lod0, "LOD0"), (lod1, "LOD1") })
				{
					List<string> problems = mesh.Validate(false);
					Assert.That(problems, Is.Empty, $"{species.Name} {level}: {string.Join("; ", problems)}");
				}
				Assert.That(lod0.TriangleCount, Is.LessThanOrEqualTo(6000), $"{species.Name} LOD0 has {lod0.TriangleCount} triangles");
				Assert.That(lod1.TriangleCount, Is.LessThan(lod0.TriangleCount), $"{species.Name} LOD1 is not lighter than LOD0");
				// The reduced level keeps the crown: it used to keep a tenth of a conifer's needle area, and from fifty
				// metres on a pine stood as a bare trunk with a few tufts.
				float fullLeaves = TreeMeshes.LeafArea(lod0), reducedLeaves = TreeMeshes.LeafArea(lod1);
				if (fullLeaves > 0f && reducedLeaves > 0f)
				{
					Assert.That(reducedLeaves, Is.GreaterThanOrEqualTo(0.6f * fullLeaves), $"{species.Name} LOD1 keeps {reducedLeaves / fullLeaves:P0} of its leaf area");
				}
				Assert.That(lod0.Submeshes[TreeMeshes.BarkSubmesh].Count, Is.GreaterThan(0), $"{species.Name} has no trunk");
				Assert.That(lod0.Bounds.max.y, Is.GreaterThan(species.Height * 0.6f), $"{species.Name} is far shorter than its height");
				AssertSame(lod0, TreeMeshes.Build(in species, 0, Seed), species.Name);

				var bark = new Color32[16];
				for (int i = 0; i < bark.Length; i++) bark[i] = new Color32(120, 90, 60, 255);
				BillboardImpostor.Result billboard = BillboardImpostor.Render(lod1, (sub, uv, vc) => sub == 0 ? new Color(0.4f, 0.3f, 0.2f, 1f) : new Color(0.2f, 0.5f, 0.2f, 1f), 64, species.Height, 1f);
				Assert.That(billboard.Mesh.TriangleCount, Is.EqualTo(2), "one quad, turned to the camera by the shader");
				Assert.That(billboard.Mesh.Validate(false), Is.Empty);
				int opaque = 0;
				foreach (Color32 p in billboard.Pixels) if (p.a > 127) opaque++;
				Assert.That(opaque, Is.GreaterThan(billboard.Pixels.Length / 50), $"{species.Name}'s billboard is nearly empty");
			}
		}

		[Test]
		public void MeshBuilder_WindsFacesTheWayTheyAreAskedToFace()
		{
			var mesh = new MeshBuilder(1);
			int a = mesh.AddVertex(Vector3.zero, Vector3.up, Vector2.zero, Color.white);
			int b = mesh.AddVertex(Vector3.right, Vector3.up, Vector2.right, Color.white);
			int c = mesh.AddVertex(Vector3.forward, Vector3.up, Vector2.up, Color.white);
			mesh.AddTriangle(0, a, b, c, Vector3.up);
			mesh.AddTriangle(0, a, b, c, Vector3.down);
			List<int> tris = mesh.Submeshes[0];
			Vector3 first = Vector3.Cross(mesh.Positions[tris[1]] - mesh.Positions[tris[0]], mesh.Positions[tris[2]] - mesh.Positions[tris[0]]);
			Vector3 second = Vector3.Cross(mesh.Positions[tris[4]] - mesh.Positions[tris[3]], mesh.Positions[tris[5]] - mesh.Positions[tris[3]]);
			Assert.That(first.y, Is.GreaterThan(0f));
			Assert.That(second.y, Is.LessThan(0f));
		}

		// ── The spec table ────────────────────────────────────────────

		private static List<BiomeTemplate> ProjectBiomes()
		{
			var biomes = new List<BiomeTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
			{
				var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (biome != null)
				{
					biomes.Add(biome);
				}
			}
			return biomes;
		}

		[Test]
		public void SpecTable_CoversEveryBiome_AndEverySelectableOne()
		{
			List<BiomeTemplate> biomes = ProjectBiomes();
			Assume.That(biomes.Count, Is.GreaterThan(0), "no BiomeTemplate assets found");
			int selectable = 0;
			foreach (BiomeTemplate biome in biomes)
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(biome.name);
				if (biome.IsSelectable)
				{
					selectable++;
					Assert.IsNotNull(entry, $"selectable biome '{biome.name}' has no spec entry");
				}
				Assert.IsNotNull(entry, $"biome '{biome.name}' has no spec entry");
				Assert.IsNotNull(entry.Main, $"{biome.name} has no main layer");
				Assert.That(entry.Cliffs.Length, Is.GreaterThan(0), $"{biome.name} has no cliff layer");
				Assert.IsNotNull(entry.Lakebed, $"{biome.name} has no lakebed");
			}
			Assert.That(selectable, Is.GreaterThanOrEqualTo(60));
		}

		[Test]
		public void SpecTable_NamesOnlyBiomesFamiliesAndPrefabsThatExist()
		{
			var biomeNames = new HashSet<string>();
			foreach (BiomeTemplate biome in ProjectBiomes())
			{
				biomeNames.Add(biome.name);
			}
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			var seen = new HashSet<string>();
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.IsTrue(seen.Add(entry.Biome), $"{entry.Biome} is in the table twice");
				Assert.IsTrue(biomeNames.Contains(entry.Biome), $"the table names '{entry.Biome}', which is not a biome asset");
				foreach (string family in entry.Families())
				{
					Assert.IsTrue(SurfaceCatalogue.TryGround(family, out _), $"{entry.Biome} uses unknown ground family '{family}'");
				}
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter rule) in entry.Rules())
				{
					Assert.That(rule.Density, Is.InRange(0f, 50f), $"{entry.Biome} '{rule.Name}' density is outside the field's range");
					foreach (string prefab in rule.Prefabs)
					{
						Assert.IsTrue(prefabs.Contains(prefab), $"{entry.Biome} '{rule.Name}' names '{prefab}', which the generator does not make");
					}
				}
			}
		}

		[Test]
		public void Catalogue_NamesAreUnique()
		{
			var names = new HashSet<string>();
			foreach (string n in ProceduralArtCatalogue.AllPrefabNames())
			{
				Assert.IsTrue(names.Add(n), $"prefab name '{n}' is used twice");
			}
			var families = new HashSet<string>();
			foreach (SurfaceRecipe r in SurfaceCatalogue.GroundRecipes)
			{
				Assert.IsTrue(families.Add(r.Name), $"ground family '{r.Name}' is listed twice");
			}
		}

		// ── Authoring ─────────────────────────────────────────────────

		private sealed class FakeSource : IBiomeArtSource
		{
			private readonly Dictionary<string, TerrainLayer> layers = new Dictionary<string, TerrainLayer>();
			private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
			private readonly List<Object> created;

			public FakeSource(List<Object> created) => this.created = created;

			public TerrainLayer Layer(string family)
			{
				if (!layers.TryGetValue(family, out TerrainLayer layer))
				{
					layer = new TerrainLayer { name = "Ground_" + family };
					created.Add(layer);
					layers[family] = layer;
				}
				return layer;
			}

			public GameObject Prefab(string name)
			{
				if (!prefabs.TryGetValue(name, out GameObject prefab))
				{
					prefab = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
					created.Add(prefab);
					prefabs[name] = prefab;
				}
				return prefab;
			}
		}

		private BiomeTemplate MakeBiome(string name)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = name;
			created.Add(biome);
			return biome;
		}

		private Texture2D MakeTexture(string name)
		{
			var texture = new Texture2D(4, 4) { name = name };
			created.Add(texture);
			return texture;
		}

		[Test]
		public void Authoring_FillsEmptySlotsAndRules_ThenChangesNothingTheSecondTime()
		{
			BiomeTemplate biome = MakeBiome("Grassland");
			// A flat colour swatch counts as empty; the tool fills over it without erasing it.
			biome.MainTextureLayer.albedoTexture = MakeTexture("BiomeTexture_Grassland_80CC66");
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Grassland");
			var source = new FakeSource(created);

			var first = new BiomeArtAuthoring.Report();
			Assert.IsTrue(BiomeArtAuthoring.Author(biome, entry, source, first, true));
			Assert.AreSame(source.Layer(Ground.Grass), biome.MainTextureLayer.terrainLayer);
			Assert.That(biome.MainTextureLayer.albedoTexture.name, Is.EqualTo("BiomeTexture_Grassland_80CC66"), "the swatch was erased");
			Assert.That(biome.DetailTextureLayers.Count, Is.EqualTo(entry.Details.Length));
			Assert.AreSame(source.Layer(entry.Details[0].Family), biome.DetailTextureLayers[0].terrainLayer);
			Assert.That(biome.CliffTextureLayers.Count, Is.EqualTo(1));
			Assert.That(biome.CliffTextureLayers[0].minCliffAngle, Is.EqualTo(entry.Cliffs[0].MinAngle));
			Assert.AreSame(source.Layer(entry.Lakebed), biome.LakebedTextureLayer.terrainLayer);
			Assert.That(biome.MainTextureLayer.prefabSpawnRules.Count, Is.EqualTo(entry.Main.Scatter.Count));
			foreach (PrefabSpawnRule rule in biome.MainTextureLayer.prefabSpawnRules)
			{
				if (rule.spawnChannel == PrefabSpawnChannel.DetailLayer)
				{
					Assert.That(rule.maxPerChunk, Is.EqualTo(0), "a detail rule must not be capped per chunk");
				}
				Assert.That(rule.detailInstancesPerSpawn, Is.InRange(64, 255));
			}

			var second = new BiomeArtAuthoring.Report();
			Assert.IsFalse(BiomeArtAuthoring.Author(biome, entry, source, second, true), "a second run changed something:\n" + second);
			Assert.That(second.SlotsFilled + second.RulesAdded + second.RulesDisabled, Is.EqualTo(0));
		}

		[Test]
		public void Authoring_LeavesAuthoredArtAndLiveRulesAlone()
		{
			BiomeTemplate biome = MakeBiome("Forest");
			Texture2D painted = MakeTexture("Forest");
			biome.MainTextureLayer.albedoTexture = painted;
			var source = new FakeSource(created);
			GameObject realTree = source.Prefab("SomeoneElsesTree");
			biome.MainTextureLayer.prefabSpawnRules.Add(new PrefabSpawnRule { ruleName = "authored", prefabs = new[] { realTree } });

			var report = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(biome, BiomeArtSpec.For("Forest"), source, report, true);
			Assert.IsNull(biome.MainTextureLayer.terrainLayer, "an authored main layer was overwritten");
			Assert.AreSame(painted, biome.MainTextureLayer.albedoTexture);
			Assert.That(biome.MainTextureLayer.prefabSpawnRules.Count, Is.EqualTo(1), "rules were added beside a live authored rule");
			Assert.IsTrue(biome.MainTextureLayer.prefabSpawnRules[0].enableSpawning);
		}

		[Test]
		public void Authoring_SwitchesOffDeadRules_KeepsThem_AndAddsTheSpecRules()
		{
			BiomeTemplate biome = MakeBiome("Woodland");
			var source = new FakeSource(created);
			GameObject cube = source.Prefab("Cube");
			biome.MainTextureLayer.prefabSpawnRules.Add(new PrefabSpawnRule { ruleName = "cubes", prefabs = new[] { cube } });
			biome.MainTextureLayer.prefabSpawnRules.Add(new PrefabSpawnRule { ruleName = "missing", prefabs = new GameObject[] { null } });
			biome.MainTextureLayer.prefabSpawnRules.Add(new PrefabSpawnRule { ruleName = "empty", prefabs = new GameObject[0] });
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Woodland");

			var report = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(biome, entry, source, report, true);
			List<PrefabSpawnRule> rules = biome.MainTextureLayer.prefabSpawnRules;
			Assert.That(report.RulesDisabled, Is.EqualTo(3));
			for (int i = 0; i < 3; i++)
			{
				Assert.IsFalse(rules[i].enableSpawning, $"dead rule {i} still spawns");
				Assert.That(rules[i].ruleName, Does.StartWith(BiomeArtAuthoring.DisabledMark));
			}
			Assert.AreSame(cube, rules[0].prefabs[0], "a dead rule's data was changed");
			Assert.That(rules.Count, Is.EqualTo(3 + entry.Main.Scatter.Count));

			var again = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(biome, entry, source, again, true);
			Assert.That(again.RulesDisabled + again.RulesAdded, Is.EqualTo(0));
		}

		[Test]
		public void Authoring_PreviewChangesNothing()
		{
			BiomeTemplate biome = MakeBiome("Desert");
			var source = new FakeSource(created);
			var report = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(biome, BiomeArtSpec.For("Desert"), source, report, false);
			Assert.That(report.SlotsFilled, Is.GreaterThan(0));
			Assert.IsNull(biome.MainTextureLayer.terrainLayer);
			Assert.That(biome.DetailTextureLayers.Count, Is.EqualTo(0));
			Assert.That(biome.MainTextureLayer.prefabSpawnRules.Count, Is.EqualTo(0));
		}

		[Test]
		public void Authoring_BringsAnUntouchedEarlierRunsRuleOverToTheCarpet_ButKeepsAHandTunedOne()
		{
			/* Biomes authored before carpets hold Grassland's grass exactly as the old Grass helper
			 * wrote it, with no fingerprint. One untouched: it must become the spec's carpet in place,
			 * GUID and all. One whose density somebody changed: it must be left exactly as it is. */
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Grassland");
			BiomeArtSpec.Scatter grass = entry.Main.Scatter.Find(s => s.Name == "GrassLush");
			Assume.That(grass, Is.Not.Null);
			Assume.That(grass.Placement, Is.EqualTo(DetailPlacement.Carpet));
			Assume.That(grass.Earlier.Count, Is.GreaterThan(0), "the carpet should remember the scattered rule it replaced");
			var source = new FakeSource(created);
			GameObject[] prefabs = { source.Prefab(grass.Earlier[0].Prefabs[0]) };

			BiomeTemplate untouched = MakeBiome("Grassland");
			PrefabSpawnRule old = BiomeArtAuthoring.ToRule("Grassland", grass.Earlier[0], prefabs);
			old.SpecFingerprint = null; // written before fingerprints existed
			untouched.MainTextureLayer.prefabSpawnRules.Add(old);
			string guid = old.StableGuid;

			var report = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(untouched, entry, source, report, true);
			Assert.That(old.IsCarpet, Is.True, "the untouched rule should now be a carpet:\n" + report);
			Assert.That(old.carpetCoverage, Is.EqualTo(grass.CarpetCoverage).Within(1e-6f));
			Assert.That(old.minTextureWeight, Is.EqualTo(grass.MinWeight).Within(1e-6f));
			Assert.That(old.StableGuid, Is.EqualTo(guid), "updated in place, so its seed stays");
			Assert.That(old.SpecFingerprint, Is.Not.Empty, "the updated rule should be marked as the tool's");
			Assert.That(report.RulesUpdated, Is.GreaterThanOrEqualTo(1));
			Assert.That(report.ToString(), Does.Contain("updated"));

			BiomeTemplate tuned = MakeBiome("Grassland");
			PrefabSpawnRule mine = BiomeArtAuthoring.ToRule("Grassland", grass.Earlier[0], prefabs);
			mine.SpecFingerprint = null;
			mine.densityPer100m2 = 12f;
			tuned.MainTextureLayer.prefabSpawnRules.Add(mine);

			var second = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(tuned, entry, source, second, true);
			Assert.That(mine.IsCarpet, Is.False, "a hand-tuned rule was converted");
			Assert.That(mine.densityPer100m2, Is.EqualTo(12f), "a hand-tuned value was overwritten");
			Assert.That(mine.SpecFingerprint, Is.Empty, "a hand-tuned rule was marked as the tool's");
			Assert.That(second.RulesKept, Is.GreaterThanOrEqualTo(1));
		}

		[Test]
		public void Authoring_UpdatesAFingerprintedRuleWhenTheSpecChanges_UntilSomebodyTunesIt()
		{
			var source = new FakeSource(created);
			BiomeTemplate biome = MakeBiome("Meadow");

			BiomeArtAuthoring.Report Run(float coverage)
			{
				var entry = new BiomeArtSpec.Entry { Biome = "Meadow", Main = new BiomeArtSpec.Layer { Family = Ground.Grass } };
				entry.Main.Scatter.Add(new BiomeArtSpec.Scatter
				{
					Name = "Turf",
					Channel = PrefabSpawnChannel.DetailLayer,
					Prefabs = new[] { "TurfPrefab" },
					Density = 30f,
					Placement = DetailPlacement.Carpet,
					CarpetCoverage = coverage,
				});
				var report = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, report, true);
				return report;
			}

			Assert.That(Run(0.5f).RulesAdded, Is.EqualTo(1));
			PrefabSpawnRule rule = biome.MainTextureLayer.prefabSpawnRules[0];
			Assert.That(rule.carpetCoverage, Is.EqualTo(0.5f));

			BiomeArtAuthoring.Report changed = Run(0.9f);
			Assert.That(changed.RulesUpdated, Is.EqualTo(1), "an untouched spec rule should follow the spec:\n" + changed);
			Assert.That(rule.carpetCoverage, Is.EqualTo(0.9f));
			Assert.That(biome.MainTextureLayer.prefabSpawnRules.Count, Is.EqualTo(1), "updated in place, not added again");

			BiomeArtAuthoring.Report same = Run(0.9f);
			Assert.That(same.RulesUpdated + same.RulesAdded + same.RulesKept, Is.EqualTo(0), "nothing to do the second time:\n" + same);

			rule.carpetClumpFloor = 0.2f; // somebody tunes it
			BiomeArtAuthoring.Report kept = Run(0.7f);
			Assert.That(kept.RulesUpdated, Is.EqualTo(0));
			Assert.That(kept.RulesKept, Is.EqualTo(1));
			Assert.That(rule.carpetCoverage, Is.EqualTo(0.9f), "a tuned rule followed the spec");
			Assert.That(rule.carpetClumpFloor, Is.EqualTo(0.2f), "a tuned value was overwritten");
		}

		[Test]
		public void Authoring_GroupsARuleMarkedBeforeGroupsExisted()
		{
			var source = new FakeSource(created);
			BiomeTemplate biome = MakeBiome("Heath");

			BiomeArtAuthoring.Report Run(float clusterMetres)
			{
				var entry = new BiomeArtSpec.Entry { Biome = "Heath", Main = new BiomeArtSpec.Layer { Family = Ground.Grass } };
				entry.Main.Scatter.Add(new BiomeArtSpec.Scatter
				{
					Name = "Heather",
					Channel = PrefabSpawnChannel.DetailLayer,
					Prefabs = new[] { "HeatherPrefab" },
					Density = 6f,
					ClusterMetres = clusterMetres,
				});
				var report = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, report, true);
				return report;
			}

			// No radius writes, and hashes, exactly what a run before groups existed did.
			Assert.That(Run(0f).RulesAdded, Is.EqualTo(1));
			PrefabSpawnRule rule = biome.MainTextureLayer.prefabSpawnRules[0];
			string before = rule.SpecFingerprint;
			rule.clusterBackground = 0.9f; // inert without a radius, so not a tuning either
			Assert.That(BiomeArtAuthoring.Fingerprint(rule), Is.EqualTo(before), "group settings without a radius must not change the hash");
			rule.clusterBackground = 0.2f;

			BiomeArtAuthoring.Report grouped = Run(4f);
			Assert.That(grouped.RulesUpdated, Is.EqualTo(1), "an untouched rule from before groups should be brought into them:\n" + grouped);
			Assert.That(rule.clusterMetres, Is.EqualTo(4f));
			Assert.That(rule.SpecFingerprint, Is.Not.EqualTo(before));
		}

		[Test]
		public void Authoring_BringsARuleMarkedBeforeWoodsExistedIntoThem()
		{
			var source = new FakeSource(created);
			BiomeTemplate biome = MakeBiome("Copse");

			BiomeArtAuthoring.Report Run(float forestMetres)
			{
				var entry = new BiomeArtSpec.Entry { Biome = "Copse", Main = new BiomeArtSpec.Layer { Family = Ground.Grass } };
				entry.Main.Scatter.Add(new BiomeArtSpec.Scatter
				{
					Name = "Trees: Oak",
					Channel = PrefabSpawnChannel.TreeInstance,
					Prefabs = new[] { "OakPrefab" },
					Density = 0.5f,
					Spacing = 10f,
					ClusterMetres = 15f,
					ClusterSize = 6f,
					ForestMetres = forestMetres,
					ForestCover = 0.3f,
				});
				var report = new BiomeArtAuthoring.Report();
				BiomeArtAuthoring.Author(biome, entry, source, report, true);
				return report;
			}

			// No stand size writes, and hashes, exactly what a run before woods existed did.
			Assert.That(Run(0f).RulesAdded, Is.EqualTo(1));
			PrefabSpawnRule rule = biome.MainTextureLayer.prefabSpawnRules[0];
			string before = rule.SpecFingerprint;
			rule.forestCover = 0.9f; // inert without a stand size, so not a tuning either
			rule.forestOpen = 0.5f;
			Assert.That(BiomeArtAuthoring.Fingerprint(rule), Is.EqualTo(before), "wood settings without a stand size must not change the hash");

			BiomeArtAuthoring.Report wooded = Run(520f);
			Assert.That(wooded.RulesUpdated, Is.EqualTo(1), "an untouched rule from before woods should be brought into them:\n" + wooded);
			Assert.That(rule.forestMetres, Is.EqualTo(520f));
			Assert.That(rule.forestCover, Is.EqualTo(0.3f).Within(1e-6f), "the spec's cover, not the inert value");
			Assert.That(rule.IsForested, Is.True);
			Assert.That(rule.SpecFingerprint, Is.Not.EqualTo(before));

			rule.forestEdge = 0.9f; // somebody tunes the wood
			BiomeArtAuthoring.Report kept = Run(400f);
			Assert.That(kept.RulesKept, Is.EqualTo(1), "a tuned wood is somebody's work:\n" + kept);
			Assert.That(rule.forestMetres, Is.EqualTo(520f));
		}

		[Test]
		public void TreeSpecies_StandAtTheirRealMatureSizes()
		{
			Assert.That(ProceduralArtCatalogue.Trees.Length, Is.EqualTo(RealSizes.Count), "a species was added or removed: give it its real size here");
			foreach (TreeSpecies t in ProceduralArtCatalogue.Trees)
			{
				Assert.That(RealSizes.TryGetValue(t.Name, out var real), Is.True, $"{t.Name} has no real size to check against");
				Assert.That(t.Height, Is.InRange(real.height.x, real.height.y), $"{t.Name}'s height in metres");
				Assert.That(t.TrunkRadius, Is.InRange(real.trunk.x, real.trunk.y), $"{t.Name}'s trunk radius in metres");
				Assert.That(ProceduralArtCatalogue.CrownRadius(in t), Is.InRange(real.crown.x, real.crown.y), $"{t.Name}'s crown radius in metres");
			}
			Assert.That(ProceduralArtCatalogue.TryTree("Pine", out TreeSpecies pine), Is.True);
			Assert.That(ProceduralArtCatalogue.TryTree("Birch", out TreeSpecies birch), Is.True);
			Assert.That(pine.Height, Is.GreaterThan(birch.Height), "a pine overtops a birch");
		}

		[Test]
		public void TreeSpecies_BroadCrownsCarryEnoughLeafToStayFull()
		{
			// Card area against the crown's surface, as the generator hangs them (21 cards a limb, two crossed at LOD0):
			// at the old sizes about two to two and a half; a bigger crown on the old cards would be a skeleton.
			foreach (TreeSpecies t in ProceduralArtCatalogue.Trees)
			{
				if (t.Form != TreeForm.Broadleaf)
				{
					continue;
				}
				float radius = t.CrownWidth * t.Height;
				float depth = t.Height * (1f - t.CrownBase) * 0.5f;
				float surface = 4f * Mathf.PI * (radius * radius + 2f * radius * depth) / 3f;
				float cards = Mathf.Max(3, t.Branches) * 21f * 2f * t.LeafSize * t.LeafSize;
				Assert.That(cards / surface, Is.InRange(1.8f, 4.5f), $"{t.Name}'s leaf cards cover {cards / surface:F2}× its crown");
			}
		}

		[Test]
		public void SpecTable_TreeSizesVaryNaturally_AndWoodsAreSpacedByTheirCrowns()
		{
			int woods = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter s) in entry.Rules())
				{
					if (s.Channel != PrefabSpawnChannel.TreeInstance || !s.Name.StartsWith("Trees: ", System.StringComparison.Ordinal))
					{
						continue;
					}
					Assert.That(s.NonUniform, Is.True, $"{entry.Biome} / {s.Name}");
					// The spread round the rule's own size: ±15–25%, never a range that shrinks a tree to a sapling.
					float widthMid = (s.WidthScale.x + s.WidthScale.y) * 0.5f;
					float heightMid = (s.HeightScale.x + s.HeightScale.y) * 0.5f;
					Assert.That(s.WidthScale.y / s.WidthScale.x, Is.InRange(1.2f, 1.7f), $"{entry.Biome} / {s.Name} width spread");
					Assert.That(s.HeightScale.y / s.HeightScale.x, Is.InRange(1.3f, 1.7f), $"{entry.Biome} / {s.Name} height spread");
					Assert.That(heightMid, Is.EqualTo(widthMid).Within(0.05f), $"{entry.Biome} / {s.Name}: a stunted tree is smaller all over, not squashed");
					Assert.That(heightMid, Is.InRange(0.3f, 1.05f), $"{entry.Biome} / {s.Name} size against the mature tree");

					if (s.ForestMetres <= 0f)
					{
						continue;
					}
					woods++;
					float crown = 0f;
					int species = 0;
					foreach (string prefab in s.Prefabs)
					{
						string name = prefab.Substring(prefab.LastIndexOf('_') + 1);
						if (ProceduralArtCatalogue.TryTree(name, out TreeSpecies t))
						{
							crown += ProceduralArtCatalogue.CrownRadius(in t);
							species++;
						}
					}
					Assume.That(species, Is.GreaterThan(0), $"{entry.Biome} / {s.Name}: prefab names should end in the species ({string.Join(", ", s.Prefabs)})");
					crown = crown / species * heightMid;
					Assert.That(s.Spacing, Is.InRange(crown * 1.2f, crown * 1.8f), $"{entry.Biome} / {s.Name}: spacing {s.Spacing} m for {crown:F1} m crowns");
				}
			}
			Assert.That(woods, Is.GreaterThan(20), "most tree rules should stand in woods");
		}

		[Test]
		public void SpecTable_ForestBiomesAreMostlyWood_OpenBiomesHaveCopses()
		{
			float Cover(string biome, string rule)
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(biome);
				Assume.That(entry, Is.Not.Null, biome);
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter s) in entry.Rules())
				{
					if (s.Name == rule)
					{
						Assert.That(s.ForestMetres, Is.GreaterThan(0f), $"{biome} / {rule} should stand in woods");
						return s.ForestCover;
					}
				}
				Assert.Fail($"{biome} has no rule '{rule}'");
				return 0f;
			}

			Assert.That(Cover("Taiga", "Trees: Spruce"), Is.GreaterThanOrEqualTo(0.7f));
			Assert.That(Cover("Forest", "Trees: Oak, Birch, Beech"), Is.GreaterThanOrEqualTo(0.7f));
			Assert.That(Cover("Taiga", "Trees: Pine, Larch"), Is.GreaterThanOrEqualTo(0.7f), "larch joins the pine's woods rather than competing with a stand rule of its own");
			Assert.That(BiomeArtSpec.For("Forest").Retired, Has.Member(("main", "Trees: Oak, Birch")), "the rule beech joined is retired under its old name");
			Assert.That(BiomeArtSpec.For("Alpine Meadow").Retired, Has.Member(("main", "Trees: Spruce")));
			Assert.That(Cover("Jungle", "Trees: Jungle"), Is.GreaterThanOrEqualTo(0.8f));
			Assert.That(Cover("Woodland", "Trees: Oak, Birch"), Is.InRange(0.35f, 0.7f));
			Assert.That(Cover("Grassland", "Trees: Oak"), Is.LessThanOrEqualTo(0.2f));
			Assert.That(Cover("Plains", "Trees: Oak"), Is.LessThanOrEqualTo(0.1f));

			// A biome's species stand in the same woods: one stand size, so one field.
			var taiga = new List<float>();
			foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter s) in BiomeArtSpec.For("Taiga").Rules())
			{
				if (s.ForestMetres > 0f)
				{
					taiga.Add(s.ForestMetres);
					Assert.That(s.ForestMetres, Is.InRange(300f, 1500f), "woods are hundreds of metres across, not groves");
				}
			}
			Assert.That(taiga.Count, Is.EqualTo(3));
			Assert.That(taiga.TrueForAll(m => m == taiga[0]), Is.True, "the taiga's spruce, pine and birch should cut one stand field");
			Assert.That(BiomeArtSpec.For("Grassland").Rules(), Has.Some.Matches<(BiomeArtSpec.Layer, BiomeArtSpec.Scatter)>(r => r.Item2.ForestMetres == taiga[0]),
				"a grassland's copses should be the hearts of the taiga's woods");
		}

		[Test]
		public void SpecTable_EveryCountedThingGathersIntoGroups()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter s) in entry.Rules())
				{
					if (s.Channel == PrefabSpawnChannel.DetailLayer && s.Placement == DetailPlacement.Carpet)
					{
						continue;
					}
					if (s.ForestMetres > 0f)
					{
						// A wood gathers into stands instead of groups (TerrainScatter.StandField).
						Assert.That(s.Channel, Is.EqualTo(PrefabSpawnChannel.TreeInstance), $"{entry.Biome} / {s.Name}: only trees stand in woods");
						Assert.That(s.ForestCover, Is.LessThan(1f), $"{entry.Biome} / {s.Name} is one unbroken wood, with no clearing anywhere");
					}
					else
					{
						Assert.That(s.ClusterMetres, Is.GreaterThan(0f), $"{entry.Biome} / {s.Name} is scattered evenly");
						Assert.That(s.ClusterBackground, Is.LessThan(1f), $"{entry.Biome} / {s.Name} has groups but nothing in them");
						Assert.That(s.ClusterSize, Is.GreaterThan(1f), $"{entry.Biome} / {s.Name} should size its groups by members, or a sparse rule's groups hold one each");
					}
					Assert.That(s.Earlier.Exists(e => e.ClusterMetres == 0f && e.ForestMetres == 0f && e.Name == s.Name), Is.True,
						$"{entry.Biome} / {s.Name} should remember its ungrouped version, under its own name, so an earlier run's rule is recognised");
				}
			}
		}

		[Test]
		public void SpecTable_GroundCoverIsCarpet_CountedThingsAreScattered()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter rule) in entry.Rules())
				{
					bool grass = rule.Channel == PrefabSpawnChannel.DetailLayer && rule.Name.StartsWith("Grass", System.StringComparison.Ordinal);
					Assert.That(rule.Placement, Is.EqualTo(grass ? DetailPlacement.Carpet : DetailPlacement.Scattered), $"{entry.Biome} '{rule.Name}'");
					if (grass)
					{
						Assert.That(rule.CarpetCoverage, Is.InRange(0.1f, 1f), $"{entry.Biome} '{rule.Name}' carpet coverage");
						Assert.That(rule.ClumpFloor, Is.InRange(0.1f, 0.6f), $"{entry.Biome} '{rule.Name}' clump floor");
					}
				}
			}
			BiomeArtSpec.Scatter grassland = BiomeArtSpec.For("Grassland").Main.Scatter.Find(s => s.Name == "GrassLush");
			BiomeArtSpec.Scatter highDesert = BiomeArtSpec.For("High Desert").Main.Scatter.Find(s => s.Name == "GrassTuft");
			Assert.That(grassland.CarpetCoverage, Is.GreaterThan(0.8f), "grassland should read as continuous turf");
			Assert.That(highDesert.CarpetCoverage * highDesert.ClumpFloor, Is.LessThan(0.15f), "desert tufts should thin to near bare between clumps");
		}

		[Test]
		public void PlaceholderSwatches_AreRecognisedByName()
		{
			Assert.IsTrue(BiomeArtAuthoring.IsPlaceholderTexture(MakeTexture("BiomeTexture_AlpineMeadow_80CC4C")));
			Assert.IsFalse(BiomeArtAuthoring.IsPlaceholderTexture(MakeTexture("Grass")));
			Assert.IsFalse(BiomeArtAuthoring.IsPlaceholderTexture(MakeTexture("Ground_Grass_Albedo")));
		}

		// ── The season global ─────────────────────────────────────────

		[Test]
		public void Season_IsReversedInTheSouth_AndFadesAtTheEquator()
		{
			Assert.That(WeatherShaderGlobals.LocalSummer(0.5f, 45f), Is.EqualTo(1f).Within(1e-4f));
			Assert.That(WeatherShaderGlobals.LocalSummer(0.5f, -45f), Is.EqualTo(-1f).Within(1e-4f));
			Assert.That(WeatherShaderGlobals.LocalSummer(0f, 60f), Is.EqualTo(-1f).Within(1e-4f));
			Assert.That(WeatherShaderGlobals.LocalSummer(0.5f, 0f), Is.EqualTo(0f).Within(1e-4f));
			Assert.That(WeatherShaderGlobals.LocalPhase(0.5f, -30f), Is.EqualTo(0f).Within(1e-4f));
			Assert.That(WeatherShaderGlobals.LocalPhase(0.8f, 30f), Is.EqualTo(0.8f).Within(1e-4f));
			WeatherShaderGlobals.ApplySeason(0.5f, 0f, -0.2f);
			try
			{
				Vector4 season = Shader.GetGlobalVector(WeatherShaderGlobals.Season);
				Assert.That(season.w, Is.GreaterThan(0f), "an equatorial scene must still read as a known season");
				Assert.That(season.z, Is.EqualTo(-0.2f).Within(1e-5f));
			}
			finally
			{
				WeatherShaderGlobals.ClearSeason();
			}
			Assert.That(Shader.GetGlobalVector(WeatherShaderGlobals.Season), Is.EqualTo(Vector4.zero));
		}
	}
}
