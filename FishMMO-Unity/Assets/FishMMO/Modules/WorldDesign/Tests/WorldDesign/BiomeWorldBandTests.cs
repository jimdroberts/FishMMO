using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Decision 68 (2026-10-25, phase I2g-c): the WORLD band on ground layers and landforms (a ground or a landform only on worlds where
	/// water stands liquid, or only where it does not). Off (the default) nothing changes: every world admits, no mark.
	/// </summary>
	public class BiomeWorldBandTests
	{
		private static BiomeWorldConditions Airless()
		{
			BiomeWorldConditions w = BiomeWorldConditions.Earthlike;
			w.Atmosphere = AtmosphereKind.None;
			w.Water = 0f;
			return w;
		}

		private static BiomeWorldConditions Dry()
		{
			BiomeWorldConditions w = BiomeWorldConditions.Earthlike;
			w.Water = 0f;
			return w;
		}

		[Test]
		public void TheWorlds_AreWhatTheyClaim()
		{
			Assert.That(BiomeWorldConditions.Earthlike.HasLiquidWater, Is.True);
			Assert.That(Dry().HasLiquidWater, Is.False);
			Assert.That(Airless().HasLiquidWater, Is.False);
		}

		[Test]
		public void DefaultBand_AdmitsEveryWorld()
		{
			var layer = new TerrainTextureLayer();
			Assert.That(layer.worldBand.On, Is.False);
			foreach (BiomeWorldConditions world in new[] { BiomeWorldConditions.Earthlike, Airless(), Dry() })
			{
				Assert.That(layer.AdmitsWorld(world), Is.True);
				Assert.That(default(BiomeLandform).World.Admits(world), Is.True);
			}
			Assert.That(layer.AdmitsWorld(null), Is.True, "an unknown world passes");
		}

		[Test]
		public void RequiresLiquidWater_RefusesAirlessAndDry_AdmitsEarth()
		{
			var layer = new TerrainTextureLayer { worldBand = BiomeWorldBand.Of(BiomeWorldRequirement.LiquidWater) };
			Assert.That(layer.AdmitsWorld(BiomeWorldConditions.Earthlike), Is.True);
			Assert.That(layer.AdmitsWorld(Airless()), Is.False);
			Assert.That(layer.AdmitsWorld(Dry()), Is.False);
			Assert.That(layer.AdmitsWorld(null), Is.True, "an unknown world passes a set band");
		}

		[Test]
		public void ForbidsLiquidWater_IsTheReverse()
		{
			BiomeWorldBand band = BiomeWorldBand.Of(BiomeWorldRequirement.None, BiomeWorldRequirement.LiquidWater);
			Assert.That(band.Admits(BiomeWorldConditions.Earthlike), Is.False);
			Assert.That(band.Admits(Airless()), Is.True);
			Assert.That(band.Admits(Dry()), Is.True);
			Assert.That(band.Signature(), Is.EqualTo("0/" + (int)BiomeWorldRequirement.LiquidWater));
		}

		[Test]
		public void Marks_UnchangedWithoutABand_AndALayerMarksAsItsSpec()
		{
			Assert.That(BiomeArtAuthoring.ClimateMark(new TerrainTextureLayer()), Is.Empty);
			Assert.That(BiomeArtAuthoring.ClimateMark(new BiomeArtSpec.Layer()), Is.Empty);
			BiomeArtSpec.Layer wet = new BiomeArtSpec.Layer().World(BiomeWorldRequirement.LiquidWater);
			Assert.That(BiomeArtAuthoring.ClimateMark(wet), Is.EqualTo("t-|h-|w" + (int)BiomeWorldRequirement.LiquidWater + "/0"));
			var layer = new TerrainTextureLayer();
			BiomeArtAuthoring.WriteClimateBand(layer, wet);
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.EqualTo(BiomeArtAuthoring.ClimateMark(wet)));
			BiomeArtAuthoring.WriteClimateBand(layer, null);
			Assert.That(layer.worldBand.On, Is.False, "no spec writes no band");
		}

		[Test]
		public void Landform_ToStringUnchangedByDefault_MarkedOnlyWhenSet()
		{
			BiomeLandform crater = BiomeLandformPresets.Named("ImpactCrater");
			string before = crater.ToString();
			Assert.That(before, Does.Not.Match(@"\|w\d"));
			Assert.That(crater.OnWorlds(BiomeWorldRequirement.None).ToString(), Is.EqualTo(before));
			Assert.That(crater.OnWorlds(BiomeWorldRequirement.None, BiomeWorldRequirement.LiquidWater).ToString(),
				Is.EqualTo(before + "|w0/" + (int)BiomeWorldRequirement.LiquidWater));
			foreach (BiomeLandformPresets.Preset preset in BiomeLandformPresets.All)
			{
				Assert.That(preset.Form.World.On, Is.False, $"{preset.Name}: no preset is world-banded until a round names one");
			}
		}

		[Test]
		public void StandIn_AWorldBandedDetailTakesARefusedMain()
		{
			var details = new List<TerrainTextureLayer>
			{
				new TerrainTextureLayer(),
				new TerrainTextureLayer { worldBand = BiomeWorldBand.Of(BiomeWorldRequirement.None, BiomeWorldRequirement.LiquidWater) },
			};
			Assert.That(SceneTerrainPalette.StandIn(details, BiomeAtmosphereRequirement.Airless, Airless()), Is.EqualTo(1));
			Assert.That(SceneTerrainPalette.StandIn(details, BiomeAtmosphereRequirement.Standard, BiomeWorldConditions.Earthlike), Is.EqualTo(-1));
			Assert.That(SceneTerrainPalette.StandIn(new List<TerrainTextureLayer> { new TerrainTextureLayer() }, BiomeAtmosphereRequirement.None, null), Is.EqualTo(-1),
				"no banded detail: the main stays");
		}
	}
}
