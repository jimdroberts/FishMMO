using System.Collections.Generic;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The shrubs (BushMeshes) and how the GPU detail scatter draws them: valid single-sub-mesh levels within budget, at
	/// their species' size; levels that cover as much as the full plant (a player hiding in a bush is hidden from far as
	/// well as near); the level choice the compute mirrors; and the rules that keep cover the same for every player.
	/// </summary>
	[TestFixture]
	public class BushTests
	{
		private const int Seed = 1234;

		/// <summary>Triangle budgets per level: a bush near the camera, a stand of them in the middle distance, a field far off.</summary>
		private static readonly int[] Budget = { 4000, 1600, 400 };

		[Test]
		public void Bushes_AreValidDeterministicSingleSubmesh_AndWithinBudget()
		{
			foreach (BushSpecies species in ProceduralArtCatalogue.Bushes)
			{
				BushSpecies sp = species;
				int previous = int.MaxValue;
				for (int lod = 0; lod < BushMeshes.Levels; lod++)
				{
					MeshBuilder mesh = BushMeshes.Build(in sp, lod, Seed);
					List<string> problems = mesh.Validate(false);
					Assert.That(problems, Is.Empty, $"{sp.Name} LOD{lod}: {string.Join("; ", problems)}");
					Assert.That(mesh.Submeshes.Count, Is.EqualTo(1), $"{sp.Name}: a detail prototype draws one sub-mesh");
					Assert.That(mesh.TriangleCount, Is.InRange(1, Budget[lod]), $"{sp.Name} LOD{lod}");
					Assert.That(mesh.TriangleCount, Is.LessThan(previous), $"{sp.Name} LOD{lod} is no lighter than the level before");
					previous = mesh.TriangleCount;
					Assert.That(mesh.Wind.Count, Is.EqualTo(mesh.VertexCount));
					MeshBuilder again = BushMeshes.Build(in sp, lod, Seed);
					Assert.That(again.VertexCount, Is.EqualTo(mesh.VertexCount), $"{sp.Name} LOD{lod} is not deterministic");
					for (int i = 0; i < mesh.VertexCount; i++)
					{
						Assert.That(again.Positions[i], Is.EqualTo(mesh.Positions[i]), $"{sp.Name} LOD{lod} is not deterministic");
					}
				}
			}
		}

		[Test]
		public void Bushes_ComeOutAtTheirSpeciesSize_StandingOnTheGround()
		{
			foreach (BushSpecies species in ProceduralArtCatalogue.Bushes)
			{
				BushSpecies sp = species;
				Bounds b = BushMeshes.Build(in sp, 0, Seed).Bounds;
				// Leaf tips stand a little proud of the crown on the smallest bushes: well inside the ±20 % each instance is scaled by.
				Assert.That(b.max.y, Is.InRange(sp.Height * 0.85f, sp.Height * 1.2f), $"{sp.Name} height");
				float width = Mathf.Max(b.size.x, b.size.z);
				Assert.That(width, Is.InRange(sp.Width * 0.8f, sp.Width * 1.3f), $"{sp.Name} width");
				Assert.That(b.min.y, Is.LessThan(0f), $"{sp.Name} stands on air: its stems must start in the ground");
			}
		}

		[Test]
		public void ReducedLevels_CarryMoreLeafThanTheFullPlant()
		{
			foreach (BushSpecies species in ProceduralArtCatalogue.Bushes)
			{
				BushSpecies sp = species;
				float full = BushMeshes.LeafArea(BushMeshes.Build(in sp, 0, Seed));
				Assert.That(full, Is.GreaterThan(0f), sp.Name);
				Assert.That(BushMeshes.LeafArea(BushMeshes.Build(in sp, 1, Seed)) / full, Is.EqualTo(BushMeshes.ReducedFoliageShare).Within(0.05f), $"{sp.Name} LOD1");
				Assert.That(BushMeshes.LeafArea(BushMeshes.Build(in sp, 2, Seed)) / full, Is.EqualTo(BushMeshes.FarFoliageShare).Within(0.05f), $"{sp.Name} LOD2");
			}
		}

		/// <summary>
		/// The point of a bush: someone standing in it is hidden. Rays from all round at body height toward the bush's
		/// axis, stopped 25 cm short of it, must mostly meet an opaque leaf (alpha-tested against the atlas), at every
		/// level; a reduced level, which players farther off see, may not be much more see-through than the full one.
		/// The broad-leaved and box domes hide best; open shrubs (creosote, hazel's coppice stems), needle shrubs and a
		/// juniper's narrow column less, as the real ones do. Thresholds measured at two seeds (memory tools/bushproto).
		/// Ground cover under 0.6 m (heather, bilberry) hides nobody standing in it, and an open species
		/// (<see cref="BushSpecies.Open"/>: ocotillo's canes, a thornbush's sparse twigs) hides nobody by nature: both are
		/// exempt from the threshold, but their reduced levels still may not be much more see-through than the full plant,
		/// which is the fairness rule.
		/// </summary>
		[Test]
		public void Bushes_HideSomeoneStandingInThem_AtEveryLevel()
		{
			const int atlasSize = 256;
			Color32[] atlas = FoliageAtlas.Generate(atlasSize, Seed);
			foreach (BushSpecies species in ProceduralArtCatalogue.Bushes)
			{
				BushSpecies sp = species;
				// The leaf-mass domes (broad or small leaves; the legacy domes are all one of the two) hide best.
				bool dense = sp.Habit == BushHabit.Mound && (sp.LeafCell == FoliageCell.BushBroad || sp.LeafCell == FoliageCell.BushSmall);
				bool exempt = sp.Height < 0.6f || sp.Open;
				float full = 0f;
				for (int lod = 0; lod < BushMeshes.Levels; lod++)
				{
					MeshBuilder mesh = BushMeshes.Build(in sp, lod, Seed);
					float blocked = Blocked(mesh, atlas, atlasSize, sp.Height, sp.Width * 0.5f);
					if (lod == 0)
					{
						full = blocked;
						if (!exempt)
						{
							Assert.That(blocked, Is.GreaterThanOrEqualTo(dense ? 0.82f : 0.7f), $"{sp.Name}: only {blocked:P0} of sight lines to someone inside are blocked");
						}
					}
					else
					{
						Assert.That(blocked, Is.GreaterThanOrEqualTo(Mathf.Min(full - 0.15f, 0.85f)), $"{sp.Name} LOD{lod}: {blocked:P0} blocked against {full:P0} at full detail");
						if (!exempt)
						{
							Assert.That(blocked, Is.GreaterThanOrEqualTo(0.7f), $"{sp.Name} LOD{lod}: {blocked:P0} blocked");
						}
					}
				}
			}
		}

		private static float Blocked(MeshBuilder m, Color32[] atlas, int atlasSize, float height, float radius)
		{
			const int azimuths = 32, heights = 4;
			int blocked = 0, total = 0;
			List<int> indices = m.Submeshes[0];
			for (int a = 0; a < azimuths; a++)
			{
				float yaw = (a + 0.5f) * Mathf.PI * 2f / azimuths;
				var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
				for (int h = 0; h < heights; h++)
				{
					Vector3 origin = outward * (radius * 2f) + Vector3.up * (Mathf.Lerp(0.3f, 0.75f, (h + 0.5f) / heights) * height);
					float reach = radius * 2f - 0.25f;
					total++;
					for (int t = 0; t < indices.Count; t += 3)
					{
						int ia = indices[t], ib = indices[t + 1], ic = indices[t + 2];
						if (m.Colors[ia].a < 200 || !Hit(origin, -outward, m.Positions[ia], m.Positions[ib], m.Positions[ic], out float distance, out float u, out float v) || distance >= reach)
						{
							continue;
						}
						Vector2 uv = m.UVs[ia] * (1f - u - v) + m.UVs[ib] * u + m.UVs[ic] * v;
						int x = Mathf.Clamp((int)(uv.x * atlasSize), 0, atlasSize - 1), y = Mathf.Clamp((int)(uv.y * atlasSize), 0, atlasSize - 1);
						if (atlas[y * atlasSize + x].a >= 115)
						{
							blocked++;
							break;
						}
					}
				}
			}
			return blocked / (float)total;
		}

		private static bool Hit(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
		{
			t = u = v = 0f;
			Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
			float det = Vector3.Dot(e1, p);
			if (Mathf.Abs(det) < 1e-9f)
			{
				return false;
			}
			float inv = 1f / det;
			Vector3 s = o - a;
			u = Vector3.Dot(s, p) * inv;
			if (u < 0f || u > 1f)
			{
				return false;
			}
			Vector3 q = Vector3.Cross(s, e1);
			v = Vector3.Dot(d, q) * inv;
			if (v < 0f || u + v > 1f)
			{
				return false;
			}
			t = Vector3.Dot(e2, q) * inv;
			return t > 0f;
		}

		/// <summary>
		/// A whip shrub (ocotillo) is canes from one foot: every level keeps its canes (its leaves would float without
		/// them), and its leaves hang along the canes rather than on a crown shell, so the plant is taller than it is wide.
		/// </summary>
		[Test]
		public void WhipShrubs_KeepTheirCanesAtEveryLevel_AndStandTallerThanWide()
		{
			int whips = 0;
			foreach (BushSpecies species in ProceduralArtCatalogue.Bushes)
			{
				if (species.Habit != BushHabit.Whip)
				{
					continue;
				}
				whips++;
				BushSpecies sp = species;
				for (int lod = 0; lod < BushMeshes.Levels; lod++)
				{
					MeshBuilder mesh = BushMeshes.Build(in sp, lod, Seed);
					int wood = 0;
					foreach (int i in mesh.Submeshes[0])
					{
						wood += mesh.Colors[i].a == 0 ? 1 : 0;
					}
					Assert.That(wood, Is.GreaterThan(0), $"{sp.Name} LOD{lod} has no canes");
				}
				Bounds b = BushMeshes.Build(in sp, 0, Seed).Bounds;
				Assert.That(b.size.y, Is.GreaterThan(Mathf.Max(b.size.x, b.size.z)), $"{sp.Name}: a fountain of canes, not a dome");
			}
			Assert.That(whips, Is.GreaterThan(0), "no whip shrub in the catalogue");
		}
		
		/// <summary>The design's shrub list (vegetation expansion, 2026-10-10) is in the catalogue under its names: the spec table refers to them by name.</summary>
		[Test]
		public void TheExpansionsShrubs_AreAllInTheCatalogue()
		{
			foreach (string name in new[] { "Heather", "DwarfBirch", "Bilberry", "Holly", "Hawthorn", "Rabbitbrush", "Saltbush", "Tamarisk", "Ocotillo", "Oleander", "Cistus", "DwarfBamboo", "Thornbush", "Palmetto" })
			{
				Assert.That(ProceduralArtCatalogue.TryBush(name, out _), name);
			}
		}
		
		[Test]
		public void BushNames_AreWhatTheClientScatterTakesAsCover()
		{
			Assert.That(DetailScatterSettings.BushPrefix, Is.EqualTo(ProceduralArtCatalogue.BushPrefix), "the generator's and the client's bush prefix must match");
			var none = new DetailScatterSettings { PrototypePrefixes = new string[0] };
			foreach (BushSpecies sp in ProceduralArtCatalogue.Bushes)
			{
				string prefab = ProceduralArtCatalogue.BushPrefab(sp.Name);
				Assert.That(DetailScatterSettings.IsBush(prefab), prefab);
				Assert.That(none.IsScatterPrototype(prefab), $"{prefab}: a bush is always on the GPU scatter, whatever the prefix list says");
				Assert.That(ProceduralArtCatalogue.BushMesh(sp.Name, 0), Is.EqualTo(prefab), "level 0 is the prefab's own mesh name");
			}
			Assert.IsFalse(DetailScatterSettings.IsBush("Detail_ShrubSmall"), "the small detail shrubs are ground cover, not bushes");
		}

		/// <summary>
		/// A bush prefab as the generator writes it (root mesh and renderer, bare <c>LOD1</c>/<c>LOD2</c> MeshFilters) passes
		/// the terrain scatter's detail check, while any other detail with a second mesh is still refused. The check used to
		/// refuse every bush ("has more than one MeshFilter"), so a repaint skipped every bush rule and none ever grew.
		/// </summary>
		[Test]
		public void TerrainScatter_TakesABushPrefabWithItsLevels_ButNotOtherMultiMeshDetails()
		{
			var validate = typeof(TerrainScatter).GetMethod("ValidateDetailPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			Assert.That(validate, Is.Not.Null, "TerrainScatter.ValidateDetailPrefab was renamed: update this test");
			var created = new List<Object>();
			try
			{
				GameObject Make(string name, bool levelRenderer)
				{
					var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
					var material = new Material(Shader.Find("Hidden/InternalErrorShader")) { enableInstancing = true };
					var root = new GameObject(name);
					created.Add(mesh); created.Add(material); created.Add(root);
					root.AddComponent<MeshFilter>().sharedMesh = mesh;
					root.AddComponent<MeshRenderer>().sharedMaterial = material;
					for (int lod = 1; lod < BushMeshes.Levels; lod++)
					{
						var child = new GameObject(ProceduralArtCatalogue.BushLevelChild(lod));
						child.transform.SetParent(root.transform, false);
						child.AddComponent<MeshFilter>().sharedMesh = mesh;
						if (levelRenderer)
						{
							child.AddComponent<MeshRenderer>().sharedMaterial = material;
						}
					}
					return root;
				}
				string Why(GameObject prefab)
				{
					object[] args = { prefab, false };
					return (string)validate.Invoke(null, args);
				}
				Assert.That(Why(Make(ProceduralArtCatalogue.BushPrefab("Hazel"), false)), Is.Null, "a generated bush must be a valid detail");
				Assert.That(Why(Make(ProceduralArtCatalogue.BushPrefab("Hazel"), true)), Is.Not.Null, "a level with a renderer would draw twice in edit mode");
				Assert.That(Why(Make("Detail_Fern", false)), Is.Not.Null, "only counted details (bushes) carry levels");
			}
			finally
			{
				foreach (Object o in created)
				{
					Object.DestroyImmediate(o);
				}
			}
		}

		[Test]
		public void SpecTable_BushRules_AreCountedDetailsOfRealSpecies()
		{
			int rules = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter s) in entry.Rules())
				{
					if (s.Prefabs == null || s.Prefabs.Length == 0 || !s.Prefabs[0].StartsWith(ProceduralArtCatalogue.BushPrefix, System.StringComparison.Ordinal))
					{
						continue;
					}
					rules++;
					string species = s.Prefabs[0].Substring(ProceduralArtCatalogue.BushPrefix.Length);
					Assert.That(ProceduralArtCatalogue.TryBush(species, out _), $"{entry.Biome} / {s.Name}: no species '{species}'");
					Assert.That(s.Channel, Is.EqualTo(PrefabSpawnChannel.DetailLayer), $"{entry.Biome} / {s.Name}: a bush is a detail (no collider)");
					Assert.That(s.Placement, Is.EqualTo(DetailPlacement.Scattered), $"{entry.Biome} / {s.Name}: bushes are counted, not a carpet");
					Assert.That(s.Coverage, Is.EqualTo(BiomeArtSpec.BushCoverage), $"{entry.Biome} / {s.Name}: one placement is one bush only at the calibrated value");
					Assert.That(s.Spacing, Is.GreaterThan(0f), $"{entry.Biome} / {s.Name}: bushes grow apart");
				}
			}
			Assert.That(rules, Is.GreaterThan(30), "bushes should grow in most land biomes");
		}

		// ── The scatter's levels (DetailScatterMath, mirrored by FishDetailScatter.compute) ──

		[Test]
		public void SelectLevel_StepsAtTheHeightScaledDistances_AndCrossFadesInEqualMagnitudes()
		{
			var steps = new Vector2(12f, 40f);
			const float band = 0.15f, height = 2f;
			Assert.That(DetailScatterMath.SelectLevel(5f, height, 3, steps, band, out float fade, out int partner, out _), Is.EqualTo(0));
			Assert.That(fade, Is.EqualTo(0f));
			Assert.That(partner, Is.EqualTo(-1));
			// In level 0's last 15 % (20.4 .. 24 m): fading out into level 1, the same magnitude both ways.
			Assert.That(DetailScatterMath.SelectLevel(22.2f, height, 3, steps, band, out fade, out partner, out float partnerFade), Is.EqualTo(0));
			Assert.That(partner, Is.EqualTo(1));
			Assert.That(fade, Is.EqualTo(-partnerFade));
			Assert.That(partnerFade, Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(DetailScatterMath.SelectLevel(50f, height, 3, steps, band, out _, out partner, out _), Is.EqualTo(1));
			Assert.That(partner, Is.EqualTo(-1));
			Assert.That(DetailScatterMath.SelectLevel(300f, height, 3, steps, band, out fade, out partner, out _), Is.EqualTo(2));
			Assert.That(partner, Is.EqualTo(-1), "the last level fades into nothing but the reach's own edge");
			// A two-level type steps once; a one-level type never.
			Assert.That(DetailScatterMath.SelectLevel(300f, height, 2, steps, band, out _, out _, out _), Is.EqualTo(1));
			Assert.That(DetailScatterMath.SelectLevel(300f, height, 1, steps, band, out fade, out partner, out _), Is.EqualTo(0));
			Assert.That(fade, Is.EqualTo(0f));
		}

		[Test]
		public void LevelsReached_CoversEveryLevelAnInstanceInTheRangeCanDraw()
		{
			var steps = new Vector2(12f, 40f);
			const float band = 0.15f;
			var rng = new System.Random(5);
			for (int i = 0; i < 4000; i++)
			{
				float near = (float)rng.NextDouble() * 300f, far = near + (float)rng.NextDouble() * 60f;
				float minHeight = 0.5f + (float)rng.NextDouble() * 2f, maxHeight = minHeight * (1f + (float)rng.NextDouble() * 0.5f);
				int levels = 1 + rng.Next(3);
				int mask = DetailScatterMath.LevelsReached(near, far, minHeight, maxHeight, levels, steps, band);
				for (int k = 0; k < 8; k++)
				{
					float d = Mathf.Lerp(near, far, k / 7f), h = Mathf.Lerp(minHeight, maxHeight, ((i + k) % 5) / 4f);
					int level = DetailScatterMath.SelectLevel(d, h, levels, steps, band, out _, out int partner, out _);
					Assert.That(mask & (1 << level), Is.Not.Zero, $"level {level} at {d} m, height {h}, not in the bound's levels {mask}");
					if (partner >= 0)
					{
						Assert.That(mask & (1 << partner), Is.Not.Zero, $"partner {partner} at {d} m, height {h}, not in {mask}");
					}
				}
			}
		}

		[Test]
		public void BushReach_GoesByHeightAlone_WithinItsClamp()
		{
			var s = new DetailScatterSettings();
			Assert.That(s.BushDrawDistance(0.2f), Is.EqualTo(s.BushDrawMin));
			Assert.That(s.BushDrawDistance(2f), Is.EqualTo(Mathf.Clamp(2f * s.BushDrawMetresPerMetre, s.BushDrawMin, s.BushDrawMax)));
			Assert.That(s.BushDrawDistance(100f), Is.EqualTo(s.BushDrawMax));
			Assert.That(s.BushDrawDistance(3f), Is.GreaterThanOrEqualTo(s.BushDrawDistance(2f)), "a taller bush is seen at least as far");
		}
	}
}
