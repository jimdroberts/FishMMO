using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2o (decisions 39, 41, 44; 2026-10-18): the world gate on spawn rules, air bands on ground layers, the "needs breathable
	/// air" flag on point-of-interest kinds, blade sway by the world's air and rigid blades, Glow-only lights for biomes found only
	/// under thin air or none, and the code-table clean-ups. Pure: no Unity object is made (templates and terrain layers are
	/// native), so what needs one is read from the assets' YAML or the sources.
	/// </summary>
	public class BiomeWorldGateTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";

		private static string Code(string path) => File.ReadAllText(path);

		private static BiomeWorldConditions World(AtmosphereKind air, float meanTemperature = 0.5f, float giantField = 0f)
		{
			BiomeWorldConditions world = BiomeWorldConditions.Earthlike;
			world.Atmosphere = air;
			world.MeanTemperature = meanTemperature;
			world.GiantMagneticField = giantField;
			return world;
		}

		private static IEnumerable<BiomeArtSpec.Scatter> RulesOf(BiomeArtSpec.Entry entry)
		{
			var layers = new List<BiomeArtSpec.Layer> { entry.Main, entry.Bed, entry.RiverBed };
			layers.AddRange(entry.Details);
			foreach (BiomeArtSpec.Layer layer in layers)
			{
				if (layer == null)
				{
					continue;
				}
				foreach (BiomeArtSpec.Scatter s in layer.Scatter)
				{
					yield return s;
					foreach (BiomeArtSpec.Scatter earlier in s.Earlier)
					{
						yield return earlier;
					}
				}
			}
		}

		/// <summary>The biome template assets' YAML by asset name (83; Default Climate is not a biome).</summary>
		private static Dictionary<string, string> TemplateYaml()
		{
			var all = new Dictionary<string, string>();
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (text.Contains("m_Script:") && text.Contains("mainTextureLayer:"))
				{
					all[Path.GetFileNameWithoutExtension(path)] = text;
				}
			}
			return all;
		}

		private static BiomeAtmosphereRequirement AtmosphereOf(string yaml)
		{
			Match m = Regex.Match(yaml, @"\n  Atmosphere: (\d+)");
			return m.Success ? (BiomeAtmosphereRequirement)int.Parse(m.Groups[1].Value) : BiomeAtmosphereRequirement.Any;
		}

		// ── The world gate (decision 41) ─────────────────────────────

		[Test]
		public void DefaultGate_IsOpen_AndAdmitsEveryWorld()
		{
			BiomeWorldGate gate = default;
			Assert.That(gate.IsOpen, Is.True);
			Assert.That(gate.Signature(), Is.Empty);
			Assert.That(BiomeWorldGate.Open.IsOpen, Is.True);
			Assert.That(new PrefabSpawnRule().worldGate.IsOpen, Is.True, "a rule saved before the gate loads open");
			foreach (AtmosphereKind air in (AtmosphereKind[])Enum.GetValues(typeof(AtmosphereKind)))
			{
				foreach (float t in new[] { -8f, -5f, -1f, 0f, 0.5f, 5f, float.NaN })
				{
					Assert.That(gate.Admits(World(air, t, 2f)), Is.True, $"{air} {t}");
					Assert.That(TerrainScatter.GrowsOn(new PrefabSpawnRule(), World(air, t)), Is.True);
				}
			}
			Assert.That(TerrainScatter.GrowsOn(null, BiomeWorldConditions.Earthlike), Is.False);
			// "Open" by construction too: no air, no flags, temperatures at or past the open ends.
			Assert.That(BiomeWorldGate.Of(BiomeAtmosphereRequirement.None).IsOpen, Is.True);
			Assert.That(BiomeWorldGate.Of(BiomeAtmosphereRequirement.None, -20f, 20f).IsOpen, Is.True);
			Assert.That(new BiomeWorldGate { useTemperature = true, temperatureRange = new Vector2(-10f, 10f) }.IsOpen, Is.True, "both ends open");
		}

		[Test]
		public void Gate_ReadsTheWorldsAir_MeanTemperature_AndFlags()
		{
			BiomeWorldGate thin = BiomeWorldGate.Air(BiomeAtmosphereRequirement.Thin);
			Assert.That(thin.Admits(World(AtmosphereKind.Thin)), Is.True);
			Assert.That(thin.Admits(World(AtmosphereKind.None)), Is.False);
			Assert.That(thin.Admits(World(AtmosphereKind.Standard)), Is.False);
			Assert.That(thin.Admits(World(AtmosphereKind.Thick)), Is.False);
			BiomeWorldGate breathing = BiomeWorldGate.Air(BiomeAtmosphereRequirement.Breathing);
			Assert.That(breathing.Admits(World(AtmosphereKind.None)), Is.False);
			Assert.That(breathing.Admits(World(AtmosphereKind.Thick)), Is.True);

			// The world's MEAN temperature, closed ends inclusive, an open end past ±10.
			BiomeWorldGate cold = BiomeWorldGate.Of(BiomeAtmosphereRequirement.None, maxTemperature: -2f);
			Assert.That(cold.IsOpen, Is.False);
			Assert.That(cold.Admits(World(AtmosphereKind.None, -5f)), Is.True);
			Assert.That(cold.Admits(World(AtmosphereKind.None, -2f)), Is.True);
			Assert.That(cold.Admits(World(AtmosphereKind.None, -1.9f)), Is.False);
			Assert.That(cold.Admits(World(AtmosphereKind.None, float.NaN)), Is.False, "an unknown temperature fails a closed range");
			BiomeWorldGate band = BiomeWorldGate.Of(BiomeAtmosphereRequirement.None, -1f, 1f);
			Assert.That(band.Admits(World(AtmosphereKind.Standard, 0.5f)), Is.True);
			Assert.That(band.Admits(World(AtmosphereKind.Standard, 1.5f)), Is.False);
			Assert.That(band.Admits(World(AtmosphereKind.Standard, -1.5f)), Is.False);

			// The world's flags: exactly BiomeWorldConditions.Meets, so a gate means what a template's Requires means.
			BiomeWorldGate giant = BiomeWorldGate.Of(BiomeAtmosphereRequirement.None, requires: BiomeWorldRequirement.GiantMagnetosphere);
			Assert.That(giant.Admits(World(AtmosphereKind.None, -5f, 2f)), Is.True);
			Assert.That(giant.Admits(World(AtmosphereKind.None, -5f, 0.1f)), Is.False);
			foreach (BiomeWorldRequirement flag in (BiomeWorldRequirement[])Enum.GetValues(typeof(BiomeWorldRequirement)))
			{
				var g = new BiomeWorldGate { requires = flag };
				foreach (BiomeWorldConditions w in new[] { BiomeWorldConditions.Earthlike, World(AtmosphereKind.None, -5f, 2f), World(AtmosphereKind.Thin, 3f) })
				{
					Assert.That(g.Admits(w), Is.EqualTo(w.Meets(flag)), flag.ToString());
				}
			}

			// All parts together: every one must hold.
			BiomeWorldGate rimecandle = BiomeWorldGate.Of(BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin, float.NegativeInfinity, -1f,
				BiomeWorldRequirement.GiantMagnetosphere);
			Assert.That(rimecandle.Admits(World(AtmosphereKind.None, -5f, 2f)), Is.True);
			Assert.That(rimecandle.Admits(World(AtmosphereKind.Standard, -5f, 2f)), Is.False);
			Assert.That(rimecandle.Admits(World(AtmosphereKind.None, 0f, 2f)), Is.False);
			Assert.That(rimecandle.Admits(World(AtmosphereKind.None, -5f, 0f)), Is.False);
			Assert.That(TerrainScatter.GrowsOn(new PrefabSpawnRule { worldGate = rimecandle }, World(AtmosphereKind.None, -5f, 0f)), Is.False);

			// Signatures tell gates apart and are stable text.
			var signatures = new[] { thin, breathing, cold, band, giant, rimecandle }.Select(g => g.Signature()).ToList();
			Assert.That(signatures.Distinct().Count(), Is.EqualTo(signatures.Count));
			Assert.That(cold.Signature(), Is.EqualTo("a0|r0|t-,-20000"));
		}

		[Test]
		public void Scatter_SkipsAGatedRuleWhole_AndWorksTheWorldOutFromTheScenesBody()
		{
			string scatter = Code(WorldFolder + "/Terrain/TerrainScatter.cs");
			Assert.That(scatter, Does.Contain("BiomeWorldConditions world = climate != null ? climate.Conditions : BiomeWorldConditions.Earthlike;"));
			Assert.That(scatter, Does.Contain("if (!GrowsOn(rule, world))"));
			Assert.That(scatter, Does.Contain("return rule != null && (rule.worldGate.IsOpen || rule.worldGate.Admits(world));"));
			// Skipped before the prefab resolve and the attribution: like a rule with no prefabs, nothing else moves.
			Assert.That(scatter.IndexOf("if (!GrowsOn(rule, world))", StringComparison.Ordinal),
				Is.LessThan(scatter.IndexOf("scatterRule.Prefabs = options.Prefabs != null", StringComparison.Ordinal)));
		}

		[Test]
		public void Authoring_WritesTheGate_FingerprintsItOnlyWhenSet_AndTheSpecHookSetsIt()
		{
			var s = new BiomeArtSpec.Scatter { Name = "Starwort", Channel = PrefabSpawnChannel.DetailLayer, Prefabs = new[] { "Detail_Starwort" }, Density = 1.5f };
			s.Earlier.Add(s.Clone());
			PrefabSpawnRule open = BiomeArtAuthoring.ToRule("Crater", s, new GameObject[0]);
			Assert.That(open.worldGate.IsOpen, Is.True);
			MethodInfo onWorlds = typeof(BiomeArtSpec).GetMethod("OnWorlds", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.That(onWorlds, Is.Not.Null, "BiomeArtSpec.OnWorlds");
			onWorlds.Invoke(null, new object[] { s, BiomeAtmosphereRequirement.Thin, float.NegativeInfinity, float.PositiveInfinity, BiomeWorldRequirement.None });
			Assert.That(s.World.air, Is.EqualTo(BiomeAtmosphereRequirement.Thin));
			Assert.That(s.Earlier[0].World.air, Is.EqualTo(BiomeAtmosphereRequirement.Thin), "earlier versions too, as Band does");
			PrefabSpawnRule gated = BiomeArtAuthoring.ToRule("Crater", s, new GameObject[0]);
			Assert.That(gated.worldGate.air, Is.EqualTo(BiomeAtmosphereRequirement.Thin));
			Assert.That(gated.SpecFingerprint, Is.Not.EqualTo(open.SpecFingerprint));
			Assert.That(BiomeArtAuthoring.Fingerprint(gated), Is.EqualTo(gated.SpecFingerprint));
			// Open: the fingerprint is what it was before the gate existed (the gate is hashed only when set).
			PrefabSpawnRule reopened = BiomeArtAuthoring.ToRule("Crater", s, new GameObject[0]);
			reopened.worldGate = default;
			Assert.That(BiomeArtAuthoring.Fingerprint(reopened), Is.EqualTo(open.SpecFingerprint));
			string authoring = Code(WorldFolder + "/ProceduralArt/BiomeArtAuthoring.cs");
			Assert.That(authoring, Does.Contain("to.worldGate = from.worldGate;"));
			Assert.That(authoring, Does.Contain("|| !rule.worldGate.IsOpen)"), "a gated rule is never taken for a pre-fingerprint one");
		}

		/// <summary>Defaults identical for all 83 biomes: no spec rule has a gate, no spec layer or cliff an air band, no asset either.</summary>
		[Test]
		public void Defaults_NoBiomeGatesARule_BandsAGroundByAir_OrFlagsAKindYet()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			int rules = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach (BiomeArtSpec.Scatter s in RulesOf(entry))
				{
					Assert.That(s.World.IsOpen, Is.True, $"{entry.Biome} {s.Name}");
					rules++;
				}
				Assert.That(entry.Main == null || entry.Main.AirBand == BiomeAtmosphereRequirement.None, Is.True, entry.Biome);
				Assert.That(entry.Details.All(d => d == null || d.AirBand == BiomeAtmosphereRequirement.None), Is.True, entry.Biome);
				Assert.That(entry.Cliffs.Length, Is.LessThanOrEqualTo(1), entry.Biome);
				Assert.That(entry.Cliffs.All(c => c.AirBand == BiomeAtmosphereRequirement.None), Is.True, entry.Biome);
				// Crater's file clears its old volcanic ash (decision 39); no other file names an emission.
				Assert.That(entry.Emission.HasValue, Is.EqualTo(entry.Biome == "Crater"), entry.Biome);
			}
			Assert.That(rules, Is.GreaterThan(83 * 4));
			Assert.That(BiomeArtSpec.For("Crater").Emission, Is.EqualTo(((string)null, 0f)));

			Dictionary<string, string> yaml = TemplateYaml();
			Assert.That(yaml.Count, Is.EqualTo(83));
			foreach (KeyValuePair<string, string> pair in yaml)
			{
				// Nothing serialised yet: Unity writes the new fields on the next save, at their defaults.
				Assert.That(Regex.IsMatch(pair.Value, @"worldGate:\s*\n\s*air: [1-9]"), Is.False, pair.Key);
				Assert.That(Regex.IsMatch(pair.Value, @"airBand: [1-9]"), Is.False, pair.Key);
				Assert.That(Regex.IsMatch(pair.Value, @"NeedsBreathableAir: 1"), Is.False, pair.Key);
			}
		}

		// ── Air bands on ground layers (decision 44) ─────────────────

		[Test]
		public void AirBands_AdmitTheirAirs_NoBandAdmitsAll_AndAMainHandsOverToTheFirstBandedDetail()
		{
			var layer = new TerrainTextureLayer();
			Assert.That(layer.airBand, Is.EqualTo(BiomeAtmosphereRequirement.None));
			foreach (BiomeAtmosphereRequirement air in new[] { BiomeAtmosphereRequirement.None, BiomeAtmosphereRequirement.Airless, BiomeAtmosphereRequirement.Thin,
				BiomeAtmosphereRequirement.Standard, BiomeAtmosphereRequirement.Thick })
			{
				Assert.That(layer.AdmitsAir(air), Is.True, $"unbanded under {air}");
			}
			layer.airBand = BiomeAtmosphereRequirement.Airless;
			Assert.That(layer.AdmitsAir(BiomeAtmosphereRequirement.Airless), Is.True);
			Assert.That(layer.AdmitsAir(BiomeAtmosphereRequirement.Thin), Is.False);
			Assert.That(layer.AdmitsAir(BiomeAtmosphereRequirement.None), Is.True, "an unknown world passes every band");

			var plain = new TerrainTextureLayer();
			var airless = new TerrainTextureLayer { airBand = BiomeAtmosphereRequirement.Airless };
			var thin = new TerrainTextureLayer { airBand = BiomeAtmosphereRequirement.Thin };
			var details = new List<TerrainTextureLayer> { plain, airless, thin };
			Assert.That(SceneTerrainPalette.AirStandIn(details, BiomeAtmosphereRequirement.Thin), Is.EqualTo(2), "the first BANDED detail naming the air");
			Assert.That(SceneTerrainPalette.AirStandIn(details, BiomeAtmosphereRequirement.Airless), Is.EqualTo(1));
			Assert.That(SceneTerrainPalette.AirStandIn(details, BiomeAtmosphereRequirement.Standard), Is.EqualTo(-1), "none names it: the main stays");
			Assert.That(SceneTerrainPalette.AirStandIn(details, BiomeAtmosphereRequirement.None), Is.EqualTo(-1));
			Assert.That(SceneTerrainPalette.AirStandIn((List<TerrainTextureLayer>)null, BiomeAtmosphereRequirement.Thin), Is.EqualTo(-1));

			// The palette's wiring: main stand-in, banded details and cliffs left out, both scene palettes told the world's air.
			string palette = Code(WorldFolder + "/Terrain/SceneTerrainPalette.cs");
			Assert.That(palette, Does.Contain("int standIn = mainSource != null && !mainSource.AdmitsAir(worldAir) ? AirStandIn(biome, worldAir) : -1;"));
			Assert.That(palette, Does.Contain("if (d == standIn || detail != null && !detail.AdmitsAir(worldAir))"));
			Assert.That(palette, Does.Contain("if (cliff != null && !cliff.AdmitsAir(worldAir))"));
			Assert.That(palette, Does.Contain("Source = mainSource, Role = PaletteRole.Main, Slot = mainSlot"));
			Assert.That(palette, Does.Contain("!PaintsGround(biome, wayGround, standIn, worldAir)"), "a left-out ground's own ways spend no channel");
			string generator = Code(WorldFolder + "/SceneGenerator.cs");
			Assert.That(Regex.Matches(generator, Regex.Escape("WorldAir(system, request.Body)")).Count, Is.EqualTo(2), "the scene and its horizon");
		}

		[Test]
		public void AirBandMarks_RideTheClimateMark_UnchangedWithoutOne()
		{
			var layer = new TerrainTextureLayer { useClimateBand = true, temperatureRange = new Vector2(-0.5f, 1f) };
			string climateOnly = BiomeArtAuthoring.ClimateMark(layer);
			Assert.That(climateOnly, Is.EqualTo("t-5000,10000,1000|h-"), "the I2j-a mark, as it was");
			layer.airBand = BiomeAtmosphereRequirement.Thin;
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.EqualTo(climateOnly + "|a2"));
			var airOnly = new TerrainTextureLayer { airBand = BiomeAtmosphereRequirement.Airless };
			Assert.That(BiomeArtAuthoring.ClimateMark(airOnly), Is.EqualTo("t-|h-|a1"));
			Assert.That(BiomeArtAuthoring.ClimateMark(new TerrainTextureLayer()), Is.Empty);

			BiomeArtSpec.Layer spec = new BiomeArtSpec.Layer { Family = "Dust" }.Air(BiomeAtmosphereRequirement.Airless);
			Assert.That(BiomeArtAuthoring.ClimateMark(spec), Is.EqualTo("t-|h-|a1"));
			var written = new TerrainTextureLayer();
			BiomeArtAuthoring.WriteClimateBand(written, spec);
			Assert.That(written.airBand, Is.EqualTo(BiomeAtmosphereRequirement.Airless));
			Assert.That(written.useClimateBand, Is.False);
			Assert.That(BiomeArtAuthoring.ClimateMark(written), Is.EqualTo(BiomeArtAuthoring.ClimateMark(spec)));
			BiomeArtAuthoring.WriteClimateBand(written, null);
			Assert.That(written.airBand, Is.EqualTo(BiomeAtmosphereRequirement.None));

			// A second cliff of another air: the entry's hook, authored into cliff/1 by the climate pass.
			var entry = new BiomeArtSpec.Entry { Biome = "Dust Sea", Cliffs = new[] { new BiomeArtSpec.Cliff { Family = "A" }.Air(BiomeAtmosphereRequirement.Airless) } };
			entry.AddCliff(new BiomeArtSpec.Cliff { Family = "B", MinAngle = 34f }.Air(BiomeAtmosphereRequirement.Thin));
			Assert.That(entry.Cliffs.Select(c => c.Family), Is.EqualTo(new[] { "A", "B" }));
			Assert.That(entry.Cliffs[1].AirBand, Is.EqualTo(BiomeAtmosphereRequirement.Thin));
			Assert.That(Code(WorldFolder + "/ProceduralArt/BiomeArtAuthoring.cs"), Does.Contain("Spec(BiomeLocalArt.SlotCliff + i,"));
		}

		// ── Points of interest that need breathable air (decision 39) ─

		[Test]
		public void BreathableAir_IsEarthlikeOrThick_AndTheGateZeroesOnlyFlaggedKindsThere()
		{
			Assert.That(BiomeAir.IsBreathable(AtmosphereKind.None), Is.False);
			Assert.That(BiomeAir.IsBreathable(AtmosphereKind.Thin), Is.False);
			Assert.That(BiomeAir.IsBreathable(AtmosphereKind.Standard), Is.True);
			Assert.That(BiomeAir.IsBreathable(AtmosphereKind.Thick), Is.True);
			Assert.That(BiomeAir.MaskOf(AtmosphereKind.Standard), Is.EqualTo(BiomeAtmosphereRequirement.Standard));
			Assert.That(BiomeAir.MaskOf(AtmosphereKind.None), Is.EqualTo(BiomeAtmosphereRequirement.Airless));

			var village = new BiomeSiteKind(POIType.Village, 1.2f, true);
			var peak = new BiomeSiteKind(POIType.Peak, 1f);
			Assert.That(BiomeSiteAir.WeightOn(village, true), Is.EqualTo(1.2f));
			Assert.That(BiomeSiteAir.WeightOn(village, false), Is.EqualTo(0f));
			Assert.That(BiomeSiteAir.WeightOn(peak, false), Is.EqualTo(1f));
			Assert.That(BiomeSiteAir.WeightOn(new BiomeSiteKind(POIType.Peak, -2f), true), Is.EqualTo(0f));

			// Marks: an entry without the flag prints as before, so every mark written before still reads the same.
			Assert.That(peak.ToString(), Is.EqualTo("Peak×1"));
			Assert.That(village.ToString(), Is.EqualTo("Village×1.2×air"));
			Assert.That(new BiomeSiteKind(POIType.Village, 1.2f).ToString(), Is.EqualTo("Village×1.2"));

			string stage = Code(WorldFolder + "/PointsOfInterest/PointOfInterestStage.cs");
			Assert.That(stage, Does.Contain("bool breathable = BiomeAir.IsBreathable(BiomeWorldConditions.For(system, Request.Body).Atmosphere);"));
			Assert.That(Code(WorldFolder + "/Terrain/BiomeTemplateArt.cs"), Does.Contain("kinds[entry.Kind] = BiomeSiteAir.WeightOn(entry, breathable);"));
		}

		/// <summary>
		/// The default per kind: lived-in, worked and kept kinds need breathable air; no kind the catalogue DETECTS (falls, peaks,
		/// craters, vents) does, nor any relic. Every spec writes it so, and a file can say otherwise per kind.
		/// </summary>
		[Test]
		public void SiteAirDefaults_AreTheLivedInKinds_NeverADetectedOne_AndEverySpecWritesThem()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (POIType kind in (POIType[])Enum.GetValues(typeof(POIType)))
			{
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				if (rule != null && rule.Placement == PointOfInterestPlacement.Detected)
				{
					Assert.That(BiomeSiteAir.NeedsAirByDefault(kind), Is.False, $"{kind} is detected (natural)");
				}
			}
			foreach (POIType kind in new[] { POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Port, POIType.Camp, POIType.Shrine,
				POIType.Temple, POIType.Mine, POIType.Quarry, POIType.HerbGrove, POIType.Hermitage, POIType.Windmill })
			{
				Assert.That(BiomeSiteAir.NeedsAirByDefault(kind), Is.True, kind.ToString());
			}
			foreach (POIType kind in new[] { POIType.Ruins, POIType.RuinedTower, POIType.Obelisk, POIType.Monument, POIType.Statue, POIType.Barrow,
				POIType.Crypt, POIType.Battlefield, POIType.Wreck, POIType.Portal, POIType.LeyNexus, POIType.FallenStar, POIType.CrystalFormation,
				POIType.OreVein, POIType.DungeonEntrance, POIType.MonsterDen, POIType.AncientRoad, POIType.Crater, POIType.Cryovolcano })
			{
				Assert.That(BiomeSiteAir.NeedsAirByDefault(kind), Is.False, kind.ToString());
			}
			int flagged = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach (BiomeSiteKind k in entry.SiteKinds(rules))
				{
					Assert.That(k.NeedsBreathableAir, Is.EqualTo(BiomeSiteAir.NeedsAirByDefault(k.Kind)), $"{entry.Biome} {k.Kind}");
					flagged += k.NeedsBreathableAir ? 1 : 0;
				}
			}
			Assert.That(flagged, Is.GreaterThan(83));

			var dust = new BiomeArtSpec.Entry { Biome = "Dust Sea" };
			dust.Hosts(POIType.Hermitage, 0.3f).Air(POIType.Hermitage, false).Hosts(POIType.Ruins, 0.3f).Air(POIType.Ruins, true);
			List<BiomeSiteKind> list = dust.SiteKinds(rules);
			Assert.That(list.Single(k => k.Kind == POIType.Hermitage).NeedsBreathableAir, Is.False, "the file's word over the default");
			Assert.That(list.Single(k => k.Kind == POIType.Ruins).NeedsBreathableAir, Is.True);
		}

		/// <summary>
		/// Today's cut scenes (2026-10-18): the two on Moron (Earth-like air) lose no site; the three on thin or airless worlds lose
		/// only their lived-in kinds once re-authored and regenerated (Belron 1's bandit camp and camp; Galris 1's camp and shrine;
		/// Ithaewyn 1's camp and quarry), as reported.
		/// </summary>
		[Test]
		public void TodaysScenes_OnlyThinAndAirlessWorldsLoseSites_AndOnlyTheirLivedInOnes()
		{
			string[] kinds = Enum.GetNames(typeof(POIType));
			var lost = new Dictionary<string, List<string>>();
			foreach (string atlas in Directory.GetFiles("Assets/Templates/World/Atlas/Scenes", "*.asset"))
			{
				string text = File.ReadAllText(atlas);
				Match scene = Regex.Match(text, @"\n  SceneName: ([^\n]+)");
				Match body = Regex.Match(text, @"\n  Body: \{fileID: \d+, guid: ([0-9a-f]+)");
				if (!scene.Success || !body.Success || !Regex.IsMatch(text, @"\n  CutRadiusKm: [1-9]"))
				{
					continue;
				}
				string meta = Directory.GetFiles("Assets/Templates/World/Systems", "*.asset.meta", SearchOption.AllDirectories)
					.First(m => File.ReadAllText(m).Contains("guid: " + body.Groups[1].Value));
				var air = (AtmosphereKind)int.Parse(Regex.Match(File.ReadAllText(meta.Substring(0, meta.Length - 5)), @"\n  Atmosphere: (\d+)").Groups[1].Value);
				string name = scene.Groups[1].Value.Trim();
				string pois = Directory.GetFiles("Assets/Scenes/WorldScene", name + " Points of Interest.asset", SearchOption.AllDirectories).FirstOrDefault();
				if (pois == null)
				{
					continue;
				}
				var gone = new List<string>();
				foreach (Match m in Regex.Matches(File.ReadAllText(pois), @"\n    Kind: (\d+)"))
				{
					var kind = (POIType)Enum.Parse(typeof(POIType), kinds[int.Parse(m.Groups[1].Value)]);
					if (!BiomeAir.IsBreathable(air) && BiomeSiteAir.NeedsAirByDefault(kind))
					{
						gone.Add(kind.ToString());
					}
				}
				gone.Sort(StringComparer.Ordinal);
				lost[name] = gone;
			}
			Assert.That(lost.Keys, Is.SupersetOf(new[] { "Moron 1", "Moron 2", "Belron 1", "Galris 1", "Ithaewyn 1" }));
			Assert.That(lost["Moron 1"], Is.Empty);
			Assert.That(lost["Moron 2"], Is.Empty);
			Assert.That(lost["Belron 1"], Is.EqualTo(new[] { "BanditCamp", "Camp" }), string.Join(", ", lost["Belron 1"]));
			Assert.That(lost["Galris 1"], Is.EqualTo(new[] { "Camp", "Shrine" }), string.Join(", ", lost["Galris 1"]));
			Assert.That(lost["Ithaewyn 1"], Is.EqualTo(new[] { "Camp", "Quarry" }), string.Join(", ", lost["Ithaewyn 1"]));
		}

		// ── No flame under thin air or none (decision 41) ────────────

		[Test]
		public void GlowLights_TakeEveryFlamesPlace_OnlyForBiomesFoundUnderThinAirOrNone()
		{
			Assert.That(BiomeAir.OnlyThinAir(BiomeAtmosphereRequirement.Airless), Is.True);
			Assert.That(BiomeAir.OnlyThinAir(BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin), Is.True);
			Assert.That(BiomeAir.OnlyThinAir(BiomeAtmosphereRequirement.Thin | BiomeAtmosphereRequirement.Standard), Is.False);
			Assert.That(BiomeAir.OnlyThinAir(BiomeAtmosphereRequirement.Any), Is.False);
			Assert.That(BiomeAir.OnlyThinAir(BiomeAtmosphereRequirement.None), Is.False);

			List<BiomeWayLight> way = BiomeGlowLights.WayLights();
			List<BiomeWayLight> oldWay = BiomeWayLights.Default();
			Assert.That(way.Select(l => (l.Kind, l.Spacing)), Is.EqualTo(oldWay.Select(l => (l.Kind, l.Spacing))), "same kinds, same spacing");
			Assert.That(way.All(l => BiomeGlowLights.Glows(l.Light)), Is.True);
			List<BiomeCaveLight> cave = BiomeGlowLights.CaveLights();
			List<BiomeCaveLight> oldCave = BiomeCaveLights.Default();
			Assert.That(cave.Select(l => (l.Place, l.Spacing)), Is.EqualTo(oldCave.Select(l => (l.Place, l.Spacing))), "same places, same spacing");
			Assert.That(cave.All(l => BiomeGlowLights.Glows(l.Light)), Is.True);
			Assert.That(cave.Single(l => l.Place == BiomeCaveLightPlace.Route).Light, Is.EqualTo(BiomeWayLightKind.CrystalPost), "as both drafts mark routes");
			foreach (BiomeWayLightKind kind in (BiomeWayLightKind[])Enum.GetValues(typeof(BiomeWayLightKind)))
			{
				Assert.That(BiomeGlowLights.Glows(kind), Is.EqualTo(kind == BiomeWayLightKind.Glowjar || kind == BiomeWayLightKind.CrystalPost || kind == BiomeWayLightKind.RuneStone));
			}
			// Every glow light stands on the ground, which every cave place takes (a drift's at the wall's foot).
			Assert.That(cave.All(l => BiomeWayLights.MountOf(l.Light) == BiomeWayLightMount.Ground), Is.True);

			// Which templates: read off the assets; every other biome's spec gives exactly the old lists.
			Dictionary<string, string> yaml = TemplateYaml();
			var thinOnly = yaml.Where(p => BiomeAir.OnlyThinAir(AtmosphereOf(p.Value))).Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
			Assert.That(thinOnly, Is.EqualTo(new[] { "Cryovolcanic Plain", "Dust Sea", "Ice Geyser Field", "Impact Basin", "Lava Tube", "Nitrogen Ice Field",
				"Radiation Plain", "Regolith Plain", "Rille" }));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.WayLights(false).Select(l => l.ToString()), Is.EqualTo(entry.WayLights().Select(l => l.ToString())), entry.Biome);
				Assert.That(entry.CaveLights(false).Select(l => l.ToString()), Is.EqualTo(BiomeCaveLights.Default().Select(l => l.ToString())), entry.Biome);
				if (thinOnly.Contains(entry.Biome))
				{
					Assert.That(entry.WayLights(true).All(l => BiomeGlowLights.Glows(l.Light)), Is.True, entry.Biome);
					Assert.That(entry.CaveLights(true).All(l => BiomeGlowLights.Glows(l.Light)), Is.True, entry.Biome);
				}
			}
			string authoring = Code(WorldFolder + "/ProceduralArt/BiomeArtAuthoring.cs");
			Assert.That(authoring, Does.Contain("bool glowOnly = BiomeAir.OnlyThinAir(biome.Atmosphere);"));
			Assert.That(authoring, Does.Contain("biome.WayLights, entry.WayLights(glowOnly)"));
			Assert.That(authoring, Does.Contain("biome.CaveLights, entry.CaveLights(glowOnly)"));
		}

		// ── Blades: sway by the world's air, rigid blades (decision 41) ─

		[Test]
		public void BladeSway_FollowsTheWorldsAir_AndRigidBladesNeverSway()
		{
			Assert.That(BiomeAir.Sway(AtmosphereKind.None), Is.EqualTo(0f));
			Assert.That(BiomeAir.Sway(AtmosphereKind.Thin), Is.EqualTo(0.3f));
			Assert.That(BiomeAir.Sway(AtmosphereKind.Standard), Is.EqualTo(1f), "Earth-like air sways exactly as before");
			Assert.That(BiomeAir.Sway(AtmosphereKind.Thick), Is.EqualTo(1f));

			var look = new BiomeGrassType();
			string before = look.Signature();
			Assert.That(look.Rigid, Is.False);
			Assert.That(before, Does.Not.Contain("|R"), "a look that is not rigid signs as before");
			Assert.That(look.Clone().Rigidly().Signature(), Is.EqualTo(before + "|R"));
			Assert.That(look.Clone().Rigidly().Clone().Rigid, Is.True, "a clone keeps it");
			foreach (string prefix in BiomeGrassDefaults.BladePrefixes)
			{
				Assert.That(BiomeGrassDefaults.For(prefix + "Test").Rigid, Is.False, prefix);
			}

			string renderer = Code("Assets/Scripts/Client/World/Terrain/Grass/GrassBladeRenderer.cs");
			Assert.That(renderer, Does.Contain("publishedParams[2] = new Vector4(s.WindStrength * air, s.GustWaveMetres, s.Flutter * air, s.IdleBreeze * air);"));
			Assert.That(renderer, Does.Contain("SetType(t, RowMore, new Vector4(head != GrassBladeHead.None ? Mathf.Clamp01(look.HeadShare) : 0f, sprinkleLevel, look.Rigid ? 1f : 0f, 0f));"));
			Assert.That(renderer, Does.Contain("RowMore = 2"));
			string system = Code("Assets/Scripts/Client/World/Terrain/Grass/GrassBladeSystem.cs");
			Assert.That(system, Does.Contain("renderer.AirSway = AirSway();"));
			Assert.That(system, Does.Contain("BiomeAir.Sway(world.WorldConditions.Atmosphere)"));

			string shader = Code("Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.hlsl");
			Assert.That(shader, Does.Contain("#define GRASS_ROW_MORE 2"));
			Assert.That(shader, Does.Contain("float typeSway = 1.0 - saturate(GrassTypeRow(type, GRASS_ROW_MORE).z);"));
			Assert.That(shader, Does.Contain("* _GrassParams2.x / stiffness) * typeSway;"), "the bend (and with it the sheen)");
			Assert.That(shader, Does.Contain("(0.3 + speed + gust) * calm * typeSway;"), "the flutter");
			// Every pass of the blade shader compiles with DXC (the house rule): none of them includes the FXC-only instancing.
			string pass = Code("Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.shader");
			Assert.That(Regex.Matches(pass, "HLSLPROGRAM").Count, Is.EqualTo(Regex.Matches(pass, @"#pragma use_dxc vulkan").Count));
			Assert.That(pass + shader, Does.Not.Contain("FishIndirectInstancing.hlsl"));
		}

		// ── Code-table clean-ups (decisions 39, 44) ──────────────────

		[Test]
		public void CodeTables_CraterAndDustSea_LeaveTheNameTablesAndTheirOldRows()
		{
			string weather = Code(WorldFolder + "/WeatherContentGenerator.cs");
			Assert.That(weather, Does.Not.Contain("{ \"Crater\","), "the weather content generator's volcanic Crater row is gone");
			Assert.That(weather, Does.Contain("{ \"Volcanic\", Emitting(\"Volcanic Ash\", 0.15f) },"));
			Assert.That(BiomeArtAuthoring.EmissionMark(null, 0f), Is.EqualTo("none"));
			Assert.That(BiomeArtAuthoring.EmissionMark("Volcanic Ash", 0.15f), Is.EqualTo("Volcanic Ash@1500"));

			Assert.That(BiomeNamingGenerator.IsSpecNamed("Crater"), Is.True);
			Assert.That(BiomeNamingGenerator.IsSpecNamed("Dust Sea"), Is.True);
			Assert.That(BiomeNamingGenerator.IsSpecNamed("Volcanic"), Is.False);
			Assert.That(BiomeNamingGenerator.PoiWordsFor("Crater"), Is.Empty, "no volcanic words offered to an impact crater");
			Assert.That(BiomeNamingGenerator.PoiWordsFor("Volcanic"), Is.Not.Empty);
			string naming = Code(WorldFolder + "/BiomeNamingGenerator.cs");
			Assert.That(naming, Does.Contain("biome.IsAuthored(BiomeTemplate.Authored.NamingPart(nameof(SerializableBiomePhonology.POITypeWords)))"),
				"the spec's naming column wins over the table");

			Assert.That(PointOfInterestTemplateSpecs.Arid, Does.Not.Contain("Dust Sea"));
			Assert.That(PointOfInterestTemplateSpecs.Arid, Is.EqualTo(new[] { "Desert", "High Desert", "Badlands", "Salt Flat", "Oasis", "Scrubland", "Wasteland" }));
		}
	}
}
