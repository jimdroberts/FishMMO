using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The sea floor's life: on the layer that is painted under the sea, in bands of depth, as meshes that
	/// face the right way and sway (or not) like the thing they are.
	/// </summary>
	[TestFixture]
	public class SeaFloorTests
	{
		private static readonly string[] SeaBiomes =
		{
			"Abyssal Plain", "Abyss", "Deep Ocean", "Ocean", "Seamount", "Underwater Canyon", "Coastal Water", "Coral Reef", "Subsurface Ocean Vent",
		};

		private static bool IsSeaDetail(string prefab, out DetailSpec spec)
		{
			spec = default;
			const string prefix = "Detail_";
			return prefab.StartsWith(prefix, StringComparison.Ordinal)
				&& ProceduralArtCatalogue.TryDetail(prefab.Substring(prefix.Length), out spec)
				&& SeaFloorMeshes.IsAquatic(spec.Plant.Kind);
		}

		[Test]
		public void EverySeaBiome_HasLifeOnItsLakebed_AndNoneOnLayersTheSeaNeverPaints()
		{
			foreach (string name in SeaBiomes)
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.That(entry, Is.Not.Null, name);
				Assert.That(entry.Bed, Is.Not.Null, $"{name} has nothing on its sea floor");
				Assert.That(entry.Bed.Scatter, Is.Not.Empty, name);
				Assert.That(entry.Bed.Family, Is.EqualTo(entry.Lakebed), $"{name}: the bed's rules ride on the lakebed layer");
			}
			// Sea life anywhere but the bed only ever grew on islands (the land gate keeps everything else above the tide).
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((BiomeArtSpec.Layer layer, BiomeArtSpec.Scatter rule) in entry.Rules())
				{
					foreach (string prefab in rule.Prefabs)
					{
						if (IsSeaDetail(prefab, out _))
						{
							Assert.That(layer, Is.SameAs(entry.Bed), $"{entry.Biome} '{rule.Name}' is sea life on a layer that is never under the sea");
						}
					}
				}
			}
		}

		[Test]
		public void LightLovingLife_KeepsToTheShallows_AndCoralToWarmWater()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				if (entry.Bed == null)
				{
					continue;
				}
				foreach (BiomeArtSpec.Scatter rule in entry.Bed.Scatter)
				{
					if (rule.Name == "Kelp" || rule.Name == "Seaweed")
					{
						Assert.That(rule.Depth.y, Is.InRange(1f, 40f), $"{entry.Biome} '{rule.Name}' needs light: a deepest depth within the photic zone");
						Assert.That(rule.Depth.x, Is.GreaterThan(0.5f), $"{entry.Biome} '{rule.Name}' should start under the low tide");
					}
					if (rule.Name == "BrainCoral" || rule.Name == "TableCoral")
					{
						Assert.That(entry.Biome, Is.EqualTo("Coral Reef"), $"reef-building coral in {entry.Biome}");
					}
					Assert.That(rule.Depth.y <= 0f || rule.Depth.y > rule.Depth.x, Is.True, $"{entry.Biome} '{rule.Name}' has an empty depth band");
				}
			}
			Assert.That(BiomeArtSpec.For("Subsurface Ocean Vent").Bed.Scatter.Exists(r => r.Name == "TubeWorms"), Is.True, "a vent should have its tube worms");
		}

		[Test]
		public void RulesTheSpecMovedOff_AreRecordedForTheAuthoringTool()
		{
			Assert.That(BiomeArtSpec.For("Ocean").Retired, Has.Member(("main", "Kelp")));
			Assert.That(BiomeArtSpec.For("Coral Reef").Retired, Has.Member(("main", "Coral")));
			Assert.That(BiomeArtSpec.For("Coastal Water").Retired, Has.Member(("detail/0", "Kelp")));
		}

		[Test]
		public void TheDepthBand_IsFullInside_ThinsAtItsEnds_AndOpenEndsAreOpen()
		{
			var band = new Vector2(4f, 30f);
			Assert.That(TerrainScatter.DepthBand(Vector2.zero, -500f), Is.EqualTo(1f), "no band: anywhere");
			Assert.That(TerrainScatter.DepthBand(band, -15f), Is.EqualTo(1f));
			Assert.That(TerrainScatter.DepthBand(band, -2f), Is.EqualTo(0f), "shallower than the band");
			Assert.That(TerrainScatter.DepthBand(band, -40f), Is.EqualTo(0f), "deeper than the band");
			float edge = TerrainScatter.DepthBand(band, -5f);
			Assert.That(edge, Is.InRange(0.01f, 0.99f), "thinning toward the shallow end");
			Assert.That(TerrainScatter.DepthBand(new Vector2(6f, 0f), -2000f), Is.EqualTo(1f), "an open deep end");
			Assert.That(TerrainScatter.DepthBand(new Vector2(0f, 20f), -0.1f), Is.EqualTo(1f), "an open shallow end");
		}

		/// <summary>The sea floor's kinds added by the vegetation expansion (2026-10-10), MusselBed … BacterialMat in <see cref="DetailKind"/>.</summary>
		private static readonly DetailKind[] NewSeaKinds =
		{
			DetailKind.MusselBed, DetailKind.Barnacles, DetailKind.SeaLettuce, DetailKind.SandDollar, DetailKind.SeaCucumber, DetailKind.BrittleStar,
			DetailKind.SeaPen, DetailKind.Crinoid, DetailKind.GlassSponge, DetailKind.ColdCoral, DetailKind.SoftCoral, DetailKind.GiantClam,
			DetailKind.Xenophyophore, DetailKind.BacterialMat,
		};

		[Test]
		public void EveryDesignedSeaKind_IsCatalogued_Built_AndUnderTheSea()
		{
			var seen = new HashSet<(DetailKind, int)>();
			foreach (DetailSpec spec in ProceduralArtCatalogue.Details)
			{
				seen.Add((spec.Plant.Kind, spec.Plant.Variant));
			}
			foreach (DetailKind kind in NewSeaKinds)
			{
				Assert.That(seen.Contains((kind, 0)), Is.True, $"no detail is a {kind}");
				Assert.That(SeaFloorMeshes.IsAquatic(kind), Is.True, $"{kind} lives under the sea");
			}
			// The bed's other forms (oysters, a vent's clams) and the stalked glass sponges of the mud.
			Assert.That(seen.Contains((DetailKind.MusselBed, 1)) && seen.Contains((DetailKind.MusselBed, 2)), Is.True, "mussel bed variants");
			Assert.That(seen.Contains((DetailKind.GlassSponge, 1)), Is.True, "stalked glass sponges");
			// The mat's land cousin is built with the sea floor but grows in a cave, never under water.
			Assert.That(seen.Contains((DetailKind.SlimeMat, 0)), Is.True, "a cave's slime mould");
			Assert.That(SeaFloorMeshes.IsAquatic(DetailKind.SlimeMat), Is.False, "slime mould is not aquatic");

			foreach (DetailSpec spec in ProceduralArtCatalogue.Details)
			{
				DetailPlant plant = spec.Plant;
				if (Array.IndexOf(NewSeaKinds, plant.Kind) < 0 && plant.Kind != DetailKind.SlimeMat)
				{
					continue;
				}
				MeshBuilder mesh = VegetationMeshes.Build(in plant, 7);
				Assert.That(mesh.TriangleCount, Is.GreaterThan(0), spec.Name);
				Assert.That(mesh.Validate(false), Is.Empty, spec.Name);
				// Each carries its own grouping and sink, since the spec's helpers know only the legacy names.
				Assert.That(spec.GroupMetres, Is.GreaterThan(0f), $"{spec.Name}: group radius");
				Assert.That(spec.GroupSize, Is.GreaterThan(0f), $"{spec.Name}: group size");
				Assert.That(spec.Sink.y, Is.GreaterThan(0f).And.GreaterThanOrEqualTo(spec.Sink.x), $"{spec.Name}: sink {spec.Sink}");
			}
		}

		[Test]
		public void LyingThings_LieLow_AndStandingThings_Stand()
		{
			foreach (DetailSpec spec in ProceduralArtCatalogue.Details)
			{
				DetailPlant plant = spec.Plant;
				MeshBuilder mesh;
				switch (plant.Kind)
				{
					case DetailKind.SandDollar:
					case DetailKind.BrittleStar:
					case DetailKind.MusselBed:
					case DetailKind.SeaCucumber:
					case DetailKind.BacterialMat:
					case DetailKind.SlimeMat:
						mesh = VegetationMeshes.Build(in plant, 7);
						Bounds lying = mesh.Bounds;
						Assert.That(lying.size.y, Is.LessThan(0.35f * Mathf.Max(lying.size.x, lying.size.z)), $"{spec.Name} should lie on the bed: {lying.size}");
						Assert.That(lying.max.y, Is.LessThan(0.15f), $"{spec.Name} stands {lying.max.y} m tall");
						break;
					case DetailKind.SeaPen:
					case DetailKind.Crinoid:
					case DetailKind.GlassSponge:
						mesh = VegetationMeshes.Build(in plant, 7);
						Bounds standing = mesh.Bounds;
						Assert.That(standing.max.y, Is.GreaterThan(0.5f * plant.Height), $"{spec.Name} should stand about {plant.Height} m tall: {standing.max.y}");
						// Rooted: some of it below the bed (a sea pen's bulb, a stalk's foot, a basket's rooting tuft).
						Assert.That(standing.min.y, Is.LessThan(0f), $"{spec.Name} is not rooted");
						break;
				}
			}
		}

		[Test]
		public void TheSeasSmallRocks_AreNodulesAndCoralRubble()
		{
			var materials = new Dictionary<string, string>();
			foreach (RockMaterialSpec m in ProceduralArtCatalogue.AllRockMaterials)
			{
				materials[m.Name] = m.GroundFamily;
			}
			// A manganese nodule is a matte brown-black knobbly crust: the ash's grains, not basalt's fractured gloss.
			Assert.That(materials["Nodule"], Is.EqualTo(Ground.Ash));
			Assert.That(materials["Coral"], Is.EqualTo(Ground.Coral));
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			foreach (string name in new[] { "Nodule", "Coral" })
			{
				Assert.That(prefabs, Does.Contain(ProceduralArtCatalogue.PebblesPrefab(name)));
				Assert.That(prefabs, Does.Contain(ProceduralArtCatalogue.SmallRocksPrefab(name)));
			}
		}

		[Test]
		public void SeaFloorMeshes_FaceOutward_AndOnlyWhatGivesSways()
		{
			foreach (DetailSpec spec in ProceduralArtCatalogue.Details)
			{
				DetailPlant plant = spec.Plant;
				if (!SeaFloorMeshes.IsAquatic(plant.Kind))
				{
					continue;
				}
				Vector2 give = SeaFloorMeshes.Sway(plant.Kind);
				// What gives with the surge: weed and kelp, fans, anemones' tentacles, a vent's plumes, sea lettuce's sheets,
				// a sea pen's and a crinoid's feathers, a soft coral's flesh. Stony corals, sponges, shells, lying animals,
				// beds, barnacled stones and mats do not.
				bool gives = plant.Kind == DetailKind.Kelp || plant.Kind == DetailKind.Seaweed || plant.Kind == DetailKind.SeaFan
					|| plant.Kind == DetailKind.Anemone || plant.Kind == DetailKind.TubeWorms || plant.Kind == DetailKind.SeaLettuce
					|| plant.Kind == DetailKind.SeaPen || plant.Kind == DetailKind.Crinoid || plant.Kind == DetailKind.SoftCoral;
				Assert.That(give.x > 0f, Is.EqualTo(gives), $"{spec.Name}: sway {give.x}");
				if (plant.Kind != DetailKind.BrainCoral && plant.Kind != DetailKind.Sponge && plant.Kind != DetailKind.TableCoral)
				{
					continue;
				}
				// The solid ones are lathes. One of them alone (a cluster's members stand apart, so the cluster has
				// no middle to face away from), centred on the up axis: its first ring of faces is the outside of
				// its foot — a dome's tuck, a table's stalk, a sponge's outer wall — and must face away from the axis.
				// (A sponge's inside wall faces in by design, so the whole shape is no test.)
				plant.Count = 1;
				MeshBuilder mesh = VegetationMeshes.Build(in plant, 7);
				List<int> tris = mesh.Submeshes[0];
				int sides = plant.Kind == DetailKind.BrainCoral ? 28 : plant.Kind == DetailKind.TableCoral ? 32 : 16;
				int outward = 0, total = 0;
				for (int t = 0; t < 2 * sides * 3 && t < tris.Count; t += 3)
				{
					Vector3 a = mesh.Positions[tris[t]], b = mesh.Positions[tris[t + 1]], c = mesh.Positions[tris[t + 2]];
					Vector3 face = Vector3.Cross(b - a, c - a);
					if (face.sqrMagnitude < 1e-12f)
					{
						continue;
					}
					Vector3 mid = (a + b + c) / 3f;
					total++;
					if (Vector3.Dot(face, new Vector3(mid.x, 0f, mid.z)) > 0f)
					{
						outward++;
					}
				}
				Assert.That(total, Is.GreaterThan(sides), spec.Name);
				Assert.That(outward, Is.EqualTo(total), $"{spec.Name}: {outward}/{total} of its foot's faces face outward");
			}
		}
	}
}
