using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FishMMO.Client;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Variant = FishMMO.Shared.WorldDesign.ProceduralArtVariants.Variant;
using MaterialKeywords = FishMMO.Shared.WorldDesign.ProceduralArtVariants.MaterialKeywords;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The scatter's material reduction (2026-11-04): identical materials share one clone (TerrainGpuRenderer.ContentKey), and the
	/// alpha-tested vegetation kinds of one structure draw through one family material whose table holds each kind's scalars
	/// (TerrainVegTableMath, FishVegetationPasses.hlsl FISH_VEG_TABLE), named per vertex by the pooled mesh's uv4.
	/// </summary>
	public class DetailScatterFamilyTests
	{
		private const string Passes = "Assets/Prefabs/Client/Weather/Shaders/FishVegetationPasses.hlsl";

		[Test]
		public void TheShaderReadsFromTheTable_ExactlyTheTablesProperties()
		{
			string code = ProjectFiles.ReadAllText(Passes);
			var defined = new HashSet<string>(Regex.Matches(code, @"#define (_\w+)\s+FishVeg\w+").Cast<Match>().Select(m => m.Groups[1].Value));
			Assert.That(defined, Is.EquivalentTo(TerrainVegTableMath.TableProperties),
				"a property the family's members may differ in must come from the row, and only those may (the key keeps the rest equal)");
		}

		[Test]
		public void TheRowsColumns_AreWhereTheShaderLoadsThem()
		{
			string setup = SourceScanPins.Body(ProjectFiles.ReadAllText(Passes), "void FishVegTableSetup(float vertexRow)");
			Assert.That(setup, Is.Not.Null);
			var r = new TerrainVegTableMath.Row
			{
				HealthyColor = new Color(0.1f, 0.2f, 0.3f, 0.4f),
				DryColor = new Color(0.5f, 0.6f, 0.7f, 0.8f),
				GroundSink = new Vector4(0.01f, 0.03f, 0f, 0f),
				SnowBury = 1f, TintPatchMetres = 2f, WindSway = 3f, WindFlutter = 4f,
				Aquatic = 5f, Evergreen = 6f, Deciduous = 7f, TintSpread = 8f,
				VaryLean = 9f, VaryPartDroop = 10f, DistanceFade = 11f, Clinging = 12f,
				WeatherAmount = 13f, Translucency = 14f,
			};
			var pixels = new Color[TerrainVegTableMath.Columns * 2];
			TerrainVegTableMath.WriteRow(r, pixels, 1);
			Color[] row = pixels.Skip(TerrainVegTableMath.Columns).ToArray();
			// Each scalar the shader takes from column c, component k, is the row's.
			var reads = new Dictionary<string, float>
			{
				{ "FishVegSnowBury = c3.x", row[3].r }, { "FishVegTintPatchMetres = c3.y", row[3].g },
				{ "FishVegWindSway = c3.z", row[3].b }, { "FishVegWindFlutter = c3.w", row[3].a },
				{ "FishVegAquatic = c4.x", row[4].r }, { "FishVegEvergreen = c4.y", row[4].g },
				{ "FishVegDeciduous = c4.z", row[4].b }, { "FishVegTintSpread = c4.w", row[4].a },
				{ "FishVegVaryLean = c5.x", row[5].r }, { "FishVegVaryPartDroop = c5.y", row[5].g },
				{ "FishVegDistanceFade = c5.z", row[5].b }, { "FishVegClinging = c5.w", row[5].a },
				{ "FishVegWeatherAmount = c6.x", row[6].r }, { "FishVegTranslucency = c6.y", row[6].g },
			};
			float[] expected = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f };
			int i = 0;
			foreach (KeyValuePair<string, float> read in reads)
			{
				StringAssert.Contains(read.Key + ";", setup);
				Assert.That(read.Value, Is.EqualTo(expected[i++]), read.Key);
			}
			StringAssert.Contains("FishVegHealthyColor = (half4)LOAD_TEXTURE2D(_FishVegTable, int2(0, row));", setup);
			StringAssert.Contains("FishVegDryColor = (half4)LOAD_TEXTURE2D(_FishVegTable, int2(1, row));", setup);
			StringAssert.Contains("FishVegGroundSink = (half4)LOAD_TEXTURE2D(_FishVegTable, int2(2, row));", setup);
			Assert.That(row[0], Is.EqualTo(r.HealthyColor));
			Assert.That(row[1], Is.EqualTo(r.DryColor));
			Assert.That(row[2], Is.EqualTo(new Color(0.01f, 0.03f, 0f, 0f)));
		}

		[Test]
		public void ARowsColours_AreLinear_ItsSinkAsItIs()
		{
			Shader shader = Shader.Find("FishMMO/Vegetation");
			Assume.That(shader, Is.Not.Null);
			var m = new Material(shader);
			try
			{
				m.SetColor("_HealthyColor", new Color(0.9f, 0.95f, 0.9f, 1f));
				m.SetVector("_GroundSink", new Vector4(0.02f, 0.04f, 0f, 0f));
				TerrainVegTableMath.Row linear = TerrainVegTableMath.RowOf(m, true);
				Assert.That(linear.HealthyColor, Is.EqualTo(new Color(0.9f, 0.95f, 0.9f, 1f).linear), "Unity hands the shader a Color property linear");
				Assert.That(linear.GroundSink, Is.EqualTo(new Vector4(0.02f, 0.04f, 0f, 0f)), "a Vector property is passed as it is");
				Assert.That(TerrainVegTableMath.RowOf(m, false).HealthyColor, Is.EqualTo(new Color(0.9f, 0.95f, 0.9f, 1f)));
			}
			finally
			{
				Object.DestroyImmediate(m);
			}
		}

		private static Material Plant(Shader shader)
		{
			var m = new Material(shader);
			m.EnableKeyword("_ALPHATEST_ON");
			m.SetFloat("_Cutoff", 0.45f);
			m.SetColor("_HealthyColor", new Color(0.9f, 0.95f, 0.9f, 1f));
			m.SetFloat("_WindSway", 0.2f);
			return m;
		}

		[Test]
		public void TheKeys_SplitMaterialsByWhatTheyDrawWith()
		{
			Shader shader = Shader.Find("FishMMO/Vegetation");
			Assume.That(shader, Is.Not.Null);
			Material a = Plant(shader), b = Plant(shader), c = Plant(shader), d = Plant(shader);
			try
			{
				var sb = new StringBuilder();
				// a and b: identical (two kinds the art generator wrote apart): one clone.
				Assert.That(TerrainGpuRenderer.ContentKey(b, sb), Is.EqualTo(TerrainGpuRenderer.ContentKey(a, new StringBuilder())));
				// c differs in table properties only: another clone, the same family.
				c.SetColor("_HealthyColor", new Color(1f, 0.97f, 0.9f, 1f));
				c.SetFloat("_WindSway", 0.35f);
				c.SetVector("_GroundSink", new Vector4(0.03f, 0.06f, 0f, 0f));
				Assert.That(TerrainGpuRenderer.ContentKey(c, sb), Is.Not.EqualTo(TerrainGpuRenderer.ContentKey(a, new StringBuilder())));
				Assert.That(TerrainGpuRenderer.VegFamilyKey(c, sb), Is.EqualTo(TerrainGpuRenderer.VegFamilyKey(a, new StringBuilder())));
				// d differs in a shared property (the cutoff): its own family.
				d.SetFloat("_Cutoff", 0.5f);
				Assert.That(TerrainGpuRenderer.VegFamilyKey(d, sb), Is.Not.EqualTo(TerrainGpuRenderer.VegFamilyKey(a, new StringBuilder())));
				// And a keyword or a queue splits the content too.
				b.DisableKeyword("_ALPHATEST_ON");
				Assert.That(TerrainGpuRenderer.ContentKey(b, sb), Is.Not.EqualTo(TerrainGpuRenderer.ContentKey(a, new StringBuilder())));
				b.EnableKeyword("_ALPHATEST_ON");
				b.renderQueue = 2451;
				Assert.That(TerrainGpuRenderer.ContentKey(b, sb), Is.Not.EqualTo(TerrainGpuRenderer.ContentKey(a, new StringBuilder())));
			}
			finally
			{
				foreach (Material m in new[] { a, b, c, d })
				{
					Object.DestroyImmediate(m);
				}
			}
		}

		[Test]
		public void APooledPartsVertices_NameItsRow()
		{
			var parts = new List<Mesh>();
			for (int p = 0; p < 3; p++)
			{
				var mesh = new Mesh();
				int n = 4 + p * 3;
				var v = new Vector3[n];
				for (int i = 0; i < n; i++)
				{
					v[i] = new Vector3(i, p, 0f);
				}
				mesh.vertices = v;
				var tri = new List<int>();
				for (int i = 0; i + 2 < n; i++)
				{
					tri.AddRange(new[] { 0, i + 1, i + 2 });
				}
				mesh.SetTriangles(tri, 0);
				parts.Add(mesh);
			}
			int[] rows = { 5, 0, 17 };
			Mesh pool = DetailScatterRenderer.PoolMesh(parts, rows);
			try
			{
				var uv = new List<Vector2>();
				pool.GetUVs(TerrainVegTableMath.RowChannel, uv);
				Assert.That(uv.Count, Is.EqualTo(pool.vertexCount));
				Vector3[] pooled = pool.vertices;
				using Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(pool);
				uint[] indices = data[0].GetIndexData<uint>().ToArray();
				for (int p = 0; p < parts.Count; p++)
				{
					int start = (int)pool.GetIndexStart(p), count = (int)pool.GetIndexCount(p), baseVertex = (int)pool.GetBaseVertex(p);
					for (int k = 0; k < count; k++)
					{
						int vertex = (int)indices[start + k] + baseVertex;
						Assert.That(pooled[vertex].y, Is.EqualTo(p), "the part's own vertex");
						Assert.That(uv[vertex].x, Is.EqualTo(rows[p]), $"part {p}'s vertex {vertex} carries its row");
					}
				}
			}
			finally
			{
				Object.DestroyImmediate(pool);
				foreach (Mesh m in parts)
				{
					Object.DestroyImmediate(m);
				}
			}
		}

		[Test]
		public void TheBuild_ListsTheFamilyVariant_ForAlphaTestedVegetationOnly()
		{
			static IReadOnlyDictionary<PassType, HashSet<string>> P(string twin) => new Dictionary<PassType, HashSet<string>>
			{
				{ PassType.ScriptableRenderPipeline, new HashSet<string> { "_ALPHATEST_ON", ProceduralArtVariants.VegTableKeyword } },
			};
			List<Variant> variants = ProceduralArtVariants.Variants(new[]
			{
				new MaterialKeywords("Assets/Grass.mat", "FishMMO/Vegetation", new[] { "_ALPHATEST_ON" }),
				new MaterialKeywords("Assets/Bark.mat", "FishMMO/Vegetation", new string[0]),
			}, P);
			Assert.That(variants, Is.EqualTo(new[]
			{
				new Variant("FishMMO/Vegetation Indirect", PassType.ScriptableRenderPipeline, ""),
				new Variant("FishMMO/Vegetation Indirect", PassType.ScriptableRenderPipeline, "_ALPHATEST_ON"),
				new Variant("FishMMO/Vegetation Indirect", PassType.ScriptableRenderPipeline, "_ALPHATEST_ON _FISH_VEG_TABLE"),
			}));
			Assert.That(ProceduralArtVariants.VegTableKeyword, Is.EqualTo(TerrainVegTableMath.Keyword));
			StringAssert.Contains("keywords: _ALPHATEST_ON _FISH_VEG_TABLE", ProjectFiles.ReadAllText("Assets/Prefabs/Shared/Biomes/Generated/Variants/FishIndirectVariants.shadervariants"));
		}

		[Test]
		public void Wiring_FamiliesDrawFromTheirPoolsOnEveryPath()
		{
			string code = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Terrain/Scatter/DetailScatterRenderer.cs");
			// Alpha-tested vegetation only, as the build lists it.
			StringAssert.Contains("!clone.IsKeywordEnabled(\"_ALPHATEST_ON\")", code);
			// Batched or not, a family command's mesh is its pool (only the pool's vertices name its row).
			Assert.That(SourceScanPins.InOrder(code, "BuildVegTables();", "GroupCommands();", "PoolFamilyCommands();", "args.Add(ArgsOf(command));"), Is.Null);
			StringAssert.Contains("submesh = pool.Index[(command.Source, command.Row)];", code);
			// A part no pool can take keeps its own clone.
			StringAssert.Contains("bool pooled = family && Poolable(mesh);", code);
			StringAssert.Contains("rp.material = pooled ? vegFamily.Material : clone;", code);
			// Identical materials share one clone in both renderers.
			StringAssert.Contains("string key = mergedState ? TerrainGpuRenderer.ContentKey(material, contentKey) : null;", code);
			StringAssert.Contains("string key = MergeIdenticalMaterials ? ContentKey(material, contentKey) : null;",
				SourceScanPins.ReadCode("Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs"));
		}
	}
}
