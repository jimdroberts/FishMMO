using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// What the authored biome rounds' spec files now give (2026-10-26 on): the values the old-table pins skip for these biomes
	/// (<see cref="BiomeAuthoredRounds"/>), pinned here biome by biome, and for every authored file that it names only grounds and
	/// prefabs the generator makes (BiomeProceduralArtTests' own check of that loads the biome assets, so runs only in Unity).
	/// </summary>
	public class BiomeAuthoredRoundTests
	{
		/// <summary>
		/// Shrubs stand apart (2026-10-27): the rounds first planted them at four to ten times the old woods' density and the cut
		/// floor became one unbroken carpet of crowns. A rule's crowns cover at most a twentieth of its ground and a layer's
		/// shrubs together at most the old Forest floor's share (bushes per 100 m² × crown area).
		/// </summary>
		[Test]
		public void AuthoredFiles_PlantShrubsApart_NeverAsACarpet()
		{
			const float PerRule = 0.05f, PerLayer = 0.15f, Slack = 0.005f;
			var dense = new List<string>();
			foreach (string biome in BiomeAuthoredRounds.All)
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(biome);
				foreach (BiomeArtSpec.Layer layer in new[] { entry.Main }.Concat(entry.Details))
				{
					if (layer == null)
					{
						continue;
					}
					float total = 0f;
					foreach (BiomeArtSpec.Scatter s in layer.Scatter.Where(s => s.Name != null && s.Name.StartsWith("Bush: ")))
					{
						string species = s.Name.Substring("Bush: ".Length);
						Assert.That(ProceduralArtCatalogue.TryBush(species, out BushSpecies b), Is.True, $"{biome}: unknown shrub {species}");
						float radius = BushMeshes.CrownRadius(in b) * 0.5f * (s.WidthScale.x + s.WidthScale.y);
						float cover = s.Density * Mathf.PI * radius * radius / 100f;
						if (cover > PerRule + Slack) dense.Add($"{biome} {layer.Family}: {species} crowns cover {cover:P0} of the ground");
						total += cover;
					}
					if (total > PerLayer + Slack) dense.Add($"{biome} {layer.Family}: shrubs together cover {total:P0} of the ground");
				}
			}
			Assert.That(dense, Is.Empty, string.Join("\n", dense));
		}

		/// <summary>Every authored file names only generated grounds, prefabs the generator makes, and densities the field holds.</summary>
		[Test]
		public void AuthoredFiles_NameOnlyGroundsAndPrefabsThatExist()
		{
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			foreach (string biome in BiomeAuthoredRounds.All)
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(biome);
				Assert.That(entry, Is.Not.Null, biome);
				foreach (string family in entry.Families().Concat(new[] { entry.PathGround, entry.RoadGround, entry.CobbleGround, entry.BarSand, entry.BarGravel }))
				{
					Assert.That(SurfaceCatalogue.TryGround(family, out _), Is.True, $"{biome} uses unknown ground family '{family}'");
				}
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter rule) in entry.Rules())
				{
					Assert.That(rule.Density, Is.InRange(0f, 50f), $"{biome} '{rule.Name}'");
					foreach (string prefab in rule.Prefabs)
					{
						Assert.That(prefabs.Contains(prefab), Is.True, $"{biome} '{rule.Name}' names '{prefab}', which the generator does not make");
					}
				}
				// The bed layers and the riverbed's rules too (neither is in Families() or Rules()).
				foreach (BiomeArtSpec.Entry.BedSpec bed in entry.BedLayers)
				{
					Assert.That(SurfaceCatalogue.TryGround(bed.Layer.Family, out _), Is.True, $"{biome} beds unknown ground family '{bed.Layer.Family}'");
				}
				Assert.That(entry.BedLayers.Count, Is.LessThanOrEqualTo(BiomeTemplate.MaximumBedLayers), $"{biome}: at most the template's bed layers");
				foreach (BiomeArtSpec.Scatter rule in entry.RiverBed != null ? entry.RiverBed.Scatter : new List<BiomeArtSpec.Scatter>())
				{
					foreach (string prefab in rule.Prefabs)
					{
						Assert.That(prefabs.Contains(prefab), Is.True, $"{biome} riverbed '{rule.Name}' names '{prefab}', which the generator does not make");
					}
				}
				foreach (string node in entry.GatheringResources())
				{
					Assert.That(GatheringResourceCatalogue.Find(node), Is.Not.Null, $"{biome} gathers '{node}', which the catalogue lacks");
				}
				foreach (BiomeLandform form in entry.LandformList())
				{
					Assert.That(form.Carves || form.Paints, Is.True, $"{biome}: landform '{form.Kind}' does something");
				}
				// The vines it grows and the lines it lays are ones the generator and the line table know.
				foreach (BiomeVineSpecies vine in entry.VineGrowth().Species)
				{
					Assert.That(ProceduralArtCatalogue.Vines.Any(v => v.Name == vine.Species), Is.True, $"{biome} grows unknown vine '{vine.Species}'");
				}
				Assert.That(entry.LinearLines().Select(l => l.Unknown), Is.All.Empty, $"{biome}: every line style resolves");
			}
		}

		/// <summary>
		/// The air invariants for every authored biome that paints a thin-aired or airless band (decisions 7, 41, 44): every plant
		/// gated to the living air or riding a ground that paints only there, every flame lighting only a kind sited where the air is
		/// breathable (the rest Glow), every lived-in kind needing that air, and no burn regime.
		/// </summary>
		[Test]
		public void AuthoredFiles_OffTheBreathableAir_GrowNothing_BurnNothing_AndHouseNobody()
		{
			string[] plants = { "Detail_Grass", "Detail_Flowers", "Detail_Lichen", "Detail_Fern", "Detail_Herb", "Detail_Cushion", "Detail_Moss",
				ProceduralArtCatalogue.BushPrefix, "Tree_" };
			// Cryoconite holes are dust in the ice, not life (the Glacier round's draft: they pock thin worlds' ice too).
			var mineral = new HashSet<string> { "LichenCryoconite" };
			const BiomeAtmosphereRequirement off = BiomeAtmosphereRequirement.Thin | BiomeAtmosphereRequirement.Airless;
			int offAir = 0;
			foreach (string biome in BiomeAuthoredRounds.All)
			{
				BiomeArtSpec.Entry e = BiomeArtSpec.For(biome);
				if (!new[] { e.Main }.Concat(e.Details).Any(l => l != null && (l.AirBand & off) != 0))
				{
					continue;
				}
				offAir++;
				foreach ((BiomeArtSpec.Layer layer, BiomeArtSpec.Scatter rule) in e.Rules())
				{
					if (!mineral.Contains(rule.Name) && rule.Prefabs.Any(p => plants.Any(q => p.StartsWith(q, System.StringComparison.Ordinal))))
					{
						Assert.That(!rule.World.IsOpen || layer.AirBand == BiomeAtmosphereRequirement.Living, Is.True, $"{biome} {rule.Name} grows off the living air");
					}
				}
				Dictionary<POIType, BiomeSiteKind> sited = e.SiteKinds().ToDictionary(k => k.Kind);
				foreach (BiomeWayLight light in e.WayLights())
				{
					if (!BiomeGlowLights.Glows(light.Light) && sited.TryGetValue(light.Kind, out BiomeSiteKind site))
					{
						Assert.That(site.NeedsBreathableAir, Is.True, $"{biome}: {light.Kind}'s {light.Light} would burn off the breathable air");
					}
				}
				foreach (BiomeSiteKind kind in e.SiteKinds().Where(k => BiomeSiteAir.NeedsAirByDefault(k.Kind)))
				{
					Assert.That(kind.NeedsBreathableAir, Is.True, $"{biome}: {kind.Kind} is lived in");
				}
				Assert.That(e.BurnRegime ?? BiomeBurnRegime.None, Is.EqualTo(BiomeBurnRegime.None), $"{biome}: nothing burns off the breathable air");
			}
			Assert.That(offAir, Is.EqualTo(4), "Ice Sheet, Glacier, Scree, Rocky Terrain");
		}

		/// <summary>The two sea-floor rounds paint and grow under the sea, by depth, with rules that name the sea.</summary>
		[Test]
		public void CoastalWaterAndOcean_PaintAndGrowUnderTheSea()
		{
			foreach (string biome in new[] { "Coastal Water", "Ocean" })
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(biome);
				Assert.That(entry.SeaFloorKind, Is.EqualTo(BiomeSeaFloor.Ground), biome);
				Assert.That(entry.Details.Count(d => d.SeaDepth.HasValue), Is.GreaterThanOrEqualTo(3), $"{biome}: depth-banded grounds");
				foreach (BiomeArtSpec.Layer layer in new[] { entry.Main }.Concat(entry.Details))
				{
					foreach (BiomeArtSpec.Scatter rule in layer.Scatter)
					{
						Assert.That(BiomeSeaFloors.GrowsUnderSea(entry.SeaFloorKind, rule.Waters), Is.True, $"{biome} '{rule.Name}' grows under the sea");
					}
				}
				Assert.That(entry.GrassTypes(), Is.Empty, $"{biome}: no grass");
				Assert.That(entry.FieldCrops(), Is.Empty, $"{biome}: no fields");
				Assert.That(entry.GroveHerbs(), Is.Empty, $"{biome}: no herb groves' herbs");
				Assert.That(entry.VineGrowth().Species, Is.Empty, $"{biome}: no vines");
				var kinds = entry.SiteKinds().Select(k => k.Kind).ToList();
				foreach (POIType land in new[] { POIType.Spring, POIType.Waterfall, POIType.Rapids, POIType.River, POIType.Lake, POIType.Delta, POIType.Peak,
					POIType.Pass, POIType.Mesa, POIType.Butte, POIType.Gorge, POIType.Sinkhole, POIType.Village, POIType.Town })
				{
					Assert.That(kinds.Contains(land), Is.False, $"{biome} hosts no {land}");
				}
				Assert.That(kinds.Contains(POIType.SunkenShip), Is.True, biome);
				foreach (POIType town in new[] { POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Port })
				{
					Assert.That(BiomeWayLights.TryFor(entry.WayLights(), town, out _), Is.False, $"{biome}: no streets to light ({town})");
				}
				Assert.That(entry.Weathering().Finish, Is.EqualTo(BiomeStructureFinish.None), biome);
				Assert.That(entry.Weathering().CoatCover, Is.EqualTo(0.9f), biome);
			}
		}

		[Test]
		public void CoastalWater_IsTheCelticShelf()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Coastal Water");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Dolerite"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Granite", "Slate" }));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Sarn" }));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Cassiterite, BiomeCaveCrystalKind.Bellspar }));
			Assert.That(e.GatheringResources(), Is.EqualTo(new[] { "Kelp Holdfast", "Flat Oyster Bed", "Scallop Bed", "Carrageen", "Dulse", "Maerl", "Cassiterite", "Bellspar" }));
			Assert.That(e.SeaLifeKinds(), Does.Not.Contain("Sea turtle"));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Coralline));
			Assert.That(e.SeaLook().Visibility, Is.EqualTo(15f));
			Assert.That(e.CaveLights().Single(l => l.Place == BiomeCaveLightPlace.Route).Light, Is.EqualTo(BiomeWayLightKind.Bellglow));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.SunkenCity, out BiomeWayLight ys) && ys.Light == BiomeWayLightKind.Bellglow, Is.True);
			Assert.That(e.CaveShellFor(CaveForm.SeaCave), Is.EqualTo("Dolerite"));
			// The cold band's urchin barren and its sugar kelp; the warm band's golden kelp.
			Assert.That(e.Details.Single(d => d.Family == Ground.CoralBarren).Temperature, Is.EqualTo(new Vector2(-1f, -0.4f)));
			Assert.That(e.Rules().Any(r => r.rule.Name == "KelpSugar" && r.rule.Temperature == new Vector2(-1f, -0.4f)), Is.True);
			Assert.That(e.Rules().Any(r => r.rule.Name == "KelpGolden" && r.rule.Temperature == new Vector2(0.45f, 1f)), Is.True);
			// Jewel anemones only on steep rock (decision 38's slopeMin).
			Assert.That(e.Rules().Single(r => r.rule.Name == "JewelAnemone").rule.Slope, Is.EqualTo(new Vector2(35f, 60f)));
			Assert.That(e.Description, Does.StartWith("Atlantic shallows"));
			Assert.That(e.Naming.Onsets, Does.Contain("Ker"));
		}

		[Test]
		public void Ocean_IsTheSlopeSea()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Ocean");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Mudstone"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Sandstone", "Granite" }));
			List<BiomeLandform> forms = e.LandformList();
			Assert.That(forms.Select(l => l.Kind), Is.EqualTo(new[] { "CoralMounds", "Pockmarks", "PloughMarks" }));
			// The draft's Earth depths at the scene's scale (Arthis 0.3575): Lophelia's 200–1200 m are 72–430 m in the scene.
			Assert.That(forms[0].Depth, Is.EqualTo(new Vector2(72f, 430f)));
			Assert.That(forms[2].Depth, Is.EqualTo(new Vector2(72f, 180f)));
			Assert.That(e.SeaFloats().Select(f => f.Kind), Is.EqualTo(new[] { BiomeSeaFloatKind.Corkline, BiomeSeaFloatKind.Growler }));
			Assert.That(e.GlowModes().Select(g => (g.Source, g.Mode, g.Warm)),
				Is.EquivalentTo(new[] { (SceneGlowSource.Crystal, SceneGlowMode.Wind, true), (SceneGlowSource.Way, SceneGlowMode.Wind, true) }));
			Assert.That(e.UnderwaterSiteDepth, Is.EqualTo(new Vector2(60f, 200f)));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Siderite, BiomeCaveCrystalKind.GalernaStone }));
			Assert.That(e.GatheringResources(), Is.EqualTo(new[] { "Lophelia Coral", "Bubblegum Coral", "Baleen Plate", "Siderite Spar", "Galerna-stone", "Lamia Comb" }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Coldreef));
			var kinds = e.SiteKinds().Select(k => k.Kind).ToList();
			Assert.That(kinds.Contains(POIType.Longline) && kinds.Contains(POIType.WhaleTow) && !kinds.Contains(POIType.Bay), Is.True);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Longline, out BiomeWayLight line) && line.Light == BiomeWayLightKind.Kriseilu, Is.True);
			// The pockmark floor and the plough-mark diamicton paint only on their landforms.
			Assert.That(e.Details.Single(d => d.Family == Ground.MudPockmark).ZoneBand, Is.EqualTo(BiomeLandformBand.Of(BiomeLandformPart.Floor, "Pockmarks")));
			Assert.That(e.Details.Single(d => d.Family == Ground.GravelDiamicton).Temperature, Is.EqualTo(new Vector2(-1f, -0.3f)));
			// The landform presets paint their own detail slots: d1 the coral rubble, d2 the pockmark floor, d4 the diamicton.
			Assert.That(e.Details[0].Family, Is.EqualTo(Ground.SandShelfEdge));
			Assert.That(e.Details[1].Family, Is.EqualTo(Ground.CoralRubble));
			Assert.That(e.Details[2].Family, Is.EqualTo(Ground.MudPockmark));
			Assert.That(e.Details[4].Family, Is.EqualTo(Ground.GravelDiamicton));
			Assert.That(e.Description, Does.StartWith("The slope sea"));
		}
		/// <summary>Round G3 (2026-10-26): the treeless arctic — gneiss, permafrost landforms, the prostrate mat, no fields, frozen moats.</summary>
		[Test]
		public void Tundra_IsTheTreelessArctic()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Tundra");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Gneiss"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Gneiss", "Granite", "Schist", "Basalt" }));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Pingo", "Palsa", "ThawSlump" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.LichenTundraMat));
			Assert.That(e.Details[0].Family, Is.EqualTo(Ground.MossPolygonMire));
			Assert.That(e.Details[1].Family, Is.EqualTo(Ground.GravelSortedCircles));
			Assert.That(e.Rules().Any(r => r.rule.Channel == PrefabSpawnChannel.TreeInstance && r.rule.Name.StartsWith("Trees", System.StringComparison.Ordinal)), Is.False,
				"treeless");
			Assert.That(e.Rules().Any(r => r.rule.Name == "Bush: ArcticWillow") && e.Rules().Any(r => r.rule.Name == "Bush: Bearberry"), Is.True);
			Assert.That(e.FieldCrops(), Is.Empty, "no fields");
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Frozen));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Tundra | BiomeHabitat.FreshWater));
			Assert.That(e.VineGrowth().Species, Is.Empty);
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Lichen));
			Assert.That(e.Weathering().Finish, Is.EqualTo(BiomeStructureFinish.None));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Labradorite, BiomeCaveCrystalKind.Aurorite }));
			Assert.That(e.GatheringResources(), Does.Not.Contain("Iron Vein").And.Not.Contain("Frostpine Elder").And.Contain("Mammoth Ivory"));
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Village], Is.EqualTo(0.35f));
			Assert.That(kinds[POIType.Town], Is.EqualTo(0.08f));
			Assert.That(kinds.ContainsKey(POIType.City) || kinds.ContainsKey(POIType.Capital) || kinds.ContainsKey(POIType.Castle), Is.False);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.LampCairn, Is.True);
			Assert.That(e.Description, Does.StartWith("Treeless arctic tundra"));
		}

		/// <summary>Round G3 (2026-10-26): the ice cap's interior — charnockite nunataks, katabatic sastrugi, a Thin band, nobody living there.</summary>
		[Test]
		public void IceSheet_IsTheCapsInterior()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Ice Sheet");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Charnockite"));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Nunatak", "BlueIceTail", "Megadunes", "SpiralTroughs", "SwissCheese" }));
			// The blue-ice tail anchors in the lee of the entry before it; the Swiss-cheese pits paint the Thin slab.
			Assert.That(e.LandformList()[1].Anchor, Is.EqualTo(BiomeLandformAnchor.LeeOf));
			Assert.That(e.Details[e.LandformList()[4].PaintGround - 1].Family, Is.EqualTo(Ground.FrostSwissCheese));
			Assert.That(e.Details[e.LandformList()[0].PaintGround - 1].Family, Is.EqualTo(Ground.RockCharnockiteFell));
			Assert.That(e.Details[e.LandformList()[1].PaintGround - 1].Family, Is.EqualTo(Ground.IceBlueField));
			Assert.That(e.Main.AirBand, Is.EqualTo(BiomeAtmosphereRequirement.Living));
			Assert.That(e.Details.Any(d => d.AirBand.HasFlag(BiomeAtmosphereRequirement.Thin) && d.Family == Ground.IceLayeredDeposit), Is.True, "the Thin main stand-in");
			Assert.That(e.Cliffs.Length, Is.EqualTo(2));
			Assert.That(e.Rules().Any(r => r.rule.Name == "Formations: Katabatic sastrugi" && r.rule.Mode.Facing == BiomeScatterFacing.Downslope
				&& r.rule.Mode.FacingOffset == 30f), Is.True);
			Assert.That(e.GrassTypes(), Is.Empty, "no grass");
			Assert.That(e.FieldCrops(), Is.Empty);
			Assert.That(e.GroveHerbs(), Is.Empty);
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Frozen));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Windpacked));
			Assert.That(e.GlowModes().Single().Mode, Is.EqualTo(SceneGlowMode.Wind));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.ClearQuartz, BiomeCaveCrystalKind.FrostCrystal,
				BiomeCaveCrystalKind.Hornice }));
			var kinds = e.SiteKinds().Select(k => k.Kind).ToList();
			// A keep stands even here since the fantasy wave (2026-10-27: keeps and towers at least 1 in every earth land biome), held by
			// the cap's enemies or lying ruined (its .Prefers); every lived-in place stays out.
			foreach (POIType settled in new[] { POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Port, POIType.Castle,
				POIType.Fortress, POIType.TradingPost, POIType.Island, POIType.Bay, POIType.SunkenShip, POIType.SmugglersCove })
			{
				Assert.That(kinds.Contains(settled), Is.False, $"Ice Sheet hosts no {settled}");
			}
			Assert.That(e.SiteKinds().Where(k => k.Kind == POIType.Waystation || k.Kind == POIType.Camp).All(k => k.NeedsBreathableAir), Is.True);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Waystation, out BiomeWayLight w) && w.Light == BiomeWayLightKind.SergeLamp, Is.True);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out _), Is.False);
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.LinearLines().Count, Is.EqualTo(2));
			Assert.That(e.Naming.Onsets, Does.Contain("Muus"));
		}

		/// <summary>Round G3 (2026-10-26): the valley glacier — greenstone, the glacier landform bundle, snow bridges, a Thin band.</summary>
		[Test]
		public void Glacier_IsTheValleyGlacier()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Glacier");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Greenstone"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Greenstone", "Granite", "Gneiss", "Limestone" }));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "CrevasseField", "Moulin", "SupraglacialChannels", "LateralMoraine",
				"MedialMoraine", "TerminalMoraine", "KettlePonds", "Ogives", "SnoutPortal" }));
			// The presets paint the draft's slots: d1 the icefall (crevasses), d3 the till (moraines).
			Assert.That(e.Details[e.LandformList()[0].PaintGround - 1].Family, Is.EqualTo(Ground.IceIcefall));
			Assert.That(e.Details[e.LandformList()[4].PaintGround - 1].Family, Is.EqualTo(Ground.GravelTill));
			Assert.That(e.Details[e.LandformList()[3].PaintGround - 1].Family, Is.EqualTo(Ground.GravelTill));
			// Seracs stand on the icefall's steep steps, not on the flat ice.
			BiomeArtSpec.Scatter seracs = e.Details[1].Scatter.Single(s => s.Name == "Seracs");
			Assert.That(seracs.Slope, Is.EqualTo(new Vector2(18f, 40f)));
			Assert.That(e.Main.Scatter.Any(s => s.Name == "Seracs"), Is.False);
			// Every plant is gated to breathable air (decision 41).
			foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter rule) in e.Rules())
			{
				if (rule.Prefabs.Any(p => p.StartsWith("Detail_Grass", System.StringComparison.Ordinal) || p.StartsWith("Detail_Flowers", System.StringComparison.Ordinal)
					|| p.StartsWith(ProceduralArtCatalogue.BushPrefix, System.StringComparison.Ordinal) || p.StartsWith("Tree_", System.StringComparison.Ordinal)))
				{
					Assert.That(rule.World.IsOpen, Is.False, rule.Name);
				}
			}
			Assert.That(e.CrossingStyle, Is.EqualTo(BiomeCrossingStyle.SnowBridge));
			Assert.That(e.FloatingPlants().Select(f => f.Kind), Is.EqualTo(new[] { BiomeFloatingPlantKind.BergyBits }));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Ice | BiomeHabitat.Mountain));
			Assert.That(e.FieldCrops(), Is.Empty);
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Frozen));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Floured));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.CupHoar, BiomeCaveCrystalKind.FrostCrystal,
				BiomeCaveCrystalKind.Nagamani }));
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Village], Is.EqualTo(0.1f));
			Assert.That(kinds.ContainsKey(POIType.Town) || kinds.ContainsKey(POIType.SunkenShip) || kinds.ContainsKey(POIType.Mesa), Is.False);
			Assert.That(e.Description, Does.StartWith("A valley glacier"));
		}

		/// <summary>Round G4 (2026-10-26): the Carpathian high pastures — flysch, three reaches, shepherds, 60° shakes, stormglass.</summary>
		[Test]
		public void AlpineMeadow_IsTheHighPastures()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Alpine Meadow");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Flysch"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Flysch", "Andesite", "Limestone", "Conglomerate", "Granite" }));
			// The three reaches: the meltline's snow and melt-turf (temperature), the dry limestone and the wet springs (humidity).
			Assert.That(e.Details.Single(d => d.Family == Ground.SnowLatePatch).Temperature, Is.EqualTo(new Vector2(-1f, -0.35f)));
			Assert.That(e.Details.Single(d => d.Family == Ground.LimestoneKarren).Humidity, Is.EqualTo(new Vector2(-1f, 0.1f)));
			Assert.That(e.Details.Single(d => d.Family == Ground.MossSpringFlush).Humidity, Is.EqualTo(new Vector2(0.3f, 1f)));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Grassland | BiomeHabitat.Mountain | BiomeHabitat.FreshWater));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Sunburnt));
			Assert.That(e.StructurePalette().Surfaces.Any(s => s.Role == BiomeStructureRole.Roof && s.Pitch == BiomeRoofPitch.Pitch60), Is.True, "Goral shakes at 60°");
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Stibnite, BiomeCaveCrystalKind.Rhodochrosite,
				BiomeCaveCrystalKind.Stormglass }));
			Assert.That(e.GlowModes().Single(g => g.Source == SceneGlowSource.Crystal).Mode, Is.EqualTo(SceneGlowMode.Flicker));
			Assert.That(e.FieldSurface("Detail_GrassMountainOats"), Is.EqualTo(BiomeFieldSurface.RidgeAndFurrow));
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.LinearLines().Count(l => l.Style.Form == BiomeLinearForm.LeaningPole), Is.EqualTo(2));
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Camp], Is.EqualTo(1.8f));
			Assert.That(kinds[POIType.IceCave], Is.EqualTo(0.6f));
			Assert.That(kinds.ContainsKey(POIType.City) || kinds.ContainsKey(POIType.Capital) || kinds.ContainsKey(POIType.Port), Is.False);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Camp, out _), Is.False, "the folds are unlit");
			Assert.That(e.Rules().Any(r => r.rule.Mode.Pattern == BiomeScatterPattern.Rings), Is.True, "fairy rings");
			Assert.That(e.Naming.Onsets, Does.Contain("Plai"));
		}

		/// <summary>Round G4 (2026-10-26): the Afromontane cloud forest — phonolite, four reaches, ambas, teff terraces, red water.</summary>
		[Test]
		public void MountainSlope_IsTheCloudForest()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Mountain Slope");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Phonolite"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Phonolite", "Basalt", "Gneiss", "Andesite", "Tuff" }));
			Assert.That(e.Details.Single(d => d.Family == Ground.HeathEricaMoor).Temperature, Is.EqualTo(new Vector2(-0.45f, 0.05f)), "the frost-moor");
			Assert.That(e.Details.Single(d => d.Family == Ground.ForestFloorCoffee).Temperature, Is.EqualTo(new Vector2(0.55f, 1f)), "the coffee forest");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Amba" }));
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Forest));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Mountain | BiomeHabitat.Forest | BiomeHabitat.FreshWater));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Epiphytic));
			Assert.That(e.FieldSurface("Detail_GrassTeff"), Is.EqualTo(BiomeFieldSurface.Terraced));
			Assert.That(e.FieldRiser("Detail_GrassTeff"), Is.EqualTo(BiomeTerraceRiser.EarthBund));
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Does.Contain("Detail_Enset"), "a non-grass crop");
			Assert.That(e.CaveShellFor(CaveForm.LavaTube), Is.EqualTo("Basalt"));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Hydrophane, BiomeCaveCrystalKind.Moonstone,
				BiomeCaveCrystalKind.Amethyst }));
			Assert.That(e.WaterLook().Turbidity, Is.EqualTo(0.8f), "the red laterite water");
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.AncientRoad, out BiomeWayLight road) && road.Light == BiomeWayLightKind.CrystalPost, Is.True);
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Monastery], Is.EqualTo(1.8f));
			Assert.That(kinds.ContainsKey(POIType.Port) || kinds.ContainsKey(POIType.Windmill) || kinds.ContainsKey(POIType.Lighthouse), Is.False);
			Assert.That(e.Naming.Onsets, Does.Contain("Zag"));
		}

		/// <summary>
		/// Round G4 (2026-10-26): the moving stone of the summits — granodiorite, the scree landforms the falling stone runs on, and,
		/// on the thin-aired and airless worlds that carry it (Belron 1, Ithaewyn 1), bare talus with nothing alive and nobody living there.
		/// </summary>
		[Test]
		public void Scree_IsTheMovingStone_AndBareOffTheBreathableAir()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Scree");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Granodiorite"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Granodiorite", "Granite", "Gneiss" }));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "ScreeCone", "ScreeChutes", "ProtalusRampart", "NivationHollow", "PinnacleRidge",
				"WalkingRock", "ScreeRockGlacier", "ScreeStoneRiver", "ScreeMudflowFan", "ScreeBoulderTracks" }));
			Assert.That(e.Details[e.LandformList()[0].PaintGround - 1].Family, Is.EqualTo(Ground.RockOpenworkTalus), "the cones' coarse toe");
			Assert.That(e.Details[e.LandformList()[6].PaintGround - 1].Family, Is.EqualTo(Ground.GravelRockGlacierSkin));
			Assert.That(e.Details[e.LandformList()[7].PaintGround - 1].Family, Is.EqualTo(Ground.RockStoneRiver));
			Assert.That(e.Rules().Any(r => r.rule.Graded), Is.True, "talus graded down the cones");
			// The air bands: the main ground on living air only, the rust scree (Thin) and vacuum talus (Airless) in its place elsewhere.
			Assert.That(e.Main.AirBand, Is.EqualTo(BiomeAtmosphereRequirement.Living));
			Assert.That(e.Details.Single(d => d.AirBand == BiomeAtmosphereRequirement.Thin).Family, Is.EqualTo(Ground.GravelRustScree));
			Assert.That(e.Details.Single(d => d.AirBand == BiomeAtmosphereRequirement.Airless).Family, Is.EqualTo(Ground.RegolithVacuumTalus));
			// Every plant is gated to the living air (decisions 7, 41) or rides a ground that only paints there.
			foreach ((BiomeArtSpec.Layer layer, BiomeArtSpec.Scatter rule) in e.Rules())
			{
				if (rule.Prefabs.Any(p => p.StartsWith("Detail_Grass", System.StringComparison.Ordinal) || p.StartsWith("Detail_Flowers", System.StringComparison.Ordinal)
					|| p.StartsWith("Detail_Lichen", System.StringComparison.Ordinal) || p.StartsWith("Detail_Fern", System.StringComparison.Ordinal)
					|| p.StartsWith(ProceduralArtCatalogue.BushPrefix, System.StringComparison.Ordinal) || p.StartsWith("Tree_", System.StringComparison.Ordinal)))
				{
					bool gated = !rule.World.IsOpen || layer.AirBand == BiomeAtmosphereRequirement.Living;
					Assert.That(gated, Is.True, $"{rule.Name} grows off the living air");
				}
			}
			// Nobody lives there without breathable air: every lived-in kind needs it, the talus springs need liquid water too.
			foreach (BiomeSiteKind kind in e.SiteKinds())
			{
				if (kind.Kind == POIType.Village || kind.Kind == POIType.Fortress || kind.Kind == POIType.Tower || kind.Kind == POIType.Waystation
					|| kind.Kind == POIType.Hermitage || kind.Kind == POIType.Camp || kind.Kind == POIType.Quarry || kind.Kind == POIType.Landmark)
				{
					Assert.That(kind.NeedsBreathableAir, Is.True, $"{kind.Kind} needs air");
				}
			}
			Assert.That(e.SiteKinds().Single(k => k.Kind == POIType.Spring).NeedsLiquidWater, Is.True);
			var kinds = e.SiteKinds().Select(k => k.Kind).ToList();
			foreach (POIType none in new[] { POIType.Town, POIType.City, POIType.Port, POIType.Lighthouse, POIType.SmugglersCove, POIType.Temple, POIType.Island })
			{
				Assert.That(kinds.Contains(none), Is.False, $"Scree hosts no {none}");
			}
			// Off the breathable air nothing burns (decision 41): every flame lights a kind that is only sited where the air is
			// breathable (the cave dressing swaps its own lights for glows by the world's air, I2n).
			Dictionary<POIType, BiomeSiteKind> sited = e.SiteKinds().ToDictionary(k => k.Kind);
			foreach (BiomeWayLight light in e.WayLights())
			{
				if (!BiomeGlowLights.Glows(light.Light) && sited.TryGetValue(light.Kind, out BiomeSiteKind site))
				{
					Assert.That(site.NeedsBreathableAir, Is.True, $"{light.Kind}'s {light.Light} would burn off the breathable air");
				}
			}
			Assert.That(e.CrossingStyle, Is.EqualTo(BiomeCrossingStyle.GoblinsBridge));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Dry));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Stoneshot));
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Is.EqualTo(new[] { "Detail_CropPerilla" }));
			Assert.That(e.VineGrowth().Species, Is.Empty);
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.Naming.Onsets, Does.Contain("Geum"));
		}

		/// <summary>
		/// Round G5 (2026-10-26): the Sonoran scrub of the canal builders — caliche its own rock, the bajada landforms, the saguaro
		/// under its nurse trees, creosote evenly spaced, the barrels leaning to the sun, the canal and the warm sunshell glow.
		/// </summary>
		[Test]
		public void Scrubland_IsTheSonoranCanalCountry()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Scrubland");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Caliche"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Granite", "Gneiss", "Basalt", "Limestone" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.SoilBajadaLoam));
			Assert.That(e.Details.Select(d => d.Family), Is.EqualTo(new[] { Ground.SiltCreosoteFlat, Ground.CalicheBench, Ground.GravelGrusBajada,
				Ground.BasaltMalpais, Ground.LitterMesquiteBosque }));
			Assert.That(e.Cliffs[0].Family, Is.EqualTo(Ground.CliffMylonite));
			Assert.That((e.PathGround, e.RoadGround, e.CobbleGround), Is.EqualTo((Ground.PathClearedTrail, Ground.PathCanalBank, Ground.FlagstoneCalicheSetts)));
			Assert.That(e.Details[3].WayPath, Is.EqualTo(Ground.PathMalpaisTrail));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Bajada", "Pediment", "BraidedWash", "Malpais", "Maar", "OvalCourt", "PlatformMound" }));
			// The draft's retired kit grows nowhere; the Sonoran kit does, with its placement modes.
			string[] retired = { "Detail_ShrubDry", "Detail_ShrubSmall", "Detail_GrassTuft", "Detail_Bush_Sagebrush", "Detail_Bush_Thornbush", "Detail_Bush_Cistus",
				"Tree_Olive", "Tree_Acacia", "Detail_RosetteYucca" };
			Assert.That(e.Rules().SelectMany(r => r.rule.Prefabs).Intersect(retired), Is.Empty);
			Assert.That(e.Rules().Any(r => r.rule.Name == "Trees: Saguaro" && r.rule.Mode.Anchor == BiomeScatterAnchor.Nurse), Is.True, "nursed saguaros");
			Assert.That(e.Rules().Any(r => r.rule.Name == "Bush: Creosote" && r.rule.Mode.Pattern == BiomeScatterPattern.Even), Is.True, "even creosote");
			Assert.That(e.Rules().Any(r => r.rule.Name == "CactusFishhook" && r.rule.Mode.Lean == BiomeScatterLean.Sunward), Is.True, "leaning barrels");
			Assert.That(e.Rules().Any(r => r.rule.Name == "GrassBushMuhly" && r.rule.Mode.Anchor == BiomeScatterAnchor.Shelter), Is.True, "muhly under shrubs");
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Peridot, BiomeCaveCrystalKind.Sunshell }));
			Assert.That(e.GlowModes().Any(g => g.Source == SceneGlowSource.Crystal && g.Mode == SceneGlowMode.AfterSunset && g.Warm), Is.True);
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Rainmelt));
			Assert.That(e.Weathering().Finish, Is.EqualTo(BiomeStructureFinish.None));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Scrub | BiomeHabitat.Desert));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Wet));
			Assert.That(e.VineGrowth().Species.Select(v => v.Species), Is.EqualTo(new[] { "DesertMistletoe" }));
			Assert.That(e.GatheringResources(), Does.Contain("Copper Lode").And.Contain("Saguaro Fruit").And.Contain("Sunshell Scatter").And.Not.Contain("Sunpetal Patch"));
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Does.Contain("Detail_GrassMaizeCanal"));
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.LinearLines().Any(l => l.Style.Form == BiomeLinearForm.GreatCanal), Is.True);
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Village], Is.EqualTo(1.6f));
			foreach (POIType none in new[] { POIType.Castle, POIType.Monastery, POIType.Crypt, POIType.Barrow, POIType.Graveyard, POIType.Oasis, POIType.Port,
				POIType.Windmill, POIType.LumberCamp, POIType.HuntingLodge, POIType.Mesa, POIType.SunkenShip })
			{
				Assert.That(kinds.ContainsKey(none), Is.False, $"Scrubland hosts no {none}");
			}
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.TrayFire, Is.True);
			Assert.That(e.Description, Does.StartWith("Sonoran thorn scrub"));
		}

		/// <summary>
		/// Round G5 (2026-10-26): the Zimbabwe plateau's veld — granite its own rock, the kopje and termite-island landforms, the annual
		/// veld fires with their fire lilies, rain-glow crystals, dry moats and the stone-house kings' walls.
		/// </summary>
		[Test]
		public void Savanna_IsTheGraniteVeld()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Savanna");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Granite"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Gneiss", "Schist", "Quartzite", "Sandstone", "Basalt" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.GrassDryTawnyVeld));
			Assert.That(e.Details.Select(d => d.Family), Is.EqualTo(new[] { Ground.SandGraniteSandveld, Ground.ClayMopane, Ground.SoilGreenstoneRed,
				Ground.RockDwalaSlab, Ground.MudVleiTurf }));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Kopje", "Dwala", "Pan", "TermiteIslands", "TableHill" }));
			// The kopjes and dwalas paint the slab (d3, slot 4), the pans the vlei turf (d4, slot 5).
			Assert.That(e.LandformList().Where(l => l.Kind == "Kopje" || l.Kind == "Dwala").Select(l => l.PaintGround), Is.All.EqualTo(4));
			Assert.That(e.LandformList().Single(l => l.Kind == "Pan").PaintGround, Is.EqualTo(5));
			Assert.That(e.BurnRegime, Is.EqualTo(BiomeBurnRegime.Annual));
			Assert.That(e.LookOf("Detail_FlowersFireLily").AfterBurn, Is.True, "fire lilies only on the burnt veld");
			Assert.That(e.LookOf("Detail_GrassThatch").AfterBurn, Is.False);
			Assert.That(e.Rules().Any(r => r.rule.Zone.On && r.rule.Name == "Trees: Jackalberry"), Is.True, "jackalberry on the termite islands");
			string[] retired = { "Detail_GrassDry", "Detail_ShrubDry", "Detail_Euphorbia", "Detail_GrassSavanna", "Tree_Acacia", "Tree_Baobab", "Detail_Bush_Thornbush" };
			Assert.That(e.Rules().SelectMany(r => r.rule.Prefabs).Intersect(retired), Is.Empty);
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Reefgold, BiomeCaveCrystalKind.Emerald, BiomeCaveCrystalKind.Rainglass }));
			Assert.That(e.GlowModes().Any(g => g.Source == SceneGlowSource.Crystal && g.Mode == SceneGlowMode.Rain), Is.True);
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Termitecast));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Grassland | BiomeHabitat.Scrub | BiomeHabitat.Settlement | BiomeHabitat.Ruins));
			Assert.That((int)e.Habitat, Is.EqualTo(278568));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Dry));
			Assert.That(e.VineGrowth().Species.Select(v => v.Species), Is.EqualTo(new[] { "RockFigRoots", "Gloriosa" }));
			Assert.That(e.GatheringResources(), Does.Contain("Reef Gold").And.Not.Contain("Gold Vein").And.Not.Contain("Sunpetal Patch").And.Not.Contain("Elder Ironbark"));
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Is.EqualTo(new[] { "Detail_GrassPearlMillet", "Detail_Bambara" }));
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Village], Is.EqualTo(1.6f));
			foreach (POIType none in new[] { POIType.Port, POIType.Lighthouse, POIType.Temple, POIType.Monastery, POIType.StoneCircle, POIType.Barrow,
				POIType.Ossuary, POIType.Oasis, POIType.Watermill, POIType.LumberCamp, POIType.SunkenShip })
			{
				Assert.That(kinds.ContainsKey(none), Is.False, $"Savanna hosts no {none}");
			}
			Assert.That(e.SiteVariants().Any(sv => sv.Kind == POIType.Village && sv.Variant == "Yurts" && sv.Weight > 0f), Is.True);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Town, out BiomeWayLight t) && t.Light == BiomeWayLightKind.SoapstoneBowl, Is.True);
			Assert.That(e.Description, Does.StartWith("Head-high golden grass"));
		}
	
		/// <summary>Round G1 (2026-10-26): the primeval beech-and-fir forest on its own mossy sandstone, the fey in its rare corners.</summary>
		[Test]
		public void Forest_IsThePrimevalBeechAndFirWood()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Forest");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Sandstone"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Sandstone", "Shale", "Limestone", "Schist", "Gneiss" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.ForestFloorBeech));
			Assert.That(e.Details.Select(d => d.Family), Is.EqualTo(new[] { Ground.MossCushion, Ground.ForestFloorRamsons, Ground.NeedleLitterFir, Ground.SoilRootHumus,
				Ground.SandstoneForest }));
			Assert.That(e.Details[1].Height, Is.EqualTo(new Vector2(0.45f, 0.52f)), "the ramsons are low and flat");
			var trees = e.Rules().Where(r => r.rule.Channel == PrefabSpawnChannel.TreeInstance).SelectMany(r => r.rule.Prefabs).ToList();
			foreach (string species in new[] { "BeechForest", "OakForest", "Lime", "Hornbeam", "SilverFir", "Yew", "Ash" })
			{
				Assert.That(trees, Does.Contain("Tree_" + species), species);
			}
			Assert.That(trees, Does.Not.Contain("Tree_Spruce").And.Not.Contain("Tree_Birch"), "Taiga's trees went back to Taiga");
			var all = e.Rules().SelectMany(r => r.rule.Prefabs).ToList();
			foreach (string gone in new[] { "Detail_Bush_Rhododendron", "Detail_Bush_Laurel", "Detail_Bush_Bilberry", "Detail_DebrisNeedle", "Detail_ShrubSmall" })
			{
				Assert.That(all, Does.Not.Contain(gone), gone);
			}
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Forest));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Forest | BiomeHabitat.FreshWater));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.GreenFluorite, BiomeCaveCrystalKind.Wyrdstone }));
			Assert.That(e.Weathering().Finish, Is.EqualTo(BiomeStructureFinish.Mossy));
			Assert.That(e.Weathering().Chance, Is.EqualTo(0.35f));
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Town], Is.EqualTo(0.6f));
			Assert.That(kinds[POIType.City], Is.EqualTo(0.3f));
			Assert.That(kinds[POIType.Capital], Is.EqualTo(0.1f));
			Assert.That(kinds[POIType.HuntingLodge], Is.EqualTo(2.2f));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.TorchPost, Is.True);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.FeyRing, out BiomeWayLight f) && f.Light == BiomeWayLightKind.Glowjar, Is.True);
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Is.EqualTo(new[] { "Detail_GrassOats", "Detail_GrassSpelt", "Detail_FlowersFieldBean" }));
			Assert.That(e.GatheringResources(), Does.Contain("Yew Heartwood").And.Contain("Foxfire Wood"));
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.VineGrowth().Species.Select(v2 => v2.Species), Is.EqualTo(new[] { "Ivy", "Honeysuckle" }));
		}

		/// <summary>Round G1 (2026-10-26): the managed lowland wood on chalk and flint — coppice, pasture, glades, hedges, tiles.</summary>
		[Test]
		public void Woodland_IsTheManagedLowlandWood()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Woodland");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Chalk"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Chalk", "Limestone", "Shale", "Conglomerate" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.GrassWoodPasture));
			Assert.That(e.Details.Length, Is.EqualTo(6));
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Temperate));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Woodland | BiomeHabitat.Grassland));
			var all = e.Rules().SelectMany(r => r.rule.Prefabs).ToList();
			foreach (string gone in new[] { "Detail_GrassLush", "Detail_FlowersMeadow", "Detail_Fern", "Detail_Ivy", "Detail_MushroomForest", "Detail_Bush_Holly" })
			{
				Assert.That(all, Does.Not.Contain(gone), gone);
			}
			Assert.That(all, Does.Contain("Detail_FlowersBluebell").And.Contain("Detail_Bush_HazelCoppice").And.Contain("Tree_SweetChestnut"));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.FlintGeode, BiomeCaveCrystalKind.Sunstone }));
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Village], Is.EqualTo(1.3f));
			Assert.That(kinds[POIType.Town], Is.EqualTo(0.9f));
			Assert.That(kinds[POIType.City], Is.EqualTo(0.5f));
			Assert.That(kinds[POIType.Capital], Is.EqualTo(0.3f));
			Assert.That(e.LinearLines().Count(l => l.Style.Kind == BiomeLinearKind.Hedge), Is.EqualTo(3), "laid hedges along clearings, lanes and fields");
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.OrchardEntries().Select(o => o.TreePrefab), Is.EqualTo(new[] { "Tree_CrabApple", "Tree_WildCherry" }));
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Does.Contain("Detail_GrassRye").And.Contain("Detail_FlowersWoad"));
			Assert.That(e.GatheringResources(), Does.Contain("King Oak").And.Not.Contain("Ancient Oak").And.Not.Contain("Morel Cluster"));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Graveyard, out BiomeWayLight g) && g.Light == BiomeWayLightKind.LanterneDesMorts, Is.True);
			Assert.That(e.Weathering().Chance, Is.EqualTo(0.12f));
			Assert.That(e.Description, Does.StartWith("Open lowland woodland"));
		}

		/// <summary>Round G7 (2026-10-26): the Saharan erg and reg — Nubian sandstone, the dune mix, three humidity reaches, no fields, drifted walls.</summary>
		[Test]
		public void Desert_IsTheSaharanErgAndReg()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Desert");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Sandstone"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Sandstone", "Quartzite", "Granite", "Basalt" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.SandErg));
			Assert.That(e.Details.Single(d => d.Family == Ground.SandRedSheet).Humidity, Is.EqualTo(new Vector2(-0.6f, 1f)), "the fringe");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "StarDune", "Seif", "TransverseDunes", "Barchans", "Nebkha", "Guelta", "Paleolake" }));
			// The seifs paint their gassi corridors (d2, slot 3), the palaeolake its marl (d4, slot 5).
			Assert.That(e.LandformList().Single(l => l.Kind == "Seif").PaintGround, Is.EqualTo(3));
			Assert.That(e.LandformList().Single(l => l.Kind == "Paleolake").PaintGround, Is.EqualTo(5));
			Assert.That(e.Details[2].Family, Is.EqualTo(Ground.GravelGassi));
			Assert.That(e.Details[4].Family, Is.EqualTo(Ground.ClayPaleolakeMarl));
			Assert.That(e.TerrainProcessName, Is.EqualTo("Erg"));
			Assert.That(TerrainProcessTable.TryGetProfile("Erg", out _), Is.True);
			string[] retired = { "Tree_Saguaro", "Tree_JoshuaTree", "Detail_Cholla", "Detail_CactusBarrel", "Detail_Bush_Creosote", "Detail_Bush_Ocotillo", "Detail_GrassDune" };
			Assert.That(e.Rules().SelectMany(r => r.rule.Prefabs).Intersect(retired), Is.Empty, "the American kit left Desert");
			Assert.That(e.Rules().Any(r => r.rule.Name == "FlowersSchouwia" && r.rule.Mode.Pattern == BiomeScatterPattern.RainCells), Is.True, "acheb in the rain cells");
			Assert.That(e.FieldCrops(), Is.Empty, "no fields");
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Drifted));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Desert | BiomeHabitat.Ruins));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Malachite, BiomeCaveCrystalKind.Sunglass }));
			Assert.That(e.GlowModes().Any(g => g.Source == SceneGlowSource.Crystal && g.Mode == SceneGlowMode.AfterSunset && g.Warm), Is.True, "sunglass glows warm after dusk");
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.PiercedJar, Is.True);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Camp, out _), Is.False, "the camps are unlit");
			Assert.That(e.StructurePalette().Surfaces.Any(s => s.Role == BiomeStructureRole.Roof && s.Pitch == BiomeRoofPitch.Flat), Is.True, "flat clay roofs");
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.GatheringResources(), Does.Contain("Sunglass Lump").And.Contain("Copper Lode").And.Not.Contain("Gold Vein").And.Not.Contain("Sunpetal Patch"));
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.DuneSea], Is.EqualTo(2f));
			foreach (POIType none in new[] { POIType.Port, POIType.Windmill, POIType.Watermill, POIType.LumberMill, POIType.FishingCamp, POIType.Lighthouse,
				POIType.SunkenShip, POIType.AbandonedFarm, POIType.Monastery })
			{
				Assert.That(kinds.ContainsKey(none), Is.False, $"Desert hosts no {none}");
			}
			Assert.That(e.Naming.Onsets, Does.Contain("Tin"));
			Assert.That(e.Description, Does.StartWith("The great sand sea"));
		}

		/// <summary>Round G7 (2026-10-26): the Cappadocian fairy-chimney country — tuff, the rose and red reaches, rills and caprock mesas, vines and apricots.</summary>
		[Test]
		public void Badlands_IsTheFairyChimneyCountry()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Badlands");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Tuff"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Tuff", "Basalt", "Andesite", "Pumice", "Obsidian", "Limestone" }));
			Assert.That(e.Details.Single(d => d.Family == Ground.TuffRilled).Temperature, Is.EqualTo(new Vector2(-1f, 0.45f)), "the rose reach");
			Assert.That(e.Details.Single(d => d.Family == Ground.TuffRedValley).Temperature, Is.EqualTo(new Vector2(0.45f, 1f)), "the red reach");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Rills", "CaprockMesa", "Wash", "CitadelCrag" }));
			Assert.That(e.LandformList().Single(l => l.Kind == "Rills").PaintGround, Is.EqualTo(1));
			Assert.That(e.LandformList().Single(l => l.Kind == "CaprockMesa").PaintGround, Is.EqualTo(5));
			Assert.That(e.LandformList().Single(l => l.Kind == "Wash").PaintGround, Is.EqualTo(2));
			Assert.That(e.Rules().Any(r => r.rule.Name == "Formations: Capped chimneys" && r.rule.Mode.Pattern == BiomeScatterPattern.Fields), Is.True, "chimney fields");
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Is.EqualTo(new[] { "Detail_CropBushVine", "Detail_CropPumpkin", "Detail_CropChickpea" }));
			Assert.That(e.OrchardEntries().Select(o => o.Tree), Is.EqualTo(new[] { "Apricot" }));
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.Variants.Select(v => v.Name), Is.EqualTo(new[] { "Frozen", "Scorched" }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Ashworn));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Desert | BiomeHabitat.Mountain | BiomeHabitat.Cave | BiomeHabitat.Ruins));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.FireOpal, BiomeCaveCrystalKind.Obsidian, BiomeCaveCrystalKind.Perilight }));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.CarvedLantern, Is.True);
			Assert.That(e.VineGrowth().Species.Select(s => s.Species), Is.EqualTo(new[] { "WildMadder", "WhiteBryony" }));
			string[] retired = { "Detail_Bush_Sagebrush", "Detail_Bush_Rabbitbrush", "Detail_Bush_Creosote", "Detail_PadCactus", "Tree_Pinyon" };
			Assert.That(e.Rules().SelectMany(r => r.rule.Prefabs).Intersect(retired), Is.Empty);
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Monastery], Is.EqualTo(1.6f));
			foreach (POIType none in new[] { POIType.Port, POIType.Windmill, POIType.Watermill, POIType.LumberMill, POIType.LumberCamp, POIType.FishingCamp })
			{
				Assert.That(kinds.ContainsKey(none), Is.False, $"Badlands hosts no {none}");
			}
			Assert.That(e.Naming.Onsets, Does.Contain("Zel"));
			Assert.That(e.Description, Does.StartWith("Fairy-chimney badlands"));
		}

		/// <summary>Round G7 (2026-10-26): the Afar salt plain — Saltrock, the pan and its ridges, the cold band, brine water, salt rafts, no fields.</summary>
		[Test]
		public void SaltFlat_IsTheAfarSaltPlain()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Salt Flat");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Saltrock"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Saltrock", "Halite", "Gypsum", "Basalt", "Travertine" }));
			Assert.That(e.WaterRole, Is.EqualTo(BiomeWaterRole.SaltFlat));
			Assert.That(e.Details.Single(d => d.Family == Ground.SaltAlkaliPlaya).Temperature, Is.EqualTo(new Vector2(-1f, 0.25f)), "the cold band");
			Assert.That(e.Details.Single(d => d.Family == Ground.SaltEtchedCrust).Temperature, Is.EqualTo(new Vector2(0.25f, 1f)), "etched crust off in the cold band");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "SaltPan", "SaltRidges", "BrineEye", "SaltDome", "SaltDomeDolines" }));
			Assert.That(e.LandformList().Single(l => l.Kind == "SaltRidges").PaintGround, Is.EqualTo(1));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Desert | BiomeHabitat.Barren | BiomeHabitat.Settlement | BiomeHabitat.Ruins));
			Assert.That((int)e.Habitat, Is.EqualTo(409728));
			Assert.That(e.FloatingPlants().Select(f => f.Kind), Is.EqualTo(new[] { BiomeFloatingPlantKind.SaltRaft }));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Halite, BiomeCaveCrystalKind.Sylvite, BiomeCaveCrystalKind.Embersalt }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Dewfluted));
			Assert.That(e.FieldCrops(), Is.Empty);
			Assert.That(e.VineGrowth().Species, Is.Empty);
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Town, out BiomeWayLight t) && t.Light == BiomeWayLightKind.SaltLamp, Is.True);
			// The wardenblooms grow only under breathable air (decision 41's world gate).
			Assert.That(e.Rules().Where(r => r.rule.Name == "FlowersWardenbloom").Select(r => r.rule.World.air),
				Is.All.EqualTo(BiomeAtmosphereRequirement.Standard | BiomeAtmosphereRequirement.Thick));
			Assert.That(e.LookOf("Detail_FlowersWardenbloom").Rigid, Is.True, "salt petals never sway");
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.SaltFlat], Is.EqualTo(2f));
			foreach (POIType none in new[] { POIType.Capital, POIType.Port, POIType.Castle, POIType.Lighthouse, POIType.Monastery, POIType.Temple, POIType.FishingCamp,
				POIType.LumberCamp, POIType.FeyRing, POIType.Windmill, POIType.Barrow, POIType.Volcano })
			{
				Assert.That(kinds.ContainsKey(none), Is.False, $"Salt Flat hosts no {none}");
			}
			Assert.That(e.SiteVariants().Any(sv => sv.Kind == POIType.Camp && sv.Variant == "Yurts" && sv.Weight > 1f), Is.True, "mat domes (yurt stand-in)");
			Assert.That(e.Naming.Onsets, Does.Contain("Dab"));
			Assert.That(e.Description, Does.StartWith("A dead lake of salt"));
		}
		/// <summary>Round G9 (2026-10-26): the Maldivian atoll shelf — Coralstone, painted and grown under the sea, faru and kandu, cowries, no fields.</summary>
		[Test]
		public void CoralReef_IsTheMaldivianAtoll()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Coral Reef");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Coralstone"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Limestone", "Basalt" }));
			Assert.That(e.SeaFloorKind, Is.EqualTo(BiomeSeaFloor.Ground));
			foreach (BiomeArtSpec.Layer layer in new[] { e.Main }.Concat(e.Details))
			{
				foreach (BiomeArtSpec.Scatter rule in layer.Scatter)
				{
					Assert.That(BiomeSeaFloors.GrowsUnderSea(e.SeaFloorKind, rule.Waters), Is.True, $"Coral Reef '{rule.Name}' grows under the sea");
				}
			}
			Assert.That(e.Details.Count(d => d.SeaDepth.HasValue), Is.GreaterThanOrEqualTo(5), "depth-banded grounds");
			Assert.That(e.Details.Single(d => d.Family == Ground.ReefBleached).Temperature, Is.EqualTo(new Vector2(0.9f, 1f)), "the hot band bleaches");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Faru", "BlueHole", "Kandu" }));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Apatite, BiomeCaveCrystalKind.Lagoonglass }));
			Assert.That(e.GatheringResources(), Is.EqualTo(new[] { "Pearl Oyster Bed", "Hirigaa Block", "Money Cowrie", "Black Coral", "Apatite", "Lagoonglass" }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Reefgrown));
			Assert.That(e.GlowModes().Select(g => (g.Source, g.Mode)),
				Is.EquivalentTo(new[] { (SceneGlowSource.Crystal, SceneGlowMode.Caustic), (SceneGlowSource.Way, SceneGlowMode.Caustic) }));
			Assert.That(e.GrassTypes(), Is.Empty);
			Assert.That(e.FieldCrops(), Is.Empty, "wheat removed");
			Assert.That(e.VineGrowth().Species, Is.Empty);
			Assert.That(e.GroveHerbs(), Is.Empty);
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Wet));
			Assert.That(e.SeaLook().Visibility, Is.EqualTo(35f));
			Assert.That(e.CaveShellFor(CaveForm.SeaCave), Is.EqualTo("Coralstone"));
			var kinds = e.SiteKinds().Select(k => k.Kind).ToList();
			Assert.That(kinds.Contains(POIType.SunkenCity) && kinds.Contains(POIType.FishingCamp) && kinds.Contains(POIType.Island), Is.True);
			foreach (POIType land in new[] { POIType.Spring, POIType.Waterfall, POIType.Rapids, POIType.River, POIType.Lake, POIType.Delta, POIType.Peak,
				POIType.Pass, POIType.Mesa, POIType.Butte, POIType.Gorge, POIType.Sinkhole, POIType.Village, POIType.Town })
			{
				Assert.That(kinds.Contains(land), Is.False, $"Coral Reef hosts no {land}");
			}
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.FishingCamp, out BiomeWayLight camp) && camp.Light == BiomeWayLightKind.NicheLamp, Is.True);
			Assert.That(e.Description, Does.StartWith("Warm atoll shallows"));
			Assert.That(e.Naming.Onsets, Does.Contain("Dhi"));
		}

		/// <summary>Round G9 (2026-10-26): the Baltic amber strand — Beachrock, the tide-banded strand, two vegetation bands, dune landforms, no fields.</summary>
		[Test]
		public void Beach_IsTheAmberStrand()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Beach");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Beachrock"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Granite", "Gneiss" }));
			Assert.That(e.SeaFloorKind, Is.EqualTo(BiomeSeaFloor.Shore), "a land biome: its bed layers carry the surf");
			Assert.That(e.Details.Single(d => d.Family == Ground.LichenGreyDune).Temperature, Is.EqualTo(new Vector2(-1f, 0.45f)), "the cool grey dune");
			Assert.That(e.Details.Single(d => d.Family == Ground.SandWarmBackdune).Temperature, Is.EqualTo(new Vector2(0.45f, 1f)), "the warm backdune");
			Assert.That(e.Details.Single(d => d.Family == Ground.SandSwash).SeaDepth.HasValue, Is.True, "the swash reads the tide");
			Assert.That(e.Details.Single(d => d.Family == Ground.BeachrockPavement).SeaDepth.HasValue, Is.True, "beachrock reads the tide");
			Assert.That(e.BedLayers.Select(b => b.Layer.Family), Is.EqualTo(new[] { Ground.SandTrough, Ground.SiltOffshore }));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Foredune", "ParabolicDune", "Blowout" }));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Aragonite, BiomeCaveCrystalKind.Pearlspar }));
			Assert.That(e.GatheringResources(), Is.EqualTo(new[] { "Strand Amber", "Sea Holly Root", "Marram Bundle", "Drift Timber", "Sea Grape", "Pearlspar" }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Sandblasted));
			Assert.That(e.FieldCrops(), Is.Empty, "wheat removed");
			Assert.That(e.VineGrowth().Species.Select(v => v.Species), Is.EqualTo(new[] { "SeaBindweed" }));
			Assert.That(e.LinearLines().Select(l => l.Unknown), Is.All.Empty);
			Assert.That(e.Rules().Any(r => r.rule.Name == "GrassSeaOats" && r.rule.Temperature == new Vector2(0.45f, 1f)), Is.True, "sea-oats only on the warm strand");
			Assert.That(e.Rules().Any(r => r.rule.Prefabs.Any(p => p.Contains("StrandCoconut")) && r.rule.Temperature == new Vector2(0.65f, 1f)), Is.True, "coconut palms from t 0.65");
			Assert.That(e.Rules().Any(r => r.rule.Name == "GrassDune" || r.rule.Name == "Pandanus"), Is.False, "the generic dune kit retired");
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.FishingCamp], Is.EqualTo(2f));
			foreach (POIType none in new[] { POIType.City, POIType.Quarry, POIType.LumberCamp, POIType.BogShrine, POIType.StiltVillage, POIType.WitchHut })
			{
				Assert.That(kinds.ContainsKey(none) && kinds[none] > 0f, Is.False, $"Beach hosts no {none}");
			}
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.AmberLamp, Is.True);
			Assert.That(e.SeaLook().Visibility, Is.EqualTo(9f));
			Assert.That(e.Description, Does.StartWith("Pale amber strand"));
			Assert.That(e.Naming.Onsets, Does.Contain("Nid"));
		}

		/// <summary>Round G9 (2026-10-26): the hadal trench — Peridotite, painted and grown under the sea, seeps, no light, no grass, moat wet.</summary>
		[Test]
		public void Abyss_IsTheHadalTrench()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Abyss");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Peridotite"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Basalt", "Serpentinite" }));
			Assert.That(e.SeaFloorKind, Is.EqualTo(BiomeSeaFloor.Ground));
			foreach (BiomeArtSpec.Layer layer in new[] { e.Main }.Concat(e.Details))
			{
				foreach (BiomeArtSpec.Scatter rule in layer.Scatter)
				{
					Assert.That(BiomeSeaFloors.GrowsUnderSea(e.SeaFloorKind, rule.Waters), Is.True, $"Abyss '{rule.Name}' grows under the sea");
				}
			}
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Wet), "hadal water never freezes");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "MudVolcano", "BrineLake" }));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.FireIce, BiomeCaveCrystalKind.Hadalite }));
			Assert.That(e.GatheringResources(), Is.EqualTo(new[] { "Seep Mussel Bed", "Fire-Ice Crust", "Leviathan Bone", "Lure Bulb", "Hadalite" }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Encrusted));
			Assert.That(e.GrassTypes(), Is.Empty, "no light, no photosynthesis");
			Assert.That(e.FieldCrops(), Is.Empty);
			Assert.That(e.VineGrowth().Species, Is.Empty);
			Assert.That(e.GroveHerbs(), Is.Empty);
			Assert.That(e.UnderwaterSiteDepth.x, Is.EqualTo(200f));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.SunkenCity, out BiomeWayLight l) && l.Light == BiomeWayLightKind.Lurelight, Is.True);
			var kinds = e.SiteKinds().Select(k => k.Kind).ToList();
			Assert.That(kinds.Contains(POIType.Gorge) && kinds.Contains(POIType.SunkenShip) && kinds.Contains(POIType.SeaCrystalFormation), Is.True);
			foreach (POIType none in new[] { POIType.Island, POIType.Bay, POIType.Headland, POIType.River, POIType.Peak, POIType.Village })
			{
				Assert.That(kinds.Contains(none), Is.False, $"Abyss hosts no {none}");
			}
			Assert.That(e.SeaLook().Visibility, Is.EqualTo(40f));
			Assert.That(e.Description, Does.StartWith("The hadal trenches"));
			Assert.That(e.Naming.Onsets, Does.Contain("Grund"));
		}

		/// <summary>Round G6 (2026-10-26): the Pannonian puszta — loess, black earth, soda pans, goldbeard, the kunhalom, reed and whitewash.</summary>
		[Test]
		public void Grassland_IsThePannonianPuszta()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Grassland");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Loess"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Loess", "Limestone", "Shale", "Basalt" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.GrassPusztaSward));
			Assert.That(e.Details.Select(d => d.Family), Does.Contain(Ground.SaltCrustSzik).And.Contain(Ground.LoessWall));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Kunhalom" }));
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Arid));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Grassland | BiomeHabitat.FreshWater));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Wet));
			Assert.That(e.Rules().Any(r => r.rule.Name == "GrassLush" || r.rule.Name == "Bush: Gorse" || r.rule.Name == "Trees: Oak"), Is.False, "the shared content is gone");
			Assert.That(e.Rules().Any(r => r.rule.Name == "Trees: WildPear") && e.Rules().Any(r => r.rule.Name == "Trees: TurkeyOak"), Is.True);
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Is.EqualTo(new[] { "Detail_GrassMillet", "Detail_CropHemp", "Detail_CropMelon", "Detail_GrassWheat" }));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Dogtooth, BiomeCaveCrystalKind.Skyglass }));
			Assert.That(e.GatheringResources(), Does.Contain("Chamomile Patch").And.Not.Contain("Silverleaf Patch").And.Not.Contain("Ancient Oak"));
			Assert.That(e.Weathering().Finish, Is.EqualTo(BiomeStructureFinish.None));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.CrookLantern, Is.True);
			Assert.That(e.Description, Does.StartWith("Meadow-steppe on black earth"));
		}

		/// <summary>Round G6 (2026-10-26): the High Plains prairie — red beds, wallows and playas, cottonwoods, earth lodges, dry moats.</summary>
		[Test]
		public void Plains_IsTheMixedGrassPrairie()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Plains");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Redbed"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Redbed", "Shale", "Limestone", "Sandstone", "Quartzite" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.GrassDryMixedPrairie));
			Assert.That(e.Details[3].Family, Is.EqualTo(Ground.SoilBuffaloWallow), "the Wallow preset paints ground 4");
			Assert.That(e.Details[4].Family, Is.EqualTo(Ground.ClayPlayaFloor), "the Playa preset paints ground 5");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Wallow", "Playa", "PlatformMound", "ConicalMound" }));
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Arid));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Dry));
			Assert.That(e.Rules().Any(r => r.rule.Name == "Trees: PlainsCottonwood"), Is.True);
			Assert.That(e.Rules().Any(r => r.rule.Name == "Trees: Oak" || r.rule.Name == "GrassFeather" || r.rule.Name == "Bush: Rabbitbrush"), Is.False);
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.BariteRose, BiomeCaveCrystalKind.SatinSpar, BiomeCaveCrystalKind.Sunspoke }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Dusted));
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Village], Is.EqualTo(1.6f));
			Assert.That(kinds.ContainsKey(POIType.Windmill) || kinds.ContainsKey(POIType.Watermill) || kinds.ContainsKey(POIType.LumberMill), Is.False, "no mills");
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Camp, out _), Is.False, "camps unlit");
			Assert.That(e.Description, Does.StartWith("Mixed-grass plains"));
		}

		/// <summary>Round G6 (2026-10-26): the Sary-Arka steppe — porphyry sopkas, sors, takyrs and bobak mounds, yurts, no vines, burns.</summary>
		[Test]
		public void Steppe_IsTheSaryArka()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Steppe");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Porphyry"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Porphyry", "Granite", "Sandstone" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.GrassDryKovylSward));
			Assert.That(e.Details[2].Family, Is.EqualTo(Ground.ClayTakyr));
			Assert.That(e.Details[3].Family, Is.EqualTo(Ground.SaltCrustSor));
			Assert.That(e.Details[4].Family, Is.EqualTo(Ground.GravelPorphyryGrit), "the Sopka preset paints ground 5");
			Assert.That(e.Details[6].Family, Is.EqualTo(Ground.SoilBobakMound), "the BobakMounds preset paints ground 7");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Sopka", "BobakMounds", "ObaChain", "SaltPan", "Playa", "Wash" }));
			Assert.That(e.Rules().Any(r => r.rule.Name == "Bush: Sagebrush" || r.rule.Name == "Bush: Rabbitbrush" || r.rule.Name == "Bush: Juniper"), Is.False,
				"High Desert's sage is gone (decision 50)");
			Assert.That(e.VineGrowth().Species, Is.Empty);
			Assert.That(e.BurnRegime, Is.EqualTo(BiomeBurnRegime.Occasional));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Grassland | BiomeHabitat.Scrub));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Burandrift));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Dioptase, BiomeCaveCrystalKind.WolfsEye }));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Camp, out BiomeWayLight v) && v.Light == BiomeWayLightKind.KazanFire, Is.True);
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.Camp], Is.EqualTo(1.8f));
			Assert.That(kinds.ContainsKey(POIType.Castle) || kinds.ContainsKey(POIType.Windmill), Is.False);
			Assert.That(e.Naming.Onsets, Does.Contain("Sary"));
		}

		/// <summary>Round G6 (2026-10-26): terraced schist hill country — terraces and orchards, drystone walls, three climate reaches.</summary>
		[Test]
		public void Hills_IsTheTerracedSchistCountry()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Hills");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Schist"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Schist", "Slate", "Quartzite", "Limestone", "Granite" }));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.GrassHillPasture));
			Assert.That(e.Details.Any(d => d.Family == Ground.GrassFellTurf && d.Temperature.HasValue), Is.True, "the cold fells' own banded ground");
			Assert.That(e.Rules().Any(r => r.rule.Name.Contains("Granite") || r.rule.Name == "Bush: Gorse" || r.rule.Name == "Bush: Heather"), Is.False);
			Assert.That(e.OrchardEntries().Select(o => o.Tree), Is.EqualTo(new[] { "OliveTerrace", "Almond" }));
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Arid));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Scrub | BiomeHabitat.Grassland | BiomeHabitat.Mountain));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Dry));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Lichen));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Garnet, BiomeCaveCrystalKind.Staurolite,
				BiomeCaveCrystalKind.ClearQuartz, BiomeCaveCrystalKind.Heartgarnet }));
			Assert.That(e.GatheringResources(), Does.Contain("Gold Vein").And.Contain("Saffron Crocus Patch").And.Not.Contain("Ancient Oak"));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.Alminha, Is.True);
			Assert.That(e.Description, Does.StartWith("Terraced schist hills"));
		}

		/// <summary>Round G8 (2026-10-26): the Frisian Wadden — tide-banded saltings, a treeless coast, till and terps, tide four times as strong.</summary>
		[Test]
		public void Estuary_IsTheFrisianWadden()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Estuary");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Till"));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.MudSchlickFlat));
			Assert.That(e.Details[6].Family, Is.EqualTo(Ground.SiltCreekBank), "the TidalCreeks preset paints ground 7");
			Assert.That(e.Details.Count(d => d.SeaDepth.HasValue && d.SeaDepthInTides), Is.GreaterThanOrEqualTo(5), "zoned by the tide");
			Assert.That(e.SeaFloorKind, Is.EqualTo(BiomeSeaFloor.ToLowWater));
			Assert.That(e.TideStrength, Is.EqualTo(4f));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "TidalCreeks", "Terp", "Warft", "SeaDike", "SaltPannes" }));
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Coastal));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Wetland | BiomeHabitat.Coast));
			Assert.That(e.Rules().Any(r => r.rule.Name == "Reeds" || r.rule.Name == "Bush: Willow" || r.rule.Name == "Trees: Alder"), Is.False, "no reed, willow or alder on the saltings");
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Vivianite, BiomeCaveCrystalKind.Ebbspar }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Saltbloom));
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Is.EqualTo(new[] { "Detail_CropFlax", "Detail_CropCamelina", "Detail_CropMadder" }));
			Assert.That(e.GatheringResources(), Does.Contain("Glasswort").And.Contain("Ebbspar").And.Not.Contain("Samphire Patch"));
			Assert.That(e.Description, Does.StartWith("Tidal marsh"));
			Assert.That(e.Naming.Onsets, Does.Contain("Rung"));
		}

		/// <summary>Round G8 (2026-10-26): the Batiha reed marsh — giant reed, shell marl, tells and dibin islands, reed boardwalks, two reaches.</summary>
		[Test]
		public void Wetlands_IsTheBatihaReedMarsh()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Wetlands");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Limestone"), "gypsum is formations-only: the marl limestone carries the bluffs");
			Assert.That(e.Rocks, Does.Contain("Gypsum"));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.MudReedbedSilt));
			Assert.That(e.Details[4].Family, Is.EqualTo(Ground.SandDibinSilt), "the Tell preset paints ground 5");
			Assert.That(e.Details.Any(d => d.Family == Ground.SaltSabkhaLevee && d.Temperature.HasValue), Is.True, "the warm reach's sabkha");
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Tell", "Dibin" }));
			Assert.That(e.Boardwalks, Is.EqualTo(BiomeBoardwalks.Paths | BiomeBoardwalks.Streets));
			Assert.That(e.Rules().Any(r => r.rule.Name == "Reeds" || r.rule.Name == "GrassTall" || r.rule.Name == "Trees: Birch, Alder"), Is.False);
			Assert.That(e.FloatingPlants().Select(f => f.Kind), Does.Contain(BiomeFloatingPlantKind.Azolla));
			Assert.That(e.FloatingPlants().Select(f => f.Kind), Does.Contain(BiomeFloatingPlantKind.WaterSoldier));
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Wetland));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Wetland | BiomeHabitat.FreshWater));
			Assert.That(e.SeaLifeKinds(), Is.Empty, "inland fresh water");
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Carnelian, BiomeCaveCrystalKind.Selenite, BiomeCaveCrystalKind.Abzulite }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Silted));
			Assert.That(e.GatheringResources(), Does.Contain("Qasab Sheaf").And.Not.Contain("Mirewort Patch"));
			Assert.That(BiomeWayLights.TryFor(e.WayLights(), POIType.Village, out BiomeWayLight v) && v.Light == BiomeWayLightKind.NaphthaLamp, Is.True);
			Assert.That(e.Description, Does.StartWith("Open reed marsh"));
		}

		/// <summary>Round G8 (2026-10-26): the Swahili mangal — tide-zoned stands, coral rag, pole-walks, tide three times as strong, jungle look.</summary>
		[Test]
		public void Mangrove_IsTheSwahiliMangal()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Mangrove");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("CoralRag"));
			Assert.That(e.Main.Family, Is.EqualTo(Ground.MudMkokoBrown));
			Assert.That(e.Details[3].Family, Is.EqualTo(Ground.SaltUkandaCrust), "the Ukanda preset paints ground 4");
			Assert.That(e.Details[4].Family, Is.EqualTo(Ground.RagPavement), "the RagTerrace preset paints ground 5");
			Assert.That(e.Details[6].Family, Is.EqualTo(Ground.MudCreekBankMangal), "the MangroveCreeks preset paints ground 7");
			Assert.That(e.SeaFloorKind, Is.EqualTo(BiomeSeaFloor.ToLowWater));
			Assert.That(e.TideStrength, Is.EqualTo(3f));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "MangroveCreeks", "MudIslands", "Ukanda", "RagTerrace", "RagDolines" }));
			Assert.That(e.LookClass, Is.EqualTo(BiomeLookClass.Jungle));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Jungle | BiomeHabitat.Wetland | BiomeHabitat.Coast));
			Assert.That(e.Rules().Any(r => r.rule.Name == "Reeds" || r.rule.Name == "Trees: Nipa" || r.rule.Name == "Bush: Hibiscus"), Is.False);
			Assert.That(e.Rules().Any(r => r.rule.Name == "Trees: Mkoko"), Is.True);
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Marcasite, BiomeCaveCrystalKind.Nuru }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Tideline));
			Assert.That(e.VineGrowth().Species.Select(s => s.Species), Does.Contain("Derris").And.Not.Contain("Liana"));
			Assert.That(e.GatheringResources(), Does.Contain("Boriti Poles").And.Not.Contain("Bloodroot Patch"));
			Assert.That(e.Description, Does.StartWith("Tidal forest"));
			Assert.That(e.Naming.Onsets, Does.Contain("Mto"));
		}

		/// <summary>Lake (round G10): molasse strand and shelf, depth-zoned beds, no trees, pile-folk sites.</summary>
		[Test]
		public void Lake_IsTheClearAlpineLake()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Lake");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Molasse"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Limestone", "Gneiss" }));
			Assert.That(e.Lakebed, Is.EqualTo(Ground.LimestoneFurrowShelf));
			Assert.That(e.BedLayers.Select(b => b.Layer.Family), Is.EqualTo(new[] { Ground.SiltPondweedHalde, Ground.MudVarvedGyttja }));
			Assert.That(e.BedLayers.Select(b => b.Depth), Is.EqualTo(new[] { new Vector2(3f, 9f), new Vector2(9f, 0f) }));
			Assert.That(e.Rules().Any(r => r.rule.Name.StartsWith("Trees")), Is.False, "Lake grows no trees");
			Assert.That(e.Rules().Any(r => r.rule.Name == "Reeds" || r.rule.Name == "GrassLush" || r.rule.Name == "Driftwood"), Is.False);
			Assert.That(e.Details.Single(d => d.Family == Ground.GravelPushRampart).Temperature, Is.EqualTo(new Vector2(-1f, -0.2f)));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.CavePearl, BiomeCaveCrystalKind.Nixglass }));
			Assert.That(e.GatheringResources(), Is.EqualTo(new[] { "Lake Ore", "Lake Ball", "Spongilla", "Bulrush Bundle", "Nixglass" }));
			Assert.That(e.FieldCrops(), Is.Empty);
			var kinds = e.SiteKinds().Select(k => k.Kind).ToList();
			Assert.That(kinds, Has.Member(POIType.StiltVillage).And.Member(POIType.DrownedVillage).And.Not.Member(POIType.Village));
			Assert.That(e.Weathering().Finish, Is.EqualTo(BiomeStructureFinish.Algae));
			Assert.That(e.Description, Does.StartWith("Clear cold lake"));
			Assert.That(e.Naming.Onsets, Does.Contain("Atter"));
		}

		/// <summary>River (round G10): greywacke, current-banded beds, one tree (the crack willow), river-folk sites.</summary>
		[Test]
		public void River_IsTheRhenishRiver()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("River");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Greywacke"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Slate", "Quartzite", "Basalt" }));
			Assert.That(e.Riverbed, Is.EqualTo(Ground.GravelRhineRun));
			Assert.That(e.BedLayers.Count, Is.EqualTo(3));
			Assert.That(e.BedLayers.Single(b => b.Layer.Family == Ground.GreywackeReefRibs).Current, Is.EqualTo(new Vector2(1.6f, 0f)));
			Assert.That(e.Rules().Where(r => r.rule.Name.StartsWith("Trees")).Select(r => r.rule.Name).Distinct(), Is.EqualTo(new[] { "Trees: CrackWillow" }));
			Assert.That(e.Rules().Any(r => r.rule.Name == "Reeds" || r.rule.Name == "GrassLush" || r.rule.Name == "Log jams"), Is.False);
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.FreshWater));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Agate, BiomeCaveCrystalKind.RhinegoldSeam }));
			Assert.That(e.GatheringResources(), Does.Contain("River Gold").And.Not.Contain("Gold Vein").And.Not.Contain("Pearl Oyster Bed"));
			Assert.That(e.FieldCrops(), Is.Empty);
			var kinds = e.SiteKinds().Select(k => k.Kind).ToList();
			Assert.That(kinds, Has.Member(POIType.Watermill).And.Member(POIType.Keep).And.Not.Member(POIType.StiltVillage));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Tarred));
			Assert.That(e.Description, Does.StartWith("Strong grey-green river"));
			Assert.That(e.Naming.Onsets, Does.Contain("Kaub"));
		}

		/// <summary>Jungle (round G11): laterite and granite inselbergs, bronze litter, a canopy of iroko and mahogany, earth-style compounds.</summary>
		[Test]
		public void Jungle_IsTheEdoYorubaRainforest()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Jungle");
			Assert.That(e.Main.Family, Is.EqualTo(Ground.JungleFloorBronze));
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Laterite"));
			Assert.That(e.Rocks, Does.Contain("Granite").And.Contain("Gneiss").And.Not.Contain("Basalt"));
			Assert.That(e.Rules().Select(r => r.rule.Name).Distinct(), Does.Contain("Trees: Iroko").And.Not.Contain("Trees: Kapok").And.Not.Contain("Trees: Jungle"));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.WatermelonTourmaline, BiomeCaveCrystalKind.Leafglass }));
			Assert.That(e.CrossingStyle, Is.EqualTo(BiomeCrossingStyle.Liana));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Dry));
			Assert.That(e.GatheringResources(), Does.Contain("Kola Nuts").And.Not.Contain("Bloodroot Patch"));
			Assert.That(e.FieldCrops().Select(c => c.Prefab), Does.Contain("Detail_CropYam"));
			Assert.That(e.Description, Does.StartWith("Living lowland rainforest"));
			Assert.That(e.Naming.Onsets, Does.Contain("Ob"));
		}

		/// <summary>Bamboo Forest (round G11): andesite hills, running-bamboo stand-ins, terraced paddy and tea, Japanese-register naming.</summary>
		[Test]
		public void BambooForest_IsTheHeianBambooHills()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Bamboo Forest");
			Assert.That(e.Main.Family, Is.EqualTo(Ground.ForestFloorBambooGrove));
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Andesite"));
			Assert.That(e.Rocks, Does.Contain("Tuff").And.Not.Contain("Basalt"));
			Assert.That(e.Rules().Select(r => r.rule.Name).Distinct(), Does.Contain("Trees: Moso").And.Not.Contain("Trees: Bamboo").And.Not.Contain("Trees: TreeFern"));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Twinquartz, BiomeCaveCrystalKind.Jadeite, BiomeCaveCrystalKind.Hotarubi }));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Dry));
			Assert.That(e.Habitat, Is.EqualTo(BiomeHabitat.Forest | BiomeHabitat.Mountain | BiomeHabitat.FreshWater | BiomeHabitat.Settlement));
			Assert.That(e.GatheringResources(), Does.Contain("Takenoko Shoot").And.Not.Contain("Morel Cluster"));
			Assert.That(e.Description, Does.StartWith("Running bamboo groves"));
			Assert.That(e.Naming.Onsets, Does.Contain("Shi"));
		}
		/// <summary>
		/// Rocky Terrain (round G12): the shattered high cold desert of the Pamir — hornfels its own rock, the pamir and comb landforms, and,
		/// on the thin-aired and airless worlds that carry it (Belron 1, Ithaewyn 1), bare frost-shattered rock with nobody living there.
		/// </summary>
		[Test]
		public void RockyTerrain_IsThePamir_AndBareOffTheBreathableAir()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Rocky Terrain");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Hornfels"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Hornfels", "Marble", "Granite" }));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "Pamir", "ShatterComb", "AlluvialFan", "RockGlacier", "SaltPan" }));
			Assert.That(e.Details[e.LandformList()[0].PaintGround - 1].Family, Is.EqualTo(Ground.GrassDrySazTurf), "the pamir floor");
			// The air bands: the main ground on living air only, the rust-lee (Thin) and zap-shattered (Airless) rock in its place elsewhere.
			Assert.That(e.Main.AirBand, Is.EqualTo(BiomeAtmosphereRequirement.Living));
			Assert.That(e.Details.Single(d => d.AirBand == BiomeAtmosphereRequirement.Thin).Family, Is.EqualTo(Ground.RockRustLeeHornstone));
			Assert.That(e.Details.Single(d => d.AirBand == BiomeAtmosphereRequirement.Airless).Family, Is.EqualTo(Ground.RockZapShatter));
			// Every plant is gated to the living air (decisions 7, 41), the rigid mineral wool to the thin and airless worlds.
			foreach ((BiomeArtSpec.Layer layer, BiomeArtSpec.Scatter rule) in e.Rules())
			{
				if (rule.Prefabs.Any(p => p.StartsWith("Detail_Grass", System.StringComparison.Ordinal) || p.StartsWith("Detail_Flowers", System.StringComparison.Ordinal)
					|| p.StartsWith("Detail_Lichen", System.StringComparison.Ordinal) || p.StartsWith("Detail_Cushion", System.StringComparison.Ordinal)
					|| p.StartsWith("Detail_Herb", System.StringComparison.Ordinal)
					|| p.StartsWith(ProceduralArtCatalogue.BushPrefix, System.StringComparison.Ordinal) || p.StartsWith("Tree_", System.StringComparison.Ordinal)))
				{
					bool gated = !rule.World.IsOpen || layer.AirBand == BiomeAtmosphereRequirement.Living;
					Assert.That(gated, Is.True, $"{rule.Name} grows off the living air");
				}
			}
			Assert.That(e.Rules().Where(r => r.rule.Name == "GrassStonewool").Select(r => r.rule.World.IsOpen), Is.All.False);
			// Nobody lives there without breathable air: every lived-in or worked kind needs it, the springs and lakes need water.
			var lived = new[] { POIType.Village, POIType.Town, POIType.City, POIType.Keep, POIType.Fortress, POIType.Castle, POIType.Tower, POIType.Waystation,
				POIType.TradingPost, POIType.Watermill, POIType.Camp, POIType.BanditCamp, POIType.HuntingLodge, POIType.Hermitage, POIType.Shrine,
				POIType.Quarry, POIType.Mine };
			Dictionary<POIType, BiomeSiteKind> sited = e.SiteKinds().ToDictionary(k => k.Kind);
			foreach (POIType kind in lived)
			{
				Assert.That(sited[kind].NeedsBreathableAir, Is.True, $"{kind} needs air");
			}
			// The natural kinds stay on the airless worlds (the draft: Belron 1 and Ithaewyn 1 keep their landmarks and fallen stars).
			foreach (POIType natural in new[] { POIType.Peak, POIType.Pass, POIType.Cave, POIType.Landmark, POIType.FallenStar, POIType.OreVein })
			{
				Assert.That(sited[natural].NeedsBreathableAir, Is.False, $"{natural} is natural");
			}
			Assert.That(sited[POIType.Spring].NeedsLiquidWater, Is.True);
			foreach (POIType none in new[] { POIType.Capital, POIType.Port, POIType.Lighthouse, POIType.Windmill, POIType.LumberMill, POIType.SunkenShip, POIType.Island })
			{
				Assert.That(sited.ContainsKey(none), Is.False, $"Rocky Terrain hosts no {none}");
			}
			// Off the breathable air nothing burns (decision 41): every flame lights a kind that is only sited where the air is breathable.
			foreach (BiomeWayLight light in e.WayLights())
			{
				if (!BiomeGlowLights.Glows(light.Light) && sited.TryGetValue(light.Kind, out BiomeSiteKind site))
				{
					Assert.That(site.NeedsBreathableAir, Is.True, $"{light.Kind}'s {light.Light} would burn off the breathable air");
				}
			}
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Lajvard, BiomeCaveCrystalKind.Shabchiragh }));
			Assert.That(e.CrossingStyle, Is.EqualTo(BiomeCrossingStyle.Cantilever));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Sunfrost));
			Assert.That(e.GeothermalShares.Value.Signature(), Is.EqualTo(new BiomeGeothermalShares(0f, 0.8f, 0.2f, 0f).Signature()), "warm springs, no geysers");
			Assert.That(e.VineGrowth().Species, Is.Empty);
			Assert.That(e.Naming.Onsets, Does.Contain("Shugh"));
		}

		/// <summary>Geyser Basin (round G12): the Icelandic hot highland — rhyolite its own rock, the sinter apron on the heat mask, the hot sapphire pools.</summary>
		[Test]
		public void GeyserBasin_IsTheHotHighland()
		{
			BiomeArtSpec.Entry e = BiomeArtSpec.For("Geyser Basin");
			Assert.That(CliffRocks.OwnRock(e), Is.EqualTo("Rhyolite"));
			Assert.That(e.Rocks, Is.EqualTo(new[] { "Rhyolite", "Obsidian", "Basalt", "Tuff" }));
			Assert.That(e.LandformList().Select(l => l.Kind), Is.EqualTo(new[] { "SinterShield", "ExplosionCrater", "PaintedHills" }));
			// The ground bands of the heat mask: the apron in the core, the halo in the ring, the tephra and fonn in the cold.
			Assert.That(e.Details.Single(d => d.Family == Ground.SinterGeyseriteApron).HeatBand, Is.EqualTo((Vector2?)new Vector2(0.65f, 1f)));
			Assert.That(e.Details.Single(d => d.Family == Ground.MossWarmHalo).HeatBand, Is.EqualTo((Vector2?)new Vector2(0.1f, 0.4f)));
			Assert.That(e.Details.Single(d => d.Family == Ground.AshTephraSheet).HeatBand, Is.EqualTo((Vector2?)new Vector2(0f, 0.1f)));
			Assert.That(e.Rules().Select(r => r.rule.Name), Has.None.StartsWith("Trees: ").And.None.Contains("Sinter cones").And.None.Contains("Sulphur crystals"));
			Assert.That(e.Emission, Is.EqualTo(("Steam", 0.5f)));
			Assert.That(e.WaterLook().Hot, Is.True);
			Assert.That(e.WaterLook().Turbidity, Is.EqualTo(0.2f));
			Assert.That(e.MoatWater, Is.EqualTo(BiomeMoatWater.Wet));
			Assert.That(e.CaveCrystals().Select(c => c.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.IcelandSpar, BiomeCaveCrystalKind.Stilbite, BiomeCaveCrystalKind.Solarsteinn }));
			Assert.That(e.Weathering().Coat, Is.EqualTo(BiomeStructureCoat.Sintered));
			var kinds = e.SiteKinds().ToDictionary(k => k.Kind, k => k.Weight);
			Assert.That(kinds[POIType.HotSpring], Is.EqualTo(2f));
			Assert.That(kinds.ContainsKey(POIType.City) || kinds.ContainsKey(POIType.Port) || kinds.ContainsKey(POIType.Castle) || kinds.ContainsKey(POIType.Windmill), Is.False);
			Assert.That(e.Naming.Onsets, Does.Contain("Hver"));
		}
	}
}
