using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2n "caves have a biome" (2026-10-25; decisions 37, 46, 51, 57): the per-form interior links, the shells an interior
	/// forms in, the template formations and glow (defaults = the dressing's old rules, so nothing moves until a round changes them),
	/// the underground band, the Glow fallback for linked lights, the host's accent crystals, the merged variant weights, the cave
	/// life's interior id, and the wiring of every consumer. Pure: no Unity object is made (templates are native).
	/// </summary>
	public class BiomeCaveInteriorTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static string Code(string path) => File.ReadAllText(path);

		// ── The link ──────────────────────────────────────────────────

		[Test]
		public void EverySurfaceBiome_LinksCavesIceCavesAndLavaTubes_ButNeverItselfOrAMine()
		{
			List<BiomeCaveInterior> forest = BiomeCaveInteriors.DefaultLinks("Forest");
			Assert.That(forest.Select(l => l.ToString()), Is.EqualTo(new[] { "Cave=Cave", "Grotto=Cave", "SeaCave=Cave", "IceCave=Ice Cave", "LavaTube=Lava Tube" }));
			Assert.That(BiomeCaveInteriors.DefaultLinks("Cave").Select(l => l.Form), Is.EqualTo(new[] { BiomeCaveForm.IceCave, BiomeCaveForm.LavaTube }));
			Assert.That(BiomeCaveInteriors.DefaultLinks("Lava Tube").Any(l => l.Form == BiomeCaveForm.LavaTube), Is.False, "the Lava Tube's tubes are its own");
			Assert.That(BiomeCaveInteriors.DefaultInterior(BiomeCaveForm.Mine), Is.Null);
			Assert.That(BiomeCaveInteriors.TryLink(forest, BiomeCaveForm.Grotto, out string grotto) && grotto == "Cave", Is.True);
			Assert.That(BiomeCaveInteriors.TryLink(forest, BiomeCaveForm.Mine, out _), Is.False);
			// The spec writes exactly the defaults until a round names its own: the swamps link their caves and grottos to the Swamp
			// Cave (2026-10-25, decisions 86–88, I2n-c), every other form as the defaults.
			var swamps = new[] { "Swamp", "Swamp Ruins", "Swamp Temple" };
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				List<string> expected = BiomeCaveInteriors.DefaultLinks(entry.Biome).Select(l => l.ToString()).ToList();
				if (swamps.Contains(entry.Biome))
				{
					expected = expected.Select(l => l.StartsWith("Cave=") ? "Cave=Swamp Cave" : l.StartsWith("Grotto=") ? "Grotto=Swamp Cave" : l).ToList();
				}
				Assert.That(entry.CaveInteriors().Select(l => l.ToString()), Is.EqualTo(expected), entry.Biome);
			}
			Assert.That(BiomeArtSpec.For("Swamp Cave").CaveInteriorRocks(), Is.EqualTo(BiomeCaveInteriors.SolubleRocks), "a swamp's granite cave stays its own");
		}

		[Test]
		public void TheLinkHolds_OnlyInTheInteriorsRocks()
		{
			// Decisions 37 + 46: the soluble set, Travertine and Dolomite with it; Granite is no karst.
			List<string> cave = BiomeCaveInteriors.DefaultInteriorRocks("Cave");
			foreach (string rock in new[] { "Limestone", "Chalk", "Marble", "Gypsum", "Travertine", "Dolomite", "limestone" })
			{
				Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.Cave, cave, rock), Is.True, rock);
			}
			foreach (string rock in new[] { "Granite", "Schist", "Sandstone", "Basalt", "Ice", "", null })
			{
				Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.Cave, cave, rock), Is.False, rock ?? "null");
			}
			// Decision 51: ice and the rock ice caves' rocks.
			List<string> ice = BiomeCaveInteriors.DefaultInteriorRocks("Ice Cave");
			Assert.That(new[] { "Ice", "Limestone", "Dolomite", "Marble", "Gypsum" }.All(r => BiomeCaveInteriors.Holds(BiomeCaveForm.IceCave, ice, r)), Is.True);
			Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.IceCave, ice, "Chalk"), Is.False);
			// Decision 57: basalt, whatever the Lava Tube's own air.
			List<string> tube = BiomeCaveInteriors.DefaultInteriorRocks("Lava Tube");
			Assert.That(tube, Is.EqualTo(new[] { "Basalt" }));
			Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.LavaTube, tube, "Basalt"), Is.True);
			Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.LavaTube, tube, "Andesite"), Is.False);
			// A mine never links; an interior with no rocks named holds anywhere; a biome nobody links names none.
			Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.Mine, cave, "Limestone"), Is.False);
			Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.Cave, new List<string>(), "Granite"), Is.True);
			Assert.That(BiomeCaveInteriors.DefaultInteriorRocks("Forest"), Is.Empty);
			Assert.That(BiomeArtSpec.For("Cave").CaveInteriorRocks(), Is.EqualTo(cave));
			Assert.That(BiomeArtSpec.For("Forest").CaveInteriorRocks(), Is.Empty);
		}

		// ── Formations and glow: the defaults are the dressing's old rules ──

		[Test]
		public void DefaultFormations_AreTheDressingsOldSet_ForEveryForm()
		{
			foreach (BiomeCaveForm form in System.Enum.GetValues(typeof(BiomeCaveForm)))
			{
				List<BiomeCaveFormation> list = BiomeCaveInteriors.DefaultFormations(form);
				Assert.That(list.All(f => f.Form == form && f.Weight == 1f && f.WorldGate.IsOpen), Is.True, form.ToString());
				Assert.That(list.Select(f => f.Role).Distinct().Count(), Is.EqualTo(list.Count), "one shape a place");
				// Every shape the dressing placed in the cave's own rock, and no other (a natural cave's crystal is a deposit, not its rock).
				bool deposit = form == BiomeCaveForm.Cave || form == BiomeCaveForm.Grotto;
				var own = list.Where(f => !(deposit && f.Role == BiomeCaveFormationRole.Crystal)).Select(f => f.Shape).Distinct().OrderBy(s => s).ToList();
				Assert.That(own, Is.EqualTo(PointOfInterestCaveDressing.FormationShapes((CaveForm)form).OrderBy(s => s).ToList()), form.ToString());
			}
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.CaveFormations().Select(f => f.ToString()), Is.EqualTo(BiomeCaveInteriors.DefaultFormations().Select(f => f.ToString())), entry.Biome);
				Assert.That(entry.CaveGlows().Select(g => g.ToString()), Is.EqualTo(BiomeCaveInteriors.DefaultGlows().Select(g => g.ToString())), entry.Biome);
			}
		}

		[Test]
		public void AFilesFormations_ReplaceOnlyItsForm_AndTheWorldGateShutsThemOff()
		{
			var entry = new BiomeArtSpec.Entry { Biome = "Test" };
			entry.CaveFormation(CaveForm.IceCave, BiomeCaveFormationRole.Floor, "IceColumn", 2f)
				.CaveFormation(CaveForm.IceCave, BiomeCaveFormationRole.Floor, "Stalagmite", 1f, BiomeWorldGate.Air(BiomeAtmosphereRequirement.Standard | BiomeAtmosphereRequirement.Thick));
			List<BiomeCaveFormation> list = entry.CaveFormations();
			Assert.That(list.Count(f => f.Form == BiomeCaveForm.IceCave), Is.EqualTo(2), "the ice cave's defaults gone");
			Assert.That(list.Where(f => f.Form == BiomeCaveForm.Cave).Select(f => f.ToString()),
				Is.EqualTo(BiomeCaveInteriors.DefaultFormations(BiomeCaveForm.Cave).Select(f => f.ToString())), "other forms keep theirs");

			BiomeWorldConditions earth = BiomeWorldConditions.Earthlike, airless = BiomeWorldConditions.Earthlike;
			airless.Atmosphere = AtmosphereKind.None;
			Assert.That(BiomeCaveInteriors.Shapes(list, BiomeCaveForm.IceCave, BiomeCaveFormationRole.Floor, in earth).Count, Is.EqualTo(2));
			List<(string Shape, float Weight)> bare = BiomeCaveInteriors.Shapes(list, BiomeCaveForm.IceCave, BiomeCaveFormationRole.Floor, in airless);
			Assert.That(bare.Select(s => s.Shape), Is.EqualTo(new[] { "IceColumn" }), "no drip formation without air");
			Assert.That(BiomeCaveInteriors.Shapes(list, BiomeCaveForm.IceCave, BiomeCaveFormationRole.Hanging, in earth), Is.Empty, "a place the file leaves out is unformed");
			// The weighted pick: IceColumn takes two thirds of the rolls.
			List<(string, float)> both = BiomeCaveInteriors.Shapes(list, BiomeCaveForm.IceCave, BiomeCaveFormationRole.Floor, in earth);
			Assert.That(BiomeCaveInteriors.Pick(both, 0.6f), Is.EqualTo("IceColumn"));
			Assert.That(BiomeCaveInteriors.Pick(both, 0.7f), Is.EqualTo("Stalagmite"));
			Assert.That(BiomeCaveInteriors.Pick(new List<(string, float)>(), 0.5f), Is.Null);
		}

		[Test]
		public void DefaultGlow_IsThePalettesOldRuleByForm()
		{
			Assert.That(BiomeCaveInteriors.DefaultGlow(BiomeCaveForm.IceCave).ToString(), Is.EqualTo("IceCave=IceBlue|0|0"));
			Assert.That(BiomeCaveInteriors.DefaultGlow(BiomeCaveForm.LavaTube).ToString(), Is.EqualTo("LavaTube=Amber|0|0"));
			Assert.That(BiomeCaveInteriors.DefaultGlow(BiomeCaveForm.Mine).ToString(), Is.EqualTo("Mine=chance|1|0"));
			Assert.That(BiomeCaveInteriors.DefaultGlow(BiomeCaveForm.Cave).ToString(), Is.EqualTo("Cave=chance|1|1"));
			// A fungus weight of 1 leaves the crystal share as it was; 0 is all crystal; more fungus less crystal.
			Assert.That(BiomeCaveInteriors.CrystalShare(0.4f, 0f), Is.EqualTo(1f));
			Assert.That(BiomeCaveInteriors.CrystalShare(0.4f, 2f), Is.EqualTo(0.4f / (0.4f + 1.2f)).Within(1e-6f));
			string palette = Code(WorldFolder + "/PointsOfInterest/Features/CaveGlowPalette.cs");
			Assert.That(palette, Does.Contain("BiomeCaveGlow glow = CaveInteriorArt.Glow(caves.Content, form);"));
			Assert.That(palette, Does.Contain("FungusWeight = 1f,"));
			string glow = Code(WorldFolder + "/PointsOfInterest/Features/PointOfInterestCaveDressing.Glow.cs");
			Assert.That(glow, Does.Contain("return fungus == 1f ? share : BiomeCaveInteriors.CrystalShare(share, fungus);"));
			Assert.That(glow, Does.Contain("chance *= Colours().WormChance;"));
		}

		// ── Crystals, lights, habitat, variants ───────────────────────

		[Test]
		public void TheHostsGlowingCrystals_AreAccentsInItsLinkedInteriors()
		{
			var interior = new List<BiomeCaveCrystal> { new BiomeCaveCrystal(BiomeCaveCrystalKind.FrostCrystal, 1f) };
			var host = new List<BiomeCaveCrystal>
			{
				new BiomeCaveCrystal(BiomeCaveCrystalKind.Amethyst, 1f),
				new BiomeCaveCrystal(BiomeCaveCrystalKind.Wyrdstone, 0.6f),
				new BiomeCaveCrystal(BiomeCaveCrystalKind.FrostCrystal, 3f),
			};
			IReadOnlyList<BiomeCaveCrystal> merged = BiomeCaveInteriors.WithHostAccents(interior, host);
			Assert.That(merged.Select(c => c.ToString()), Is.EqualTo(new[] { "FrostCrystal×1", "Wyrdstone×0.3" }), "glowing kinds only, at half weight, never twice");
			Assert.That(BiomeCaveInteriors.WithHostAccents(interior, new List<BiomeCaveCrystal> { new BiomeCaveCrystal(BiomeCaveCrystalKind.Selenite, 1f) }), Is.SameAs(interior));
		}

		[Test]
		public void LinkedLights_TurnToGlow_WhereTheAirWillNotBurn()
		{
			var torch = new BiomeCaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 17f);
			Assert.That(BiomeCaveInteriors.ForAir(in torch, true).ToString(), Is.EqualTo(torch.ToString()));
			Assert.That(BiomeCaveInteriors.ForAir(in torch, false).ToString(), Is.EqualTo("MineDrift=Glowjar@17"));
			var route = new BiomeCaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.LanternStand, 0f);
			Assert.That(BiomeCaveInteriors.ForAir(in route, false).Light, Is.EqualTo(BiomeWayLightKind.CrystalPost));
			var glowjar = new BiomeCaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.Glowjar, 0f);
			Assert.That(BiomeCaveInteriors.ForAir(in glowjar, false).ToString(), Is.EqualTo(glowjar.ToString()));
			// Decision 57: a thin/airless-only interior keeps fire cave lights; others as I2o.
			Assert.That(BiomeCaveInteriors.GlowCaveLights(true, true), Is.False);
			Assert.That(BiomeCaveInteriors.GlowCaveLights(true, false), Is.True);
			string art = Code(WorldFolder + "/Terrain/CaveInteriorArt.cs");
			Assert.That(art, Does.Contain("light = BiomeCaveInteriors.ForAir(in light, breathable);"));
		}

		[Test]
		public void ALinkedSpotsHabitat_IsTheInteriors_ButTheLandKeepsItsBarrenBit()
		{
			const int barren = (int)AmbientHabitat.Barren, cave = (int)AmbientHabitat.Cave, ice = (int)AmbientHabitat.Ice, volcanic = (int)AmbientHabitat.Volcanic;
			Assert.That(BiomeCaveInteriors.LinkedHabitat(volcanic, barren, barren), Is.EqualTo(0), "the Lava Tube's own Barren is its lava fields'");
			Assert.That(BiomeCaveInteriors.LinkedHabitat(ice | barren, cave, barren), Is.EqualTo(cave | barren), "barren land over it stays barren");
			Assert.That(BiomeCaveInteriors.LinkedHabitat(volcanic, cave | ice, barren), Is.EqualTo(cave | ice));
			// A spot of no interior keeps the land's (every spot baked before 2026-10-25).
			var spot = new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Floor };
			Assert.That(AmbientCaveLife.SpotHabitat(in spot, AmbientHabitat.Volcanic | AmbientHabitat.Barren), Is.EqualTo(AmbientHabitat.Volcanic | AmbientHabitat.Barren));
			spot.Biome = 123456789;
			Assert.That(AmbientCaveLife.SpotHabitat(in spot, AmbientHabitat.Forest), Is.EqualTo(AmbientHabitat.Forest), "an id this client does not hold");
			string life = Code(WorldFolder + "/PointsOfInterest/Features/PointOfInterestCaveDressing.Life.cs");
			Assert.That(life, Does.Contain("int inside = caves.Linked ? BiomeRegistry.IDOf(caves.Interior) : 0;"));
			Assert.That(Code("Assets/Scripts/Client/World/AmbientLife/AmbientCaveLife.cs"), Does.Contain("habitat = SpotHabitat(in spot, habitat);"));
		}

		[Test]
		public void MergedVariants_HostFirst_InteriorAtFourTenthsWhereTheHostNamesItsOwn()
		{
			var host = new List<BiomeSiteVariant> { new BiomeSiteVariant(POIType.IceCave, "Camp", 2f) };
			var interior = new List<BiomeSiteVariant> { new BiomeSiteVariant(POIType.IceCave, "Camp", 9f), new BiomeSiteVariant(POIType.IceCave, "Sleepers", 1f),
				new BiomeSiteVariant(POIType.Cave, "Glowworm", 1f) };
			Assert.That(BiomeCaveInteriors.MergedVariantWeight(host, interior, POIType.IceCave, "Camp", 0.5f, out bool named), Is.EqualTo(2f));
			Assert.That(named, Is.False);
			Assert.That(BiomeCaveInteriors.MergedVariantWeight(host, interior, POIType.IceCave, "Sleepers", 0.5f, out named), Is.EqualTo(0.4f).Within(1e-6f));
			Assert.That(named, Is.True, "its pieces take the interior's palette");
			Assert.That(BiomeCaveInteriors.MergedVariantWeight(host, interior, POIType.Cave, "Glowworm", 0.5f, out named), Is.EqualTo(1f), "the host names no cave variants");
			Assert.That(BiomeCaveInteriors.MergedVariantWeight(host, interior, POIType.Cave, "Deep", 0.5f, out named), Is.EqualTo(0.5f));
			Assert.That(named, Is.False);
		}

		// ── The underground band ──────────────────────────────────────

		[Test]
		public void TheUndergroundBand_DefaultsToBoth_AndRidesTheClimateMark()
		{
			Assert.That((int)BiomeUnderground.Both, Is.EqualTo(0), "every layer before the band");
			Assert.That(new TerrainTextureLayer().underground, Is.EqualTo(BiomeUnderground.Both));
			Assert.That(BiomeUndergrounds.OnSurface(BiomeUnderground.Interior), Is.False);
			Assert.That(BiomeUndergrounds.OnSurface(BiomeUnderground.Both) && BiomeUndergrounds.OnSurface(BiomeUnderground.Surface), Is.True);
			Assert.That(BiomeUndergrounds.CaveRank(BiomeUnderground.Interior), Is.LessThan(BiomeUndergrounds.CaveRank(BiomeUnderground.Both)));
			Assert.That(BiomeUndergrounds.CaveRank(BiomeUnderground.Surface), Is.EqualTo(-1), "never on a cave floor");
			// The mark: unchanged for Both (every old mark holds), "|u2" for Interior.
			Assert.That(BiomeArtAuthoring.ClimateMark(new BiomeArtSpec.Layer { Family = "Mud" }), Is.EqualTo(string.Empty));
			Assert.That(BiomeArtAuthoring.ClimateMark(new BiomeArtSpec.Layer { Family = "Mud" }.Underground(BiomeUnderground.Interior)), Is.EqualTo("t-|h-|u2"));
			var layer = new TerrainTextureLayer();
			BiomeArtAuthoring.WriteClimateBand(layer, new BiomeArtSpec.Layer { Family = "Mud" }.Underground(BiomeUnderground.Interior));
			Assert.That(layer.underground, Is.EqualTo(BiomeUnderground.Interior));
			Assert.That(BiomeArtAuthoring.ClimateMark(layer), Is.EqualTo("t-|h-|u2"));
			BiomeArtAuthoring.WriteClimateBand(layer, null);
			Assert.That(layer.underground, Is.EqualTo(BiomeUnderground.Both));
			// No spec names a band yet: nothing on any template changes.
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That((entry.Main?.UndergroundBand ?? BiomeUnderground.Both) == BiomeUnderground.Both && entry.Details.All(d => d == null || d.UndergroundBand == BiomeUnderground.Both)
					&& entry.Cliffs.All(c => c.UndergroundBand == BiomeUnderground.Both), Is.True, entry.Biome);
			}
			string palette = Code(WorldFolder + "/Terrain/SceneTerrainPalette.cs");
			Assert.That(palette, Does.Contain("detail.AdmitsAir(worldAir) && BiomeUndergrounds.OnSurface(detail.underground);"), "an interior ground's ways spend no channel");
			Assert.That(palette, Does.Contain("detail.AdmitsAir(worldAir) && BiomeUndergrounds.OnSurface(detail.underground))"), "never the air stand-in");
			string rock = Code(WorldFolder + "/PointsOfInterest/Shapers/CaveRockLayers.cs");
			Assert.That(rock, Does.Contain("int rank = layer != null ? BiomeUndergrounds.CaveRank(layer.underground) : -1;"));
			Assert.That(rock, Does.Contain("if (caves.Linked && shell == CliffRocks.Ice)"), "the interior's inner wall: an ice shell only");
			Assert.That(rock, Does.Contain("cliff.underground == BiomeUnderground.Interior && cliff.HasAlbedo"));
		}

		// ── Wiring ────────────────────────────────────────────────────

		[Test]
		public void EveryConsumer_ReadsTheCavesBiomes()
		{
			string shaper = Code(WorldFolder + "/PointsOfInterest/Shapers/CaveShaper.cs");
			Assert.That(shaper, Does.Contain("CaveBiome caves = CaveInteriorArt.Of(context.Request, site, system);"));
			Assert.That(shaper, Does.Contain("CaveRockLook look = CaveRockLayers.For(caves, system.Form, rock);"));
			Assert.That(shaper, Does.Contain("CaveCoverSpecies.Attach(Build(context.Root, system, mouths, meshes, material, colliders), system, CaveInteriorArt.Of(context.Request, site, system));"));
			string cover = Code(WorldFolder + "/PointsOfInterest/Shapers/CaveCoverSpecies.cs");
			Assert.That(cover, Does.Contain("BiomeTemplate biome = caves.Host;"), "the cover's climate is the land's");
			Assert.That(cover, Does.Contain("CaveGlowPalettes.For(form, seed, in caves)"));
			string main = Code(WorldFolder + "/PointsOfInterest/Features/PointOfInterestCaveDressing.cs");
			Assert.That(main, Does.Contain("ApplyFormations(set, CaveInteriorArt.Formations(caves.Content), system.Form, in world, system.Seed);"));
			Assert.That(main, Does.Contain("var dresser = new Dresser(context, system, set, in caves, BiomeAir.IsBreathable(in world),"));
			// The mouth's growth and the land over the cave stay the host's.
			Assert.That(main, Does.Contain("PointOfInterestCaveMouths.GrowthFor(PointOfInterestCaveMouths.BiomeOf(context, system))"));
			string fungi = Code(WorldFolder + "/PointsOfInterest/Features/PointOfInterestCaveDressing.Fungi.cs");
			Assert.That(fungi, Does.Contain("CaveFungi.TraitsOf(host, vines, caves.Linked ? CaveInteriorArt.Crystals(in caves) : null)"));
			Assert.That(Code(WorldFolder + "/PointsOfInterest/Features/PointOfInterestCaveDressing.Hanging.cs"), Does.Contain("over = ReferenceEquals(over, null) ? host : over;"));
			Assert.That(Code(WorldFolder + "/PointsOfInterest/Features/SpawnerFeatures.cs"), Does.Contain("offered = CaveInteriorArt.Gathering(in caves);"));
			Assert.That(Code(WorldFolder + "/PointsOfInterest/PointOfInterestStage.cs"), Does.Contain("weights = CaveInteriorArt.SiteVariantWeights(in caves);"));
			Assert.That(Code(WorldFolder + "/PointsOfInterest/PointOfInterestSiteContext.cs"), Does.Contain("palette = BiomeTemplateArt.StructurePalette(caves.Interior);"));
			string authoring = Code(WorldFolder + "/ProceduralArt/BiomeArtAuthoring.cs");
			Assert.That(authoring, Does.Contain("AuthorCaveInteriors(biome, entry, report, apply);"));
			foreach (string field in new[] { "CaveInteriors", "CaveInteriorRocks", "CaveFormations", "CaveGlows" })
			{
				Assert.That(authoring, Does.Contain($"BiomeTemplate.Authored.{field}, biome.{field}, entry.{field}()"), field);
			}
			Assert.That(BiomeTemplate.Authored.CaveInteriors, Is.EqualTo("caveInteriors"));
			Assert.That(BiomeTemplate.Authored.CaveGlows, Is.EqualTo("caveGlows"));
		}
	}
}
