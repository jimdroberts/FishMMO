using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The crystals a biome's caves grow (<see cref="BiomeTemplate.CaveCrystals"/>, 2026-10-16, phase I6c): the mineral deposits
	/// in their chambers and, for the kinds that glow, what lines their ways. Append-only (stored by value); each kind is drawn
	/// in its own habit and material by the biome art generator (ProceduralArtCatalogue.CaveMinerals).
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 every cave and grotto grew selenite (blades of gypsum) wherever it was, each cluster with a cave light of
	/// a hue drawn by chance; that is every biome's default. An ice cave's crystals are its own ice (its shell's, as its
	/// formations are) and a lava tube grows none, whatever this list holds.
	/// </remarks>
	public enum BiomeCaveCrystalKind : byte
	{
		/// <summary>Selenite: milky gypsum blades with chisel tips, a cave light beside them (the old crystal of every cave).</summary>
		Selenite = 0,
		/// <summary>Amethyst: a broken geode lined with violet points.</summary>
		Amethyst = 1,
		/// <summary>Clear quartz: a druse of small water-clear points carpeting the rock.</summary>
		ClearQuartz = 2,
		/// <summary>Smoky quartz: a few stout brown-grey prisms out of a pegmatite pocket.</summary>
		SmokyQuartz = 3,
		/// <summary>Green fluorite: interlocking translucent green cubes.</summary>
		GreenFluorite = 4,
		/// <summary>Frost crystal: six-sided columns of clear ice (an ice cave's own crystal) in a cold land's caves.</summary>
		FrostCrystal = 5,
		/// <summary>Obsidian shards: glassy black blades and conchoidal splinters jutting from the rock.</summary>
		Obsidian = 6,
		/// <summary>Amberlight (fantasy): rounded nodules of glowing resin-amber in a rock bed.</summary>
		Amberlight = 7,
		/// <summary>Wyrdstone (fantasy): a mossy geode whose green-violet points glow from within.</summary>
		Wyrdstone = 8,
		/// <summary>Sunstone (fantasy, volcanic): tabular blades of orange-gold crystal alight like embers, out of scoria.</summary>
		Sunstone = 9,

		// 2026-10-26 (NEW-CRYSTALS, decision 34): every kind the 83 biome drafts name, appended in the drafts' order. No biome
		// grows one until its round names it (every template still grows selenite), so nothing a cut lays changes. A kind
		// two drafts share (decision 97: Aragonite, Sal Ammoniac; also Vivianite, Dogtooth, Thenardite, Pyrite) is ONE kind,
		// each biome's own look of it a BiomeCaveCrystal.Look; the fantasy kinds glow (Glows), the real ones do not.
		/// <summary>Phillipsite: colourless-white zeolite prism sprays and cruciform twins on red clay (AbyssalPlain).</summary>
		Phillipsite = 10,
		/// <summary>Lodeheart (fantasy, glows): a split manganese nodule, concentric black rings round a pale aquamarine core that glows (AbyssalPlain).</summary>
		Lodeheart = 11,
		/// <summary>FireIce: methane hydrate: translucent yellow-white waxy cauliflower masses (Abyss).</summary>
		FireIce = 12,
		/// <summary>Hadalite (fantasy, glows): violet-black prisms with a slow cold heartbeat glow (Abyss).</summary>
		Hadalite = 13,
		/// <summary>Gwindel: water-clear twisted rock-crystal prism fans with smoky cores (Alpine).</summary>
		Gwindel = 14,
		/// <summary>PinkFluorite: rose octahedra on milky quartz (Alpine).</summary>
		PinkFluorite = 15,
		/// <summary>Starfrost (fantasy, glows): frost-white six-rayed star clusters that twinkle cold silver-violet (Alpine).</summary>
		Starfrost = 16,
		/// <summary>Stibnite: steel-grey metallic needle sprays with a blue tarnish (AlpineMeadow).</summary>
		Stibnite = 17,
		/// <summary>Rhodochrosite: raspberry-red rhombs banded with white on quartz (AlpineMeadow).</summary>
		Rhodochrosite = 18,
		/// <summary>Stormglass (fantasy, glows): smoky violet-grey spheres full of cloud, threads of lightning flickering blue-white in them (AlpineMeadow).</summary>
		Stormglass = 19,
		/// <summary>FireOpal: orange-red jelly nodules in vesicles, flashing a play of colour (Badlands).</summary>
		FireOpal = 20,
		/// <summary>Perilight (fantasy, glows): water-clear botryoidal hyalite crusts glowing cold green-gold (Badlands).</summary>
		Perilight = 21,
		/// <summary>Twinquartz: Japan-law twins: flat heart-shaped V-paired quartz plates, water-clear (BambooForest).</summary>
		Twinquartz = 22,
		/// <summary>Jadeite: white to apple-green waxy jade nodules (BambooForest).</summary>
		Jadeite = 23,
		/// <summary>Hotarubi (fantasy, glows): firefly-glass: a clear druse full of drifting yellow-green pinpoints (BambooForest).</summary>
		Hotarubi = 24,
		/// <summary>Aragonite: sea-cave needle sprays, cream-white and satin (Farmland's own look: flos-ferri coralloid sprays) (Beach, Farmland).</summary>
		Aragonite = 25,
		/// <summary>Pearlspar (fantasy, glows): nacreous iridescent plates with a pastel shimmer (Beach).</summary>
		Pearlspar = 26,
		/// <summary>Galena: lead-silver metallic cubes on white calcite (Castle).</summary>
		Galena = 27,
		/// <summary>Chalcopyrite: brassy masses with a peacock tarnish (Castle).</summary>
		Chalcopyrite = 28,
		/// <summary>Wyrmglass (fantasy, glows): milk-white translucent spindles each with a coiled serpent-shaped inclusion, glowing white-gold (Castle).</summary>
		Wyrmglass = 29,
		/// <summary>BlueJohn: Castleton banded fluorite: nodules banded purple-blue and honey-yellow (Cave).</summary>
		BlueJohn = 30,
		/// <summary>Helictite: calcite that grows against gravity: curling wire filaments, white-translucent with clear tips (Cave).</summary>
		Helictite = 31,
		/// <summary>Echospar (fantasy, glows): optical calcite rhombs, water-clear with a doubled image and a faint cold blue-white glow (Cave).</summary>
		Echospar = 32,
		/// <summary>Cassiterite: Cornish tinstone: black-brown adamantine stubby prisms and knee twins (CoastalWater).</summary>
		Cassiterite = 33,
		/// <summary>Bellspar (fantasy, glows): violet-blue hollow prisms that ring like drowned bells (CoastalWater).</summary>
		Bellspar = 34,
		/// <summary>Apatite: sea-green glassy hexagonal prisms on a cream phosphorite crust (CoralReef).</summary>
		Apatite = 35,
		/// <summary>Lagoonglass (fantasy, glows): turquoise blades that hold the lagoon's light (CoralReef).</summary>
		Lagoonglass = 36,
		/// <summary>Tektite: bottle-green to black glass splash-forms with sculpted pitted skins (Crater).</summary>
		Tektite = 37,
		/// <summary>Starglass (fantasy, glows): black glass blades shot with violet-white star-points (Crater).</summary>
		Starglass = 38,
		/// <summary>SalAmmoniac: skeletal hopper cubes and feathers, white to cream (Sulphur Flats' own look: feathery fronds) (CryovolcanicPlain, SulphurFlats).</summary>
		SalAmmoniac = 39,
		/// <summary>Eitrglass (fantasy, glows): translucent teal blades with a sloshing liquid-venom core (CryovolcanicPlain).</summary>
		Eitrglass = 40,
		/// <summary>Dogtooth: calcite scalenohedra in vugs: honey to water-clear spearheads (Grassland's own look: in loess-doll geodes) (DeepOcean, Grassland).</summary>
		Dogtooth = 41,
		/// <summary>Stellamaris (fantasy, glows): blue-white clear prisms each holding one twinkling pinpoint of light (DeepOcean).</summary>
		Stellamaris = 42,
		/// <summary>Malachite: banded green botryoidal crusts with azurite-blue spots (Desert).</summary>
		Malachite = 43,
		/// <summary>Sunglass (fantasy, glows): pale lemon-green translucent desert-glass lumps with frosted skins, glowing warm (Desert).</summary>
		Sunglass = 44,
		/// <summary>Ilmenite: black tabular plates with a bronze-steel sheen (DustSea).</summary>
		Ilmenite = 45,
		/// <summary>Phaseglass (fantasy, glows): milky blue-white blades whose glow follows the moon (DustSea).</summary>
		Phaseglass = 46,
		/// <summary>Vivianite: marsh-clay phosphate: radiating blue-green blades darkening to indigo (Peat Bog's own look: deep blue-green blades round buried bone) (Estuary, PeatBog).</summary>
		Vivianite = 47,
		/// <summary>Ebbspar (fantasy, glows): water-clear rhombs with a grey-green heart whose glow follows the tide (Estuary).</summary>
		Ebbspar = 48,
		/// <summary>Celestine: pale sky-blue tabular blades with a pearly lustre (Farmland).</summary>
		Celestine = 49,
		/// <summary>Fogpearl (fantasy, glows): nests of cave pearls in gour pools with a pearl-white breathing glow (Farmland).</summary>
		Fogpearl = 50,
		/// <summary>Cinnabar: vermilion rhombohedral crystals on white dolomite, adamantine (ForestRuins).</summary>
		Cinnabar = 51,
		/// <summary>Farrstone (fantasy, glows): the kings' radiance: honey-gold bipyramids glowing with a slow warm pulse (ForestRuins).</summary>
		Farrstone = 52,
		/// <summary>Realgar: scarlet-orange resinous prisms on white calcite (Fortress).</summary>
		Realgar = 53,
		/// <summary>Orpiment: lemon-gold foliated masses, pearly (Fortress).</summary>
		Orpiment = 54,
		/// <summary>Dalistone (fantasy, glows): pale honey-white prisms curved like a tur's horn, breathing a warm glow (Fortress).</summary>
		Dalistone = 55,
		/// <summary>IcelandSpar: clear colourless rhombohedra with a faint honey tint and a doubled image (GeyserBasin).</summary>
		IcelandSpar = 56,
		/// <summary>Stilbite: peach-salmon bow-tie sheaves with white sprays in basalt vugs (GeyserBasin).</summary>
		Stilbite = 57,
		/// <summary>Solarsteinn (fantasy, glows): sunstone spar rhombs whose white-gold glow brightens on the faces turned toward the sun (GeyserBasin).</summary>
		Solarsteinn = 58,
		/// <summary>CupHoar: hollow hexagonal depth-hoar cups, glittering (Glacier).</summary>
		CupHoar = 59,
		/// <summary>Nagamani (fantasy, glows): water-clear prisms with a sea-green core that glows (Glacier).</summary>
		Nagamani = 60,
		/// <summary>Skyglass (fantasy, glows): the sky's tears: pale-blue clear shards with a soft glow (Grassland).</summary>
		Skyglass = 61,
		/// <summary>Turquoise: sky-blue to green nuggets and spider-web veins in brown limonite (HighDesert).</summary>
		Turquoise = 62,
		/// <summary>Gueststar (fantasy, glows): frost-white shard sprays each with one burning blue-white point that twinkles (HighDesert).</summary>
		Gueststar = 63,
		/// <summary>Garnet: wine-red almandine dodecahedra in schist (Hills).</summary>
		Garnet = 64,
		/// <summary>Staurolite: fairy crosses: brown cross-twinned prisms (Hills).</summary>
		Staurolite = 65,
		/// <summary>Heartgarnet (fantasy, glows): the moura's heart: a deep-red garnet glowing from within with a slow heartbeat (Hills).</summary>
		Heartgarnet = 66,
		/// <summary>Hoarfeather: giant sublimation hoar: fern-branched six-armed plates, white-translucent (IceCave).</summary>
		Hoarfeather = 67,
		/// <summary>Cryocalcite: pearly cream-to-amber spherule clusters on melted-out ice (IceCave).</summary>
		Cryocalcite = 68,
		/// <summary>Chudfire (fantasy, glows): a clear ice prism with a frozen gold-orange flame inside (IceCave).</summary>
		Chudfire = 69,
		/// <summary>Hydrohalite: water-clear to pale-blue platy prisms of a salt that forms only below freezing (IceGeyserField).</summary>
		Hydrohalite = 70,
		/// <summary>Hyalite (fantasy, glows): water-clear beaded opal crusts glowing cold green (IceGeyserField).</summary>
		Hyalite = 71,
		/// <summary>Nightpearl (fantasy, glows): luminous pearls nested in nacre-lined ice cups, a pink-lilac sheen (IcePalace).</summary>
		Nightpearl = 72,
		/// <summary>ScythianEmerald: deep green beryl hexagonal prisms in white quartz (IceRuin).</summary>
		ScythianEmerald = 73,
		/// <summary>Hornice (fantasy, glows): blue ice prisms with horn-like growth rings, glowing with the wind (IceSheet).</summary>
		Hornice = 74,
		/// <summary>Jadeice: bottle-green banded marine-ice prisms (IceShelf).</summary>
		Jadeice = 75,
		/// <summary>Swellglass (fantasy, glows): sea-green glass prisms that brighten and dim with the swell (IceShelf).</summary>
		Swellglass = 76,
		/// <summary>Chrysolite: olivine, the medieval chrysolite: bottle-green granular nodules (ImpactBasin).</summary>
		Chrysolite = 77,
		/// <summary>MansionPearl (fantasy, glows): milky nacre-white nodules each lit on its own mansion night (ImpactBasin).</summary>
		MansionPearl = 78,
		/// <summary>WatermelonTourmaline: striated prisms, a pink core in a green rind, in white pegmatite (Jungle).</summary>
		WatermelonTourmaline = 79,
		/// <summary>Leafglass (fantasy, glows): translucent leaf-green glassy plates veined like leaf skeletons (Jungle).</summary>
		Leafglass = 80,
		/// <summary>Specularite: specular hematite: mirror-black glittering plates with a steel sheen (JungleRuins).</summary>
		Specularite = 81,
		/// <summary>Glyphjade (fantasy, glows): translucent green-white crystal with dark glyph-shaped inclusions (JungleRuins).</summary>
		Glyphjade = 82,
		/// <summary>Zircon: stubby honey-brown bipyramids, adamantine (JungleTemple).</summary>
		Zircon = 83,
		/// <summary>Monsoonglass (fantasy, glows): rain-grey translucent blades with silver streaks, bright in the wet season (JungleTemple).</summary>
		Monsoonglass = 84,
		/// <summary>Smithsonite: zinc grape-crusts in pastel teal, waxy (Karst).</summary>
		Smithsonite = 85,
		/// <summary>Mistjade (fantasy, glows): celadon prisms holding a slow white mist (Karst).</summary>
		Mistjade = 86,
		/// <summary>CavePearl: polished cream-white calcite spheres nested in rimstone cups (Lake).</summary>
		CavePearl = 87,
		/// <summary>Nixglass (fantasy, glows): water-clear blue-green prisms that look wet, a slow ripple glow (Lake).</summary>
		Nixglass = 88,
		/// <summary>Thenardite: cave cotton: white sulphate efflorescence crusts and hair tufts (Volcanic's own look: hair-needles on tube walls) (LavaTube, Volcanic).</summary>
		Thenardite = 89,
		/// <summary>OlivineBomb: bottle-green glassy olivine nodules bursting from broken basalt bombs (LavaTube).</summary>
		OlivineBomb = 90,
		/// <summary>Magecstone (fantasy, glows): shards of the captive sun: white-gold prisms with a warm glow (LavaTube).</summary>
		Magecstone = 91,
		/// <summary>Marcasite: brassy cockscomb spear crystals with a blue-violet tarnish (Mangrove).</summary>
		Marcasite = 92,
		/// <summary>Nuru (fantasy, glows): milky blue-white blades that sparkle like disturbed seafire (Mangrove).</summary>
		Nuru = 93,
		/// <summary>Simetite: Sicilian red amber: translucent wine-red to orange nodules (MarblePalace).</summary>
		Simetite = 94,
		/// <summary>Fatastone (fantasy, glows): mirage quartz: clear prisms whose cores hold a faint moving image of towers (MarblePalace).</summary>
		Fatastone = 95,
		/// <summary>Waxspar: soft waxy colourless-cream rhombs (MethaneLake).</summary>
		Waxspar = 96,
		/// <summary>Adamant: lodestone octahedra, black steel with a violet temper sheen (MethaneLake).</summary>
		Adamant = 97,
		/// <summary>Orphanstone (fantasy, glows): a lone wine-red carbuncle with a slow warm breath (MethaneLake).</summary>
		Orphanstone = 98,
		/// <summary>Cristobalite: snow-white spherule clusters in vesicles and glass (MoltenSurface).</summary>
		Cristobalite = 99,
		/// <summary>BrimstoneAmber: glassy blood-red quenched-sulphur beads and crusts (MoltenSurface).</summary>
		BrimstoneAmber = 100,
		/// <summary>Dracontite (fantasy, glows): a dragon's-brain stone: ember-red prisms with a star core (MoltenSurface).</summary>
		Dracontite = 101,
		/// <summary>Hydrophane: milky-white to honey opal nodules with rainbow fire flecks (MountainSlope).</summary>
		Hydrophane = 102,
		/// <summary>Moonstone (fantasy, glows): tabular sanidine with a drifting blue sheen (MountainSlope).</summary>
		Moonstone = 103,
		/// <summary>NitrogenSpar: hexagonal nitrogen ice, colourless-to-peach and waxy (NitrogenIceField).</summary>
		NitrogenSpar = 104,
		/// <summary>Tearglass (fantasy, glows): clusters of clear teardrops with a faint breathing glow (NitrogenIceField).</summary>
		Tearglass = 105,
		/// <summary>DesertRose: sand-included gypsum rosettes, buff-pink (Oasis).</summary>
		DesertRose = 106,
		/// <summary>Alum: water-clear octahedra with a white efflorescent bloom (Oasis).</summary>
		Alum = 107,
		/// <summary>Oraclestone (fantasy, glows): honey-gold translucent calcite holding a ram-horn spiral, glowing warm at dusk (Oasis).</summary>
		Oraclestone = 108,
		/// <summary>Siderite: iron spar: honey-brown saddle rhombs on cream ankerite (Ocean).</summary>
		Siderite = 109,
		/// <summary>GalernaStone (fantasy, glows): smoky storm-violet quartz prisms with an ember core that brightens as the wind rises (Ocean).</summary>
		GalernaStone = 110,
		/// <summary>Wispglass (fantasy, glows): pale green-gold nodules with a slow flickering glow (PeatBog).</summary>
		Wispglass = 111,
		/// <summary>Stillglass (fantasy, glows): clear prisms each holding a water drop or snowflake stopped mid-fall, a steady glow (PermanentIce).</summary>
		Stillglass = 112,
		/// <summary>BariteRose: rose-rock rosettes, reddish-brown with sand-dusted petals (Plains).</summary>
		BariteRose = 113,
		/// <summary>SatinSpar: silky white fibrous gypsum veins with a chatoyant sheen (Plains).</summary>
		SatinSpar = 114,
		/// <summary>Sunspoke (fantasy, glows): clear quartz shot through with golden needles radiating like wheel spokes, glowing warm gold (Plains).</summary>
		Sunspoke = 115,
		/// <summary>Antozonite: the fetid spar: fluorite darkened to violet-black with violet edges (RadiationPlain).</summary>
		Antozonite = 116,
		/// <summary>Septentrion (fantasy, glows): seven-rayed star clusters of clear white crystal (RadiationPlain).</summary>
		Septentrion = 117,
		/// <summary>Balas: the medieval balas ruby: pink spinel octahedra (RegolithPlain).</summary>
		Balas = 118,
		/// <summary>Levanite (fantasy, glows): smoky violet-grey bladed prisms bright at sunrise and sunset (RegolithPlain).</summary>
		Levanite = 119,
		/// <summary>Sapphire: deep cornflower corundum barrels tapering both ends (Rille).</summary>
		Sapphire = 120,
		/// <summary>Shamirspar (fantasy, glows): clear blades split clean as glass, a violet-white band sliding along each (Rille).</summary>
		Shamirspar = 121,
		/// <summary>Agate: banded chalcedony geodes, grey-blue, rust and milk round a rock-crystal core (River).</summary>
		Agate = 122,
		/// <summary>RhinegoldSeam (fantasy, glows): old-gold threads in milky quartz, a warm shimmer like light through moving water (River).</summary>
		RhinegoldSeam = 123,
		/// <summary>Kyanite: sky-blue bladed sprays in white quartz pods (RockyCoast).</summary>
		Kyanite = 124,
		/// <summary>Haarglass (fantasy, glows): finfolk glass: frosted sea-green prisms holding a drowned light (RockyCoast).</summary>
		Haarglass = 125,
		/// <summary>Lajvard: lapis lazuli: deep ultramarine veins and nodules flecked with gold pyrite (RockyTerrain).</summary>
		Lajvard = 126,
		/// <summary>Shabchiragh (fantasy, glows): the night-lamp gem: rose-red spinel octahedra glowing warm after sunset (RockyTerrain).</summary>
		Shabchiragh = 127,
		/// <summary>Wollastonite: white silky radiating sprays (RunawayGreenhousePlain).</summary>
		Wollastonite = 128,
		/// <summary>Pyrrhotite: bronze-brown metallic hexagonal plates (RunawayGreenhousePlain).</summary>
		Pyrrhotite = 129,
		/// <summary>Sibylleaf (fantasy, glows): thin curved translucent crimson-amber leaves (RunawayGreenhousePlain).</summary>
		Sibylleaf = 130,
		/// <summary>Halite: water-clear to pale pink hopper cubes (SaltFlat).</summary>
		Halite = 131,
		/// <summary>Sylvite: translucent brick-red cubes (SaltFlat).</summary>
		Sylvite = 132,
		/// <summary>Embersalt (fantasy, glows): amber-rose cubes with a warm glow from dusk (SaltFlat).</summary>
		Embersalt = 133,
		/// <summary>Reefgold: milky quartz prisms with leafy native gold on their faces (Savanna).</summary>
		Reefgold = 134,
		/// <summary>Emerald: deep green hexagonal prisms in black schist (Savanna).</summary>
		Emerald = 135,
		/// <summary>Rainglass (fantasy, glows): blue-green prisms that brighten with the rain (Savanna).</summary>
		Rainglass = 136,
		/// <summary>Goblinfire (fantasy, glows): blue-green cold crystals whose glow hops from point to point (Scree).</summary>
		Goblinfire = 137,
		/// <summary>Peridot: olive-green glassy grains in black basalt bombs (Scrubland).</summary>
		Peridot = 138,
		/// <summary>Sunshell (fantasy, glows): an iridescent spiral shell-crystal glowing warm after sunset (Scrubland).</summary>
		Sunshell = 139,
		/// <summary>Lapastone (fantasy, glows): water-clear blue-white prisms through which pale threads of light dart (Seamount).</summary>
		Lapastone = 140,
		/// <summary>Analcime: water-clear to milky zeolite trapezohedra from pillow vesicles (Seamount).</summary>
		Analcime = 141,
		/// <summary>Rutilquartz: Venus hair: water-clear prisms pierced by golden needles (Snow).</summary>
		Rutilquartz = 142,
		/// <summary>MidwinterSpar (fantasy, glows): smoke-violet bladed spar with a white core, brightest in the polar night (Snow).</summary>
		MidwinterSpar = 143,
		/// <summary>Dioptase: vivid emerald-green rhombohedral prisms on white quartz (Steppe).</summary>
		Dioptase = 144,
		/// <summary>WolfsEye (fantasy, glows): smoky-amber chatoyant chalcedony nodules with a moving bright eye (Steppe).</summary>
		WolfsEye = 145,
		/// <summary>Covellite: indigo-blue iridescent hexagonal plates (SubsurfaceOceanVent).</summary>
		Covellite = 146,
		/// <summary>Anhydrite: white-lilac bladed prisms (SubsurfaceOceanVent).</summary>
		Anhydrite = 147,
		/// <summary>Gledstone (fantasy, glows): ember-red sulphide prisms glowing at night (SubsurfaceOceanVent).</summary>
		Gledstone = 148,
		/// <summary>NativeSulphur: lemon-amber translucent bipyramids, resinous (SulphurFlats).</summary>
		NativeSulphur = 149,
		/// <summary>Bluebrim (fantasy, glows): sulphur bipyramids holding a flickering cold blue flame (SulphurFlats).</summary>
		Bluebrim = 150,
		/// <summary>Leadfeather: dendritic metal-frost feathers, lead-silver (SulphuricCloudDeck).</summary>
		Leadfeather = 151,
		/// <summary>Pyrite: brass striated cube druses (Swamp's own look: pyrite suns in black shale) (SulphuricCloudDeck, Swamp).</summary>
		Pyrite = 152,
		/// <summary>Cloudheart (fantasy, glows): milk-saffron translucent spheroids, brighter as the deck sinks (SulphuricCloudDeck).</summary>
		Cloudheart = 153,
		/// <summary>Mireopal (fantasy, glows): milky green-indigo opal nodules with a slow drifting inner glow (Swamp).</summary>
		Mireopal = 154,
		/// <summary>Aetite: eagle-stone: hollow limonite geodes, a powdery ochre hollow round a loose core (SwampCave).</summary>
		Aetite = 155,
		/// <summary>BollasEye (fantasy, glows): chatoyant honey-green ovoids with one slit of light across (SwampCave).</summary>
		BollasEye = 156,
		/// <summary>Hauyne: sapphire-blue small dodecahedra in pale pumice (SwampRuins).</summary>
		Hauyne = 157,
		/// <summary>Leucite: white glassy trapezohedra in grey lava (SwampRuins).</summary>
		Leucite = 158,
		/// <summary>Saeculite (fantasy, glows): the ages: a smoky-honey prism of ten bands, one lit a day (SwampRuins).</summary>
		Saeculite = 159,
		/// <summary>TrapicheEmerald: green beryl prisms whose sections show a six-spoked black star (SwampTemple).</summary>
		TrapicheEmerald = 160,
		/// <summary>Mistberyl (fantasy, glows): pale green-white translucent columns, brighter as the mist thickens (SwampTemple).</summary>
		Mistberyl = 161,
		/// <summary>Quicksilver: mercury frozen solid: mirror-bright rhombohedra (TholinPlain).</summary>
		Quicksilver = 162,
		/// <summary>Hazeamber (fantasy, glows): polymerised haze: translucent orange botryoids glowing warm at night (TholinPlain).</summary>
		Hazeamber = 163,
		/// <summary>Epsomite: clear needles with rust-stained roots (TidalFracture).</summary>
		Epsomite = 164,
		/// <summary>Sakhrastone (fantasy, glows): dust of the Rock: deep ruby tabular plates glowing cold ruby (TidalFracture).</summary>
		Sakhrastone = 165,
		/// <summary>Labradorite: dark grey blades with a blue-green-gold schiller flash (Tundra).</summary>
		Labradorite = 166,
		/// <summary>Aurorite (fantasy, glows): glassy shards glowing a slow green-to-violet shift (Tundra).</summary>
		Aurorite = 167,
		/// <summary>Ikaite: cold-seafloor carbonate: chalky-white spiky stellate clusters (its own look: brown-grey glendonite pseudomorphs) (UnderwaterCanyon).</summary>
		Ikaite = 168,
		/// <summary>Tremorspar (fantasy, glows): smoky-lavender porcelain blades flaring with the canyon's tremors (UnderwaterCanyon).</summary>
		Tremorspar = 169,
		/// <summary>Demantoid: vivid emerald-green garnet dodecahedra in serpentine, with rainbow fire (Valley).</summary>
		Demantoid = 170,
		/// <summary>Sphene: titanite: flat wedge envelope twins, honey-green, adamantine (Valley).</summary>
		Sphene = 171,
		/// <summary>Laurinite (fantasy, glows): the rose garden's roses: rose-pink crystal rosettes of curved petals with a soft rose glow (Valley).</summary>
		Laurinite = 172,
		/// <summary>SulphurNeedles: lemon sulphur needles in vugs (the sulphur Crystals formation) (VolcanicCave).</summary>
		SulphurNeedles = 173,
		/// <summary>Smokestone: obsidian mirror-shards under whose gloss a slow smoke curls (VolcanicCave).</summary>
		Smokestone = 174,
		/// <summary>Hearthglass (fantasy, glows): ember-red glass shards with a molten core (Volcanic).</summary>
		Hearthglass = 175,
		/// <summary>Hieratite: white-to-grey fluosilicate crusts and tiny octahedra with a faint lilac cast (VolcanicTemple).</summary>
		Hieratite = 176,
		/// <summary>Forgeheart (fantasy, glows): ember-orange prisms with an anvil-dark core (VolcanicTemple).</summary>
		Forgeheart = 177,
		/// <summary>Goethite: black botryoidal iron crust with an iridescent skin (Wasteland).</summary>
		Goethite = 178,
		/// <summary>Weepglass (fantasy, glows): smoky quartz points with a red drop of light at the tip after sunset (Wasteland).</summary>
		Weepglass = 179,
		/// <summary>Carnelian: translucent orange-red chalcedony nodules and banded geode halves (Wetlands).</summary>
		Carnelian = 180,
		/// <summary>Abzulite (fantasy, glows): deep sweet-water-blue prisms with a slow inner tide (Wetlands).</summary>
		Abzulite = 181,
		/// <summary>FlintGeode: hollow flint lined with chalcedony and drusy quartz (Woodland).</summary>
		FlintGeode = 182,
	}

	/// <summary>One crystal a biome's caves grow and how often among its others (<see cref="BiomeTemplate.CaveCrystals"/>).</summary>
	[Serializable]
	public struct BiomeCaveCrystal
	{
		[Tooltip("The crystal: its habit (prisms, geodes, druses, blades, nodules…), its colour, and whether it glows.")]
		public BiomeCaveCrystalKind Kind;
		[Tooltip("How often a chamber's crystals are this kind among the biome's others (0 never).")]
		[Min(0f)] public float Weight;
		[Tooltip("Which of the kind's looks it grows in (0 its own; 1 on a biome's own look of a kind other biomes share: its habit and colour, the art generator's CaveMineralLooks).")]
		public byte Look;

		public BiomeCaveCrystal(BiomeCaveCrystalKind kind, float weight, byte look = 0)
		{
			Kind = kind;
			Weight = weight;
			Look = look;
		}

		/// <summary>The entry as the authoring marks and reports it: "Kind×weight", a look other than the kind's own as "Kind#look×weight".</summary>
		public override string ToString() => Kind + (Look == 0 ? "" : "#" + Look) + "×" + Weight.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>The cave-crystal rules that do not depend on a template asset.</summary>
	public static class BiomeCaveCrystals
	{
		/// <summary>What every biome's caves grew until 2026-10-16: selenite alone. A fresh list each call.</summary>
		public static List<BiomeCaveCrystal> Default() => new List<BiomeCaveCrystal> { new BiomeCaveCrystal(BiomeCaveCrystalKind.Selenite, 1f) };

		/// <summary>
		/// The kinds that glow by themselves (their own light, day and night): what lines a cave's ways when a biome grows them
		/// and what a crystal way-light post carries. The others glitter under a lamp; selenite keeps the cave light it always
		/// had beside it.
		/// </summary>
		public static bool Glows(BiomeCaveCrystalKind kind) => glowing[(byte)kind];

		/// <summary>The kinds that glow (the first three since 2026-10-16, the drafts' fantasy kinds since 2026-10-26), as a table by value.</summary>
		private static readonly bool[] glowing = GlowTable(
			BiomeCaveCrystalKind.Amberlight, BiomeCaveCrystalKind.Wyrdstone, BiomeCaveCrystalKind.Sunstone,
			BiomeCaveCrystalKind.Lodeheart, BiomeCaveCrystalKind.Hadalite, BiomeCaveCrystalKind.Starfrost, BiomeCaveCrystalKind.Stormglass,
			BiomeCaveCrystalKind.Perilight, BiomeCaveCrystalKind.Hotarubi, BiomeCaveCrystalKind.Pearlspar, BiomeCaveCrystalKind.Wyrmglass,
			BiomeCaveCrystalKind.Echospar, BiomeCaveCrystalKind.Bellspar, BiomeCaveCrystalKind.Lagoonglass, BiomeCaveCrystalKind.Starglass,
			BiomeCaveCrystalKind.Eitrglass, BiomeCaveCrystalKind.Stellamaris, BiomeCaveCrystalKind.Sunglass, BiomeCaveCrystalKind.Phaseglass,
			BiomeCaveCrystalKind.Ebbspar, BiomeCaveCrystalKind.Fogpearl, BiomeCaveCrystalKind.Farrstone, BiomeCaveCrystalKind.Dalistone,
			BiomeCaveCrystalKind.Solarsteinn, BiomeCaveCrystalKind.Nagamani, BiomeCaveCrystalKind.Skyglass, BiomeCaveCrystalKind.Gueststar,
			BiomeCaveCrystalKind.Heartgarnet, BiomeCaveCrystalKind.Chudfire, BiomeCaveCrystalKind.Hyalite, BiomeCaveCrystalKind.Nightpearl,
			BiomeCaveCrystalKind.Hornice, BiomeCaveCrystalKind.Swellglass, BiomeCaveCrystalKind.MansionPearl, BiomeCaveCrystalKind.Leafglass,
			BiomeCaveCrystalKind.Glyphjade, BiomeCaveCrystalKind.Monsoonglass, BiomeCaveCrystalKind.Mistjade, BiomeCaveCrystalKind.Nixglass,
			BiomeCaveCrystalKind.Magecstone, BiomeCaveCrystalKind.Nuru, BiomeCaveCrystalKind.Fatastone, BiomeCaveCrystalKind.Orphanstone,
			BiomeCaveCrystalKind.Dracontite, BiomeCaveCrystalKind.Moonstone, BiomeCaveCrystalKind.Tearglass, BiomeCaveCrystalKind.Oraclestone,
			BiomeCaveCrystalKind.GalernaStone, BiomeCaveCrystalKind.Wispglass, BiomeCaveCrystalKind.Stillglass, BiomeCaveCrystalKind.Sunspoke,
			BiomeCaveCrystalKind.Septentrion, BiomeCaveCrystalKind.Levanite, BiomeCaveCrystalKind.Shamirspar, BiomeCaveCrystalKind.RhinegoldSeam,
			BiomeCaveCrystalKind.Haarglass, BiomeCaveCrystalKind.Shabchiragh, BiomeCaveCrystalKind.Sibylleaf, BiomeCaveCrystalKind.Embersalt,
			BiomeCaveCrystalKind.Rainglass, BiomeCaveCrystalKind.Goblinfire, BiomeCaveCrystalKind.Sunshell, BiomeCaveCrystalKind.Lapastone,
			BiomeCaveCrystalKind.MidwinterSpar, BiomeCaveCrystalKind.WolfsEye, BiomeCaveCrystalKind.Gledstone, BiomeCaveCrystalKind.Bluebrim,
			BiomeCaveCrystalKind.Cloudheart, BiomeCaveCrystalKind.Mireopal, BiomeCaveCrystalKind.BollasEye, BiomeCaveCrystalKind.Saeculite,
			BiomeCaveCrystalKind.Mistberyl, BiomeCaveCrystalKind.Hazeamber, BiomeCaveCrystalKind.Sakhrastone, BiomeCaveCrystalKind.Aurorite,
			BiomeCaveCrystalKind.Tremorspar, BiomeCaveCrystalKind.Laurinite, BiomeCaveCrystalKind.Hearthglass, BiomeCaveCrystalKind.Forgeheart,
			BiomeCaveCrystalKind.Weepglass, BiomeCaveCrystalKind.Abzulite);

		private static bool[] GlowTable(params BiomeCaveCrystalKind[] kinds)
		{
			var table = new bool[256];
			foreach (BiomeCaveCrystalKind kind in kinds)
			{
				table[(byte)kind] = true;
			}
			return table;
		}

		/// <summary>
		/// The look a list grows a kind in (<see cref="BiomeCaveCrystal.Look"/>): its first entry of the kind with weight, else 0 (the
		/// kind's own). A biome grows one look of a kind, so whatever picked the kind from the list draws it in this look.
		/// </summary>
		public static byte LookOf(IReadOnlyList<BiomeCaveCrystal> crystals, BiomeCaveCrystalKind kind)
		{
			if (crystals != null)
			{
				foreach (BiomeCaveCrystal c in crystals)
				{
					if (c.Kind == kind && c.Weight > 0f)
					{
						return c.Look;
					}
				}
			}
			return 0;
		}

		/// <summary>The sum of a list's positive weights.</summary>
		public static float Total(IReadOnlyList<BiomeCaveCrystal> crystals, bool glowingOnly = false)
		{
			float total = 0f;
			if (crystals != null)
			{
				foreach (BiomeCaveCrystal c in crystals)
				{
					if (c.Weight > 0f && (!glowingOnly || Glows(c.Kind)))
					{
						total += c.Weight;
					}
				}
			}
			return total;
		}

		/// <summary>
		/// The kind a roll in [0, 1) picks from a list by weight (entries of no weight never; only the glowing ones when
		/// <paramref name="glowingOnly"/>); false when nothing can be picked. Pure and deterministic: a list of one kind needs
		/// no roll at all (<see cref="NeedsRoll"/>).
		/// </summary>
		public static bool TryPick(IReadOnlyList<BiomeCaveCrystal> crystals, float roll, out BiomeCaveCrystalKind kind, bool glowingOnly = false)
		{
			kind = BiomeCaveCrystalKind.Selenite;
			float total = Total(crystals, glowingOnly);
			if (total <= 0f)
			{
				return false;
			}
			float at = Mathf.Clamp01(roll) * total, run = 0f;
			bool found = false;
			foreach (BiomeCaveCrystal c in crystals)
			{
				if (c.Weight <= 0f || glowingOnly && !Glows(c.Kind))
				{
					continue;
				}
				kind = c.Kind;
				found = true;
				run += c.Weight;
				if (at < run)
				{
					return true;
				}
			}
			return found;
		}

		/// <summary>Whether picking from the list needs a roll: more than one entry of weight (else the one kind, or none).</summary>
		public static bool NeedsRoll(IReadOnlyList<BiomeCaveCrystal> crystals, bool glowingOnly = false)
		{
			int count = 0;
			if (crystals != null)
			{
				foreach (BiomeCaveCrystal c in crystals)
				{
					if (c.Weight > 0f && (!glowingOnly || Glows(c.Kind)) && ++count > 1)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// The crystal a biome's crystal way-light post carries: its heaviest glowing kind, else its heaviest kind (the first
		/// of equal weights), else selenite.
		/// </summary>
		public static BiomeCaveCrystalKind PostCrystal(IReadOnlyList<BiomeCaveCrystal> crystals)
		{
			BiomeCaveCrystalKind best = BiomeCaveCrystalKind.Selenite;
			float weight = 0f;
			bool glowing = false;
			if (crystals != null)
			{
				foreach (BiomeCaveCrystal c in crystals)
				{
					if (c.Weight <= 0f)
					{
						continue;
					}
					bool g = Glows(c.Kind);
					if (g && !glowing || g == glowing && c.Weight > weight)
					{
						best = c.Kind;
						weight = c.Weight;
						glowing = g;
					}
				}
			}
			return best;
		}
	}
}
