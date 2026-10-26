using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decision 25's leftovers (2026-10-25, phase I2g-c): a biome authored to sow nothing lays no fields, the prostrate shrub, the
	/// frost-sorted ground motifs and the bone gathering family. Every default is what was cut and generated before.
	/// </summary>
	public class BiomeDecision25Tests
	{
		private const int Seed = 1234;

		[Test]
		public void SowsNothing_OnlyAnAuthoredListThatWeighsNothing_WithNoOrchard()
		{
			var empty = new List<BiomeFieldCrop>();
			Assert.IsTrue(BiomeFieldCrops.SowsNothing(true, empty, 0), "authored empty");
			Assert.IsTrue(BiomeFieldCrops.SowsNothing(true, null, 0), "authored, no list");
			Assert.IsFalse(BiomeFieldCrops.SowsNothing(false, empty, 0), "never authored: wheat, as before");
			Assert.IsFalse(BiomeFieldCrops.SowsNothing(true, empty, 1), "an orchard is a field");
			Assert.IsFalse(BiomeFieldCrops.SowsNothing(true, new List<BiomeFieldCrop> { new BiomeFieldCrop(null, 1f) }, 0), "a crop that weighs, even with its art lost");
			Assert.IsTrue(BiomeFieldCrops.SowsNothing(true, new List<BiomeFieldCrop> { new BiomeFieldCrop(null, 0f), new BiomeFieldCrop(null, 0f) }, 0), "every weight 0");
		}

		[Test]
		public void TheFieldsBuilder_ReturnsEarlyForABiomeThatSowsNothing_AndTheSpecCanSaySo()
		{
			string fields = File.ReadAllText(Path.Combine("Assets", "Scripts", "Shared", "Implementation", "Tools", "Extensions", "Unity", "Editor", "World",
				"PointsOfInterest", "Features", "PointOfInterestFields.cs"));
			int early = fields.IndexOf("if (BiomeTemplateArt.SowsNothing(siteBiome))", System.StringComparison.Ordinal);
			Assert.That(early, Is.GreaterThan(0));
			Assert.That(early, Is.LessThan(fields.IndexOf("var fields = new List<Field>();", System.StringComparison.Ordinal)), "before any field is laid");
			var entry = new BiomeArtSpec.Entry { Biome = "Probe Biome" }.NoCrops();
			Assert.That(entry.FieldCrops(), Is.Empty);
			Assert.That(new BiomeArtSpec.Entry { Biome = "Probe Biome" }.FieldCrops(), Is.Not.Empty, "no crops named: wheat, as before");
		}

		[Test]
		public void Prostrate_StemsLieWithin15DegreesOfLevel_AndTheOthersAreAsBefore()
		{
			Assert.That((int)BushHabit.Prostrate, Is.EqualTo(7), "appended");
			Vector2 e = BushMeshes.StemElevationsOf(BushHabit.Prostrate);
			Assert.That(e.x, Is.GreaterThanOrEqualTo(-15f));
			Assert.That(e.y, Is.LessThanOrEqualTo(15f).And.GreaterThan(e.x));
			Assert.That(BushMeshes.StemRiseOf(BushHabit.Prostrate), Is.EqualTo(0.05f));
			Assert.That(BushMeshes.StemElevationsOf(BushHabit.Mound), Is.EqualTo(new Vector2(-15f, 75f)));
			Assert.That(BushMeshes.StemElevationsOf(BushHabit.Whip), Is.EqualTo(new Vector2(65f, 80f)));
			Assert.That(BushMeshes.StemRiseOf(BushHabit.Spreading), Is.EqualTo(0.12f));
			Assert.That(BushMeshes.StemRiseOf(BushHabit.Mound), Is.EqualTo(0.3f));
		}

		/// <summary>The mesh half (native maths: nativerun or Unity).</summary>
		[Test]
		public void Prostrate_BuildsALowValidMat()
		{
			BushSpecies mat = ProceduralArtCatalogue.Bushes[0];
			mat.Name = "Mat";
			mat.Habit = BushHabit.Prostrate;
			mat.Height = 0.35f;
			mat.Width = 2.4f;
			for (int lod = 0; lod < BushMeshes.Levels; lod++)
			{
				MeshBuilder mesh = BushMeshes.Build(in mat, lod, Seed);
				Assert.That(mesh.Validate(false), Is.Empty, $"LOD{lod}");
				Assert.That(mesh.Bounds.max.y, Is.LessThan(mat.Width * 0.5f), $"LOD{lod}: a mat, lower than it is wide");
			}
		}

		[Test]
		public void PolygonsAndSortedCircles_AreDeterministic_Distinct_InRange_AndTile()
		{
			const int size = 64;
			SurfaceRecipe polygons = SurfaceCatalogue.GroundRecipes[0];
			polygons.Motif = SurfaceMotif.Polygons;
			polygons.MotifCells = 6;
			polygons.MotifSize = 0.04f;
			polygons.MotifWeight = 0.8f;
			SurfaceRecipe circles = polygons;
			circles.Motif = SurfaceMotif.SortedCircles;
			circles.MotifSize = 0.25f;
			SurfaceMaps a = SurfaceSynth.Generate(in polygons, size, Seed), again = SurfaceSynth.Generate(in polygons, size, Seed);
			SurfaceMaps c = SurfaceSynth.Generate(in circles, size, Seed), cAgain = SurfaceSynth.Generate(in circles, size, Seed);
			bool differs = false;
			for (int i = 0; i < a.Height.Length; i++)
			{
				Assert.That(again.Albedo[i], Is.EqualTo(a.Albedo[i]));
				Assert.That(cAgain.Albedo[i], Is.EqualTo(c.Albedo[i]));
				Assert.That(a.Height[i], Is.InRange(0f, 1f));
				Assert.That(c.Height[i], Is.InRange(0f, 1f));
				differs |= Mathf.Abs(a.Height[i] - c.Height[i]) > 1e-3f;
			}
			Assert.IsTrue(differs, "the two motifs drew the same ground");
			foreach (SurfaceMaps maps in new[] { a, c })
			{
				double seam = 0, inner = 0;
				for (int y = 0; y < size; y++)
				{
					seam += Mathf.Abs(maps.Height[y * size] - maps.Height[y * size + size - 1]);
					for (int x = 1; x < size; x++)
					{
						inner += Mathf.Abs(maps.Height[y * size + x] - maps.Height[y * size + x - 1]);
					}
				}
				Assert.That(seam / size / (inner / (size * (size - 1)) + 1e-6), Is.LessThan(2.5), "the left and right edges meet");
			}
		}

		/// <summary>The Scrubland, Rocky Terrain and Scree drafts' landforms added with I2g-c (2026-10-25), in the drafts' spellings.</summary>
		[Test]
		public void TheLaterDraftsLandforms_Resolve()
		{
			foreach (string name in new[] { "Bajada", "Pediment", "Wash", "BraidedWash", "Malpaís", "Malpais", "Maar", "Pamir", "ShatterComb", "ScreeCone",
				"ScreeChutes", "ProtalusRampart", "NivationHollow", "PinnacleRidge", "WalkingRock" })
			{
				Assert.That(BiomeLandformPresets.TryGet(name, out BiomeLandform form), Is.True, name);
				Assert.That(form.Carves, Is.True, name);
			}
			Assert.That(BiomeLandformPresets.Named("Malpaís").Kind, Is.EqualTo("Malpais"));
		}

		[Test]
		public void Bone_IsAppendedAtSix_AndNoSiteKindGathersIt()
		{
			Assert.That((int)GatheringFamily.Bone, Is.EqualTo(6));
			Assert.That((int)GatheringFamily.Marine, Is.EqualTo(5));
			foreach (POIType kind in System.Enum.GetValues(typeof(POIType)))
			{
				Assert.That(GatheringResourceCatalogue.FamiliesFor(kind), Has.No.Member(GatheringFamily.Bone), $"{kind}");
			}
		}
	}
}
