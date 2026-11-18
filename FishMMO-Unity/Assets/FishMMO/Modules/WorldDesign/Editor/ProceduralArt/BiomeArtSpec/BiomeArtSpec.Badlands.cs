#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Badlands</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Badlands()
		{
			// The fairy-chimney country of Cappadocia in its Byzantine–Seljuk centuries (biome round, approved 2026-10-18, authored
			// 2026-10-26): a high pale plateau of soft volcanic tuff carved by rain into knife-ribbed rose-and-cream ravines, forests of
			// capped chimneys under dark basalt hats, honeycombed cones and table mesas; flat-floored washes of pink silt where bush-vines,
			// apricots, pumpkins and oleaster grow in tiny walled plots; dovecote cliffs whitewashed round their pigeon holes; a gorge with
			// a green stream under oriental planes. Its people live INSIDE the rock: houses, chapels and whole monasteries hollowed from the
			// cones and cliffs with built ashlar fronts and flat rolled-earth roofs, a pierced rock citadel over every town, cities beneath
			// the ground behind rolling millstone doors, stone-portalled hans on the road. Its fantasy is the peris': their green-gold light
			// in the cones at dusk, a wine-dark vintage, the White Mountain's ash-grey breath, an earth-dragon asleep in the red valley. Two
			// reaches by temperature (decision 29): ROSE (t < 0.45: cream and rose tuff, vines, apricot, gypsophila) and RED (t ≥ 0.45:
			// iron-red tuff, bare rills, oleaster and Christ's thorn); and by humidity WATERED (h ≥ −0.5: orchards, planes) against BARE.
			// LATER (I9 T1): every ground and object texture as Badlands' own variant (the rose-cream Tuff, the Ürgüp cream limestone);
			// until then the grounds are its own recipes and the barks, leaves and stones the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Winedark V(0.84, 0.62, 0.42) and Ashfall V(0.58, 0.08, 0.70).
			return E("Badlands", L(Ground.AshLapilliSteppe,
					// The plateau steppe: bulbous meadow-grass that browns by midsummer, beaded goatgrass, wild barley on the watered side.
					Grass("GrassBulbousPoa", 30f), Grass("GrassGoatgrass", 20f, 220), Band(Grass("GrassWildBarley", 4f, 180), -1f, 1f, -0.6f, 1f),
					// Gypsophila's white clouds (the signature), the spring flush of the rose reach, foxtail-lily spires, autumn's gold goblets.
					Plants("FlowersGypsophila", 5f), Band(Plants("FlowersSpringAsh", 8f, 200), -1f, 0.5f), Band(Plants("FlowersEremurus", 1.5f), -1f, 0.55f),
					Plants("FlowersSternbergia", 3f, 180),
					// Geven cushions everywhere, thrift on the crests, noaea on the bare side, gundelia thistles, salep orchids on the watered.
					Bush("Tragacanth", 3f), Plants("Acantholimon", 1f), Band(Plants("Noaea", 2f), -1f, 1f, -1f, -0.5f), Plants("HerbKenger", 1f),
					Band(Plants("HerbSalep", 0.3f), -1f, 1f, -0.5f, 1f),
					// Syrian rue on the disturbed ground round the villages, hollyhocks by the houses, sherds of the potters' country.
					Moded(Plants("HerbHarmal", 2f), BiomeScatterMode.NearSites("Village,Town,Monastery,Waystation", 100f)),
					Moded(Plants("FlowersHollyhock", 3f), BiomeScatterMode.NearSites("Village,Town,Monastery,Hermitage", 60f)),
					Moded(Plants("DebrisSherdsTerracotta", 3f, 128), BiomeScatterMode.NearSites("Village,Town,Ruins,AbandonedFarm", 150f, 0.02f)),
					// The oriental plane at every village fountain and han (the AncientTree's look), moss in its shade.
					// LATER (SCATTER-MODES near-water anchor): the gorge floors' planes within 25 m of water; the fountains stand in.
					Band(Moded(Trees(0.01f, 30f, 20f, "OrientalPlane"), BiomeScatterMode.NearSites("Village,Spring,Waystation,TradingPost", 30f)), -1f, 1f, -0.55f, 1f),
					Moded(Plants("MossPlaneShade", 1f), BiomeScatterMode.Nursed("OrientalPlane", 6f)),
					// The hollow stump of a fallen plane in the gorges.
					With(Moded(Logs("Hollow plane stumps", 0.002f, 60f, Wood("Stump_PlaneHollow")), BiomeScatterMode.NearSites("Gorge", 40f)), r => r.AlignToNormal = false),
					// Guano and feathers cast down at the cliff feet, thickest under the dovecotes. LATER (I7): the dovecote cliffs themselves.
					Moded(Plants("DebrisPigeon", 1f, 128), BiomeScatterMode.CliffFoot(8f)),
					Moded(Plants("DebrisPigeon", 6f, 128), BiomeScatterMode.NearSites("AbandonedFarm,Village,Monastery", 60f)),
					Plants("DebrisPumice", 2f, 128), Stones(Pebbles("Tuff"), 3f), Stones(Pebbles("Obsidian"), 0.4f)),
				Ds(
					// d0: the rilled tuff badland of the rose reach (the Rills landform paints it), wider on the bare side: capped chimney fields
					// in clusters of 20–60, honeycombed cones, knife ribs along the rills, thrift and gypsophila on the crests, golden-drops on
					// the bare tuff, hackberries rooted in the joints. Its paths are steps cut in the rock.
					D(Ground.TuffRilled, 90f, 2.5f, 22f, 60f, Moded(Outcrops("Capped chimneys", 0.04f, 14f, F(RockTypes.Tuff, "Capped"), 60f), BiomeScatterMode.ClusteredFields(180f, 0.4f)),
						Outcrops("Tuff cones", 0.008f, 45f, F(RockTypes.Tuff, "Cone"), 60f), Blocks("Tuff ribs", 0.01f, 30f, F(RockTypes.Tuff, "Rib"), 60f),
						Plants("Acantholimon", 3f, 160, 60f), Plants("FlowersGypsophila", 3f, 160, 60f), Plants("FlowersOnosma", 4f, 160, 60f),
						Bush("Tragacanth", 2f, 1f, 45f), Trees(0.002f, 40f, 60f, "RockHackberry"), Stones(SmallRocks("Tuff"), 2f)).Temp(-1f, 0.45f)
						.Ways(path: Ground.PathTuffSteps),
					// d1: the wash floors (the Wash landform paints them): pink silt in curling plates, wild barley, Ravenna grass along the
					// axes, oleasters, Christ's thorn and the copper briar at the edges, hollyhocks, pumice drift lines. The road along it is a
					// loose flood-smoothed trace.
					// LATER (SCATTER-MODES near-water anchor): the Ravenna grass within 15 m of water; the wash axes stand in.
					With(D(Ground.ClayWashFloor, 64f, 2f, 0f, 6f, Grass("GrassBulbousPoa", 10f), Grass("GrassWildBarley", 12f, 200), Grass("GrassRavenna", 6f, 160),
						Plants("FlowersHollyhock", 1f), Bush("ChristsThorn", 0.6f), Band(Bush("CopperBriar", 0.4f), -1f, 0.55f), Moded(Plants("HerbHarmal", 1f), BiomeScatterMode.NearSites("Village,Town", 200f)),
						Trees(0.008f, 18f, 15f, "Oleaster"), With(Logs("Oleaster snags", 0.003f, 40f, Wood("Snag_Oleaster")), r => r.AlignToNormal = false),
						Plants("DebrisPumice", 3f, 128), Plants("DebrisSherdsTerracotta", 1f, 128)).Ways(road: Ground.PathWashTrace),
						l => l.Height = new Vector2(0.45f, 0.48f)),
					// d2: the ash-loam tilth of the vineyards and orchards near the villages, on the watered side: the spring flush and autumn
					// gold, wild barley at the margins, salep, apricots, vine prunings.
					D(Ground.SoilAshLoam, 72f, 2f, 0f, 10f, Band(Plants("FlowersSpringAsh", 4f, 200), -1f, 0.5f), Plants("FlowersSternbergia", 3f, 180),
						Grass("GrassWildBarley", 8f, 160), Plants("HerbSalep", 0.5f), Band(Trees(0.004f, 25f, 12f, "Apricot"), -1f, 0.6f, -0.6f, 1f),
						Plants("DebrisVinePrunings", 2f, 128)).Humid(-0.55f, 1f).NearSites("Village,Town,Monastery,AbandonedFarm", 250f),
					// d3: the white lapilli fans below the chimneys: goatgrass, gypsophila, noaea, slender uncapped chimneys, fallen basalt
					// hats, spalls.
					D(Ground.GravelLapilliDrift, 60f, 2f, 6f, 25f, Grass("GrassGoatgrass", 8f, 180), Plants("FlowersGypsophila", 4f), Band(Plants("Noaea", 2f), -1f, 1f, -1f, -0.5f),
						Plants("DebrisPumice", 4f, 128), Stones(SmallRocks("Tuff"), 3f), Stones(SmallRocks("Basalt"), 1f),
						Outcrops("Tuff chimneys", 0.02f, 16f, F(RockTypes.Tuff, "Chimney"), 30f), Blocks("Tuff spalls", 0.04f, 10f, F(RockTypes.Tuff, "Spall")),
						Boulders(0.08f, 10f, BasaltRocks)),
					// d4: the mesas' black lava caprock (the CaprockMesa landform paints its tops): orange lichen on the caps, goatgrass, thrift,
					// geven, foxtail lilies, gundelia, the fallen hats' boulders.
					With(D(Ground.BasaltCaprock, 64f, 2f, 0f, 12f, Plants("LichenTuff", 6f), Grass("GrassGoatgrass", 6f, 160), Plants("Acantholimon", 2f),
						Bush("Tragacanth", 2f), Band(Plants("FlowersEremurus", 2f), -1f, 0.55f), Plants("HerbKenger", 1f), Stones(SmallRocks("Basalt"), 3f),
						Boulders(0.08f, 10f, BasaltRocks)), l => l.Height = new Vector2(0.56f, 0.6f)),
					// d5: the red valley's iron-stained tuff in the hot reach, in place of the rose tuff: golden-drops, Christ's thorn, cones
					// and ribs, fewer chimneys.
					D(Ground.TuffRedValley, 90f, 2.5f, 18f, 60f, Plants("FlowersOnosma", 3f, 160, 60f), Bush("ChristsThorn", 0.71f, 1f, 40f),
						Moded(Outcrops("Red chimneys", 0.02f, 16f, F(RockTypes.Tuff, "Capped"), 60f), BiomeScatterMode.ClusteredFields(180f, 0.35f)),
						Outcrops("Red cones", 0.008f, 45f, F(RockTypes.Tuff, "Cone"), 60f), Blocks("Red ribs", 0.01f, 30f, F(RockTypes.Tuff, "Rib"), 60f),
						Stones(SmallRocks("Tuff"), 2f)).Temp(0.45f, 1f).Ways(path: Ground.PathTuffSteps)),
				C(Ground.CliffIgnimbrite, 38f), Ground.ClayTuffOoze, Ground.SandLapilliStream)
				// The American kit and AridGeology the round retired (sagebrush, rabbitbrush, creosote, prickly pear, pinyon, the sandstone
				// hoodoos, ledges and concretions, shale stacks, petrified logs), switched off where the tool wrote them and nobody tuned them.
				.Moved("main", "ShrubDry", "Bush: Sagebrush", "Bush: Creosote", "Detail_Rocks_Sandstone", "Formations: Hoodoos", "Formations: Shale", "PadCactus",
					"Bush: Rabbitbrush", "Trees: Pinyon", "Deadwood: Petrified logs")
				.Moved("detail/0", "Boulders", "Formations: Sandstone ledges")
				.Moved("detail/1", "Formations: Sandstone concretions")
				.Ways(path: Ground.PathDonkeyAsh, road: Ground.PathRutCut, cobbles: Ground.FlagstoneTuffSetts)
				.Bars(sand: Ground.SandLapilliBar, gravel: Ground.GravelVolcanicBar)
				// Approved climate overrides: snow on the ash where the year is frozen, salt-baked ash in the hottest of the red reach (the
				// template had no variants; declared here, the scorched one only above t 0.6 so the red reach keeps its ash steppe).
				.ClimateVariant("Frozen", -1f, -0.45f)
				.ClimateVariant("Scorched", 0.6f, 1f)
				.ClimateGround("Frozen", L(Ground.AshSnowOn, Grass("GrassBulbousPoa", 6f, 160), Bush("Tragacanth", 1f)))
				.ClimateGround("Scorched", L(Ground.CrackedEarthSaltBaked, Plants("Noaea", 2f), Bush("ChristsThorn", 0.4f), Stones(Pebbles("Tuff"), 2f)))
				// Own rock tuff (its formations); approved: accepts the lava caps' basalt, the dark domes' andesite, pumice, obsidian (the
				// knapping floors) and the white lake limestone of the caps. Sandstone, shale, conglomerate and granite dropped.
				.Accepts(RockTypes.Tuff, RockTypes.Basalt, RockTypes.Andesite, RockTypes.Pumice, RockTypes.Obsidian, RockTypes.Limestone)
				// Tuff caves, grottoes and pumice-and-obsidian workings; basalt lava tubes. LATER (I2n-e F25): the Hewn cave style of the
				// underground cities (rectangular chisel-faced galleries, 3–8 levels, vent shafts, stair ramps) and the rolling millstone door
				// (I7); LATER (I9 S3): tuff dripstone at 0.2×, salt crusts and soot fans in the halls.
				.CaveShell(CaveForm.Cave, "Tuff").CaveShell(CaveForm.Grotto, "Tuff").CaveShell(CaveForm.Mine, "Tuff").CaveShell(CaveForm.LavaTube, "Basalt")
				// Fire opal nodules in the vesicles, the Badlands' mahogany-banded obsidian, and the peris' water-clear perilight crusts glowing
				// cold green-gold, lining the ways it lights.
				.Crystals((BiomeCaveCrystalKind.FireOpal, 0.4f), (BiomeCaveCrystalKind.Obsidian, 0.25f), (BiomeCaveCrystalKind.Perilight, 0.35f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Pulse)
				// Rill nets on the steep tuff, caprock mesas, flat-floored washes and the crag a citadel stands on, all carved at the cut.
				// LATER (I7 I23/I24): the citadel's rooms and the town round its foot on the CitadelCrag pad; the rock-cut dwellings in the cones.
				.Landforms("Rills", "CaprockMesa", "Wash", "CitadelCrag")
				.Look(BiomeLookClass.Desert)
				// Approved: the honeycombed cliffs' and rock churches' bats, pigeons and owls beside the desert and mountain life.
				.Wildlife(BiomeHabitat.Desert | BiomeHabitat.Mountain | BiomeHabitat.Cave | BiomeHabitat.Ruins)
				// LATER (AMBIENT-LIFE): rock-pigeon flocks out of the dovecote cliffs (the signature), alpine swifts, lesser kestrels in the
				// chimneys, griffon vultures, ravens, hoopoe, rock nuthatch, dusk bats from the churches, tortoise, starred agama, fox, winter
				// wolves, donkey trains and camel caravans on the rut-cut roads (I18); the peri lights between the chimneys at dusk, and very
				// rarely the earth-dragon in the red valley.
				.Moat(BiomeMoatWater.Dry)
				// Rose-silt water: tuff-milk pink-ochre shallows, olive-brown pools.
				.WaterTint(new BiomeWaterLook(ErgHex("#b08a74"), ErgHex("#4a3a30"), new Vector3(0.26f, 0.22f, 0.20f), 0.5f, ErgHex("#c09a88"), ErgHex("#f2ece6")))
				.Blades("GrassBulbousPoa", t => UplandBlade(t, "#5a6a48", "#9aa070", 0.45f, width: 0.4f, clump: 0.3f,
					head: GrassBladeHead.SeedHead, headSize: 0.02f, headShare: 0.4f, heads: new[] { "#7a5a6a" }))
				.Blades("GrassGoatgrass", t => UplandBlade(t, "#6a6a4a", "#a89a62", 0.35f, stiffness: 1.5f, width: 0.6f, clump: 0.4f,
					head: GrassBladeHead.SeedHead, headSize: 0.05f, headShare: 0.5f, heads: new[] { "#b07a3a" }))
				.Blades("GrassWildBarley", t => UplandBlade(t, "#4e5e3a", "#a8a070", 0.3f, width: 1.2f, clump: 0.5f,
					head: GrassBladeHead.SeedHead, headSize: 0.06f, headShare: 0.35f, heads: new[] { "#d8c890" }))
				.Blades("GrassRavenna", t => UplandBlade(t, "#4a5a3a", "#8a8a62", 0.25f, width: 2f, clump: 0.8f, heightScale: 1.4f,
					head: GrassBladeHead.Plume, headSize: 0.12f, headShare: 0.3f, heads: new[] { "#d8b8b0" }))
				.Blades("FlowersGypsophila", t => UplandBlade(t, "#8a9078", "#9aa088", 0.3f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.05f,
					heads: new[] { "#f4f2ee" }))
				.Blades("FlowersSpringAsh", t => UplandBlade(t, "#4a6a3a", "#5a7a44", 0.4f, head: GrassBladeHead.Flower, sprinkle: 14f, headSize: 0.025f,
					heads: new[] { "#3a3a9a", "#6a6ad0", "#f0c020", "#f6f4ee" }))
				.Blades("FlowersOnosma", t => UplandBlade(t, "#9a9e88", "#aab09a", 0.2f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.03f,
					heads: new[] { "#f0d050" }))
				.Blades("FlowersEremurus", t => UplandBlade(t, "#4a6a3a", "#5a7a44", 0.3f, heightScale: 2f, head: GrassBladeHead.Flower, sprinkle: 3f, headSize: 0.12f,
					heads: new[] { "#f0d8c8", "#f0a060" }))
				.Blades("FlowersHollyhock", t => UplandBlade(t, "#4a6a34", "#5a7a3e", 0.3f, heightScale: 2.4f, head: GrassBladeHead.Flower, sprinkle: 3f, headSize: 0.09f,
					heads: new[] { "#d87a9a", "#f2ece4", "#6a1a3a" }))
				.Blades("FlowersSternbergia", t => UplandBlade(t, "#3e6a2a", "#4e7a34", 0.4f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.04f,
					heads: new[] { "#f0c020" }))
				.Describe("Fairy-chimney badlands of soft volcanic tuff — rose ravines, capped cones hollowed into houses and painted rock churches, a honeycombed citadel, vines in the ash washes and cities beneath the ground")
				.Names(onsets: new[] { "Gö", "Ür", "Av", "Kar", "Ner", "Mus", "Sin", "Zel", "Ort", "Sel", "Tal", "Ih", "Pe", "Uç" },
					nuclei: new[] { "a", "e", "i", "o", "ü", "ö" },
					codas: new[] { "sar", "ara", "kent", "anos", "ari", "dere", "kaya" },
					middles: new[] { "el", "ar", "en", "is" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Under-city", "Hollow", "Cell", "Gallery", "Cistern", "Refuge" },
					dungeonPrefixes: new[] { "The Hewn", "The Sleepers'", "The Peri-lit", "The Eight-storeyed", "The Millstone", "The Ash-buried" },
					poiSuffixes: new[] { "Hisar", "Chimney", "Cone", "Han", "Dere", "Kaya", "Dovecote", "Peri-stone" },
					adjectives: new[] { "Peri-lit", "Rose", "Hollowed", "Capped", "Ash-pale", "Wine-dark", "Honeycombed", "Dragon-scorched" },
					poiWords: new[]
					{
						("village", new[] { "Chimney Village", "Hewn Village" }),
						("town", new[] { "Hewn Town", "Rock Town" }),
						("castle", new[] { "Hisar", "Rock Citadel" }),
						("keep", new[] { "Hisar" }),
						("temple", new[] { "Rock Church", "Painted Church" }),
						("monastery", new[] { "Rock Monastery", "Cone Monastery" }),
						("hermitage", new[] { "Stylite Chimney", "Hermit's Cone" }),
						("waystation", new[] { "Han", "Caravan Han" }),
						("tradingpost", new[] { "Han", "Market Han" }),
						("dungeonentrance", new[] { "Under-city", "Millstone Door" }),
						("crypt", new[] { "Rock Tombs", "Arcosolia" }),
						("gorge", new[] { "Dere", "Plane Gorge" }),
						("mesa", new[] { "Kaya", "Table Rock" }),
						("abandonedfarm", new[] { "Dovecotes", "Pigeon Cliff" }),
						("feyring", new[] { "Peri Chimney", "Peri-stone" }),
						("monsterden", new[] { "Dragon Cleft" }),
						("quarry", new[] { "Tuff Quarry" }),
					},
					about: "Anatolian Greek–Turkic voice (Gö, Ür, Av, Kar, Zel, Uç; -sar, -ara, -kent, -anos, -dere, -kaya): the fairy-chimney country")
				// Rock-cut country: monasteries and hermitages in the cones, chimney villages, hans on the roads, citadels; no mills, timber,
				// coasts or wetlands (approved as drafted). The landform kinds keep the rule's 1.
				.Sites((POIType.Village, 1.4f), (POIType.Monastery, 1.6f), (POIType.Hermitage, 1.6f), (POIType.Temple, 1.2f), (POIType.Waystation, 1.4f),
					(POIType.DungeonEntrance, 1.4f), (POIType.Mesa, 1.4f), (POIType.Gorge, 1.4f), (POIType.Castle, 1f), (POIType.Cave, 1.2f),
					(POIType.Landmark, 1.2f), (POIType.Butte, 1f), (POIType.Crypt, 1f), (POIType.Ruins, 1f), (POIType.AncientRoad, 1f),
					(POIType.Quarry, 0.8f), (POIType.Grotto, 0.8f), (POIType.NaturalArch, 0.8f), (POIType.TradingPost, 0.8f), (POIType.Graveyard, 0.8f),
					(POIType.Shrine, 0.8f), (POIType.Spring, 0.8f), (POIType.River, 0.8f), (POIType.Nest, 0.8f), (POIType.AbandonedFarm, 0.7f),
					(POIType.Keep, 1f), (POIType.Town, 0.6f), (POIType.HerbGrove, 0.6f), (POIType.MonsterDen, 0.6f), (POIType.FeyRing, 0.6f),
					(POIType.ObsidianField, 0.6f), (POIType.BanditCamp, 0.6f), (POIType.CrystalFormation, 0.5f), (POIType.Camp, 0.4f), (POIType.OreVein, 0.4f),
					(POIType.City, 0.3f), (POIType.Fortress, 0.3f), (POIType.Statue, 0.2f), (POIType.Lake, 0.2f), (POIType.Obelisk, 0.2f),
					(POIType.StoneCircle, 0.2f), (POIType.Battlefield, 0.2f), (POIType.RitualSite, 0.2f), (POIType.Barrow, 0.2f), (POIType.BossLair, 0.2f),
					(POIType.Tower, 1f), (POIType.RuinedTower, 0.2f), (POIType.Mine, 0.2f), (POIType.Monument, 0.2f), (POIType.Capital, 0.1f),
					(POIType.Portal, 0.1f), (POIType.LeyNexus, 0.1f), (POIType.FallenStar, 0.1f),
					(POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.Bridge, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.4f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 1.2f),
					(POIType.ElderMonolith, 0.8f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 1f), (POIType.KoboldDelving, 0.5f),
					(POIType.LostLibrary, 0.5f), (POIType.IdolVault, 0.4f), (POIType.Dovecote, 0.8f), (POIType.NomadCamp, 0.6f), (POIType.Necropolis, 1f),
					(POIType.RockCutFacade, 1.4f), (POIType.DjinnPrison, 1f), (POIType.CaravanHalt, 1.2f), (POIType.BeastBoneyard, 1.2f),
					(POIType.CliffPalace, 1.2f), (POIType.DreadRoad, 0.6f), (POIType.WyvernNest, 0.6f), (POIType.CliffRelief, 0.6f),
					(POIType.FireTemple, 0.4f))
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Stone", 2f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 0f)
				.Prefers(POIType.Town, "Stone", 2f)
				.Prefers(POIType.Town, "Palisaded", 0f)
				.Prefers(POIType.City, "Stone", 1f)
				.Prefers(POIType.City, "Timber", 0f)
				.Prefers(POIType.Capital, "Stone", 1f)
				.Prefers(POIType.Capital, "Timber", 0f)
				.Prefers(POIType.Castle, "Stone", 2f)
				.Prefers(POIType.Castle, "Highland", 0f)
				.Prefers(POIType.Keep, "Stone", 2f)
				.Prefers(POIType.TradingPost, "Caravanserai", 1.6f)
				.Prefers(POIType.TradingPost, "Market", 0.3f)
				.Prefers(POIType.Waystation, "Stone Inn", 1.6f)
				.Prefers(POIType.Waystation, "Inn", 0f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Fortress, "Stone", 2f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Temple, "Court", 2f)
				.Prefers(POIType.Temple, "Colonnade", 0f)
				.Prefers(POIType.Monastery, "Cloister", 2f)
				.Prefers(POIType.Monastery, "Timber", 0f)
				.Prefers(POIType.Ruins, "Burned", 0.2f)
				.Prefers(POIType.AbandonedFarm, "Farmstead", 1.4f)
				.Prefers(POIType.AbandonedFarm, "Burned", 0.2f)
				.Prefers(POIType.Quarry, "Cut Stone", 1.6f)
				.Prefers(POIType.FeyRing, "Ring", 1.2f)
				.Prefers(POIType.MonsterDen, "Lair", 1.2f)
				.Prefers(POIType.Camp, "Hide Tents", 1.2f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Hermitage, "Stone", 1.8f)
				.Prefers(POIType.Hermitage, "Cottage", 0f)
				.Prefers(POIType.DungeonEntrance, "Door", 2f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 0f)
				.Prefers(POIType.Crypt, "Tomb", 1.4f)
				.Prefers(POIType.Graveyard, "Earthen", 1.2f)
				.Prefers(POIType.Graveyard, "Railed", 0f)
				.Prefers(POIType.Ruins, "Stone", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				.Prefers(POIType.DragonLair, "Hoard", 1.2f)
				.Prefers(POIType.WreckedCaravan, "Last Camp", 1.4f)
				.Prefers(POIType.TollGate, "Toll House", 1.2f)
				.Prefers(POIType.Hamlet, "Stone Stead", 1.4f)
				.Prefers(POIType.BurnedVillage, "Cinders", 1.3f)
				.Prefers(POIType.WarbandCamp, "Horde", 1.2f)
				.Prefers(POIType.TitansBones, "Sunken Skull", 1.4f)
				.Prefers(POIType.LostLibrary, "Dry Athenaeum", 1.4f)
				.Prefers(POIType.FireTemple, "Colonnade", 1.3f)
				.Prefers(POIType.NomadCamp, "Black Tents", 1.4f)
				.Prefers(POIType.Necropolis, "City of the Dead", 1.3f)
				.Prefers(POIType.Pyramid, "Stepped", 1.2f)
				.Prefers(POIType.RockCutFacade, "Treasury", 1.3f)
				// (the fantasy wave's variants end here)
				// LATER (I7 + I24 rock-cut dwellings): Badlands' signature pieces and their variants — the chimney house (Village "Chimney
				// Village" 2.0), the cliff village of stacked ashlar fronts and tuff steps ("Hewn" 1.2/1.4), the rock church with its Fresco
				// interior (Temple 2.0; I29), the rock monastery round its court (Monastery 2.0), the stylite chimney (Hermitage 1.8), the rock
				// citadel (Castle/Keep/Fortress "Hisar" 1.8), the underground city behind its millstone door (DungeonEntrance 2.0), the han with
				// its carved portal (Waystation/TradingPost 1.6), the dovecote cliff (AbandonedFarm "Dovecotes" 1.4), the rock wine press and
				// vineyard tower, the rock tombs (Crypt 1.4, Graveyard 1.2), the potters' yard, the village fountain under its plane, the tuff
				// quarry (Quarry 1.6), the peri chimney (FeyRing/Landmark 1.2), the dragon cleft (MonsterDen 1.2) and the sleepers' hollow.
				// Until then the kit's variants below stand in at the draft's weights.
				.Favours(BiomeStructureStyle.Stone, 1.8f)
				.Favours(BiomeStructureStyle.Earth, 0.8f)
				.Favours(BiomeStructureStyle.Hide, 0.3f)
				.Favours(BiomeStructureStyle.Timber, 0.05f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: honey-cream tuff ashlar with fine tooling and thin lime joints, flat rolled-earth roofs (decisions 20/30), fronts
				// growing out of the living rock, carved rope mouldings and rosettes, walnut-brown carved doors, poplar poles, black wrought
				// grilles, cream goat-hair cloth and flat-woven banners in pomegranate red; the plateau farms' straw-and-pumice mud brick; the
				// caravan camps' goat-hair tents at 30°. LATER (NEW-SURFACES U6, I29): TuffAshlar, RolledEarth, CarvedTuff, Cicim, Fresco (the
				// rock churches' painted interiors) and AshBrick as Badlands' own surfaces; the shared ones tinted stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Sandstone, ErgHex("#d8c4a2"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Sandstone, ErgHex("#d8c4a2"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, ErgHex("#a8927a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Adobe, ErgHex("#a8927a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Monolith, ErgHex("#c8a894"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Sandstone, ErgHex("#e8dcc4"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, ErgHex("#4a3424"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Floor, BiomeStructureMaterial.Monolith, ErgHex("#c8a894"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, ErgHex("#b8a68a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Adobe, ErgHex("#b8a68a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, ErgHex("#a8927a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Adobe, ErgHex("#a8927a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, ErgHex("#a8927a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, ErgHex("#4a3424"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Canvas, ErgHex("#d8ccb0"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, ErgHex("#d8ccb0"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Canvas, ErgHex("#d8ccb0"), pitch: BiomeRoofPitch.Pitch30)
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Fieldstone, ErgHex("#b8a07a"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Sandstone, ErgHex("#cdb89c"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Sandstone, ErgHex("#d8c4a2"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Monolith, ErgHex("#c8a894"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Adobe, ErgHex("#a8927a"), pitch: BiomeRoofPitch.Flat)
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Monolith, ErgHex("#c8a090"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Monolith, ErgHex("#c8a090"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, ErgHex("#a08a68"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, ErgHex("#2a2622"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, ErgHex("#d8ccb0"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, ErgHex("#9a1a2a"))
				// Ashworn: soft tuff arrises rounded, rill runnels below the sills, a salt-white band at the foot, soot over the openings.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Ashworn, 0.5f, 1f)
				// Small walled plots on the tilth and the wash floors: ground-trained bush vines, pumpkins and chickpeas on the flat, and
				// apricot orchards.
				.Crops(("Detail_CropBushVine", 0.45f), ("Detail_CropPumpkin", 0.2f), ("Detail_CropChickpea", 0.2f))
				.CropSurface("Detail_CropBushVine", BiomeFieldSurface.Flat)
				.Orchards(new Entry.Orchard("Apricot", 0.15f, 6f, "Detail_GrassBulbousPoa"))
				// Tuff-rubble drystone round the plots, Christ's-thorn hedges along them.
				.Lines(new Entry.Line("TuffRubble"), new Entry.Line("ChristsThorn"))
				// Carved tuff lanterns on posts and wall brackets in the villages and hans, bronze lamp-wheels in the church porches, cressets on
				// the citadels, wall lanterns in the han courts, perilight posts at the peri chimneys and landmarks; the camps go unlit.
				.Lights(POIType.Village, BiomeWayLightKind.CarvedLantern, 20f)
				.Lights(POIType.Town, BiomeWayLightKind.CarvedLantern, 16f)
				.Lights(POIType.City, BiomeWayLightKind.CarvedLantern, 16f)
				.Lights(POIType.Capital, BiomeWayLightKind.CarvedLantern, 16f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.CarvedLantern, 18f)
				.Lights(POIType.Temple, BiomeWayLightKind.HangingLantern, 40f)
				.Lights(POIType.Monastery, BiomeWayLightKind.HangingLantern, 40f)
				.Lights(POIType.Hermitage, BiomeWayLightKind.HangingLantern, 40f)
				.Lights(POIType.Castle, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Waystation, BiomeWayLightKind.WallLantern, 14f)
				.Lights(POIType.FeyRing, BiomeWayLightKind.CrystalPost, 12f)
				.Lights(POIType.Landmark, BiomeWayLightKind.CrystalPost, 16f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.WallLantern)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallLantern, 14f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.WallLantern)
				// Wild madder scrambling over the ruined walls, white bryony in the planes; no ivy or creeper.
				.GrowsVines(0.15f, 0.4f, 0.08f, 0.1f, ("WildMadder", 0.5f), ("WhiteBryony", 0.3f))
				.Gathers("Fire Opal Nodule", "Perilight Cluster", "Obsidian Core", "Pumice Block", "Tragacanth Gum", "Harmal Seed", "Salep Tuber", "Kenger Gum",
					"Madder Root", "Apricot Harvest", "Grape Cluster", "Pigeon Guano")
				.Herbs("Detail_HerbHarmal", "Detail_HerbSalep", "Detail_HerbKenger", "Detail_Acantholimon")
				.ErodesAs("Badlands")
				.Loose("Silicate Sand");
		}
	}
}
#endif
