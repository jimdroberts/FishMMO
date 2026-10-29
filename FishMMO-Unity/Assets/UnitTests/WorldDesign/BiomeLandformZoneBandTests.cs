using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2g-b of the biome suite (2026-10-25, decision 55): the landform ZONE band on ground layers and spawn rules. Off (the
	/// default everywhere) nothing changes: no mark, no fingerprint, no weight; on, a layer or rule is weighted by one part of its
	/// biome's stored landforms (or, turned round, by its absence), and the painter and the scatter both read it.
	/// </summary>
	public class BiomeLandformZoneBandTests
	{
		private const string Terrain = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		private static BiomeLandformSite Crater(int biome, Vector2 at, float radius)
		{
			var site = new BiomeLandformSite
			{
				Form = BiomeLandformPresets.Named("ImpactCrater"),
				BiomeID = biome,
				Centre = at,
				Radius = radius,
				HalfLength = radius,
				HalfWidth = 1f,
				Height = 0.18f * radius * 2f,
				Seed = 5u,
				Level = float.NaN,
			};
			site.Bounds = BiomeLandforms.BoundsOf(site);
			return site;
		}

		[Test]
		public void Off_IsWeightOne_AndMarksNothing()
		{
			BiomeLandformBand off = default;
			Assert.That(off.On, Is.False);
			Assert.That(off.Signature(), Is.Empty, "a mark written before the band existed still matches");
			Assert.That(off.WeightOf(0f), Is.EqualTo(1f));
			Assert.That(BiomeLandformZones.WeightOf(null, off, 1, 0f, 0f, 0f, 0f), Is.EqualTo(1f));
			var zones = new BiomeLandformZones(new List<BiomeLandformSite> { Crater(1, Vector2.zero, 100f) }, new BiomeLandforms.Context());
			Assert.That(zones.Weight(off, 1, 0f, 0f, 0f, 0f), Is.EqualTo(1f));
			// A default rule's and layer's band is off; the spec writes none unless a round names one.
			Assert.That(new PrefabSpawnRule().landformBand.On, Is.False);
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)))
			{
				foreach (BiomeArtSpec.Layer layer in entry.Details)
				{
					Assert.That(layer.ZoneBand.On, Is.False, $"{entry.Biome} names a zone band before its round");
				}
			}
		}

		[Test]
		public void On_FollowsThePartsMask_OnlyForItsBiomeAndName()
		{
			var zones = new BiomeLandformZones(new List<BiomeLandformSite> { Crater(1, Vector2.zero, 100f) }, new BiomeLandforms.Context());
			BiomeLandformBand floor = BiomeLandformBand.Of(BiomeLandformPart.Floor);
			Assert.That(zones.Weight(floor, 1, 0f, 0f, 0f, 0f), Is.EqualTo(1f).Within(1e-4f), "on the crater's floor");
			Assert.That(zones.Weight(floor, 1, 400f, 0f, 0f, 0f), Is.EqualTo(0f), "far from it");
			Assert.That(zones.Weight(floor, 2, 0f, 0f, 0f, 0f), Is.EqualTo(0f), "another biome's crater is not this one's");
			Assert.That(zones.Weight(BiomeLandformBand.Of(BiomeLandformPart.Floor, "Pingo"), 1, 0f, 0f, 0f, 0f), Is.EqualTo(0f), "another name");
			Assert.That(zones.Weight(BiomeLandformBand.Of(BiomeLandformPart.Floor, "impactcrater"), 1, 0f, 0f, 0f, 0f), Is.EqualTo(1f).Within(1e-4f));
			BiomeLandformBand outside = BiomeLandformBand.Of(BiomeLandformPart.Floor, null, true);
			Assert.That(zones.Weight(outside, 1, 400f, 0f, 0f, 0f), Is.EqualTo(1f), "turned round: off the floor");
			// No landform stored: on is nowhere, off-the-part is everywhere.
			Assert.That(BiomeLandformZones.WeightOf(null, floor, 1, 0f, 0f, 0f, 0f), Is.EqualTo(0f));
			Assert.That(BiomeLandformZones.WeightOf(null, outside, 1, 0f, 0f, 0f, 0f), Is.EqualTo(1f));
		}

		[Test]
		public void Authoring_MarksAndFingerprints_OnlyWhenOn()
		{
			var plain = new BiomeArtSpec.Layer();
			var zoned = new BiomeArtSpec.Layer().Zone(BiomeLandformPart.Rays, "RayCrater");
			Assert.That(BiomeArtAuthoring.ClimateMark(plain), Is.Empty);
			Assert.That(BiomeArtAuthoring.ClimateMark(zoned), Is.EqualTo("t-|h-|zzone Rays RayCrater"));
		}

		[Test]
		public void Authoring_ALayerMarksAsItsSpec()
		{
			var zoned = new BiomeArtSpec.Layer().Zone(BiomeLandformPart.Rays, "RayCrater");
			var layer = new TerrainTextureLayer();
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.Empty);
			layer.landformBand = zoned.ZoneBand;
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.EqualTo(BiomeArtAuthoring.ClimateMark(zoned)), "the layer marks as its spec");
		}

		[Test]
		public void Rules_FingerprintTheBand_OnlyWhenOn()
		{
			var rule = new PrefabSpawnRule { ruleName = "Soft rims" };
			string before = BiomeArtAuthoring.Fingerprint(rule);
			StringAssert.DoesNotContain("|Z", before);
			rule.landformBand = BiomeLandformBand.Of(BiomeLandformPart.Rim, "DegradedCrater");
			Assert.That(BiomeArtAuthoring.Fingerprint(rule), Is.Not.EqualTo(before));
		}

		[Test]
		public void Wiring_PainterAndScatterReadTheScenesZones()
		{
			string scatter = Read(Terrain + "/TerrainScatter.cs");
			StringAssert.Contains("if (rule.landformBand.On)", scatter);
			StringAssert.Contains("scatterRule.Zones = options?.LandformZones;", scatter);
			Assert.That(Regex(scatter, "if \\(scatterRule\\.Zoned\\)"), Is.EqualTo(2), "the instance path and the carpet path");
			string generator = Read(World + "/SceneGenerator.cs");
			StringAssert.Contains("LandformZones = new BiomeLandformZones(SceneLandforms.Sites(scene)", generator);
			StringAssert.Contains("LandformZones = options.LandformZones, HeatAt = options.HeatAt }", generator);
			string authoring = Read(World + "/ProceduralArt/BiomeArtAuthoring.cs");
			StringAssert.Contains("layer.landformBand = spec != null ? spec.ZoneBand : default;", authoring);
			StringAssert.Contains("landformBand = s.Zone,", authoring);
			StringAssert.Contains("to.landformBand = from.landformBand;", authoring);
		}

		private static int Regex(string text, string pattern) => System.Text.RegularExpressions.Regex.Matches(text, pattern).Count;
	}
}
