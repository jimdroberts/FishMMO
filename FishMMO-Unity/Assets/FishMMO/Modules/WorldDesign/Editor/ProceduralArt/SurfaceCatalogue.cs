#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The ground families biomes paint with, by name. Constants, so a spec entry that misspells
	/// one is a compile error rather than a silently unpainted biome.
	/// </summary>
	/// <remarks>
	/// The name is the identity of everything derived from it: the terrain layer
	/// <c>Ground_&lt;Name&gt;.terrainlayer</c> and its textures <c>Ground_&lt;Name&gt;_Albedo.png</c>,
	/// <c>_Normal.png</c> and <c>_Mask.png</c>. Renaming one orphans those files and changes the
	/// file name a LOCAL override must use, so a name, once committed, stays.
	/// </remarks>
	public static class Ground
	{
		public const string Grass = "Grass";
		public const string GrassDry = "GrassDry";
		public const string GrassMeadow = "GrassMeadow";
		public const string Moss = "Moss";
		public const string ForestFloor = "ForestFloor";
		public const string NeedleLitter = "NeedleLitter";
		public const string JungleFloor = "JungleFloor";
		public const string Soil = "Soil";
		public const string Mud = "Mud";
		public const string Clay = "Clay";
		public const string Sandstone = "Sandstone";
		public const string Sand = "Sand";
		public const string SandBeach = "SandBeach";
		public const string Gravel = "Gravel";
		public const string Pebbles = "Pebbles";
		public const string Rock = "Rock";
		public const string CliffRock = "CliffRock";
		public const string Basalt = "Basalt";
		public const string Lava = "Lava";
		public const string Ash = "Ash";
		public const string Snow = "Snow";
		public const string Ice = "Ice";
		public const string SaltCrust = "SaltCrust";
		public const string Regolith = "Regolith";
		public const string Sulphur = "Sulphur";
		public const string Peat = "Peat";
		public const string Limestone = "Limestone";
		public const string Lichen = "Lichen";
		public const string Silt = "Silt";
		public const string Coral = "Coral";
		public const string Tholin = "Tholin";
		public const string Frost = "Frost";
		public const string CrackedEarth = "CrackedEarth";
		public const string Flagstone = "Flagstone";

		// ── Added by the vegetation expansion (2026-10-10) ──
		// No biome paints with one until the spec names it.

		/// <summary>Living sphagnum carpet of a raised bog: red, green and ochre cushions in hummocks, wet between.</summary>
		public const string Sphagnum = "Sphagnum";
		/// <summary>Purple-brown wiry moorland heath (ling heather), flecked with its flowers.</summary>
		public const string Heath = "Heath";
		/// <summary>Grey-white siliceous sinter round hot springs, in crusted rims streaked orange and brown by microbial mats.</summary>
		public const string Sinter = "Sinter";
		/// <summary>Loose black and oxidised-red cinder: the lapilli and scoria of a cinder cone's flanks.</summary>
		public const string Cinder = "Cinder";
		/// <summary>Ropy, glassy pahoehoe basalt: a lava flow's skin dragged into wrinkled folds as it set.</summary>
		public const string Pahoehoe = "Pahoehoe";
		/// <summary>Reg, desert pavement: a mosaic of wind-sorted stones blackened by desert varnish on a pale silt.</summary>
		public const string Reg = "Reg";
		/// <summary>Fine bright dust with low ripples: an airless or thin-aired world's dust ponds and mantles.</summary>
		public const string Dust = "Dust";
		/// <summary>Europa's lineae: bright ice cut by bands of rust-brown salts along its fractures.</summary>
		public const string Lineae = "Lineae";
		/// <summary>"Metal frost": the bright metallic sheen on Venus's highlands, over dark basalt.</summary>
		public const string MetalFrost = "MetalFrost";
		/// <summary>Titan's organic dune sand: dark orange-brown tholin grains in long ripples.</summary>
		public const string TholinDune = "TholinDune";

		// ── Paths (2026-10-10) ── The ground a way wears down to: what is under a biome's cover, packed by feet and
		// wheels (SurfaceCatalogue.PathGroundFor). Never painted by a biome's splat; the path overlay draws them.

		/// <summary>Trodden loam under grass and moss: dark, packed, a little grit worked up through it.</summary>
		public const string PathLoam = "PathLoam";
		/// <summary>Dusty pale earth of dry grass and steppe, small stones showing.</summary>
		public const string PathDryEarth = "PathDryEarth";
		/// <summary>Packed red earth of clay, laterite and red-rock country.</summary>
		public const string PathRedEarth = "PathRedEarth";
		/// <summary>Sand packed flat by feet and hooves, its ripples gone, darker grit in it.</summary>
		public const string PathSand = "PathSand";
		/// <summary>Grit and broken stone over rock and gravel ground: the way a scree or a moor path wears.</summary>
		public const string PathGrit = "PathGrit";
		/// <summary>Trampled snow: packed, glazed, dirtied with grit and earth.</summary>
		public const string PathSnow = "PathSnow";
		/// <summary>Wet trodden mud of bogs, marshes and jungle: dark and shining.</summary>
		public const string PathMud = "PathMud";
		/// <summary>Packed ash and cinder.</summary>
		public const string PathAsh = "PathAsh";
		/// <summary>A salt crust broken and packed into a grey track.</summary>
		public const string PathSalt = "PathSalt";
		/// <summary>A forest path: dark packed earth with the odd leaf and needle on it.</summary>
		public const string PathLitter = "PathLitter";

		// ── Jungle + Bamboo Forest (biome rounds, 2026-10-26): the Edo-Yoruba rainforest's and the Heian bamboo hills' own grounds. ──

		/// <summary>Bronze leaf litter: big leathery bronze and black-brown leaves over red earth, white mycelium threads, winged seeds.</summary>
		public const string JungleFloorBronze = "JungleFloorBronze";
		/// <summary>Red forest soil of the farm-bush clearings: crumbly laterite earth, pisoliths and slash-and-burn charcoal.</summary>
		public const string SoilRedForest = "SoilRedForest";
		/// <summary>Ravine moss and liverwort: thalloid lobes and blue-green spikemoss over wet black rock.</summary>
		public const string MossRavine = "MossRavine";
		/// <summary>Raffia-swamp mud: black leaf mats and raffia fruit scales in standing film puddles.</summary>
		public const string MudRaffiaSwamp = "MudRaffiaSwamp";
		/// <summary>An ironstone pavement (bowal): fractured plates with vermiform worm-holes, pisolite lag and ochre crust.</summary>
		public const string LateriteIronstone = "LateriteIronstone";
		/// <summary>An inselberg whaleback: grey exfoliation sheets with black rain streaks and orange crust.</summary>
		public const string GraniteWhaleback = "GraniteWhaleback";
		/// <summary>A wet gneiss gorge wall: banded migmatite, fern and moss ledges, black seeps, a red laterite brow.</summary>
		public const string CliffGneissGorge = "CliffGneissGorge";
		/// <summary>A quartz and ironstone stream bed: milky quartz, ironstone and gneiss cobbles in gold-brown sand.</summary>
		public const string SandQuartzStream = "SandQuartzStream";
		/// <summary>A leaf-silt pool floor: drowned leaves and palm-nut husks in dark silt.</summary>
		public const string MudLeafSilt = "MudLeafSilt";
		/// <summary>A broad red avenue: swept packed laterite, rain-cut runnels along the camber, pisolite lag at the edges.</summary>
		public const string LateriteAvenue = "LateriteAvenue";
		/// <summary>A forest root trail: trodden red-brown earth crossed by root ladders.</summary>
		public const string PathRootTrail = "PathRootTrail";
		/// <summary>A potsherd pavement: sherds set on edge in herringbone and fans, quartz pebble borders, red clay grout.</summary>
		public const string PotsherdPavement = "PotsherdPavement";
		/// <summary>A way laid with cut raffia fronds and split poles across the mud.</summary>
		public const string PathFrondMat = "PathFrondMat";
		/// <summary>Gold quartz-mica stream sand with ironstone grains.</summary>
		public const string SandMicaGold = "SandMicaGold";
		/// <summary>Pisolite and quartz gravel, glossy dark red-brown pea-stones among milky quartz.</summary>
		public const string GravelPisolite = "GravelPisolite";
		/// <summary>A bamboo-grove floor: narrow straw lance leaves, culm-sheath scraps and culm stub rings.</summary>
		public const string ForestFloorBambooGrove = "ForestFloorBambooGrove";
		/// <summary>A temple moss lawn: haircap stars and lime sheet moss, fallen maple stars.</summary>
		public const string MossSugigoke = "MossSugigoke";
		/// <summary>Sugi and red-pine duff: copper needles, spray tips and cones.</summary>
		public const string NeedleLitterSugi = "NeedleLitterSugi";
		/// <summary>Kuroboku, the black andosol: crumbly dark tilth with yellow grit specks and root threads.</summary>
		public const string SoilKuroboku = "SoilKuroboku";
		/// <summary>Andesite talus: platy grey blocks, moss on their top faces.</summary>
		public const string RockAndesiteTalus = "RockAndesiteTalus";
		/// <summary>Laurel-ravine litter: glossy leathery leaves, shii catkin fall and camellia petals.</summary>
		public const string ForestFloorLaurel = "ForestFloorLaurel";
		/// <summary>A platy andesite wall (the folding screens): vertical plates, black seeps, moss on the ledges.</summary>
		public const string CliffAndesitePlated = "CliffAndesitePlated";
		/// <summary>An andesite cobble and granite grus stream bed.</summary>
		public const string GravelAndesiteBed = "GravelAndesiteBed";
		/// <summary>Kuroboku pond silt with sunken bamboo leaves.</summary>
		public const string SiltKurobokuPond = "SiltKurobokuPond";
		/// <summary>Masa: tamped pale decomposed-granite road with mica glints.</summary>
		public const string RoadMasa = "RoadMasa";
		/// <summary>A bamboo-leaf woodland path: straw leaves over trodden earth, root steps.</summary>
		public const string PathBambooLeaf = "PathBambooLeaf";
		/// <summary>Ishidatami: irregular andesite slabs set on edge, moss in the joints, worn-smooth crowns.</summary>
		public const string FlagstoneIshidatami = "FlagstoneIshidatami";
		/// <summary>Stepping-stones: dressed flat andesite discs set in moss.</summary>
		public const string PathTobiishi = "PathTobiishi";
		/// <summary>Andesite block stair treads with moss risers.</summary>
		public const string PathIshidan = "PathIshidan";
		/// <summary>Masa grus sand: mica, pink feldspar and black biotite.</summary>
		public const string SandMasaGrus = "SandMasaGrus";
		/// <summary>Arc gravel: andesite, white granite, green schist and the odd red chert.</summary>
		public const string GravelArc = "GravelArc";

		// ── Forest (biome round, 2026-10-26): the primeval beech-and-fir forest's own grounds (decision 12; until I9's variants
		// of a recipe, each its own recipe). ──

		/// <summary>Beech-mast litter: copper beech leaves, tan husks and acorns, flecks of moss on a dark humus.</summary>
		public const string ForestFloorBeech = "ForestFloorBeech";
		/// <summary>Cushion moss over roots and boulders (Polytrichum, Thamnium), dark and bright greens in hummocks.</summary>
		public const string MossCushion = "MossCushion";
		/// <summary>A ramsons carpet: wild-garlic strap leaves and white star flowers over damp valley soil.</summary>
		public const string ForestFloorRamsons = "ForestFloorRamsons";
		/// <summary>Silver-fir duff: soft flat grey-brown needles and cone scales, greyer than a spruce's red duff.</summary>
		public const string NeedleLitterFir = "NeedleLitterFir";
		/// <summary>A root-laced humus bank: dark crumbly soil threaded with pale roots, sandstone grit.</summary>
		public const string SoilRootHumus = "SoilRootHumus";
		/// <summary>Mossy grey-buff forest sandstone, soft-bedded, liverwort and iron-stained.</summary>
		public const string SandstoneForest = "SandstoneForest";
		/// <summary>A forest sandstone face: cross-bedded grey-buff, green seep streaks, moss on its ledges.</summary>
		public const string SandstoneForestFace = "SandstoneForestFace";
		/// <summary>A forest pool's black leaf-mould ooze with sunken leaves.</summary>
		public const string MudLeafMould = "MudLeafMould";
		/// <summary>Tea-brown brook gravel, rounded buff and grey, drowned beech leaves caught in it.</summary>
		public const string GravelBrook = "GravelBrook";
		/// <summary>A trodden beech-leaf path: crushed copper leaves, bare loam showing through.</summary>
		public const string PathBeechLitter = "PathBeechLitter";
		/// <summary>A holloway: sunken rutted loam, leaf, root and flint grit.</summary>
		public const string PathHolloway = "PathHolloway";
		/// <summary>Mossy sub-rectangular sandstone setts, moss in the joints.</summary>
		public const string FlagstoneSandstone = "FlagstoneSandstone";
		/// <summary>Humic brook sand, mica and leaf bits.</summary>
		public const string SandHumic = "SandHumic";
		/// <summary>Sandstone and black flint shingle on a forest brook's bars.</summary>
		public const string GravelSandstoneFlint = "GravelSandstoneFlint";

		// ── Woodland (biome round, 2026-10-26): the managed lowland wood's own grounds, on chalk and flint. ──

		/// <summary>Grazed wood-pasture turf of bent and fescue, fallen oak leaves, acorn cups, chalk crumbs and molehills.</summary>
		public const string GrassWoodPasture = "GrassWoodPasture";
		/// <summary>A sunlit coppice floor: oak, yellow hazel and long chestnut leaves, spiny husks, green shoots.</summary>
		public const string ForestFloorCopse = "ForestFloorCopse";
		/// <summary>The bluebell haze: strap leaves under a blue-violet mist of bells, a few white.</summary>
		public const string ForestFloorBluebell = "ForestFloorBluebell";
		/// <summary>A bracken glade: russet dead fronds over sandy loam, green croziers, birch leaves.</summary>
		public const string GrassDryBracken = "GrassDryBracken";
		/// <summary>A flowery ride's sward: taller herb-rich grass flecked with campion, bugle, St John's wort and ox-eye.</summary>
		public const string GrassMeadowGlade = "GrassMeadowGlade";
		/// <summary>A chalk rendzina bank: thin grey-brown soil with chalk lumps, flint nodules and rabbit scrapes.</summary>
		public const string SoilRendzina = "SoilRendzina";
		/// <summary>Grassy chalk rock: soft rounded white beds with flint bands, moss and old-man's-beard grey.</summary>
		public const string ChalkWoodland = "ChalkWoodland";
		/// <summary>A white chalk face with black flint courses and drip streaks, rubble at its foot.</summary>
		public const string ChalkFace = "ChalkFace";
		/// <summary>A chalk stream's bed: pale white-cortexed and black flints under clear water, water-crowfoot green.</summary>
		public const string GravelChalkStream = "GravelChalkStream";
		/// <summary>Marl: the grey-white calcareous clay of a dew-pond or mill-pond, snail shells and chalk lumps.</summary>
		public const string MudMarl = "MudMarl";
		/// <summary>A white chalk-and-flint droveway: crushed chalk, two ruts, a grass crown, flints.</summary>
		public const string PathChalkTrack = "PathChalkTrack";
		/// <summary>A green lane: trodden turf going to clay, grass edges, flint chips and leaves.</summary>
		public const string PathGreenLane = "PathGreenLane";
		/// <summary>Flint pitching: egg-shaped flints set on edge in lime mortar, black cores and white rims.</summary>
		public const string FlagstoneFlint = "FlagstoneFlint";
		/// <summary>Chalky calcareous sand with shell grit and flint chips.</summary>
		public const string SandChalky = "SandChalky";
		/// <summary>Iron-stained flint gravel with white chalk pebbles.</summary>
		public const string GravelFlint = "GravelFlint";

		// ── Coastal Water (biome round, 2026-10-26): the Celtic Atlantic shelf's own grounds, painted under the sea (decision 12;
		// until I9's variants of a recipe, each its own recipe). ──

		/// <summary>Atlantic shell-gravel sand in tide megaripples: broken shell, pink maerl and slate grit.</summary>
		public const string SandShellGravel = "SandShellGravel";
		/// <summary>Kelp-reef bedrock: dark green-black dolerite under pink coralline paint, red-weed turf and holdfast scars.</summary>
		public const string DoleriteKelpReef = "DoleriteKelpReef";
		/// <summary>A maerl bed: pink-lilac branched nodules of coralline alga, the dead ones bleached white, sorted into bands.</summary>
		public const string Maerl = "Maerl";
		/// <summary>Tide-swept cobble ground: rounded dolerite and granite crusted with yellow and orange sponges and hydroid fuzz.</summary>
		public const string CobbleTideSwept = "CobbleTideSwept";
		/// <summary>Circalittoral muddy sand of the brittlestar beds: burrow mounds, worm tubes, banded arms.</summary>
		public const string SiltBrittlestar = "SiltBrittlestar";
		/// <summary>Honeycomb-worm reef: hummocks of fine sand tubes in a hexagonal mosaic, a sand veneer between.</summary>
		public const string SandHoneycomb = "SandHoneycomb";
		/// <summary>A drowned forest's bed: black peat and oak-root mats, piddock bore pits, hazelnut specks.</summary>
		public const string PeatLyonesse = "PeatLyonesse";
		/// <summary>An urchin barren: bare grazed coralline pavement, knobbed and pitted where the urchins sit.</summary>
		public const string CoralBarren = "CoralBarren";
		/// <summary>A drowned sea-cliff of blocky-columnar dolerite, stippled with jewel anemones in pink, green, orange, violet, white.</summary>
		public const string DoleriteSeaCliff = "DoleriteSeaCliff";
		/// <summary>Surf-sorted sand of the rock pools and the coastal fade: fine, clean, rippled, mica-flecked.</summary>
		public const string SandSurfSorted = "SandSurfSorted";
		/// <summary>A stream mouth's gravel of flat slate and white quartz.</summary>
		public const string GravelSlateQuartz = "GravelSlateQuartz";
		/// <summary>A sarn causeway's top: boulder-pitched, weed-slick, barnacle-flecked.</summary>
		public const string FlagstoneSarn = "FlagstoneSarn";
		/// <summary>A skerry slab path over barnacled granite benches with a black lichen band.</summary>
		public const string RockSkerry = "RockSkerry";
		/// <summary>Pavé of pink Breton granite setts, weed in the joints.</summary>
		public const string FlagstonePave = "FlagstonePave";
		/// <summary>Maerl-and-shell bar sand: pink maerl and white shell grit.</summary>
		public const string SandMaerlBar = "SandMaerlBar";
		/// <summary>Killas shingle: flat grey slate and quartz.</summary>
		public const string GravelKillas = "GravelKillas";

		// ── Ocean (biome round, 2026-10-26): the shelf edge and continental slope's own grounds, painted under the sea. ──

		/// <summary>Nephrops ground: warm grey-brown slope mud cratered by burrow mouths, foraminifera specks.</summary>
		public const string MudNephrops = "MudNephrops";
		/// <summary>Shelf-edge bioclastic shell sand, megarippled by the internal tide, black glauconite grains.</summary>
		public const string SandShelfEdge = "SandShelfEdge";
		/// <summary>A Lophelia rubble apron: grey heaps of dead coral sticks, white live tips, salmon and orange encrusters.</summary>
		public const string CoralRubble = "CoralRubble";
		/// <summary>A pockmark's floor: reduced black-grey mud, cracked, pale carbonate flakes.</summary>
		public const string MudPockmark = "MudPockmark";
		/// <summary>Ostur: a sponge-spicule felt of the cold slope, matted spicules and dead sponges.</summary>
		public const string SpiculeMat = "SpiculeMat";
		/// <summary>Iceberg plough-mark diamicton: furrow walls of mixed granite, basalt and quartzite clasts.</summary>
		public const string GravelDiamicton = "GravelDiamicton";
		/// <summary>Honey-grey turbidite sandstone slope ledges with sole-mark flutes and manganese-black skins.</summary>
		public const string SandstoneIgueldo = "SandstoneIgueldo";
		/// <summary>A slope scarp of massive grey-brown mudstone, burrow-mottled, pyrite-rusted, manganese-black at the top.</summary>
		public const string MudstoneScarp = "MudstoneScarp";
		/// <summary>A slope gully's gravel of flat mudstone chips and coral grit.</summary>
		public const string GravelGullyChip = "GravelGullyChip";
		/// <summary>The shelf rim's mud of the coastal fade.</summary>
		public const string MudShelfRim = "MudShelfRim";
		/// <summary>A galtzada: an edge-set sandstone cobble mule-road with a raised spine of long slabs, silted joints.</summary>
		public const string FlagstoneGaltzada = "FlagstoneGaltzada";
		/// <summary>A trail of packed Lophelia grit, white stick fragments.</summary>
		public const string PebblesCoralGrit = "PebblesCoralGrit";
		/// <summary>Arri-biribil: blue-grey sea cobbles on edge in herringbone with a honey sandstone kerb.</summary>
		public const string PebblesArriBiribil = "PebblesArriBiribil";
		/// <summary>Grey-white coral-grit bar sand with salmon specks.</summary>
		public const string SandCoralGrit = "SandCoralGrit";
		/// <summary>Bar gravel of flat honey sandstone chips and mudstone.</summary>
		public const string GravelSandstoneChip = "GravelSandstoneChip";

		// ── Tundra (biome round, 2026-10-26): the treeless arctic's own grounds: dwarf-shrub mat, ice-wedge polygons, frost-sorted circles, tussock,
		// fell-field, gneiss (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>The tundra's dwarf-shrub and lichen mat: olive-russet mottle of dwarf birch, snow lichen, bearberry red, cotton tufts.</summary>
		public const string LichenTundraMat = "LichenTundraMat";
		/// <summary>An ice-wedge polygon mire: sedgy moss cells 8 m across, dark wet troughs between raised pale lichen rims.</summary>
		public const string MossPolygonMire = "MossPolygonMire";
		/// <summary>Frost-boil sorted circles: domed grey silt centres ringed by frost-sorted stones, a Dryas and lichen fringe.</summary>
		public const string GravelSortedCircles = "GravelSortedCircles";
		/// <summary>A tussock cottongrass meadow: straw and green blades, white cotton flecks, water glinting between.</summary>
		public const string GrassTussockTundra = "GrassTussockTundra";
		/// <summary>A frost-shattered gneiss fell-field (felsenmeer): angular pink-grey plates, rock tripe, Xanthoria and map lichen.</summary>
		public const string RockFellField = "RockFellField";
		/// <summary>A late snowbank, sun-cupped, blotched pink with snow algae and gritty with dust.</summary>
		public const string SnowWatermelon = "SnowWatermelon";
		/// <summary>A banded gneiss outcrop: folded dark and pale bands, rock tripe and a grey lichen crust.</summary>
		public const string RockGneissOutcrop = "RockGneissOutcrop";
		/// <summary>Frost-heaved scree of angular gneiss grus, lichen-spotted.</summary>
		public const string GravelHeaveScree = "GravelHeaveScree";
		/// <summary>A banded gneiss face: folded pink and black bands, black seeps, orange bird-cliff lichen.</summary>
		public const string CliffGneissBanded = "CliffGneissBanded";
		/// <summary>Braided outwash gravel: sub-rounded blue-grey cobbles, pink gneiss and black basalt, a glacial-flour film.</summary>
		public const string GravelOutwashBraided = "GravelOutwashBraided";
		/// <summary>A thermokarst lake's bed: dark silt with drowned sedge tussocks.</summary>
		public const string SiltThermokarst = "SiltThermokarst";
		/// <summary>A reindeer drove road: trampled gravel in braided hoof and sledge-runner grooves, a stripped-turf edge.</summary>
		public const string PathDroveRoad = "PathDroveRoad";
		/// <summary>A lichen trod: bare peaty grit, lichen crumbs, the odd reindeer dropping.</summary>
		public const string PathTundraTrod = "PathTundraTrod";
		/// <summary>Frost-split gneiss flags, turf and moss in the joints.</summary>
		public const string FlagstoneGneissSplit = "FlagstoneGneissSplit";
		/// <summary>A slab causeway: flat gneiss slabs bedded in turf across the mire, dark water between.</summary>
		public const string PathSlabCauseway = "PathSlabCauseway";
		/// <summary>Glacial-flour silt-sand on a braided river's slow bars, mica-specked.</summary>
		public const string SandGlacialFlour = "SandGlacialFlour";
		/// <summary>Outwash bar gravel of basalt and gneiss, flecked with driftwood bark.</summary>
		public const string GravelOutwashBark = "GravelOutwashBark";

		// ── Ice Sheet (biome round, 2026-10-26): the continental ice cap's own grounds: katabatic wind-slab, glaze, blue ice, charnockite nunataks,
		// and the Thin air band's Mars-cap ices (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Katabatic wind-slab: hard snow combed into ripples along the wind, undercut prows in shadow, glitter.</summary>
		public const string SnowKatabatic = "SnowKatabatic";
		/// <summary>A thin-aired polar cap's layered deposit: ice and dust couplets, frost streaks.</summary>
		public const string IceLayeredDeposit = "IceLayeredDeposit";
		/// <summary>Wind glaze: a mirror crust polished by the falling wind, faint thermal cracks, loose drift grains.</summary>
		public const string IceWindGlaze = "IceWindGlaze";
		/// <summary>Swiss-cheese slab: flat-floored pits in carbon-dioxide ice, darker floors, bright scarps.</summary>
		public const string FrostSwissCheese = "FrostSwissCheese";
		/// <summary>A blue-ice field: old ablation ice scoured bare, wind scallops, bubble bands, the odd black star-stone.</summary>
		public const string IceBlueField = "IceBlueField";
		/// <summary>A charnockite nunatak fell: honey-rinded angular blocks, olive fresh faces, blue quartz, frigid lichens, guano and stone-oil.</summary>
		public const string RockCharnockiteFell = "RockCharnockiteFell";
		/// <summary>A dust-varnished charnockite fell, red dust in its joints, no lichen.</summary>
		public const string RockCharnockiteDust = "RockCharnockiteDust";
		/// <summary>Surface hoar: calm-night feather cups of frost, glittering, blue in their shadows.</summary>
		public const string FrostSurfaceHoar = "FrostSurfaceHoar";
		/// <summary>Blue-ice moraine: charnockite debris welling up against a nunatak in a blue-ice matrix.</summary>
		public const string GravelBlueIceMoraine = "GravelBlueIceMoraine";
		/// <summary>A wind-cut dome flank: stepped annual layers, dust bands and scallops.</summary>
		public const string IceSteppedEscarpment = "IceSteppedEscarpment";
		/// <summary>A spiral trough's scarp on a thin-aired cap: thin dark dust bands, frost rims.</summary>
		public const string IceSpiralScarp = "IceSpiralScarp";
		/// <summary>A tidewater ice face at the sea edge: sheer blue planes, white crush bands, spray rime at the foot.</summary>
		public const string IceTidewater = "IceTidewater";
		/// <summary>A nunatak wall of charnockite: exfoliation sheets, honey varnish, wind-fluted faces, guano streaks, rime plaster.</summary>
		public const string CliffCharnockite = "CliffCharnockite";
		/// <summary>A thin-aired world's nunatak wall: charnockite with red dust packed in its joints.</summary>
		public const string CliffRedDust = "CliffRedDust";
		/// <summary>Frozen melt runnels: fluted blue ice with bubble trains.</summary>
		public const string IceMeltRunnel = "IceMeltRunnel";
		/// <summary>Clear black lake ice: white crack nets over stacked bubble discs.</summary>
		public const string IceBubbleStack = "IceBubbleStack";
		/// <summary>The ice road: packed snow polished by sledge runners into ruts, hoof pocks, dung and hay wisps.</summary>
		public const string PathIceRoad = "PathIceRoad";
		/// <summary>A single-file horse trace: hoofprints half filled by the wind.</summary>
		public const string PathSnowHorseTrace = "PathSnowHorseTrace";
		/// <summary>A yard of sawn wind-slab blocks rammed flat, drift in the joints.</summary>
		public const string SnowBlockYard = "SnowBlockYard";
		/// <summary>Charnockite grus: coarse honey sand with blue quartz and feldspar grains.</summary>
		public const string SandCharnockiteGrus = "SandCharnockiteGrus";
		/// <summary>Nunatak scree shingle: angular rinded charnockite.</summary>
		public const string GravelNunatakShingle = "GravelNunatakShingle";

		// ── Glacier (biome round, 2026-10-26): the valley glacier's own grounds: ablation ice, firn, icefall, polished greenstone, moraine till, the
		// debris tongue and forefield (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Bare summer ablation ice: blue and white foliation bands, mauve ice-algae dusk, cryoconite dots in azure rims, wind grit.</summary>
		public const string IceAblation = "IceAblation";
		/// <summary>A firn basin: snow ribbed by downslope melt runnels, grey dust veils, avalanche balls.</summary>
		public const string SnowFirnRunnels = "SnowFirnRunnels";
		/// <summary>An icefall's crevassed blue ice: transverse crevasse lines, blue walls, snow-lipped edges.</summary>
		public const string IceIcefall = "IceIcefall";
		/// <summary>Glacier-polished greenstone slabs: fine striations down the flow, plucked lee steps, white amygdales, map lichen.</summary>
		public const string RockGreenstonePolished = "RockGreenstonePolished";
		/// <summary>Unsorted moraine till: angular and sub-rounded greenstone, gneiss, limestone and slate in rock flour.</summary>
		public const string GravelTill = "GravelTill";
		/// <summary>The debris-covered tongue: black angular rubble over ice, blue exposures, silt pools.</summary>
		public const string GravelDebrisTongue = "GravelDebrisTongue";
		/// <summary>The glacier forefield: raw grey sand-silt with the first trisetum, river-beauty and crust lichen.</summary>
		public const string SiltForefield = "SiltForefield";
		/// <summary>A plucked trap wall: massive greenstone flows, amygdaloidal tops, chlorite joints, fresh rockfall scars, black melt streaks.</summary>
		public const string CliffGreenstone = "CliffGreenstone";
		/// <summary>A glacier river's green boulder bed: rounded greenstone, white quartz and pink gneiss under a flour film.</summary>
		public const string GravelGreenBoulderBed = "GravelGreenBoulderBed";
		/// <summary>Varved proglacial clay: dark winter and pale summer couplets, the odd dropstone.</summary>
		public const string SiltVarvedClay = "SiltVarvedClay";
		/// <summary>A pitched pilgrim trail: greenstone slabs set on edge as steps and water-bars, pony-shoe crescents, marigold petals.</summary>
		public const string PathPitchedTrail = "PathPitchedTrail";
		/// <summary>A moraine-crest trod: a grit line, stones kicked to the kerbs.</summary>
		public const string PathMoraineTrod = "PathMoraineTrod";
		/// <summary>Great dressed limestone temple flags, tight joints, foot-polished, the odd lotus-rosette slab.</summary>
		public const string FlagstoneTempleSlab = "FlagstoneTempleSlab";
		/// <summary>Outwash flour sand rippled, black magnetite streaks and gold glints in the troughs.</summary>
		public const string SandPlacerFlour = "SandPlacerFlour";
		/// <summary>Braided-bar shingle: rounded greenstone, quartz and gneiss, flour-dried tops.</summary>
		public const string GravelBraidedShingle = "GravelBraidedShingle";
		/// <summary>A rubble trod over the debris tongue: stones kicked aside, packed grey grit, black-ice glints.</summary>
		public const string PathRubbleTrod = "PathRubbleTrod";
		/// <summary>A thin-aired world's dust-mantled glacier: lobate flow ridges under rust dust.</summary>
		public const string IceDustMantled = "IceDustMantled";
		/// <summary>A thin-aired firn: dust rimed with carbon-dioxide frost.</summary>
		public const string FrostDustRimed = "FrostDustRimed";

		// ── Alpine Meadow (biome round, 2026-10-26): the Carpathian high pastures' own grounds: bent-grass turf, hay meadow, mugo heath,
		// flysch ribs, late snow and melt-turf, spring flushes, karren (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Polonyna turf: grazed bent and red fescue under a mauve haze, arnica, gentian, flysch chips and sheep pellets.</summary>
		public const string GrassMeadowPolonyna = "GrassMeadowPolonyna";
		/// <summary>An uncut flower hay-meadow: tall wind-combed grass, hay wisps, white and red clover, violet rampion.</summary>
		public const string GrassFlowerHay = "GrassFlowerHay";
		/// <summary>Mugo-and-rhododendron dwarf-shrub heath: wiry dark shrub, crimson flowers, Iceland moss, rust needle duff.</summary>
		public const string HeathMugo = "HeathMugo";
		/// <summary>Flysch ribs and slab rubble: thin alternating sandstone and shale beds, map lichen and crustose white.</summary>
		public const string RockFlyschRibs = "RockFlyschRibs";
		/// <summary>A late snow patch in a lee hollow: dirty granular firn, needle and grit film, meltwater rills.</summary>
		public const string SnowLatePatch = "SnowLatePatch";
		/// <summary>Melt-turf just out of the snow: matted straw turf, crocus violet and white, snowbells, snow-mould film.</summary>
		public const string GrassDryMeltTurf = "GrassDryMeltTurf";
		/// <summary>A spring flush: bright gold-green moss cushions, marsh-marigold, water glints and iron film.</summary>
		public const string MossSpringFlush = "MossSpringFlush";
		/// <summary>Karren crag pasture: limestone grikes and runnels, blue moor-grass tufts, edelweiss felt, pinks.</summary>
		public const string LimestoneKarren = "LimestoneKarren";
		/// <summary>A flysch rib wall: grey-green sandstone ribs standing proud of crumbling dark shale bands, seepage streaks, moss.</summary>
		public const string CliffFlyschRib = "CliffFlyschRib";
		/// <summary>A stream bed of flat flysch slab cobbles, dark shale chips, rust stains, green algae film.</summary>
		public const string PebblesFlyschSlab = "PebblesFlyschSlab";
		/// <summary>A tarn's floor: dark silt, sunk stone-pine needles and stones.</summary>
		public const string SiltTarn = "SiltTarn";
		/// <summary>A hay-sledge skid road: a turf crown between two polished bare-earth skids, flysch chips, dung, hoof pocks.</summary>
		public const string PathSledgeTrack = "PathSledgeTrack";
		/// <summary>A sheep trod: contouring clay-grey grit with terracette risers, wool tufts and pellets.</summary>
		public const string PathSheepTrod = "PathSheepTrod";
		/// <summary>Split flysch slabs laid irregular, grass and moss in the joints, flute-cast ripples on the faces.</summary>
		public const string FlagstoneFlyschCrazy = "FlagstoneFlyschCrazy";
		/// <summary>Grey-green flysch sand: mica and black shale grit.</summary>
		public const string SandFlysch = "SandFlysch";
		/// <summary>Flat flysch sandstone shingle with dark andesite and white limestone.</summary>
		public const string GravelFlyschShingle = "GravelFlyschShingle";
		/// <summary>A lane hacked through the dwarf pine: rust needle duff, cut grey stubs, roots across.</summary>
		public const string PathKnieholzCut = "PathKnieholzCut";
		/// <summary>Worn grooves and cut steps in bare limestone, grit in the hollows.</summary>
		public const string PathKarrenSteps = "PathKarrenSteps";

		// ── Mountain Slope (biome round, 2026-10-26): the Afromontane cloud forest's own grounds: moss-sleeved humus, kikuyu glades,
		// red nitisol, the frost-moor heath, phonolite ledges, the warm coffee floor. ──
		/// <summary>Cloud-forest humus: dark leaflet litter, usnea tufts, moss flecks, podocarp needles, red earth.</summary>
		public const string ForestFloorCloudHumus = "ForestFloorCloudHumus";
		/// <summary>The moss blanket over roots and stones: deep hummocks, liverwort, filmy fern, drip-dark hollows.</summary>
		public const string MossBlanket = "MossBlanket";
		/// <summary>A kikuyu-grazed glade: dense mat grass on red earth, Meskel gold, alchemilla, dung.</summary>
		public const string GrassMeadowKikuyu = "GrassMeadowKikuyu";
		/// <summary>Red nitisol of the tilled terraces and road cuts: crumb soil, phonolite chips, laterite pisoliths, teff straw.</summary>
		public const string SoilNitisol = "SoilNitisol";
		/// <summary>The ericaceous frost-moor: heather litter and tussock over black peat, everlasting white, silver alchemilla, hoar.</summary>
		public const string HeathEricaMoor = "HeathEricaMoor";
		/// <summary>Mossy phonolite ledges and platy scree: grey-green slabs, moss on the upward faces, pale rind, sanidine glints.</summary>
		public const string RockPhonoliteLedges = "RockPhonoliteLedges";
		/// <summary>The warm coffee-forest floor: fig and cordia leaves, coffee cherries, red earth, termite earth.</summary>
		public const string ForestFloorCoffee = "ForestFloorCoffee";
		/// <summary>A phonolite wall: platy sheets cut by column joints, black seep streaks, moss and fern ledges, red laterite wash.</summary>
		public const string CliffPhonolite = "CliffPhonolite";
		/// <summary>Black and grey stream cobbles of basalt, phonolite and laterite under a red silt film.</summary>
		public const string PebblesRedSilted = "PebblesRedSilted";
		/// <summary>Red clay ooze with sunk leaves.</summary>
		public const string MudRedClay = "MudRedClay";
		/// <summary>A rain-gullied red-earth mule road: braided runnels, hoof pocks, phonolite stepping stones, dung.</summary>
		public const string PathMuleGully = "PathMuleGully";
		/// <summary>A forest trail stepped by roots: humus with root bars every half-metre or so, moss edges, mud pools.</summary>
		public const string PathRootStair = "PathRootStair";
		/// <summary>Platy phonolite flag stairs: big rectangular flags, riser shadows, moss in the joints, foot-polished centres.</summary>
		public const string FlagstoneSlabStair = "FlagstoneSlabStair";
		/// <summary>Dark phonolite-basalt sand, red-washed, sanidine flecks.</summary>
		public const string SandPhonolite = "SandPhonolite";
		/// <summary>Black, grey and red rounded gravel of basalt, phonolite, laterite and opal-white chips.</summary>
		public const string GravelRedBlack = "GravelRedBlack";
		/// <summary>A causeway of bundled giant-heather branches laid in the black peat, moss creeping in.</summary>
		public const string PathBundleCauseway = "PathBundleCauseway";
		/// <summary>Phonolite stepping-stones sunk in the moss.</summary>
		public const string PathPhonoliteSteps = "PathPhonoliteSteps";

		// ── Scree (biome round, 2026-10-26): the moving stone of the summits' own grounds: fine scree, openwork talus, block streams,
		// rock-glacier skin, fell-field nets, late snow, cone-toe loam, breathing talus, wedge spoil, and the thin and airless worlds' talus. ──
		/// <summary>Fine scree at the angle of rest: salt-and-pepper grus-and-chip, buff grus, biotite glints.</summary>
		public const string GravelFineScree = "GravelFineScree";
		/// <summary>Openwork talus: big angular blocks over black voids, coral-lichen tufts, black lichen dots.</summary>
		public const string RockOpenworkTalus = "RockOpenworkTalus";
		/// <summary>A stone river: sub-rounded blocks in a creek-shaped stream, white and grey-green crust rosettes.</summary>
		public const string RockStoneRiver = "RockStoneRiver";
		/// <summary>A rock glacier's skin: transverse furrow-and-ridge arcs, silt-filled furrows, no lichen.</summary>
		public const string GravelRockGlacierSkin = "GravelRockGlacierSkin";
		/// <summary>Fell-field nets: frost-sorted stone nets with cushion specks in the fines.</summary>
		public const string SoilFellNets = "SoilFellNets";
		/// <summary>A late snow patch on the talus: fallen grit, melt runnels, stones lying on the snow.</summary>
		public const string SnowPatchScree = "SnowPatchScree";
		/// <summary>Cone-toe loam: grus and fir duff of the steadings and fields, purple cone scales, cleared-stone flecks.</summary>
		public const string SoilConeToe = "SoilConeToe";
		/// <summary>Breathing talus: blocks with hoar rims in the gaps, black-green wet crust, frost fur on the undersides.</summary>
		public const string RockBreathingTalus = "RockBreathingTalus";
		/// <summary>Wedge-split spoil: slabby chips with half-cylinder wedge-hole scars, fresh white faces.</summary>
		public const string GravelWedgeSpoil = "GravelWedgeSpoil";
		/// <summary>A thin-aired world's rust scree: iron-skinned chips, frost in the lee, dust ripples.</summary>
		public const string GravelRustScree = "GravelRustScree";
		/// <summary>An airless world's talus: dust-mantled angular blocks, fresh fracture faces, no sorting, no lichen.</summary>
		public const string RegolithVacuumTalus = "RegolithVacuumTalus";
		/// <summary>A sheeted granodiorite pinnacle wall: exfoliation sheets, vertical joints, dark enclaves, white aplite dykes.</summary>
		public const string CliffGranodiorite = "CliffGranodiorite";
		/// <summary>A step-pool boulder bed: rounded boulders, pale grus sand pockets, a wet film.</summary>
		public const string GravelStepPool = "GravelStepPool";
		/// <summary>Tarn silt behind a rampart: sunken blocks, fir needles.</summary>
		public const string SiltRampartTarn = "SiltRampartTarn";
		/// <summary>A fortress causeway: grus tread between fitted kerb blocks, stone step bands on the slopes.</summary>
		public const string PathFortressCauseway = "PathFortressCauseway";
		/// <summary>A talus trace: compacted chips, kick-step pocks, chips sliding off the downhill edge.</summary>
		public const string PathTalusTrace = "PathTalusTrace";
		/// <summary>Wedge-split flags of a gate yard: split slabs with wedge-hole notches, grus joints.</summary>
		public const string FlagstoneWedge = "FlagstoneWedge";
		/// <summary>Grus sand: coarse feldspar-quartz granules, biotite.</summary>
		public const string SandGrus = "SandGrus";
		/// <summary>Torrent shingle: rounded salt-and-pepper stones, dark enclave cobbles.</summary>
		public const string GravelTorrentShingle = "GravelTorrentShingle";
		/// <summary>Stepping blocks across the talus: flat-topped blocks levered level, chinked with chips.</summary>
		public const string PathSteppingBlocks = "PathSteppingBlocks";

		// ── Scrubland (biome round, 2026-10-26): the Sonoran bajadas' own grounds: rotted-granite loam, creosote flats, caliche benches,
		// grus slopes, black malpaís, the mesquite bosque floor, banded mylonite fronts, braided wash sand and the canal builders' ways
		// (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Bajada loam: granite-sand loam with angular pink feldspar, quartz, gravel stringers and caliche flecks, rain-pocked.</summary>
		public const string SoilBajadaLoam = "SoilBajadaLoam";
		/// <summary>A creosote flat: pale silty desert loam, a thin mixed-dark pebble lag, white bursage leaf litter, faint polygon cracks.</summary>
		public const string SiltCreosoteFlat = "SiltCreosoteFlat";
		/// <summary>A caliche bench: calcrete hardpan breaking out of a terrace edge, nodular knobs, laminar rinds, pink iron stain.</summary>
		public const string CalicheBench = "CalicheBench";
		/// <summary>A grus slope of rotted granite: coarse grus round half-buried corestones, chartreuse map lichen on the clasts.</summary>
		public const string GravelGrusBajada = "GravelGrusBajada";
		/// <summary>Malpaís: black vesicular ropy-blocky basalt, rust rinds, olive glints, wind sand in the hollows.</summary>
		public const string BasaltMalpais = "BasaltMalpais";
		/// <summary>The mesquite bosque's floor: dark loam under tiny leaflets, tan curled pods and catkin fluff.</summary>
		public const string LitterMesquiteBosque = "LitterMesquiteBosque";
		/// <summary>A mylonite front: banded, lineated core-complex gneiss in thin grey, white and pink bands dipping 15°, north-face lichen.</summary>
		public const string CliffMylonite = "CliffMylonite";
		/// <summary>Braided wash sand: rippled arkose with mica sparkle and mud-curl drapes.</summary>
		public const string SandWashArkose = "SandWashArkose";
		/// <summary>A tank's floor (charco): cracked clay, hoof and peccary prints, a green algae skin.</summary>
		public const string ClayTank = "ClayTank";
		/// <summary>A canal bank lane: swept silt packed on the spoil berm, caliche chunks either side, no ruts.</summary>
		public const string PathCanalBank = "PathCanalBank";
		/// <summary>A cleared trail: a pale swept line, its pebbles windrowed to both edges, shell and sherd glints.</summary>
		public const string PathClearedTrail = "PathClearedTrail";
		/// <summary>Caliche setts: roughly squared caliche blocks, knobby faces, red-brown mud joints.</summary>
		public const string FlagstoneCalicheSetts = "FlagstoneCalicheSetts";
		/// <summary>A malpaís trail: a lane with its stones lifted off and stacked in low kerbs across the malpaís.</summary>
		public const string PathMalpaisTrail = "PathMalpaisTrail";
		/// <summary>Wash bar sand: rippled, with black magnetite streaks.</summary>
		public const string SandWashBar = "SandWashBar";
		/// <summary>Wash bar gravel: pink granite, grey gneiss, black basalt, white quartz and caliche lumps.</summary>
		public const string GravelWashCobble = "GravelWashCobble";

		// ── Savanna (biome round, 2026-10-26): the Zimbabwe plateau's own grounds: tawny veld over granite loam, sandveld gruss, mopane
		// clay, greenstone red earth, dwala slabs, vlei turf, the Matopo cliffs, sand rivers, pans and the cattle roads (decision 12). ──
		/// <summary>Tawny veld: grass litter and straw stubs over red-brown granite loam, quartz grit and seed husks.</summary>
		public const string GrassDryTawnyVeld = "GrassDryTawnyVeld";
		/// <summary>Granite sandveld: pale grey-buff gruss of quartz and pink feldspar, biotite flecks.</summary>
		public const string SandGraniteSandveld = "SandGraniteSandveld";
		/// <summary>Mopane clay: grey-brown sodic clay in wide cracks, hoof pocks, dropped butterfly leaves.</summary>
		public const string ClayMopane = "ClayMopane";
		/// <summary>Greenstone red earth: deep oxblood loam over the schist belts, ironstone pellets, schist chips.</summary>
		public const string SoilGreenstoneRed = "SoilGreenstoneRed";
		/// <summary>A dwala slab: bare pink-grey granite in onion-skin sheets, orange, lime and grey-green lichen rosettes, gnamma pans.</summary>
		public const string RockDwalaSlab = "RockDwalaSlab";
		/// <summary>Vlei turf: seasonally waterlogged black clay, sedge stubble, hoof pools.</summary>
		public const string MudVleiTurf = "MudVleiTurf";
		/// <summary>A Matopo cliff: rounded granite joint blocks, pink feldspar, lichen maps, tan seepage stripes.</summary>
		public const string CliffMatopo = "CliffMatopo";
		/// <summary>A sand river: coarse pink-white gruss in dune ripples, elephant digs.</summary>
		public const string SandRiverGruss = "SandRiverGruss";
		/// <summary>A pan's wallow: grey-green clay trampled in hoof rings, an algae film.</summary>
		public const string MudPanWallow = "MudPanWallow";
		/// <summary>A cattle road: trodden red-brown earth, hoof pocks, dung, a grass strip down the middle.</summary>
		public const string PathCattleRoad = "PathCattleRoad";
		/// <summary>A single-file path: a bare pale sand ribbon with quartz grit.</summary>
		public const string PathSingleFile = "PathSingleFile";
		/// <summary>A daga floor: burnished ochre clay-and-gravel, polished, scored with concentric rings.</summary>
		public const string FlagstoneDagaFloor = "FlagstoneDagaFloor";
		/// <summary>A game trail over the mopane clay: a hoof-churned grey clay furrow with dung.</summary>
		public const string PathGameTrail = "PathGameTrail";
		/// <summary>Slab steps: shallow steps pecked into the granite, cupule hollows.</summary>
		public const string PathSlabSteps = "PathSlabSteps";
		/// <summary>Pink gruss bar sand: feldspar and quartz granules.</summary>
		public const string SandPinkGruss = "SandPinkGruss";
		/// <summary>Reef-and-ironstone bar pebbles: white quartz, black ironstone and schist.</summary>
		public const string GravelReefIronstone = "GravelReefIronstone";

		// ── Grassland (biome round, 2026-10-26): the Pannonian puszta's own grounds: meadow-steppe sward on black earth, goldbeard, blown sand, szik soda pans, loess banks and walls (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Meadow-steppe sward: brome and beard-grass combed one way by the wind, dead straw, red beard-grass, black earth and banded snail shells.</summary>
		public const string GrassPusztaSward = "GrassPusztaSward";
		/// <summary>A goldbeard stand's litter: tall bronze-and-gold swathes, purple spikelets, straw.</summary>
		public const string GrassDryGoldbeard = "GrassDryGoldbeard";
		/// <summary>Chernozem: trampled black earth round the wells and herd trails, loess-filled burrows, worm casts, hoof pocks and straw.</summary>
		public const string SoilChernozem = "SoilChernozem";
		/// <summary>Blown-sand puszta: wind-scoured pale sand in faint ripples, blue-fescue tufts, fumana gold and dead culms.</summary>
		public const string SandPuszta = "SandPuszta";
		/// <summary>A szik soda pan: sodic grey clay in polygon cracks under a white soda bloom, curled flakes, red seablite, saltgrass.</summary>
		public const string SaltCrustSzik = "SaltCrustSzik";
		/// <summary>A loess bank: buff silt, rain-runnelled, snail shells, loess dolls and roots.</summary>
		public const string LoessBank = "LoessBank";
		/// <summary>A bare loess wall: soft thick beds cut by vertical joints, bee-eater burrows and a brown fossil-soil band.</summary>
		public const string LoessWall = "LoessWall";
		/// <summary>A loess bluff: columnar silt prisms banded by red-brown fossil soils, white root-tube streaks, burrow holes near the top.</summary>
		public const string CliffLoessBluff = "CliffLoessBluff";
		/// <summary>A slow meander's silty bed: smooth grey-brown silt, mussel shells and nacre, drift straw.</summary>
		public const string SiltTisza = "SiltTisza";
		/// <summary>A soda lake's ooze: grey-white alkaline mud, soda flecks, brine-shrimp pink, white feathers.</summary>
		public const string MudSodaOoze = "MudSodaOoze";
		/// <summary>A black-earth drove road: braided multi-rut cart track, grass crown, curled dried mud plates, hoof prints, puddle grey.</summary>
		public const string PathBlackEarthTrack = "PathBlackEarthTrack";
		/// <summary>A herd trail: loess-dust cattle trod, braided, hoof pocks and dung, grass at its edges.</summary>
		public const string PathHerdTrail = "PathHerdTrail";
		/// <summary>A sand track over the blown-sand puszta: pale loose sand in two deep wheel grooves.</summary>
		public const string PathSandTrackPuszta = "PathSandTrackPuszta";
		/// <summary>Herringbone brick paving of the stoneless plain: red-orange bricks in sand joints, soot by the doors, green joint moss.</summary>
		public const string BrickHerringbone = "BrickHerringbone";
		/// <summary>Micaceous river sand in ripples, mica glints and shell grit.</summary>
		public const string SandMicaRiver = "SandMicaRiver";
		/// <summary>Concretion-and-shell grit, the plain's only gravel: loess dolls, mussel shells, brick crumbs.</summary>
		public const string GravelConcretionGrit = "GravelConcretionGrit";

		// ── Desert (biome round, 2026-10-26): the West Saharan erg and reg's own grounds: wind-combed erg sand, avalanched slip faces,
		// varnished desert pavement, gassi granule corridors, varnished sandstone slabs, the green Sahara's lake marl, the fringe's red
		// sand sheets, Nubian walls, wadi gravels and the caravan ways (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Erg sand: orange-gold aeolian sand combed into wind-aligned ripples, dark heavy-mineral lag in the troughs, quartz glints.</summary>
		public const string SandErg = "SandErg";
		/// <summary>A dune's slip face: paler avalanched sand in smooth grainflow tongues, no ripples, a cornice line at the crest.</summary>
		public const string SandSlipFace = "SandSlipFace";
		/// <summary>Reg: a desert pavement of varnished black and brown pebbles, white quartz, red jasper and grey flint on a tan sand matrix.</summary>
		public const string RegPavement = "RegPavement";
		/// <summary>A gassi corridor's serir: coarse granule ripples, pink granules, dark grit on the crests.</summary>
		public const string GravelGassi = "GravelGassi";
		/// <summary>Varnished sandstone slabs: fractured bedrock under a black varnish skin, rose-ochre spalls, sand in the joints.</summary>
		public const string SandstoneVarnished = "SandstoneVarnished";
		/// <summary>A palaeolake's marl: the green Sahara's old lake bed in metre polygons, white diatomite flakes, reed casts, ostrich-shell crumbs.</summary>
		public const string ClayPaleolakeMarl = "ClayPaleolakeMarl";
		/// <summary>The fringe's red sand sheet: iron-stained sand held by grass tufts, small nebkha shadows.</summary>
		public const string SandRedSheet = "SandRedSheet";
		/// <summary>A Nubian sandstone wall: cross-bedded rose-ochre sets, black varnish curtains down from the rim, honeycomb tafoni at the foot.</summary>
		public const string CliffNubian = "CliffNubian";
		/// <summary>Wadi gravel: rounded rose sandstone, quartz and varnished black cobbles, mud-drape curls.</summary>
		public const string GravelWadi = "GravelWadi";
		/// <summary>A guelta's ooze: dark rock-pool silt under an algal film, a skin of blown sand at the rim.</summary>
		public const string ClayGueltaOoze = "ClayGueltaOoze";
		/// <summary>A caravan piste: braided camel trails packed pale, dung pellets and pad prints.</summary>
		public const string PathCaravanTrack = "PathCaravanTrack";
		/// <summary>A camel pad path: single-file dished pad prints in packed sand.</summary>
		public const string PathCamelPad = "PathCamelPad";
		/// <summary>Slab paving: thin rose sandstone flags, some on edge, clay joints with sand drifted in.</summary>
		public const string FlagstoneSlabPaving = "FlagstoneSlabPaving";
		/// <summary>Wadi flood sand: rippled pale sand with dried mud-curl flakes.</summary>
		public const string SandWadiFlood = "SandWadiFlood";
		/// <summary>Wadi bar gravel: varnished black, white quartz, rose sandstone and red jasper.</summary>
		public const string GravelWadiBar = "GravelWadiBar";
		/// <summary>A reg track: the pavement's pebbles swept aside into two pale sand lanes.</summary>
		public const string PathRegTrack = "PathRegTrack";
		/// <summary>A frost-glazed erg (the Frozen variant's): white hoar in the ripple troughs, rime on the crests' lee, varnish dulled.</summary>
		public const string SandFrostErg = "SandFrostErg";

		// ── Badlands (biome round, 2026-10-26): the Cappadocian fairy-chimney country's own grounds: pumice-ash steppe, rilled and red tuff,
		// pink wash silt, ash-loam tilth, lapilli fans, the basalt caprock, the banded ignimbrite walls and the ways worn into the tuff
		// (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Lapilli-ash steppe: pale volcanic regosol, white pumice lapilli, dark lithics, grey bunchgrass tufts, obsidian chips.</summary>
		public const string AshLapilliSteppe = "AshLapilliSteppe";
		/// <summary>Rilled tuff: bare banded rose, cream, ochre and white tuff cut by downslope runnels, tafoni pits.</summary>
		public const string TuffRilled = "TuffRilled";
		/// <summary>A wash floor: pink silt in curling mud plates, flood-drift lines of pumice, hoof pocks.</summary>
		public const string ClayWashFloor = "ClayWashFloor";
		/// <summary>Ash-loam tilth: the stone-cleared vineyard and orchard soil, pumice crumbs, vine prunings, apricot fall.</summary>
		public const string SoilAshLoam = "SoilAshLoam";
		/// <summary>A lapilli drift: white pumice fans below the chimneys, rose tuff and dark cap chips.</summary>
		public const string GravelLapilliDrift = "GravelLapilliDrift";
		/// <summary>A caprock plate: the mesa tops' rubbly black lava cap, orange lichen, ash in the joints.</summary>
		public const string BasaltCaprock = "BasaltCaprock";
		/// <summary>Red valley tuff: iron-stained banded tuff, runnelled, white salt rims at the rill heads.</summary>
		public const string TuffRedValley = "TuffRedValley";
		/// <summary>A banded ignimbrite wall: rose, cream and ochre bands under one dark welded band, honeycomb tafoni.</summary>
		public const string CliffIgnimbrite = "CliffIgnimbrite";
		/// <summary>A tuff stream's bed: pale lapilli sand with black obsidian and basalt grit and pink tuff grains, a wet film.</summary>
		public const string SandLapilliStream = "SandLapilliStream";
		/// <summary>Pink tuff ooze: smooth rose silt with drowned pumice rafts.</summary>
		public const string ClayTuffOoze = "ClayTuffOoze";
		/// <summary>A rut-cut road: the caravan way worn into the soft tuff, polished twin ruts, dust drifts, camel and donkey pocks.</summary>
		public const string PathRutCut = "PathRutCut";
		/// <summary>A donkey path: pale ash polished by hooves, dung.</summary>
		public const string PathDonkeyAsh = "PathDonkeyAsh";
		/// <summary>Tuff setts: squared cream and rose setts worn hollow, lime joints.</summary>
		public const string FlagstoneTuffSetts = "FlagstoneTuffSetts";
		/// <summary>Lapilli bar sand: pale pumice sand, obsidian glints and mica.</summary>
		public const string SandLapilliBar = "SandLapilliBar";
		/// <summary>Volcanic bar gravel: basalt, rose tuff, pumice floats and obsidian.</summary>
		public const string GravelVolcanicBar = "GravelVolcanicBar";
		/// <summary>Tuff steps: steps and footholds cut in the rock, cream, chisel-scored.</summary>
		public const string PathTuffSteps = "PathTuffSteps";
		/// <summary>A wash trace: a loose, flood-smoothed track along a wash floor.</summary>
		public const string PathWashTrace = "PathWashTrace";
		/// <summary>Snow on the ash (the Frozen variant's): white drifts in the rills' lee over grey ash.</summary>
		public const string AshSnowOn = "AshSnowOn";
		/// <summary>Salt-baked ash (the Scorched variant's): cracked buff crust under a white efflorescence.</summary>
		public const string CrackedEarthSaltBaked = "CrackedEarthSaltBaked";

		// ── Salt Flat (biome round, 2026-10-26): the Afar salt plain's own grounds: bedded amole crust in polygon plates, etched margin crust,
		// brine-edge ooze, the khaki clay pan, the black lava reg and its salted soot sand, the warm springs' cauliflower crust, the cold
		// band's alkali playa, salt cliffs and the bar roads (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Amole crust: hard bedded white halite in metre polygon plates with raised rims, brown mud threads, glints.</summary>
		public const string SaltAmoleCrust = "SaltAmoleCrust";
		/// <summary>Etched crust: rain-etched knobbly grey crust with brown mud cores.</summary>
		public const string SaltEtchedCrust = "SaltEtchedCrust";
		/// <summary>Brine-edge ooze: wet grey-brown mud, white hopper rims, a rose haloarchaea stain.</summary>
		public const string MudBrineEdge = "MudBrineEdge";
		/// <summary>The khaki clay pan of the outer playa: curled lifted mud plates, salt in the cracks.</summary>
		public const string ClayKhakiPan = "ClayKhakiPan";
		/// <summary>Afar reg: black varnished vesicular basalt pavement on pale silt, rust rinds, salt-white undersides.</summary>
		public const string RegAfarBasalt = "RegAfarBasalt";
		/// <summary>Salt-and-soot sand: black basalt sand drifts salted white.</summary>
		public const string SandSaltSoot = "SandSaltSoot";
		/// <summary>Spring crust: cream cauliflower halite round warm brine springs, thin rust bands.</summary>
		public const string SaltSpringCrust = "SaltSpringCrust";
		/// <summary>An alkali playa (the cold band's): grey-buff crust in low rims, grey-white efflorescence.</summary>
		public const string SaltAlkaliPlaya = "SaltAlkaliPlaya";
		/// <summary>A salt cliff: bedded rock salt folded with red and brown clay bands, vertical rain flutes.</summary>
		public const string CliffSaltrock = "CliffSaltrock";
		/// <summary>Wadi basalt gravel: rounded black cobbles skinned with salt, flood silt.</summary>
		public const string GravelWadiBasalt = "GravelWadiBasalt";
		/// <summary>A brine lake's floor: submerged white halite cubes, dark mud windows.</summary>
		public const string SaltBrineLakeFloor = "SaltBrineLakeFloor";
		/// <summary>A bar road: the caravan track trodden grey into the crust, hoof pocks, bar chips and dung.</summary>
		public const string PathBarRoad = "PathBarRoad";
		/// <summary>The cutters' trace: a footpath broken through the crust, slab edges and mud.</summary>
		public const string PathCuttersTrace = "PathCuttersTrace";
		/// <summary>Basalt setts: black vesicular setts with salt-white joints.</summary>
		public const string FlagstoneBasaltSetts = "FlagstoneBasaltSetts";
		/// <summary>Salt-and-pepper grit: white halite and black basalt grains.</summary>
		public const string SandSaltPepper = "SandSaltPepper";
		/// <summary>Crusted pebbles: basalt pebbles capped in salt.</summary>
		public const string GravelCrustedPebbles = "GravelCrustedPebbles";
		/// <summary>A crust causeway: cut crust slabs laid edge to edge over the ooze, brown mud joints.</summary>
		public const string PathCrustCauseway = "PathCrustCauseway";
		/// <summary>A basalt track: the lava reg cleared to both sides as low windrows, a pale silt floor.</summary>
		public const string PathBasaltTrack = "PathBasaltTrack";

		// ── Plains (biome round, 2026-10-26): the High Plains prairie's own grounds: mixed-grass, shortgrass and tallgrass swards, sandhills, buffalo wallows, playas, the red Gyp Hills (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Mixed-grass prairie sward: blue-grey wheatgrass and copper little bluestem combed by the wind, silver seed tufts, buffalo chips.</summary>
		public const string GrassDryMixedPrairie = "GrassDryMixedPrairie";
		/// <summary>Shortgrass sod: dense curly buffalograss and blue grama, eyelash flags, bare dust, a dog town's clipped rings.</summary>
		public const string GrassShortgrassSod = "GrassShortgrassSod";
		/// <summary>A tallgrass draw: big bluestem and switchgrass, wine turkey-foot heads, gold Indiangrass, sunflower discs.</summary>
		public const string GrassMeadowTallgrass = "GrassMeadowTallgrass";
		/// <summary>Sandhills: stabilised dune sand with blowout bowls, sand-reed tufts, yucca grey, Indian ricegrass.</summary>
		public const string SandSandhills = "SandSandhills";
		/// <summary>A buffalo wallow: dished, hoof-churned and rain-pooled earth, cracked mud plates, shed wool.</summary>
		public const string SoilBuffaloWallow = "SoilBuffaloWallow";
		/// <summary>A playa floor: grey-buff lake clay in polygon cracks, smartweed stems, water-clover rosettes, duck feathers.</summary>
		public const string ClayPlayaFloor = "ClayPlayaFloor";
		/// <summary>A Gyp Hills slope: brick-red silt in thin beds, white satin-spar chips, green reduction spots, grama tufts.</summary>
		public const string RedbedGypSlope = "RedbedGypSlope";
		/// <summary>Rimrock talus: white gypsum caprock blocks over red scree, dark wet streaks.</summary>
		public const string RedbedRimrockTalus = "RedbedRimrockTalus";
		/// <summary>A red bluff: thick red beds under a white gypsum caprock band, green reduction stripes, gypsum vein nets.</summary>
		public const string CliffRedbedGypsum = "CliffRedbedGypsum";
		/// <summary>A braided prairie river's bed: tawny sand in long ripples, dark heavy-mineral bands, drifted cottonwood fluff.</summary>
		public const string SandPlatteBraided = "SandPlatteBraided";
		/// <summary>An oxbow's ooze: smooth grey-brown clay, snail shells, sunk cottonwood leaves.</summary>
		public const string ClayOxbowOoze = "ClayOxbowOoze";
		/// <summary>A great trace of many travois-pole grooves braided over a swale, grass crowns between, dust drifts, hoof pocks, bison chips.</summary>
		public const string PathTravoisTrace = "PathTravoisTrace";
		/// <summary>A bison trail: a deep single-file trod hugging the contour, hoof pocks, trampled edges.</summary>
		public const string PathBisonTrail = "PathBisonTrail";
		/// <summary>A sand trace over the sandhills: a loose wide double groove.</summary>
		public const string PathSandTrace = "PathSandTrace";
		/// <summary>A red trail over the Gyp Hills: packed brick-red earth, white gypsum chips.</summary>
		public const string PathRedTrail = "PathRedTrail";
		/// <summary>Split red sandstone slabs laid irregular, grama in the joints, gypsum chips and ochre stains.</summary>
		public const string FlagstoneRedbed = "FlagstoneRedbed";
		/// <summary>Pink feldspar-quartz bar sand, mica and cottonwood twigs.</summary>
		public const string SandFeldsparBar = "SandFeldsparBar";
		/// <summary>Ogallala gravel: pink granite and quartzite pebbles, petrified wood, a bison bone now and then.</summary>
		public const string GravelOgallala = "GravelOgallala";
		/// <summary>Snow-crusted sward: drifted lee tails of snow with grass tips through them.</summary>
		public const string SnowCrustSward = "SnowCrustSward";
		/// <summary>The scorched reach's dust plain: cracked earth, sparse dead grama.</summary>
		public const string CrackedDustPlain = "CrackedDustPlain";

		// ── Estuary (biome round, 2026-10-26): the Frisian Wadden's own grounds: schlick, sand flat, zostera flat, pioneer saltings, low and
		// high marsh, pannes, creek banks, the Kliff, fluid mud, the Kleiweg and the Wattweg (decision 12; until I9's variants of a recipe,
		// each its own recipe). ──

		/// <summary>Schlick: silky grey tidal mud with a wet sheen, ladder ripples, mud-snail specks, bird-foot prints.</summary>
		public const string MudSchlickFlat = "MudSchlickFlat";
		/// <summary>A firm grey-gold sand flat: sharp ripples, coiled lugworm casts, shell hash.</summary>
		public const string SandSandwatt = "SandSandwatt";
		/// <summary>Mud combed flat by dwarf eelweed and green gutweed tufts, laid one way by the ebb.</summary>
		public const string MudZosteraFlat = "MudZosteraFlat";
		/// <summary>Soft claret-stippled pioneer mud: glasswort stipple, cordgrass bases, crab tracks.</summary>
		public const string MudPioneerSalting = "MudPioneerSalting";
		/// <summary>Low marsh: a flood-combed grey-green sward of saltmarsh-grass, sea-lavender rosettes, flood-litter straws.</summary>
		public const string GrassLowMarsh = "GrassLowMarsh";
		/// <summary>High marsh (kwelder): sheep-cropped fescue lawn, dark rush patches, thrift cushions, wool tufts.</summary>
		public const string GrassHighMarsh = "GrassHighMarsh";
		/// <summary>A salt panne: bare cracked grey mud, salt bloom, a pink sulphur-bacteria film.</summary>
		public const string SaltPanneCrust = "SaltPanneCrust";
		/// <summary>A creek's cut-bank: laminated tidal clay under an overhung sod lip, crab burrows, root threads.</summary>
		public const string SiltCreekBank = "SiltCreekBank";
		/// <summary>The Kliff: glacial boulder clay, grey below and oxidised red above, erratics set in it.</summary>
		public const string TillKliffFace = "TillKliffFace";
		/// <summary>Fluid mud of the brackish channels: black-grey, sulphidic, flocculent.</summary>
		public const string MudFluidSulphide = "MudFluidSulphide";
		/// <summary>Sub-tidal schlick, dark and strewn with blue-black mussel valves.</summary>
		public const string MudSubtidal = "MudSubtidal";
		/// <summary>The Kleiweg: a packed blue-grey marine-clay road on a low bank, cart ruts, crushed cockle.</summary>
		public const string ClayKleiweg = "ClayKleiweg";
		/// <summary>A sheep track over the saltings: narrow, trodden, muddy, hoof-slotted.</summary>
		public const string PathMudSheepTrack = "PathMudSheepTrack";
		/// <summary>The Wattweg: bare trodden flat with a footprint trail between broom stakes.</summary>
		public const string PathMudWattweg = "PathMudWattweg";
		/// <summary>Monastic brick on edge in herringbone: red, purple over-fired headers, sand joints.</summary>
		public const string FlagstoneMoppen = "FlagstoneMoppen";
		/// <summary>Shoal sand: fine grey-gold with cockle hash and pink tellins.</summary>
		public const string SandShoalCockle = "SandShoalCockle";
		/// <summary>A schill bank: shell ridge of cockle, mussel and tellin with clay balls.</summary>
		public const string GravelSchill = "GravelSchill";

		// ── Coral Reef, Beach and Abyss (biome round, 2026-10-26): the atoll shelf, the Baltic amber strand and the hadal trench's own grounds (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Atoll coral sand: Halimeda coins, red foraminifera and coral crumbs in soft ripples.</summary>
		public const string SandAtoll = "SandAtoll";
		/// <summary>The live reef framework: knobbly coral heads in olive, mauve, sage and coralline pink, fish-bite scars.</summary>
		public const string ReefFramework = "ReefFramework";
		/// <summary>A rubble field: dead branch-coral sticks under a brown turf film, bleached white tips.</summary>
		public const string RubbleBranchRods = "RubbleBranchRods";
		/// <summary>Lagoon floor: powder sand with ghost-shrimp mounds, dark craters and faint trails.</summary>
		public const string SandLagoon = "SandLagoon";
		/// <summary>A reef flat: cemented dead-coral platform, cracked, with coralline pink patches, tide pools and cowrie flecks.</summary>
		public const string ReefFlatPavement = "ReefFlatPavement";
		/// <summary>Bleached reef: white coral skeletons under a slime of filamentous algae.</summary>
		public const string ReefBleached = "ReefBleached";
		/// <summary>Fore-reef talus sand: coarse grit grading to blue-grey, rubble, sand chutes down the fall line.</summary>
		public const string SandForeReef = "SandForeReef";
		/// <summary>The drop-off wall: overhangs stippled with orange sponge, purple and pink soft coral, yellow tube coral and black coral.</summary>
		public const string CliffDropOff = "CliffDropOff";
		/// <summary>Powder lagoon sand of the coastal fade, blinding white with mica-bright grains.</summary>
		public const string SandLagoonPowder = "SandLagoonPowder";
		/// <summary>Coral-grit wash of an islet runnel: rods and knobs of broken coral, foram pink.</summary>
		public const string GravelReefGrit = "GravelReefGrit";
		/// <summary>A hirigaa causeway: dressed coral-stone blocks with tongue-and-groove joints under lime wash.</summary>
		public const string FlagstoneHirigaa = "FlagstoneHirigaa";
		/// <summary>A swept coral-sand lane: raked broom arcs, fallen frond flecks.</summary>
		public const string PathSweptSand = "PathSweptSand";
		/// <summary>Lime-set coral-rag setts: irregular rag with white lime joints.</summary>
		public const string FlagstoneRagSett = "FlagstoneRagSett";
		/// <summary>Pink foraminifera bar sand with Halimeda flecks.</summary>
		public const string SandForamBar = "SandForamBar";
		/// <summary>Cay shingle: ringing white coral sticks and clinker, sun-bleached.</summary>
		public const string GravelCayShingle = "GravelCayShingle";
		/// <summary>The singing quartz strand: fine pale sand squeaking underfoot, garnet and magnetite grains, shell grit.</summary>
		public const string SandSinging = "SandSinging";
		/// <summary>Swash sand: wet and glassy, swash-line arcs, foam scum, dark heavy-mineral streaks.</summary>
		public const string SandSwash = "SandSwash";
		/// <summary>The amber wrack line: eelgrass mats, lignite crumbs, a glint of amber, gull feathers.</summary>
		public const string LitterWrackAmber = "LitterWrackAmber";
		/// <summary>The grey dune: hair-grass, reindeer lichen cushions, crowberry mats, bare sand gaps.</summary>
		public const string LichenGreyDune = "LichenGreyDune";
		/// <summary>The warm backdune: golden sand with sea-grape leaf litter and crab-sand balls.</summary>
		public const string SandWarmBackdune = "SandWarmBackdune";
		/// <summary>A slip face and blowout: avalanche tongues down the lee, a lag of flint and pink granite pebbles.</summary>
		public const string SandSlipTongues = "SandSlipTongues";
		/// <summary>Beachrock pavement: seaward-dipping cemented benches, shell fossils, a green algae film at the low edge.</summary>
		public const string BeachrockPavement = "BeachrockPavement";
		/// <summary>A blue-earth dune scarp: glauconite clay under a sand cap, amber nodules and lignite lenses, seep streaks.</summary>
		public const string CliffBlueEarth = "CliffBlueEarth";
		/// <summary>A beach stream bed: braided current ripples, black heavy-mineral streaks.</summary>
		public const string SandBeachStream = "SandBeachStream";
		/// <summary>Swash-bar sand: sharp-crested current ripples and shell hash.</summary>
		public const string SandSwashBar = "SandSwashBar";
		/// <summary>Trough sand: megaripples with drifted eelgrass ribbons.</summary>
		public const string SandTrough = "SandTrough";
		/// <summary>Offshore sand-silt with cockle beds.</summary>
		public const string SiltOffshore = "SiltOffshore";
		/// <summary>A shell road: crushed cockle shell and pebble, cart ruts damp between.</summary>
		public const string GravelShellRoad = "GravelShellRoad";
		/// <summary>A trodden dune path: loose sand, footprints, sunk plank steps.</summary>
		public const string PathDuneTrod = "PathDuneTrod";
		/// <summary>A brushwood-mat road: pine-brush fascines pegged in the sand.</summary>
		public const string PathBrushwoodMat = "PathBrushwoodMat";
		/// <summary>Erratic cobbles: split pink rapakivi and grey granite set in sand.</summary>
		public const string FlagstoneErraticCobbles = "FlagstoneErraticCobbles";
		/// <summary>Garnet-streaked bar sand: pink and black laminae.</summary>
		public const string SandGarnetBar = "SandGarnetBar";
		/// <summary>Flint-and-erratic shingle: black flint with white cortex, pink granite pebbles.</summary>
		public const string GravelFlintShingle = "GravelFlintShingle";
		/// <summary>Hadal ooze: siliceous red-clay ooze under marine-snow fluff, burrow mounds and trails, radiolarian specks.</summary>
		public const string SiltHadalOoze = "SiltHadalOoze";
		/// <summary>The trench wall: scaly olive-black peridotite with rust iddingsite rinds and olivine glints, ooze on the ledges.</summary>
		public const string PeridotiteTrenchWall = "PeridotiteTrenchWall";
		/// <summary>Pillow talus: glass-rinded bulbous pillows with radial cracks, palagonite in the joints.</summary>
		public const string BasaltPillowTalus = "BasaltPillowTalus";
		/// <summary>Seep sediment: sulphide-black mud under white and orange Beggiatoa mats.</summary>
		public const string MudSeepSediment = "MudSeepSediment";
		/// <summary>Seep carbonate pavement: cavernous cream-grey slabs, black sulphide stains, mussel-shell hash.</summary>
		public const string ChemohermSeepPavement = "ChemohermSeepPavement";
		/// <summary>An asphalt flow: black ropy tar, brittle cracks, oil-sheen specks, an ooze dusting.</summary>
		public const string AsphaltFlow = "AsphaltFlow";
		/// <summary>A methane-hydrate crust: lumpy translucent yellow-white, pink ice-worm pits, sediment smears.</summary>
		public const string HydrateCrust = "HydrateCrust";
		/// <summary>A serpentinised trench scarp: slickensided facets, green serpentine veins, rust seep streaks.</summary>
		public const string CliffSerpentineScarp = "CliffSerpentineScarp";
		/// <summary>Turbidite channel sand: graded grey-black ripples with glass grains.</summary>
		public const string SandTurbiditeChannel = "SandTurbiditeChannel";
		/// <summary>Darker ooze of the trench rim.</summary>
		public const string SiltOozeRim = "SiltOozeRim";
		/// <summary>A drowned causeway: huge polygonal basalt setts under ooze, a white sponge crust.</summary>
		public const string FlagstoneCyclopeanCauseway = "FlagstoneCyclopeanCauseway";
		/// <summary>A sunk temple way: rectangular slabs cut with glyph grooves.</summary>
		public const string FlagstoneProcessional = "FlagstoneProcessional";
		/// <summary>Fan-set pillow-glass setts: glassy basalt arcs with ooze joints.</summary>
		public const string PebblesPillowGlass = "PebblesPillowGlass";
		/// <summary>Hyaloclastite grit: dark volcanic-glass sand.</summary>
		public const string SandHyaloclastite = "SandHyaloclastite";
		/// <summary>Peridotite and pillow gravel: olive-black chips, rust and green.</summary>
		public const string GravelPeridotiteBar = "GravelPeridotiteBar";

		// ── Steppe (biome round, 2026-10-26): the Sary-Arka's own grounds: feather-grass sward on chestnut earth, wormwood steppe, takyr and sor pans, porphyry grit, kum sand, bobak mounds, dry-valley meadow, frost steppe (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Kovyl sward: feather grass and fescue tussocks combed one way, chestnut gaps, silver awn mats, black soil crust, wandering lichen.</summary>
		public const string GrassDryKovylSward = "GrassDryKovylSward";
		/// <summary>Chestnut earth trodden round the camps, wells and folds: fine salt flecks, hoof pocks, dung.</summary>
		public const string SoilChestnut = "SoilChestnut";
		/// <summary>Wormwood steppe: sparse grass between grey semi-shrubs on light chestnut, autumn rust, saltwort rose.</summary>
		public const string GrassDryWormwoodSteppe = "GrassDryWormwoodSteppe";
		/// <summary>A takyr: smooth buff-pink clay pan in polygon plates with curled edges, no salt, dust ripples in the cracks.</summary>
		public const string ClayTakyr = "ClayTakyr";
		/// <summary>A sor: puffy hummocked salt crust over black brine mud, holes broken through to it, brown brine pools, red glasswort fringe.</summary>
		public const string SaltCrustSor = "SaltCrustSor";
		/// <summary>Porphyry grit: desert-steppe pavement of angular purple-red chips on the sopka shoulders, feldspar specks, white quartz.</summary>
		public const string GravelPorphyryGrit = "GravelPorphyryGrit";
		/// <summary>Kum: hummocky grey-yellow steppe sand stitched with sand sedge, saxaul twig litter.</summary>
		public const string SandKum = "SandKum";
		/// <summary>A bobak mound's pale calcareous spoil: burrow holes, nitrophile green.</summary>
		public const string SoilBobakMound = "SoilBobakMound";
		/// <summary>A dry valley's meadow: tall kovyl, Phlomis purple, elm leaf litter by a spring.</summary>
		public const string GrassDryLogMeadow = "GrassDryLogMeadow";
		/// <summary>Frost steppe: low fescue on frost-cracked chestnut earth, the cold band's.</summary>
		public const string GrassDryColdSteppe = "GrassDryColdSteppe";
		/// <summary>A sopka crag: blocky purple-red porphyry, pale phenocrysts, orange Xanthoria and black lichen.</summary>
		public const string CliffPorphyrySopka = "CliffPorphyrySopka";
		/// <summary>A steppe river's bed: yellow silt over rounded porphyry and quartz pebbles.</summary>
		public const string GravelSteppeRiver = "GravelSteppeRiver";
		/// <summary>Brackish ooze: black sulphide mud under white salt rims, gypsum rosettes, brine-shrimp cysts.</summary>
		public const string MudBrackishOoze = "MudBrackishOoze";
		/// <summary>The Horde road: a braided cart road of many parallel ruts, crushed grass crowns, dung, tumbleweed caught at the edges.</summary>
		public const string PathHordeRoad = "PathHordeRoad";
		/// <summary>A sheep trod: narrow braided trails of grey dust, hoof pocks.</summary>
		public const string PathSheepTrodSteppe = "PathSheepTrodSteppe";
		/// <summary>A sand track over the kum: pale sand in two deep grooves.</summary>
		public const string PathSandTrackKum = "PathSandTrackKum";
		/// <summary>The takyr's faint wheel-polish track: the pan itself is the road.</summary>
		public const string PathTakyrPolish = "PathTakyrPolish";
		/// <summary>Horde-town paving: buff square baked brick on the diagonal, wide gypsum joints, wheel-worn, sand in the joints.</summary>
		public const string BrickSquareHorde = "BrickSquareHorde";
		/// <summary>Grey river sand of quartz and feldspar with pink porphyry grains.</summary>
		public const string SandGreyRiver = "SandGreyRiver";
		/// <summary>Bar gravel of porphyry, white quartz and grey granite.</summary>
		public const string GravelPorphyryQuartz = "GravelPorphyryQuartz";

		// ── Hills (biome round, 2026-10-26): the terraced schist hill country's own grounds: sheep pasture, terrace tilth, summer-burnt slopes, broom fells, schist shatter, quartz crests, the cold fells' turf (decision 12; until I9's variants of a recipe, each its own recipe). ──
		/// <summary>Sheep-bitten hill pasture: tawny-green fescue, schist chips, mica glints, rust chips, thyme pink, sheep pellets.</summary>
		public const string GrassHillPasture = "GrassHillPasture";
		/// <summary>Terrace tilth: hoed schist loam full of flat shards, mica, vine prunings and olive leaves.</summary>
		public const string SoilTerraceTilth = "SoilTerraceTilth";
		/// <summary>A summer-burnt slope: straw grass, asphodel rosettes, schist and snail shells.</summary>
		public const string GrassDryBurntSlope = "GrassDryBurntSlope";
		/// <summary>A broom-and-heath fell: wiry tree-heath, lavender and cushion-broom litter, yellow and purple flecks.</summary>
		public const string HeathBroomFell = "HeathBroomFell";
		/// <summary>Schist shatter: flaggy plates on edge and scree, garnets, rust, yellow map lichen, orange Xanthoria.</summary>
		public const string RockSchistShatter = "RockSchistShatter";
		/// <summary>Crest grus and quartz float on the windswept summits: white and milky quartz among silver schist grit.</summary>
		public const string GravelCrestQuartz = "GravelCrestQuartz";
		/// <summary>The cold fells' turf: bleached mat-grass, frost-heaved hummocks, cushion broom litter.</summary>
		public const string GrassFellTurf = "GrassFellTurf";
		/// <summary>The frozen reach's snow cap on the hill pasture, schist showing through.</summary>
		public const string SnowHillCap = "SnowHillCap";
		/// <summary>A crenulated silver schist face: folded beds, white quartz veins, rust drip streaks, lichen blotches.</summary>
		public const string CliffSchistCrenulated = "CliffSchistCrenulated";
		/// <summary>An imbricated schist-slab stream bed: flat plates shingled downstream, white quartz cobbles, gold glints, clear water.</summary>
		public const string PebblesSchistSlab = "PebblesSchistSlab";
		/// <summary>Grey mica silt with sunk olive leaves.</summary>
		public const string SiltMica = "SiltMica";
		/// <summary>A cart road of schist grit, wheel ruts worn into the bare bedrock bands, a fescue crown, mica dust.</summary>
		public const string PathRockCart = "PathRockCart";
		/// <summary>A stony footpath of loose foot-polished schist shards, darker step risers.</summary>
		public const string PathSchistShard = "PathSchistShard";
		/// <summary>A draille: a sheep-churned ochre drove road over the fells, hoof pocks, pellets, wool tufts.</summary>
		public const string PathDrove = "PathDrove";
		/// <summary>Schist on edge: narrow plates set vertically in fan rows between granite kerbs, lichen in the old joints.</summary>
		public const string FlagstoneSchistEdge = "FlagstoneSchistEdge";
		/// <summary>Micaceous schist sand: silver and gold mica, garnet sand.</summary>
		public const string SandMicaSchist = "SandMicaSchist";
		/// <summary>Flat schist shingle with white quartz pebbles and rust-stained plates.</summary>
		public const string GravelSchistShingle = "GravelSchistShingle";

		// ── Wetlands (biome round, 2026-10-26): the Batiha reed marsh's own grounds: reedbed silt, shell marl, buffalo lawn, sabkha levee,
		// floating raft, dibin silt-sand, river banks, alabaster (decision 12; until I9's variants of a recipe, each its own recipe). ──

		/// <summary>Reedbed silt: olive-grey silt under broken culm strips, snail shells, red Azolla flecks.</summary>
		public const string MudReedbedSilt = "MudReedbedSilt";
		/// <summary>Shell marl flat: white lake-lime strewn with spired snail shells and mussel valves, a green stonewort film.</summary>
		public const string MarlShellFlat = "MarlShellFlat";
		/// <summary>Buffalo lawn: grazed knotgrass and sedge, hoof pocks, dung pats, egret feathers.</summary>
		public const string GrassBuffaloLawn = "GrassBuffaloLawn";
		/// <summary>A sabkha levee back: cracked silt with a white salt efflorescence and red salt-bush tips.</summary>
		public const string SaltSabkhaLevee = "SaltSabkhaLevee";
		/// <summary>A floating reed raft (kibasha): a tangled rhizome mat, white roots, green shoots, water in the pockets.</summary>
		public const string PeatRaftMat = "PeatRaftMat";
		/// <summary>Dibin and levee silt-sand: pale buff, sun-dried ripples, potsherds, palm litter.</summary>
		public const string SandDibinSilt = "SandDibinSilt";
		/// <summary>A river-cut silt bank: grey and buff laminae hung with roots, swallow holes.</summary>
		public const string SiltRiverBank = "SiltRiverBank";
		/// <summary>Fars alabaster: grey-white gypsum bedded with green-grey and red marl, selenite glints, flutes.</summary>
		public const string GypsumAlabaster = "GypsumAlabaster";
		/// <summary>Channel silt: olive, shell-strewn, scored by eels, drowned reed.</summary>
		public const string SiltChannelShell = "SiltChannelShell";
		/// <summary>A stonewort meadow: white marl under Chara beds, shells.</summary>
		public const string MarlStonewort = "MarlStonewort";
		/// <summary>The dyke crown: an embanked grey-clay causeway with reed-fascine edging, bitumen-patched brick.</summary>
		public const string ClayDykeCrown = "ClayDykeCrown";
		/// <summary>A buffalo track through the reed: deep hoof-trod lane, water-filled pocks, trampled culms.</summary>
		public const string PathMudBuffaloTrack = "PathMudBuffaloTrack";
		/// <summary>A salt trace: white crust broken to grey silt, trodden into a track over the sabkha.</summary>
		public const string PathSaltTrace = "PathSaltTrace";
		/// <summary>Bitumen brick: square baked yellow brick in black bitumen, seal-stamped, the odd turquoise glazed one.</summary>
		public const string FlagstoneBitumenBrick = "FlagstoneBitumenBrick";
		/// <summary>Shell-hash silt sand: pale, spired snail shells and mica.</summary>
		public const string SandShellHash = "SandShellHash";
		/// <summary>A shell bar: no stone in the alluvium, only valves, spired shells, rolled clay balls and sherds.</summary>
		public const string GravelShellBar = "GravelShellBar";

		// ── Mangrove (biome round, 2026-10-26): the Swahili coast's mangal grounds: black mkoko mud, fiddler flat, pencil-root mud, back-
		// mangal floor, the hypersaline ukanda, raised coral rag, red shamba sand, creek banks (decision 12; until I9's variants of a
		// recipe, each its own recipe). ──

		/// <summary>Mkoko mud: soft black sulphidic mud under a brown oxidised skin, crab-burrow holes with rust halos, fallen yellow leaves.</summary>
		public const string MudMkokoBrown = "MudMkokoBrown";
		/// <summary>A fiddler flat: firm grey-tan sandy mud, crab-pellet rosettes round burrows, water films.</summary>
		public const string MudFiddlerFlat = "MudFiddlerFlat";
		/// <summary>Pencil-root mud of the seaward fringe: olive diatom film, barnacle specks, oyster shell.</summary>
		public const string MudPencilRoot = "MudPencilRoot";
		/// <summary>Back-mangal floor: firm brown mud under a crab-shredded red-brown leaf mat, whelk shells, knee-root pocks.</summary>
		public const string ForestFloorMangal = "ForestFloorMangal";
		/// <summary>The ukanda: a warm tan hypersaline flat, cracked in polygons, lifted algal-mat chips, white salt rime.</summary>
		public const string SaltUkandaCrust = "SaltUkandaCrust";
		/// <summary>Raised fossil reef: grey-black razor-pitted pavement, fossil heads, red soil pockets, cream fresh breaks.</summary>
		public const string RagPavement = "RagPavement";
		/// <summary>Red shamba sand: red coastal sand-loam under the palms, frond litter, husk fibre, rag knobs.</summary>
		public const string SoilShambaRed = "SoilShambaRed";
		/// <summary>A creek bank: slumping grey mud with an exposed root mat, crab burrows, snails on the roots.</summary>
		public const string MudCreekBankMangal = "MudCreekBankMangal";
		/// <summary>The rag cliff: black-weathered, pinnacled, with a cream wave-notch band and red terra-rossa streaks.</summary>
		public const string RagCliffNotched = "RagCliffNotched";
		/// <summary>Creek bed: black fluid mud, leaf rafts and propagules.</summary>
		public const string MudCreekBedBlack = "MudCreekBedBlack";
		/// <summary>Muddy lagoon sand: grey-brown, sandfish furrows, cerith shells, algal turf.</summary>
		public const string SandLagoonMuddy = "SandLagoonMuddy";
		/// <summary>The rag-and-laterite road: red-brown sand with grey rag knuckles and coral grit in the ruts.</summary>
		public const string LateriteRagRoad = "LateriteRagRoad";
		/// <summary>A creek path: trodden mud with footprints and crab holes.</summary>
		public const string PathMudCreekPath = "PathMudCreekPath";
		/// <summary>The pole-walk: two bark-stripped mangrove poles laid lengthwise, end-lapped on forked stakes, mud between.</summary>
		public const string PathMudPoleWalk = "PathMudPoleWalk";
		/// <summary>Chokaa floor: polished lime concrete with coral aggregate, slab panels, a red-ochre border band.</summary>
		public const string FlagstoneChokaa = "FlagstoneChokaa";
		/// <summary>Creek-mouth coral-quartz sand: grey-cream, whelk and cowrie shells.</summary>
		public const string SandCreekMouth = "SandCreekMouth";
		/// <summary>Rag rubble and shell: dark jagged rag cobbles, oyster valves, propagules.</summary>
		public const string GravelRagShell = "GravelRagShell";

		// ── Lake and River (biome round, 2026-10-26): the clear cold lake's and the Rhenish river's own grounds (decision 12; until I9's variants of a recipe, each its own recipe). ──

		/// <summary>Lake strand shingle: flat wave-sorted discs imbricated seaward, a dark wet band at the low edge, algae and chaff specks.</summary>
		public const string PebblesStrandShingle = "PebblesStrandShingle";
		/// <summary>Lake isoetid flat: clean pale sand under a short shoreweed turf, mica glints.</summary>
		public const string SandIsoetidFlat = "SandIsoetidFlat";
		/// <summary>Lake drift line: reed chaff, needles, bark scraps and feathers along the waterline.</summary>
		public const string LitterGeschwemmsel = "LitterGeschwemmsel";
		/// <summary>Lake molasse ledge: soft grey-green sandstone pavement, glauconite grains and snail fossils.</summary>
		public const string MolasseLedge = "MolasseLedge";
		/// <summary>Lake ice-push rampart: shoved gravel ridges, jumbled cobbles, crushed reed.</summary>
		public const string GravelPushRampart = "GravelPushRampart";
		/// <summary>Lake molasse cliff: cross-bedded grey-green sandstone honeycombed by tafoni at the waterline, wet streaks.</summary>
		public const string MolasseTafoniCliff = "MolasseTafoniCliff";
		/// <summary>Lake inflow delta: grey glacial-flour sand in ripples, schist glints.</summary>
		public const string SandDeltaGlacialFlour = "SandDeltaGlacialFlour";
		/// <summary>Lake furrow-stone shelf: pale cobble pavement crusted blue-green by algae, etched meandering grooves.</summary>
		public const string LimestoneFurrowShelf = "LimestoneFurrowShelf";
		/// <summary>Lake halde: olive pondweed silt with sunk leaves and shell grit.</summary>
		public const string SiltPondweedHalde = "SiltPondweedHalde";
		/// <summary>Lake deeps: olive-black laminated ooze, faint laminae, sunk needles.</summary>
		public const string MudVarvedGyttja = "MudVarvedGyttja";
		/// <summary>Lake towpath: packed grey lake gravel, rope-worn verge, damp hoof pits.</summary>
		public const string GravelTowpath = "GravelTowpath";
		/// <summary>Lake strand path: pebbles pressed flat with sand between.</summary>
		public const string PathStrandTrod = "PathStrandTrod";
		/// <summary>Lake Bollenstein paving: round lake cobbles set on edge in herringbone rows, moss in the joints.</summary>
		public const string FlagstoneBollenstein = "FlagstoneBollenstein";
		/// <summary>Lake delta flour-sand bar: pale grey, mica and black schist grains.</summary>
		public const string SandDeltaFlourBar = "SandDeltaFlourBar";
		/// <summary>Lake erratic gravel bar: pink granite, gneiss and green molasse pebbles, white quartz.</summary>
		public const string GravelErraticBar = "GravelErraticBar";
		/// <summary>River flood loam: fresh grey-brown silt drape, polygonal drying cracks, twig brash and mica.</summary>
		public const string MudFloodLoam = "MudFloodLoam";
		/// <summary>River levee sand: golden flood drape rippled by the current, flotsam twigs.</summary>
		public const string SandLeveeDrape = "SandLeveeDrape";
		/// <summary>River wet margin: glistening dark mud printed with heron and beaver tracks.</summary>
		public const string MudWetMargin = "MudWetMargin";
		/// <summary>River bank ledge: steep-cleaved greywacke ribs, white quartz veins, rust weeping.</summary>
		public const string GreywackeBankLedge = "GreywackeBankLedge";
		/// <summary>River scour bank: shoved cobbles jumbled with sand between, crushed twigs.</summary>
		public const string GravelScourBank = "GravelScourBank";
		/// <summary>River narrows cliff: vertical cleavage, slate partings, white quartz veins, weeping rust.</summary>
		public const string GreywackeNarrowsCliff = "GreywackeNarrowsCliff";
		/// <summary>River riffle armour: flat cobbles shingled downstream, algal film on their tops.</summary>
		public const string GravelArmourRiffle = "GravelArmourRiffle";
		/// <summary>River Rhine gravel: rounded quartz, basalt, red sandstone and greywacke cobbles under olive algae.</summary>
		public const string GravelRhineRun = "GravelRhineRun";
		/// <summary>River Kolk: dark silt-sand of the scour pools, slow ripples, sunk leaves.</summary>
		public const string SandScourPool = "SandScourPool";
		/// <summary>River reef: bedrock ribs across the channel, potholes, torrent algae.</summary>
		public const string GreywackeReefRibs = "GreywackeReefRibs";
		/// <summary>River ford paving: big flat slabs set in gravel, cart-worn, algae in the joints.</summary>
		public const string GreywackeFordPaving = "GreywackeFordPaving";
		/// <summary>River bar track: trodden sand-gravel with hoof pits and sand lenses.</summary>
		public const string PathBarTrack = "PathBarTrack";
		/// <summary>River Rhenish setts: square basalt setts in fan arcs, wheel-polished, sand joints.</summary>
		public const string BasaltRhenishSetts = "BasaltRhenishSetts";
		/// <summary>River point-bar sand: golden-grey, mica and black heavy-mineral placer streaks.</summary>
		public const string SandPointBar = "SandPointBar";
		/// <summary>River bleached bar: sun-dried quartz-white gravel, twigs, sand lenses.</summary>
		public const string GravelBleachedBar = "GravelBleachedBar";

		// Rocky Terrain (biome round, 2026-10-26): the shattered high cold desert of the Pamir's own grounds.
		/// <summary>Frost-split hornstone pavement: knife-edged blue-black joints, cordierite knots, a rust skin and yellow and white crust lichens.</summary>
		public const string RockHornstoneShatter = "RockHornstoneShatter";
		/// <summary>Hornfels chip aprons: angular splinters with conchoidal glints and white quartz-vein chips.</summary>
		public const string GravelSplinterGrit = "GravelSplinterGrit";
		/// <summary>A pamir sedge marsh: short hummock sedge in salt-ringed turf, trampled argali paths.</summary>
		public const string GrassDrySazTurf = "GrassDrySazTurf";
		/// <summary>Cold-desert gravel steppe: tan loess-grit with sparse dark chips, wind-deflated.</summary>
		public const string GravelTereskenReg = "GravelTereskenReg";
		/// <summary>A summit block field: big black-gapped hornstone blocks, rust undersides, yellow crust.</summary>
		public const string RockSplitBlockField = "RockSplitBlockField";
		/// <summary>Alluvial-fan silt: worked tilth with braided rill lines and cleared-stone flecks.</summary>
		public const string SoilFanSilt = "SoilFanSilt";
		/// <summary>Sun-baked colluvium: crazed red-brown crust plates over grit.</summary>
		public const string ClaySunBaked = "ClaySunBaked";
		/// <summary>A thin-aired world's hornstone: rust-skinned joints, ochre dust drifts in the lee, wind-fluted faces.</summary>
		public const string RockRustLeeHornstone = "RockRustLeeHornstone";
		/// <summary>An airless world's shattered hornstone: sharp fresh fracture faces, micro-pits, no dust.</summary>
		public const string RockZapShatter = "RockZapShatter";
		/// <summary>A splinter wall: vertical hornfels sheets, rust weeps, white quartz veins, manganese streaks.</summary>
		public const string CliffHornfelsSplinter = "CliffHornfelsSplinter";
		/// <summary>Lajvard marble bands: folded grey-white strata, lapis smears, gold pyrite flecks.</summary>
		public const string CliffMarbleLajvard = "CliffMarbleLajvard";
		/// <summary>Glacier-milk cobbles: rounded grey hornfels, pale leucogranite and white marble under a silt film.</summary>
		public const string GravelPanjMilk = "GravelPanjMilk";
		/// <summary>Dragon-lake marl: soft grey-green silt with charophyte crust threads.</summary>
		public const string SiltDragonMarl = "SiltDragonMarl";
		/// <summary>A caravan track: packed grit, cleared stones heaped along the verges, twin pack-animal ruts.</summary>
		public const string GravelCaravanTrack = "GravelCaravanTrack";
		/// <summary>A pack trail: worn silt with splinter chips.</summary>
		public const string PathPackTrail = "PathPackTrail";
		/// <summary>Fort-street river cobbles set on edge in rows with mud joints, a white marble line down the centre.</summary>
		public const string FlagstoneFortStreet = "FlagstoneFortStreet";
		/// <summary>Glacier sand: grey quartz grains with a glitter of mica.</summary>
		public const string SandMicaPamir = "SandMicaPamir";
		/// <summary>Braid-bar gravel: sub-rounded mixed cobbles.</summary>
		public const string GravelBraid = "GravelBraid";

		// Geyser Basin (biome round, 2026-10-26): the hot ground of the Icelandic interior's own grounds.
		/// <summary>Frost-sorted fell-field gravel: basalt grit, rose and cream rhyolite chips, pumice-pale lapilli and obsidian glints.</summary>
		public const string GravelMelurFell = "GravelMelurFell";
		/// <summary>Geyserite apron: scalloped micro-rims and beaded popcorn knobs, opal sheen, a pink splash tinge.</summary>
		public const string SinterGeyseriteApron = "SinterGeyseriteApron";
		/// <summary>Thermophile mat: flow-aligned laminae banded orange to ochre to olive to brown, gas blisters.</summary>
		public const string SinterThermophileMat = "SinterThermophileMat";
		/// <summary>Altered rhyolite clay: crazed polygons streaked rose and kaolin-white, flecked with yellow near the vents.</summary>
		public const string ClaySolfataraClay = "ClaySolfataraClay";
		/// <summary>A mud-pot flat: spatter rings, dried skins at the rims, a wet centre.</summary>
		public const string MudPotFlat = "MudPotFlat";
		/// <summary>The warm halo: a vivid bent lawn and emerald moss cushions, never frosted.</summary>
		public const string MossWarmHalo = "MossWarmHalo";
		/// <summary>A rhyolite tephra sheet: cream lapilli 1-4 cm, grey lithics, wind ripples.</summary>
		public const string AshTephraSheet = "AshTephraSheet";
		/// <summary>A late fonn patch: sun-cupped snow banded with ash, a dark melt fringe.</summary>
		public const string SnowFonnPatch = "SnowFonnPatch";
		/// <summary>Painted scree: platy rhyolite flakes rose, cream and green-grey, with obsidian chips.</summary>
		public const string GravelPaintedScree = "GravelPaintedScree";
		/// <summary>A painted wall: flow-folded bands green-grey, ochre and lavender, bleached zones with ochre halos.</summary>
		public const string CliffRhyolitePainted = "CliffRhyolitePainted";
		/// <summary>Warm-stream gravel: rhyolite, basalt and obsidian cobbles with orange mat skins and green streamers.</summary>
		public const string GravelWarmStream = "GravelWarmStream";
		/// <summary>Hot-pool ooze: smooth silica silt with an orange mat rim band.</summary>
		public const string SinterHotPoolOoze = "SinterHotPoolOoze";
		/// <summary>A bridle-way: three to six sunken hoof grooves in gravel with turf and moss ribs between.</summary>
		public const string PathReidgataRuts = "PathReidgataRuts";
		/// <summary>A pony trod: one dished trail, frost-heaved stones pushed aside.</summary>
		public const string PathPonyTrod = "PathPonyTrod";
		/// <summary>Hella paving: irregular flat basalt flags with moss joints and dung stains.</summary>
		public const string FlagstoneHella = "FlagstoneHella";
		/// <summary>Black sandur: basalt sand with floating pumice-pale specks and obsidian glints.</summary>
		public const string SandBlackSandur = "SandBlackSandur";
		/// <summary>Rhyolite and obsidian bar gravel: rose and cream rhyolite, black glass, basalt and a little geyserite.</summary>
		public const string GravelRhyoliteObsidian = "GravelRhyoliteObsidian";
	}

	/// <summary>Bark families: the tileable trunk textures trees and woody details use.</summary>
	public static class Bark
	{
		public const string Brown = "Brown";
		public const string Pine = "Pine";
		public const string Birch = "Birch";
		public const string Palm = "Palm";
		public const string Cactus = "Cactus";
		public const string Bamboo = "Bamboo";
		public const string Dead = "Dead";

		// ── Added by the vegetation expansion (2026-10-10) ──

		/// <summary>Smooth silver-grey bark, faintly wrinkled and lichen-blotched: beech, kapok, baobab, dragon tree.</summary>
		public const string Smooth = "Smooth";
		/// <summary>Pale green-white bark with black lenticels and scars: aspen.</summary>
		public const string Aspen = "Aspen";
		/// <summary>Grey-brown, deeply fissured, twisted bark: olive.</summary>
		public const string Olive = "Olive";
		/// <summary>A date palm's stem: the diamond pattern of old frond bases.</summary>
		public const string DatePalm = "DatePalm";
		/// <summary>Shaggy reddish fibrous bark in long strips: Joshua tree, cypress, bald cypress, tree fern, banana.</summary>
		public const string Fibrous = "Fibrous";

		// ── Biome rounds (2026-10-26) ──

		/// <summary>Grey-brown scaly plates with resin streaks: the stone pine (Alpine Meadow's Cembra).</summary>
		public const string CembraPlate = "CembraPlate";
		/// <summary>Grey-brown bark peeling in plates: the East African yellowwood (Mountain Slope's Podocarpus).</summary>
		public const string Flaky = "Flaky";
		/// <summary>Red-brown bark peeling in plates: the kosso (Mountain Slope's Hagenia).</summary>
		public const string FlakyRed = "FlakyRed";
		/// <summary>Green-brown moss sleeving the bark: the giant heather (Mountain Slope).</summary>
		public const string MossSleeve = "MossSleeve";
		/// <summary>Smooth yellow-green photosynthetic bark, greyer at the foot: Scrubland's palo verde.</summary>
		public const string GreenBark = "GreenBark";
		/// <summary>Grey shaggy shreds over a dark inner bark: Scrubland's desert ironwood.</summary>
		public const string ShredGrey = "ShredGrey";
		/// <summary>Papery white-grey skin peeling off copper-red: Scrubland's elephant tree.</summary>
		public const string PaperPeel = "PaperPeel";
		/// <summary>Lime-yellow powdery bark: Savanna's fever tree.</summary>
		public const string Powder = "Powder";
		/// <summary>Pale grey bark broken into small rectangular plates: Savanna's leadwood.</summary>
		public const string SnakeSkin = "SnakeSkin";
		// Grassland (biome round, 2026-10-26).
		/// <summary>A wild pear's dark grey bark broken into small square plates.</summary>
		public const string Checker = "Checker";
		/// <summary>Turkey oak bark: deep grey furrows with orange-red fissure floors.</summary>
		public const string Cerris = "Cerris";
		/// <summary>White poplar bark: grey-white with black diamond lenticels, dark and furrowed at the foot.</summary>
		public const string WhitePoplar = "WhitePoplar";
		// Plains (biome round, 2026-10-26).
		/// <summary>Plains cottonwood bark: deep grey furrows between flat ridges.</summary>
		public const string CottonwoodRidge = "CottonwoodRidge";
		/// <summary>Bur oak bark: corky deep ridges.</summary>
		public const string CorkRidge = "CorkRidge";
		/// <summary>Osage orange bark: orange-brown fibrous strips.</summary>
		public const string OsageStrip = "OsageStrip";
		// Steppe (biome round, 2026-10-26).
		/// <summary>Steppe elm bark: rough dark grey-brown, deep-furrowed.</summary>
		public const string Karagach = "Karagach";
		/// <summary>Saxaul bark: grey-brown, spiral-fissured, very hard.</summary>
		public const string SaxaulTwist = "SaxaulTwist";
		// Desert (biome round, 2026-10-26).
		/// <summary>The desert date's grey-brown bark in deep fissures, green under-bark showing in the cracks.</summary>
		public const string BalanitesFurrow = "BalanitesFurrow";
		/// <summary>The atil's smooth grey-white bark with dark knots.</summary>
		public const string AtilPale = "AtilPale";
		// Badlands (biome round, 2026-10-26).
		/// <summary>The apricot's red-brown lenticelled bark with amber gum beads.</summary>
		public const string ApricotScale = "ApricotScale";
		/// <summary>The oleaster's shaggy grey-brown bark in long strips.</summary>
		public const string OleasterStrip = "OleasterStrip";
		/// <summary>The oriental plane's camouflage of flaking cream, olive and grey.</summary>
		public const string PlaneFlake = "PlaneFlake";
		// Hills (biome round, 2026-10-26).
		/// <summary>Cork oak bark: thick pale-grey corky ridges.</summary>
		public const string Cork = "Cork";
		/// <summary>Stone pine bark: orange-red puzzle plates between grey fissures.</summary>
		public const string PinePlate = "PinePlate";
	}

	/// <summary>
	/// The recipe for every procedural surface: ground families and bark.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Colours are picked from photographs of the real material in daylight and authored in sRGB
	/// (<see cref="Hex"/>); the ramp's three stops are the shadowed, typical and sunlit-dry tones,
	/// not light and dark versions of one colour, because real ground varies in hue as well as
	/// value (dry grass yellows, wet soil reddens).
	/// </para>
	/// <para>
	/// Tile sizes follow what the material's features measure: grass blades and leaf litter are a
	/// few centimetres, so their tiles are three metres; rock and cliff strata read at tens of
	/// centimetres, so theirs are six to ten. Too small a tile shows the repeat across a valley,
	/// too large blurs the near ground.
	/// </para>
	/// </remarks>
	public static class SurfaceCatalogue
	{
		/// <summary>An sRGB colour from #rrggbb.</summary>
		public static Color Hex(string hex)
		{
			if (string.IsNullOrEmpty(hex))
			{
				return Color.magenta;
			}
			int start = hex[0] == '#' ? 1 : 0;
			int value = Convert.ToInt32(hex.Substring(start, 6), 16);
			return new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1f);
		}

		private static Color[] Hexes(params string[] hex) => Array.ConvertAll(hex, Hex);

		private static SurfaceRecipe R(string name, float tile, float relief, string dark, string mid, string light,
			int baseCells, SurfaceMotif motif, int motifCells, float motifSize, float motifWeight, string motifDark, string motifLight,
			float smoothness, float smoothVar = 0.1f, float occlusion = 0.7f, int octaves = 5, float persistence = 0.5f, float warp = 0.05f,
			int motifCount = 0, bool vertical = false, Color[] speckles = null, int speckleCount = 0, float speckleSize = 0.002f, float metallic = 0f)
		{
			return new SurfaceRecipe
			{
				Name = name,
				TileMetres = tile,
				ReliefMetres = relief,
				Dark = Hex(dark),
				Mid = Hex(mid),
				Light = Hex(light),
				BaseCells = baseCells,
				Octaves = octaves,
				Persistence = persistence,
				Warp = warp,
				Motif = motif,
				MotifCells = motifCells,
				MotifCount = motifCount,
				MotifSize = motifSize,
				MotifWeight = motifWeight,
				MotifDark = Hex(motifDark),
				MotifLight = Hex(motifLight),
				Vertical = vertical,
				Speckles = speckles,
				SpeckleCount = speckleCount,
				SpeckleSize = speckleSize,
				Smoothness = smoothness,
				SmoothnessVariation = smoothVar,
				Metallic = metallic,
				Occlusion = occlusion,
				NormalScale = 1f,
			};
		}

		/// <summary>Every ground family, in a fixed order.</summary>
		public static readonly SurfaceRecipe[] GroundRecipes =
		{
			R(Ground.Grass, 3f, 0.04f, "#26341a", "#46632a", "#77953f", 4, SurfaceMotif.Blades, 8, 0.03f, 0.8f, "#3b5822", "#93b24c", 0.15f, motifCount: 36000),
			R(Ground.GrassDry, 3f, 0.04f, "#463b22", "#83703f", "#b29b5e", 4, SurfaceMotif.Blades, 8, 0.032f, 0.8f, "#977d46", "#d6c27e", 0.1f, motifCount: 32000),
			R(Ground.GrassMeadow, 3f, 0.04f, "#2a3a1c", "#4f6d2e", "#82a046", 4, SurfaceMotif.Blades, 8, 0.028f, 0.8f, "#43622a", "#9ab854", 0.15f, motifCount: 34000,
				speckles: Hexes("#f2f2e8", "#f0d040", "#9b6fc8", "#e07a9a"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.Moss, 3f, 0.03f, "#1f3312", "#3d5e1e", "#6f8f2e", 6, SurfaceMotif.Clumps, 24, 0.5f, 0.7f, "#2f4a18", "#8aa040", 0.25f, 0.15f, 0.8f),
			R(Ground.ForestFloor, 3f, 0.04f, "#2a1d12", "#4a3420", "#6b4c2e", 6, SurfaceMotif.Leaves, 8, 0.028f, 0.85f, "#5a3a1a", "#b07a32", 0.2f, 0.2f, 0.85f, motifCount: 2200),
			R(Ground.NeedleLitter, 3f, 0.03f, "#24180e", "#3e2a18", "#5e4024", 6, SurfaceMotif.Needles, 8, 0.03f, 0.8f, "#5a3418", "#a0663a", 0.15f, 0.1f, 0.8f, motifCount: 9000),
			R(Ground.JungleFloor, 3f, 0.05f, "#18120a", "#2c2214", "#44341e", 6, SurfaceMotif.Leaves, 8, 0.04f, 0.85f, "#2e3a14", "#6a5a24", 0.3f, 0.25f, 0.85f, motifCount: 1600),
			R(Ground.Soil, 4f, 0.03f, "#3a2a1c", "#5a4330", "#7a5e44", 8, SurfaceMotif.Granular, 64, 0.1f, 0.5f, "#2e2216", "#6a5038", 0.12f, 0.1f),
			R(Ground.Mud, 4f, 0.03f, "#2a2016", "#3e3022", "#54422e", 6, SurfaceMotif.Granular, 40, 0.1f, 0.4f, "#211912", "#4a3a28", 0.45f, 0.4f),
			R(Ground.Clay, 5f, 0.04f, "#7a3e22", "#a0562e", "#c27446", 6, SurfaceMotif.Cracks, 10, 0.04f, 0.7f, "#4a2414", "#5a2c18", 0.15f),
			R(Ground.Sandstone, 8f, 0.25f, "#8a4a2a", "#b86a3e", "#d89a64", 4, SurfaceMotif.Strata, 14, 0.1f, 0.75f, "#7a3c22", "#e0b080", 0.1f, 0.05f, 0.8f),
			R(Ground.Sand, 4f, 0.02f, "#b08a56", "#d2ae74", "#e8cc94", 6, SurfaceMotif.Ripples, 18, 0.1f, 0.6f, "#a88452", "#c8a46c", 0.12f, 0.05f, 0.4f),
			R(Ground.SandBeach, 4f, 0.02f, "#c8b48c", "#e0cfa6", "#f0e2c0", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.4f, "#b8a47c", "#d8c69c", 0.2f, 0.3f, 0.4f,
				speckles: Hexes("#f4ede0", "#cbbfa8", "#8a8478"), speckleCount: 400, speckleSize: 0.0025f),
			R(Ground.Gravel, 2f, 0.03f, "#4a443c", "#6a645a", "#8a8478", 8, SurfaceMotif.Cobbles, 40, 0.12f, 0.85f, "#5a5650", "#a8a294", 0.15f, 0.1f, 0.85f),
			R(Ground.Pebbles, 2f, 0.05f, "#4e4436", "#6e624e", "#8e8068", 8, SurfaceMotif.Cobbles, 14, 0.25f, 0.9f, "#4e4a44", "#b4aa98", 0.3f, 0.2f, 0.85f),
			R(Ground.Rock, 6f, 0.3f, "#4a4744", "#6e6a64", "#94908a", 4, SurfaceMotif.Fractured, 6, 0.06f, 0.7f, "#3a3734", "#7a766e", 0.2f, 0.1f, 0.8f),
			R(Ground.CliffRock, 10f, 0.6f, "#4e4840", "#746c60", "#9a9284", 4, SurfaceMotif.Strata, 22, 0.1f, 0.8f, "#423c34", "#a8a090", 0.15f, 0.05f, 0.85f),
			R(Ground.Basalt, 6f, 0.3f, "#1c1c1e", "#2e2e30", "#464648", 4, SurfaceMotif.Fractured, 8, 0.05f, 0.75f, "#141416", "#3a3a3c", 0.25f, 0.1f, 0.85f),
			R(Ground.Lava, 6f, 0.15f, "#1a1210", "#2a1a14", "#3a2a22", 4, SurfaceMotif.Cracks, 8, 0.05f, 0.8f, "#ff4a00", "#ffb020", 0.2f, 0.1f, 0.6f),
			R(Ground.Ash, 4f, 0.02f, "#2a2826", "#3c3a37", "#55524e", 6, SurfaceMotif.Granular, 48, 0.1f, 0.5f, "#1e1d1c", "#4a4744", 0.05f, 0.05f),
			R(Ground.Snow, 6f, 0.03f, "#c8d0dc", "#e4e9f0", "#f8fbff", 4, SurfaceMotif.Granular, 16, 0.1f, 0.3f, "#d0d8e4", "#ffffff", 0.45f, 0.1f, 0.4f),
			R(Ground.Ice, 8f, 0.05f, "#6f9cc0", "#a6c8e0", "#d8ecf8", 3, SurfaceMotif.Fractured, 4, 0.02f, 0.5f, "#e8f4ff", "#ffffff", 0.8f, 0.1f, 0.5f),
			R(Ground.SaltCrust, 6f, 0.03f, "#c8c4bc", "#e2ded6", "#f6f4f0", 6, SurfaceMotif.Ridges, 8, 0.03f, 0.7f, "#f8f8f6", "#ffffff", 0.3f, 0.1f, 0.6f),
			R(Ground.Regolith, 4f, 0.03f, "#4a4a48", "#6e6c68", "#8e8c88", 8, SurfaceMotif.Granular, 80, 0.1f, 0.5f, "#3e3e3c", "#8a8884", 0.05f, 0.05f,
				speckles: Hexes("#3a3936", "#9a9894"), speckleCount: 1500, speckleSize: 0.002f),
			R(Ground.Sulphur, 4f, 0.03f, "#b89a20", "#d8c040", "#f0e070", 6, SurfaceMotif.Cracks, 12, 0.03f, 0.6f, "#8a6a10", "#c0a030", 0.2f),
			R(Ground.Peat, 3f, 0.03f, "#1e140c", "#2e2014", "#44301e", 6, SurfaceMotif.Fibres, 48, 0.1f, 0.6f, "#120c08", "#5a4028", 0.35f, 0.3f),
			R(Ground.Limestone, 8f, 0.3f, "#9a968a", "#bcb8aa", "#dcd8ca", 4, SurfaceMotif.Fractured, 5, 0.08f, 0.7f, "#8a8676", "#d0ccbc", 0.2f, 0.1f, 0.8f),
			R(Ground.Lichen, 3f, 0.03f, "#4a4a3a", "#6a6a52", "#8a8a6a", 6, SurfaceMotif.Clumps, 30, 0.35f, 0.6f, "#7a8a4a", "#b0b070", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#d0c890", "#c0603a"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.Silt, 6f, 0.03f, "#2a3a40", "#44565a", "#627478", 4, SurfaceMotif.Granular, 32, 0.1f, 0.4f, "#22323a", "#56686c", 0.3f, 0.1f),
			R(Ground.Coral, 4f, 0.12f, "#a8a088", "#c4bca4", "#ddd6c0", 6, SurfaceMotif.Clumps, 20, 0.4f, 0.8f, "#c05a5a", "#e8a060", 0.25f, 0.1f, 0.8f,
				speckles: Hexes("#6ac0c8", "#c070c0", "#f0e080"), speckleCount: 700, speckleSize: 0.004f),
			R(Ground.Tholin, 4f, 0.03f, "#5a2a12", "#8a4a22", "#b06a34", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#4a220e", "#a05e2c", 0.08f, 0.05f),
			R(Ground.Frost, 6f, 0.03f, "#b8b0b8", "#d8d0d8", "#f0ecf0", 6, SurfaceMotif.Ridges, 10, 0.025f, 0.6f, "#e8e2e8", "#ffffff", 0.55f, 0.1f, 0.5f),
			R(Ground.CrackedEarth, 4f, 0.04f, "#6a5034", "#8a6a48", "#a8886a", 6, SurfaceMotif.Cracks, 14, 0.03f, 0.7f, "#3a2a1a", "#4a3424", 0.1f),
			R(Ground.Flagstone, 4f, 0.04f, "#5a5650", "#7a766e", "#9a968c", 6, SurfaceMotif.Cobbles, 6, 0.04f, 0.8f, "#6a665e", "#aaa69a", 0.2f, 0.1f, 0.8f),
			// ── The vegetation expansion (2026-10-10) ── Appended, so no other family's order moves (each texture is
			// seeded by its own family's name anyway, SurfaceSynth).

			// Raised-bog sphagnum: Sphagnum magellanicum's wine-red and S. papillosum's ochre cushions among green
			// hummocks a hand across, wet and shining in the hollows between; red, ochre and pale capitula flecked over.
			R(Ground.Sphagnum, 3f, 0.04f, "#2e3212", "#5a6a22", "#9a9436", 6, SurfaceMotif.Clumps, 18, 0.55f, 0.75f, "#6a2a1e", "#94a63a", 0.35f, 0.25f, 0.75f,
				speckles: Hexes("#8a3020", "#b08a30", "#a8c060"), speckleCount: 1400, speckleSize: 0.003f),
			// Ling heather moorland seen from above: dark wiry stems every way over brown peaty litter, flecked with the
			// purple of its flowers and the green of new shoots.
			R(Ground.Heath, 3f, 0.05f, "#261a1e", "#4c3840", "#6e5660", 4, SurfaceMotif.Blades, 8, 0.022f, 0.8f, "#3a2a2c", "#7a5e5e", 0.1f, motifCount: 30000,
				speckles: Hexes("#8a4a7e", "#a46a9a", "#4e5e34"), speckleCount: 4000, speckleSize: 0.003f),
			// Geyserite: pale grey sinter in crusted rims a few centimetres high, the rims stained orange and brown by
			// the thermophile mats that grow where the runoff cools.
			R(Ground.Sinter, 6f, 0.04f, "#a8a49a", "#ccc8bc", "#e8e4d8", 6, SurfaceMotif.Ridges, 10, 0.06f, 0.6f, "#a8622e", "#d8a050", 0.35f, 0.15f, 0.6f,
				speckles: Hexes("#c06a2a", "#7a5a3a", "#eee8d6"), speckleCount: 900, speckleSize: 0.004f),
			// Cinder: loose lapilli a centimetre or two across, glassy black, some oxidised red, in a finer black grit.
			R(Ground.Cinder, 2.5f, 0.04f, "#100e0e", "#221a18", "#382622", 8, SurfaceMotif.Cobbles, 56, 0.15f, 0.8f, "#161010", "#4a2218", 0.06f, 0.05f, 0.85f,
				speckles: Hexes("#7a3020", "#0e0c0c"), speckleCount: 1200, speckleSize: 0.003f),
			// Pahoehoe: a glassy black skin dragged into ropes a hand apart as the flow beneath it moved, the folds
			// curving (warped ripples) and shining where the glass is fresh.
			R(Ground.Pahoehoe, 4f, 0.08f, "#121214", "#242428", "#3c3a40", 4, SurfaceMotif.Ripples, 22, 0.1f, 0.7f, "#0c0c0e", "#4e4648", 0.55f, 0.2f, 0.8f, warp: 0.12f,
				speckles: Hexes("#4a3a5a", "#3a4a5a"), speckleCount: 400, speckleSize: 0.002f),
			// Reg: a close mosaic of wind-sorted stones two to five centimetres across, varnished brown-black and rusty
			// by the desert, set in pale silt; the varnish has a dull shine.
			R(Ground.Reg, 2.5f, 0.03f, "#8a7458", "#a88e6c", "#c4aa86", 8, SurfaceMotif.Cobbles, 44, 0.08f, 0.8f, "#2a2420", "#5e3e2a", 0.3f, 0.15f, 0.8f),
			// Dust: fine bright grains with low ripples a few tens of centimetres apart, a few darker and brighter specks.
			R(Ground.Dust, 6f, 0.015f, "#7a7670", "#9e9a92", "#c2beb4", 8, SurfaceMotif.Ripples, 14, 0.1f, 0.35f, "#8a867e", "#b4b0a6", 0.04f, 0.03f, 0.4f,
				speckles: Hexes("#6a6660", "#d8d4cc"), speckleCount: 900, speckleSize: 0.0015f),
			// Lineae: bright water ice, its fractures filled with bands of rust-brown salts (Europa's reddish material).
			R(Ground.Lineae, 8f, 0.05f, "#a8b8c4", "#c8d4dc", "#e4ecf0", 4, SurfaceMotif.Cracks, 6, 0.06f, 0.6f, "#6a3a22", "#9a5a34", 0.5f, 0.1f, 0.5f,
				speckles: Hexes("#8a4a2a"), speckleCount: 500, speckleSize: 0.003f),
			// Metal frost: a bright metallic coating crystallised in a net over dark basalt, brassy where pyrite shows.
			R(Ground.MetalFrost, 4f, 0.03f, "#4e4e52", "#86868a", "#b8b6b0", 6, SurfaceMotif.Ridges, 12, 0.03f, 0.6f, "#c8c4b8", "#e8e4d4", 0.55f, 0.15f, 0.6f,
				speckles: Hexes("#b8a060", "#8a8a90"), speckleCount: 1000, speckleSize: 0.002f, metallic: 0.6f),
			// Tholin dunes: dark orange-brown organic sand in long ripples, darker grains in the troughs.
			R(Ground.TholinDune, 4f, 0.03f, "#4a2410", "#7a3e1c", "#a05c2e", 6, SurfaceMotif.Ripples, 16, 0.1f, 0.6f, "#5a2a12", "#9a5a2c", 0.06f, 0.05f, 0.45f,
				speckles: Hexes("#2e160a"), speckleCount: 600, speckleSize: 0.002f),
			// ── Paths (2026-10-10) ── What a way wears down to (PathGroundFor). Low relief and fine grain: packed ground has
			// lost its loose crumb, and what shows is grit and small stones worked up through it, a little shine where pressed.

			// Trodden loam: dark brown packed earth, fine and close, grey grit and the odd pebble.
			R(Ground.PathLoam, 3f, 0.015f, "#2c2219", "#46382a", "#63513d", 6, SurfaceMotif.Granular, 36, 0.12f, 0.45f, "#221a12", "#7a6650", 0.2f, 0.1f, 0.75f,
				speckles: Hexes("#8a847a", "#5e5a54", "#a49a88"), speckleCount: 1600, speckleSize: 0.003f),
			// Dry earth: dusty fawn, paler where the dust lies thickest, pale and grey stones showing.
			R(Ground.PathDryEarth, 3f, 0.015f, "#6a5842", "#8c7656", "#ae9672", 6, SurfaceMotif.Granular, 40, 0.12f, 0.45f, "#5a4834", "#c2ad8a", 0.1f, 0.05f, 0.75f,
				speckles: Hexes("#d0c4ac", "#7a7064", "#9a8e7e"), speckleCount: 1800, speckleSize: 0.003f),
			// Red earth: packed laterite, rust and brick, pale stones in it.
			R(Ground.PathRedEarth, 3f, 0.015f, "#5e3020", "#82462c", "#a46244", 6, SurfaceMotif.Granular, 40, 0.12f, 0.45f, "#4a2416", "#b4744e", 0.12f, 0.05f, 0.75f,
				speckles: Hexes("#c8a080", "#5a3a2a"), speckleCount: 1200, speckleSize: 0.003f),
			// Packed sand: the ripples trodden out, a firmer, slightly darker crust with grit in it.
			R(Ground.PathSand, 4f, 0.01f, "#9c7c54", "#bc9c70", "#d6ba8c", 6, SurfaceMotif.Granular, 24, 0.1f, 0.35f, "#8a6c48", "#e0c69a", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#7a6a54", "#e8dcc0"), speckleCount: 1000, speckleSize: 0.0025f),
			// Grit: angular broken stone a centimetre or two across in a grey-brown fine.
			R(Ground.PathGrit, 2.5f, 0.02f, "#544e46", "#726a60", "#90887a", 8, SurfaceMotif.Cobbles, 64, 0.06f, 0.6f, "#4a4640", "#a8a092", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#3a3632", "#b4ac9c"), speckleCount: 1400, speckleSize: 0.003f),
			// Trampled snow: packed and glazed, greyer than fresh snow, dirtied with earth and grit.
			R(Ground.PathSnow, 5f, 0.01f, "#9aa2ae", "#bcc4d0", "#dde3ec", 4, SurfaceMotif.Granular, 20, 0.1f, 0.35f, "#8a929e", "#eef2f8", 0.55f, 0.15f, 0.5f,
				speckles: Hexes("#6a645c", "#8a8278"), speckleCount: 900, speckleSize: 0.003f),
			// Mud: dark, wet, shining where it is pressed, bits of green and peat in it.
			R(Ground.PathMud, 3f, 0.012f, "#1a130c", "#2a2016", "#3e3022", 6, SurfaceMotif.Granular, 30, 0.12f, 0.4f, "#120d08", "#4e3c2a", 0.55f, 0.35f, 0.7f,
				speckles: Hexes("#4a4034", "#2e3a1c"), speckleCount: 600, speckleSize: 0.003f),
			// Ash: packed charcoal grey, finer than the loose ash beside it, rusty cinders in it.
			R(Ground.PathAsh, 3f, 0.012f, "#1a1817", "#2a2826", "#3e3b38", 6, SurfaceMotif.Granular, 50, 0.1f, 0.45f, "#121110", "#4a4642", 0.08f, 0.05f, 0.7f,
				speckles: Hexes("#5a3a2a", "#6a6660"), speckleCount: 1000, speckleSize: 0.003f),
			// Salt: the crust broken and packed into a grey track, its polygons gone.
			R(Ground.PathSalt, 4f, 0.01f, "#9e9a92", "#bcb8b0", "#d6d2ca", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#8a867e", "#e6e2da", 0.3f, 0.1f, 0.6f,
				speckles: Hexes("#7a766e"), speckleCount: 800, speckleSize: 0.003f),
			// A forest path: dark packed earth, a few leaves and needles kicked onto it, roots' grit.
			R(Ground.PathLitter, 3f, 0.015f, "#2a1f15", "#433222", "#5e4733", 6, SurfaceMotif.Leaves, 8, 0.025f, 0.35f, "#5a3a1a", "#9a6a32", 0.15f, 0.15f, 0.8f,
				motifCount: 400, speckles: Hexes("#6a6258", "#3a2e22"), speckleCount: 800, speckleSize: 0.003f),

			// ── Jungle + Bamboo Forest (biome rounds, 2026-10-26) ── The drafts' palettes and grains; tiles as the families' own.
			R(Ground.JungleFloorBronze, 3f, 0.04f, "#24160c", "#4a3020", "#725034", 6, SurfaceMotif.Leaves, 8, 0.045f, 0.85f, "#8a5a2a", "#2a1a10", 0.2f, 0.2f, 0.85f,
				motifCount: 2200, speckles: Hexes("#c8b090", "#e0dccc", "#b0905a", "#7a3a1e"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.SoilRedForest, 5f, 0.04f, "#5a2210", "#8a3a1e", "#b4623a", 6, SurfaceMotif.Granular, 36, 0.1f, 0.6f, "#3e1a0c", "#1a1410", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#3e1a0c", "#1a1410"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.MossRavine, 3f, 0.04f, "#16301a", "#285022", "#4a7432", 6, SurfaceMotif.Clumps, 18, 0.5f, 0.75f, "#2a5a22", "#2a5a5a", 0.25f, 0.15f, 0.8f,
				speckles: Hexes("#1a1a18"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.MudRaffiaSwamp, 4f, 0.03f, "#1e160e", "#34281a", "#524430", 6, SurfaceMotif.Leaves, 8, 0.03f, 0.6f, "#1a120c", "#6a3a1a", 0.6f, 0.3f, 0.7f,
				motifCount: 900),
			R(Ground.LateriteIronstone, 8f, 0.2f, "#34160c", "#6a2e18", "#9a4a28", 4, SurfaceMotif.Fractured, 10, 0.08f, 0.7f, "#2a1010", "#b8702a", 0.12f, 0.05f, 0.8f,
				speckles: Hexes("#2a1010", "#b8702a"), speckleCount: 900, speckleSize: 0.004f),
			R(Ground.GraniteWhaleback, 12f, 0.3f, "#2e2e2c", "#626058", "#9a968a", 4, SurfaceMotif.Fractured, 14, 0.07f, 0.7f, "#141412", "#c8601e", 0.2f, 0.05f, 0.8f,
				speckles: Hexes("#c8601e", "#141412"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.CliffGneissGorge, 16f, 0.6f, "#22241e", "#4a4c42", "#7a7a6a", 4, SurfaceMotif.Strata, 18, 0.1f, 0.8f, "#2e5a22", "#8a3a1e", 0.2f, 0.05f, 0.85f,
				vertical: true, speckles: Hexes("#2e5a22", "#8a3a1e"), speckleCount: 600, speckleSize: 0.005f),
			R(Ground.SandQuartzStream, 2f, 0.03f, "#4a3420", "#7a5a38", "#a88a60", 8, SurfaceMotif.Cobbles, 20, 0.1f, 0.8f, "#e0d8c8", "#4a1e10", 0.35f, 0.15f, 0.85f,
				speckles: Hexes("#2a1a10"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.MudLeafSilt, 3f, 0.03f, "#1a140c", "#2e2416", "#463826", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#3a2414", "#5a4630", 0.5f, 0.3f, 0.7f),
			R(Ground.LateriteAvenue, 8f, 0.02f, "#6a2612", "#a44624", "#c8703e", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#4a2010", "#d08a5a", 0.3f, 0.2f, 0.7f,
				speckles: Hexes("#4a2010", "#3a1a0e"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.PathRootTrail, 3f, 0.03f, "#3e1c10", "#6a321c", "#8e4e2c", 6, SurfaceMotif.Fibres, 20, 0.12f, 0.6f, "#4a3a28", "#8a6a4a", 0.2f, 0.2f, 0.8f,
				speckles: Hexes("#2a4a1e"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.PotsherdPavement, 3f, 0.03f, "#5a2e1a", "#9a5a36", "#c48c5e", 6, SurfaceMotif.Cobbles, 24, 0.06f, 0.85f, "#e0dcd0", "#7a3a22", 0.4f, 0.15f, 0.85f),
			R(Ground.PathFrondMat, 2f, 0.03f, "#4a3a1e", "#7a6a38", "#a8985a", 6, SurfaceMotif.Fibres, 16, 0.2f, 0.7f, "#3a2a14", "#c0b070", 0.25f, 0.15f, 0.8f),
			R(Ground.SandMicaGold, 2f, 0.02f, "#8a6a40", "#b8965e", "#dcc490", 6, SurfaceMotif.Ripples, 8, 0.1f, 0.4f, "#7a5a34", "#e4d0a0", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#f0ead8", "#4a1e10"), speckleCount: 700, speckleSize: 0.0025f),
			R(Ground.GravelPisolite, 2f, 0.03f, "#3a1a0e", "#6e4a30", "#b0987a", 8, SurfaceMotif.Cobbles, 38, 0.1f, 0.85f, "#2a1008", "#d8d0c0", 0.35f, 0.15f, 0.85f),
			R(Ground.ForestFloorBambooGrove, 3f, 0.04f, "#4a3e26", "#7a6a42", "#a89868", 6, SurfaceMotif.Leaves, 8, 0.02f, 0.85f, "#c4b070", "#6e6a38", 0.2f, 0.2f, 0.85f,
				motifCount: 3000, speckles: Hexes("#8a5a34", "#9a9a6a"), speckleCount: 230, speckleSize: 0.003f),
			R(Ground.MossSugigoke, 3f, 0.03f, "#22401a", "#46682a", "#7a9a44", 6, SurfaceMotif.Clumps, 24, 0.4f, 0.8f, "#3e6a22", "#8aa83a", 0.25f, 0.15f, 0.8f,
				speckles: Hexes("#b8301a"), speckleCount: 80, speckleSize: 0.004f),
			R(Ground.NeedleLitterSugi, 5f, 0.03f, "#3a2618", "#6a4628", "#9a7048", 6, SurfaceMotif.Needles, 8, 0.022f, 0.8f, "#a86a3a", "#6a4a2a", 0.15f, 0.1f, 0.8f,
				motifCount: 6000, speckles: Hexes("#5a3a22"), speckleCount: 60, speckleSize: 0.004f),
			R(Ground.SoilKuroboku, 5f, 0.04f, "#141210", "#2a2620", "#4a4236", 6, SurfaceMotif.Granular, 40, 0.1f, 0.6f, "#0e0c0a", "#5a4a30", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#c8b070", "#5a4a30"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.RockAndesiteTalus, 8f, 0.25f, "#3e3c3a", "#5e5c58", "#8a8680", 4, SurfaceMotif.Fractured, 10, 0.08f, 0.7f, "#4a6a28", "#9aa890", 0.15f, 0.05f, 0.8f,
				speckles: Hexes("#4a6a28", "#9aa890"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.ForestFloorLaurel, 4f, 0.04f, "#1e2214", "#3a3e26", "#646a44", 6, SurfaceMotif.Leaves, 8, 0.03f, 0.8f, "#2e3420", "#d8cc98", 0.25f, 0.15f, 0.85f,
				motifCount: 1800, speckles: Hexes("#b81a2a"), speckleCount: 60, speckleSize: 0.003f),
			R(Ground.CliffAndesitePlated, 16f, 0.6f, "#3a3a3c", "#5a5a58", "#807e78", 4, SurfaceMotif.Strata, 24, 0.08f, 0.8f, "#1e1e1c", "#3e5a22", 0.2f, 0.05f, 0.85f,
				vertical: true, speckles: Hexes("#3e5a22"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.GravelAndesiteBed, 2f, 0.03f, "#3a3a36", "#6a6862", "#a8a49a", 8, SurfaceMotif.Cobbles, 30, 0.12f, 0.85f, "#d8d2c4", "#5a6a58", 0.35f, 0.15f, 0.85f),
			R(Ground.SiltKurobokuPond, 4f, 0.03f, "#1e1c18", "#34302a", "#4a443a", 6, SurfaceMotif.Leaves, 8, 0.02f, 0.5f, "#6a5a30", "#8a7a40", 0.4f, 0.2f, 0.7f,
				motifCount: 200),
			R(Ground.RoadMasa, 6f, 0.02f, "#8a7e68", "#b8ac92", "#dcd2bc", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#a89c84", "#e8e0cc", 0.3f, 0.2f, 0.7f,
				speckles: Hexes("#f0ece0", "#c8a090"), speckleCount: 550, speckleSize: 0.0025f),
			R(Ground.PathBambooLeaf, 3f, 0.02f, "#5a4a30", "#8a7650", "#b0a070", 6, SurfaceMotif.Leaves, 8, 0.02f, 0.6f, "#c4b070", "#4a3a24", 0.2f, 0.15f, 0.8f,
				motifCount: 600),
			R(Ground.FlagstoneIshidatami, 3f, 0.04f, "#4a4a46", "#6a6a64", "#8e8c84", 6, SurfaceMotif.Cobbles, 8, 0.05f, 0.8f, "#3e5a24", "#9a9890", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#3e5a24"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.PathTobiishi, 3f, 0.03f, "#22401a", "#46682a", "#7a9a44", 6, SurfaceMotif.Cobbles, 6, 0.1f, 0.7f, "#5a5a56", "#8a8a84", 0.25f, 0.15f, 0.8f),
			R(Ground.PathIshidan, 3f, 0.04f, "#3e3e3a", "#5e5c56", "#807e76", 6, SurfaceMotif.Cobbles, 6, 0.08f, 0.8f, "#3e5a22", "#8a8880", 0.25f, 0.1f, 0.8f),
			R(Ground.SandMasaGrus, 2f, 0.02f, "#9a8e74", "#c4b898", "#e4dcc4", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.4f, "#8a7e64", "#ece4d0", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#f0ece0", "#c8a090", "#2a2826"), speckleCount: 650, speckleSize: 0.0025f),
			R(Ground.GravelArc, 2f, 0.03f, "#3a3836", "#6e6a64", "#a8a296", 8, SurfaceMotif.Cobbles, 34, 0.12f, 0.85f, "#5e5c58", "#d8d2c4", 0.35f, 0.15f, 0.85f),
			// ── Forest (biome round, 2026-10-26) ── The draft's palettes (dark/mid/light, motif dark/light) and grains; tiles as the
			// families' own (a motif's size is a share of its tile, so a leaf litter stays a 3 m tile).
			R(Ground.ForestFloorBeech, 3f, 0.04f, "#2a1810", "#52321a", "#86552c", 6, SurfaceMotif.Leaves, 8, 0.032f, 0.85f, "#7a4220", "#c08a48", 0.2f, 0.2f, 0.85f,
				motifCount: 2600, speckles: Hexes("#4a3020", "#8a6a30", "#4e6a24"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.MossCushion, 3f, 0.04f, "#17280e", "#34521a", "#5e8228", 6, SurfaceMotif.Clumps, 22, 0.55f, 0.75f, "#23400f", "#86a83a", 0.25f, 0.15f, 0.8f,
				speckles: Hexes("#6a4020"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.ForestFloorRamsons, 3f, 0.04f, "#1e3a12", "#3a6620", "#5e8e30", 6, SurfaceMotif.Leaves, 8, 0.05f, 0.85f, "#2e5418", "#7aa840", 0.3f, 0.2f, 0.8f,
				motifCount: 1800, speckles: Hexes("#f4f4ee", "#3a2818"), speckleCount: 1200, speckleSize: 0.003f),
			R(Ground.NeedleLitterFir, 3f, 0.03f, "#201812", "#3c2e22", "#5c4834", 6, SurfaceMotif.Needles, 8, 0.022f, 0.8f, "#3a2a1a", "#7a6248", 0.15f, 0.1f, 0.8f,
				motifCount: 7000, speckles: Hexes("#6a4a2e", "#3e5a20"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.SoilRootHumus, 4f, 0.04f, "#1a120c", "#30221a", "#4e3a2a", 8, SurfaceMotif.Fibres, 40, 0.1f, 0.6f, "#5a4030", "#8a6a4a", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#a89a7a"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.SandstoneForest, 8f, 0.25f, "#5e5a4c", "#8a8470", "#b2aa90", 4, SurfaceMotif.Strata, 10, 0.08f, 0.6f, "#5a5646", "#bcb498", 0.12f, 0.05f, 0.8f,
				speckles: Hexes("#3e5a22", "#2a4a1a", "#8a5a30"), speckleCount: 900, speckleSize: 0.004f),
			R(Ground.SandstoneForestFace, 10f, 0.6f, "#4a4638", "#77705c", "#a49a80", 4, SurfaceMotif.Strata, 18, 0.1f, 0.8f, "#2e3a1e", "#4a6226", 0.12f, 0.05f, 0.85f,
				vertical: true, speckles: Hexes("#2e3a1e", "#4a6226"), speckleCount: 700, speckleSize: 0.005f),
			R(Ground.MudLeafMould, 4f, 0.03f, "#120c08", "#22180e", "#3a2a1a", 6, SurfaceMotif.Leaves, 8, 0.03f, 0.6f, "#3a2412", "#5a3a1e", 0.5f, 0.35f, 0.7f,
				motifCount: 900),
			R(Ground.GravelBrook, 2f, 0.03f, "#2e241a", "#5a4a38", "#867058", 8, SurfaceMotif.Cobbles, 36, 0.12f, 0.85f, "#6a6a62", "#9a8a6a", 0.35f, 0.15f, 0.85f,
				speckles: Hexes("#6a3a1a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.PathBeechLitter, 3f, 0.015f, "#2a1c12", "#44301e", "#64482e", 6, SurfaceMotif.Leaves, 8, 0.02f, 0.4f, "#5a3418", "#9a6a38", 0.15f, 0.15f, 0.8f,
				motifCount: 900, speckles: Hexes("#6a5a48", "#3a2a1c"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.PathHolloway, 4f, 0.02f, "#22180f", "#3c2c1e", "#5a4430", 6, SurfaceMotif.Granular, 36, 0.12f, 0.5f, "#1e150e", "#6a5440", 0.3f, 0.25f, 0.75f,
				speckles: Hexes("#7a4a22", "#4a3424", "#8a8478"), speckleCount: 1400, speckleSize: 0.003f),
			R(Ground.FlagstoneSandstone, 4f, 0.04f, "#6a6250", "#8e8670", "#b0a88e", 6, SurfaceMotif.Cobbles, 8, 0.05f, 0.8f, "#3e5420", "#b8b096", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#3e5420", "#7a4a22"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.SandHumic, 4f, 0.02f, "#6a5a44", "#8e7c60", "#b09c7c", 6, SurfaceMotif.Ripples, 12, 0.1f, 0.4f, "#5e5040", "#bca888", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#d8d0c0", "#4a3018"), speckleCount: 700, speckleSize: 0.0025f),
			R(Ground.GravelSandstoneFlint, 2f, 0.03f, "#5a4e3e", "#7e705a", "#a2947a", 8, SurfaceMotif.Cobbles, 48, 0.12f, 0.85f, "#2a2826", "#b0a284", 0.2f, 0.1f, 0.85f),
			// ── Woodland (biome round, 2026-10-26) ── The draft's palettes and grains; tiles as the families' own.
			R(Ground.GrassWoodPasture, 3f, 0.03f, "#2e3c1a", "#4e6a2a", "#7e9a44", 4, SurfaceMotif.Blades, 8, 0.02f, 0.8f, "#3e5a22", "#9ab454", 0.15f, motifCount: 38000,
				speckles: Hexes("#8a6a30", "#6a5a34", "#e0dccf", "#4a3a28"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.ForestFloorCopse, 3f, 0.04f, "#3a2814", "#6a4a26", "#9a7440", 6, SurfaceMotif.Leaves, 8, 0.035f, 0.85f, "#8a6a30", "#c0a048", 0.2f, 0.2f, 0.85f,
				motifCount: 2400, speckles: Hexes("#a89040", "#9a7030", "#7a5a30", "#5a7a2a"), speckleCount: 1100, speckleSize: 0.003f),
			R(Ground.ForestFloorBluebell, 3f, 0.04f, "#1e3418", "#3a5e26", "#5e8a34", 6, SurfaceMotif.Leaves, 8, 0.04f, 0.85f, "#2e5222", "#6a9a3a", 0.25f, 0.2f, 0.8f,
				motifCount: 1600, speckles: Hexes("#4a4ab8", "#6a5ad0", "#f0f0f4", "#8a6a30"), speckleCount: 2700, speckleSize: 0.003f),
			R(Ground.GrassDryBracken, 3f, 0.04f, "#3a1e10", "#6e3a1a", "#a8622a", 4, SurfaceMotif.Blades, 8, 0.03f, 0.8f, "#5a2e14", "#c07a34", 0.1f, motifCount: 22000,
				speckles: Hexes("#5a8a2a", "#c0a880", "#c8a838"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.GrassMeadowGlade, 3f, 0.04f, "#2c3a1a", "#50702e", "#86a64a", 4, SurfaceMotif.Blades, 8, 0.03f, 0.8f, "#43622a", "#a0bc58", 0.15f, motifCount: 30000,
				speckles: Hexes("#c84a78", "#4a5ac0", "#f0c020", "#f6f4ee"), speckleCount: 1600, speckleSize: 0.003f),
			R(Ground.SoilRendzina, 4f, 0.03f, "#4a3e2c", "#7a6a50", "#a8987a", 8, SurfaceMotif.Granular, 64, 0.1f, 0.5f, "#3a3022", "#c8c0ae", 0.12f, 0.1f, 0.75f,
				speckles: Hexes("#ece8de", "#2a2a2c", "#d8d4c8", "#8a6a30"), speckleCount: 1900, speckleSize: 0.004f),
			R(Ground.ChalkWoodland, 8f, 0.2f, "#a8a496", "#d0ccbe", "#eeebe2", 4, SurfaceMotif.Strata, 6, 0.08f, 0.55f, "#2a2a2e", "#f2f0e8", 0.15f, 0.05f, 0.75f,
				speckles: Hexes("#6a7a3a", "#d8d4cc", "#b08a5a"), speckleCount: 800, speckleSize: 0.004f),
			R(Ground.ChalkFace, 10f, 0.5f, "#9c988a", "#cac6b8", "#f0ede4", 4, SurfaceMotif.Strata, 10, 0.1f, 0.75f, "#2a2a2c", "#e0dccf", 0.15f, 0.05f, 0.8f,
				vertical: true, speckles: Hexes("#2a2a2c", "#8a8a7a"), speckleCount: 900, speckleSize: 0.005f),
			R(Ground.GravelChalkStream, 2f, 0.03f, "#6e6858", "#a49c84", "#cec6ae", 8, SurfaceMotif.Cobbles, 34, 0.12f, 0.85f, "#2e2c2a", "#d8d2c0", 0.3f, 0.15f, 0.85f,
				speckles: Hexes("#4e7a2e", "#e0dccf"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.MudMarl, 4f, 0.03f, "#5a564a", "#827e70", "#a8a494", 6, SurfaceMotif.Granular, 32, 0.1f, 0.4f, "#4a463c", "#b8b4a4", 0.4f, 0.3f, 0.7f,
				speckles: Hexes("#e8e4da", "#d8d4c8", "#6a5a34"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.PathChalkTrack, 4f, 0.02f, "#a49e8e", "#cfcabc", "#ebe7dc", 6, SurfaceMotif.Granular, 40, 0.12f, 0.45f, "#8a8678", "#f2efe6", 0.12f, 0.05f, 0.7f,
				speckles: Hexes("#2a2a2c", "#d8d4c8", "#6a8a3a"), speckleCount: 1300, speckleSize: 0.003f),
			R(Ground.PathGreenLane, 3f, 0.015f, "#3a3020", "#5a4a32", "#7e6a4c", 6, SurfaceMotif.Granular, 30, 0.12f, 0.45f, "#2e261a", "#5a7a34", 0.2f, 0.15f, 0.75f,
				speckles: Hexes("#5a7a34", "#2a2a2c", "#8a6a30"), speckleCount: 1200, speckleSize: 0.003f),
			R(Ground.FlagstoneFlint, 3f, 0.04f, "#2a2a2c", "#6a6866", "#d8d4c8", 6, SurfaceMotif.Cobbles, 12, 0.04f, 0.85f, "#1e1e20", "#e0dacb", 0.45f, 0.2f, 0.8f,
				speckles: Hexes("#d8d2c0", "#5a6a2a"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.SandChalky, 4f, 0.02f, "#a8a088", "#c8c0a8", "#e0d8c4", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.35f, "#9a927c", "#ece4d0", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#f0ece4", "#3a3a3a"), speckleCount: 700, speckleSize: 0.0025f),
			R(Ground.GravelFlint, 2f, 0.03f, "#6a5a40", "#8a7a5a", "#b8a888", 8, SurfaceMotif.Cobbles, 48, 0.12f, 0.85f, "#3a3430", "#e8e4da", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#8a6a3a"), speckleCount: 400, speckleSize: 0.004f),

			// ── Coastal Water (biome round, 2026-10-26) ── The draft's palettes and grains. One motif a recipe: where the draft gives
			// two (the kelp reef's joints and its coralline crust) the one that reads under water is drawn, the other is a speckle.
			// LATER (I9 T7): a Honeycomb motif for the Sabellaria reef (fine Cracks cells stand in) and a joint-plus-crust pair.
			R(Ground.SandShellGravel, 4f, 0.03f, "#5e5c50", "#8a8574", "#b4ad98", 6, SurfaceMotif.Ripples, 5, 0.1f, 0.6f, "#4e4c42", "#c4bca6", 0.15f, 0.05f, 0.5f,
				speckles: Hexes("#e6e0d0", "#c48a9a", "#3a3e44"), speckleCount: 950, speckleSize: 0.003f),
			R(Ground.DoleriteKelpReef, 6f, 0.3f, "#1c2220", "#36403c", "#5a6660", 4, SurfaceMotif.Clumps, 30, 0.35f, 0.6f, "#c88aa0", "#e2b4c2", 0.35f, 0.15f, 0.8f,
				speckles: Hexes("#6e2430", "#4a3a22", "#141a18"), speckleCount: 900, speckleSize: 0.005f),
			R(Ground.Maerl, 3f, 0.06f, "#7a4a5a", "#b0808e", "#dcbcc6", 8, SurfaceMotif.Cobbles, 60, 0.12f, 0.85f, "#8a5a6a", "#e8d0d6", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#ece6e0"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.CobbleTideSwept, 4f, 0.06f, "#343430", "#5a5852", "#86827a", 8, SurfaceMotif.Cobbles, 18, 0.25f, 0.9f, "#2a2a26", "#9a968e", 0.3f, 0.2f, 0.85f,
				speckles: Hexes("#d8b030", "#d86a20", "#c8c0b0"), speckleCount: 700, speckleSize: 0.005f),
			R(Ground.SiltBrittlestar, 5f, 0.03f, "#3c3428", "#584e3e", "#766a56", 6, SurfaceMotif.Granular, 60, 0.1f, 0.4f, "#2a241c", "#8a7a64", 0.3f, 0.2f,
				speckles: Hexes("#8a6a6a", "#c8bca8"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.SandHoneycomb, 3f, 0.05f, "#5e4e3a", "#8a765a", "#b49e7e", 6, SurfaceMotif.Cracks, 60, 0.03f, 0.7f, "#4a3c2c", "#a89878", 0.12f, 0.05f, 0.75f),
			R(Ground.PeatLyonesse, 4f, 0.04f, "#160f0a", "#2a2018", "#463626", 6, SurfaceMotif.Fibres, 20, 0.1f, 0.6f, "#3a2a1a", "#5a4430", 0.45f, 0.3f,
				speckles: Hexes("#0a0806", "#6a4a2a"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.CoralBarren, 5f, 0.08f, "#5e3e48", "#8e6070", "#be90a0", 6, SurfaceMotif.Clumps, 16, 0.35f, 0.6f, "#d8a8b8", "#e8c0cc", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#3a2a34"), speckleCount: 200, speckleSize: 0.006f),
			R(Ground.DoleriteSeaCliff, 12f, 0.6f, "#141a18", "#2a3430", "#4a5650", 4, SurfaceMotif.Strata, 14, 0.1f, 0.7f, "#101412", "#5a6660", 0.3f, 0.1f, 0.85f,
				vertical: true, speckles: Hexes("#e070a0", "#60c080", "#e08040", "#9060c0", "#f0ece8"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.SandSurfSorted, 3f, 0.02f, "#6e6a5a", "#9a947e", "#c4bca4", 6, SurfaceMotif.Ripples, 9, 0.1f, 0.6f, "#5e5a4c", "#cec6ae", 0.15f, 0.05f, 0.4f,
				speckles: Hexes("#d8d4c8"), speckleCount: 500, speckleSize: 0.002f),
			R(Ground.GravelSlateQuartz, 2f, 0.03f, "#2e3236", "#54585c", "#8a8a86", 8, SurfaceMotif.Cobbles, 34, 0.12f, 0.85f, "#3a3e44", "#e4e0d8", 0.35f, 0.15f, 0.85f),
			R(Ground.FlagstoneSarn, 5f, 0.08f, "#262a26", "#44483e", "#6a6c5a", 6, SurfaceMotif.Cobbles, 6, 0.2f, 0.85f, "#3a4a22", "#c8c4b8", 0.45f, 0.2f, 0.85f),
			R(Ground.RockSkerry, 3f, 0.1f, "#4e4440", "#7a6c64", "#a8988c", 4, SurfaceMotif.Clumps, 20, 0.3f, 0.6f, "#d0ccc0", "#e0dcd0", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#1a1a18"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.FlagstonePave, 3f, 0.04f, "#5a4a44", "#8a746a", "#b49e92", 6, SurfaceMotif.Cobbles, 10, 0.05f, 0.85f, "#3a4a2a", "#c4b0a4", 0.2f, 0.1f, 0.8f),
			R(Ground.SandMaerlBar, 2f, 0.02f, "#8a7a72", "#b4a29a", "#dccac2", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#7a6a64", "#e4d4cc", 0.12f, 0.05f,
				speckles: Hexes("#c48a9a", "#ece6dc"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.GravelKillas, 2f, 0.03f, "#2a2e32", "#50565c", "#80868a", 8, SurfaceMotif.Cobbles, 40, 0.12f, 0.85f, "#3a3e44", "#e4e0d8", 0.2f, 0.1f, 0.85f),

			// ── Ocean (biome round, 2026-10-26) ── The draft's palettes and grains. LATER (I9 T7): a Craters motif for the Nephrops
			// burrow mouths and Ridges-plus-Cobbles for the plough-mark furrows (Granular and Cobbles stand in).
			R(Ground.MudNephrops, 4f, 0.03f, "#3a342c", "#5e5446", "#847864", 6, SurfaceMotif.Granular, 46, 0.1f, 0.45f, "#24201a", "#6e6252", 0.4f, 0.3f,
				speckles: Hexes("#c8c0ac", "#24201a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SandShelfEdge, 3f, 0.02f, "#6a6252", "#948a74", "#c0b498", 6, SurfaceMotif.Ripples, 4, 0.1f, 0.6f, "#5a5244", "#ccc0a4", 0.12f, 0.05f, 0.4f,
				speckles: Hexes("#e0d8c4", "#2a3022"), speckleCount: 550, speckleSize: 0.0025f),
			R(Ground.CoralRubble, 3f, 0.1f, "#5e5a52", "#8e887c", "#d8d4ca", 6, SurfaceMotif.Clumps, 22, 0.3f, 0.75f, "#6e6a62", "#e8e4dc", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#f0ece4", "#e89070", "#c86a30"), speckleCount: 260, speckleSize: 0.004f),
			R(Ground.MudPockmark, 4f, 0.03f, "#1e1e1c", "#34322e", "#4e4a44", 6, SurfaceMotif.Granular, 50, 0.1f, 0.45f, "#161614", "#5a564e", 0.45f, 0.35f,
				speckles: Hexes("#a8a090"), speckleCount: 150, speckleSize: 0.004f),
			R(Ground.SpiculeMat, 4f, 0.03f, "#4a4a52", "#6e6e78", "#9a9aa4", 6, SurfaceMotif.Fibres, 30, 0.08f, 0.6f, "#3e3e46", "#b0b0b8", 0.3f, 0.2f,
				speckles: Hexes("#c8c4b8"), speckleCount: 300, speckleSize: 0.005f),
			R(Ground.GravelDiamicton, 3f, 0.05f, "#3a3630", "#5a544a", "#807868", 8, SurfaceMotif.Cobbles, 22, 0.18f, 0.85f, "#2e2e2e", "#d8d0c0", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#a89088", "#2e2e2e", "#d8d0c0"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.SandstoneIgueldo, 8f, 0.25f, "#5e5240", "#8a7a5e", "#b8a888", 4, SurfaceMotif.Strata, 6, 0.1f, 0.75f, "#4a4032", "#c4b494", 0.12f, 0.05f, 0.8f,
				speckles: Hexes("#1a1816"), speckleCount: 400, speckleSize: 0.006f),
			R(Ground.MudstoneScarp, 14f, 0.6f, "#3a342e", "#5a5248", "#7e746a", 4, SurfaceMotif.Fractured, 10, 0.06f, 0.7f, "#2a2620", "#8a5a2a", 0.15f, 0.05f, 0.85f,
				vertical: true, speckles: Hexes("#2a2620", "#8a5a2a", "#1a1816"), speckleCount: 700, speckleSize: 0.005f),
			R(Ground.GravelGullyChip, 2f, 0.03f, "#2e2a26", "#4e4840", "#78705e", 8, SurfaceMotif.Cobbles, 34, 0.12f, 0.85f, "#3a342e", "#d8d4ca", 0.2f, 0.1f, 0.85f),
			R(Ground.MudShelfRim, 4f, 0.03f, "#2e2a24", "#4a443a", "#6a6252", 6, SurfaceMotif.Granular, 44, 0.1f, 0.4f, "#221e1a", "#7a7262", 0.4f, 0.3f),
			R(Ground.FlagstoneGaltzada, 4f, 0.05f, "#6a5c44", "#94846a", "#c0b094", 6, SurfaceMotif.Cobbles, 14, 0.08f, 0.85f, "#5e5446", "#ccbc9e", 0.2f, 0.15f, 0.8f),
			R(Ground.PebblesCoralGrit, 3f, 0.02f, "#8a8478", "#b4aea2", "#dad6cc", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#7a746a", "#f0ece4", 0.15f, 0.1f),
			R(Ground.PebblesArriBiribil, 3f, 0.05f, "#2e3a44", "#4e5e6a", "#8a98a2", 8, SurfaceMotif.Cobbles, 18, 0.15f, 0.9f, "#94846a", "#9aa8b2", 0.3f, 0.15f, 0.85f),
			R(Ground.SandCoralGrit, 2f, 0.02f, "#8a867c", "#b0aca0", "#d4d0c4", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#7a766c", "#e0dcd0", 0.12f, 0.05f,
				speckles: Hexes("#e8a080"), speckleCount: 150, speckleSize: 0.003f),
			R(Ground.GravelSandstoneChip, 2f, 0.03f, "#5a4e3c", "#84765c", "#ac9e84", 8, SurfaceMotif.Cobbles, 40, 0.12f, 0.85f, "#3a342e", "#b8aa90", 0.2f, 0.1f, 0.85f),
			// ── Tundra (biome round, 2026-10-26) ── The draft's palettes and grains; tiles as the families' own, the patterned grounds at their cells'
			// size (polygons 8 m, circles 2.4 m).
			R(Ground.LichenTundraMat, 3f, 0.03f, "#3a3622", "#66603e", "#928a62", 6, SurfaceMotif.Clumps, 40, 0.3f, 0.65f, "#8a4a24", "#d8d09a", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#b02a1c", "#f4f2ea", "#5a6a2e", "#2a2420"), speckleCount: 2600, speckleSize: 0.003f),
			R(Ground.MossPolygonMire, 24f, 0.12f, "#3e4a24", "#62703a", "#8a9450", 4, SurfaceMotif.Polygons, 3, 0.06f, 0.8f, "#141410", "#a0a078", 0.3f, 0.25f, 0.75f,
				speckles: Hexes("#7a7a44"), speckleCount: 1500, speckleSize: 0.004f),
			R(Ground.GravelSortedCircles, 12f, 0.06f, "#6a665c", "#8a857a", "#a8a296", 6, SurfaceMotif.SortedCircles, 5, 0.15f, 0.85f, "#4a4640", "#b0aa9c", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#5a6a3a", "#d8d09a"), speckleCount: 800, speckleSize: 0.004f),
			R(Ground.GrassTussockTundra, 3f, 0.04f, "#3a3a20", "#6e6a3c", "#a49a5c", 4, SurfaceMotif.Blades, 8, 0.022f, 0.8f, "#6a7a3a", "#b8a868", 0.3f, 0.15f, 0.8f, motifCount: 28000,
				speckles: Hexes("#f6f6f0", "#2a3030"), speckleCount: 1900, speckleSize: 0.003f),
			R(Ground.RockFellField, 6f, 0.3f, "#4a4446", "#7a7070", "#a69a96", 4, SurfaceMotif.Fractured, 10, 0.06f, 0.75f, "#5a4a4a", "#c8b8b0", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#1e1a18", "#d8782a", "#b8c060"), speckleCount: 900, speckleSize: 0.004f),
			R(Ground.SnowWatermelon, 6f, 0.04f, "#b8b4b8", "#d8d2d6", "#f0ecf0", 4, SurfaceMotif.Clumps, 16, 0.35f, 0.45f, "#c8808c", "#e0a8b0", 0.45f, 0.1f, 0.4f,
				speckles: Hexes("#6a6058"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.RockGneissOutcrop, 6f, 0.3f, "#3e383a", "#6a6062", "#948888", 4, SurfaceMotif.Strata, 8, 0.1f, 0.6f, "#2a2420", "#c8c8b0", 0.2f, 0.1f, 0.8f, warp: 0.15f,
				speckles: Hexes("#2a2420", "#c8c8b0"), speckleCount: 600, speckleSize: 0.004f),
			R(Ground.GravelHeaveScree, 2f, 0.05f, "#5a5452", "#807876", "#a49c98", 8, SurfaceMotif.Cobbles, 50, 0.12f, 0.85f, "#4a4442", "#b8b48c", 0.12f, 0.1f, 0.85f,
				speckles: Hexes("#b8b48c"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.CliffGneissBanded, 10f, 0.6f, "#3a3436", "#6a6064", "#9a8e8e", 4, SurfaceMotif.Strata, 18, 0.1f, 0.8f, "#2e2a2a", "#b89890", 0.15f, 0.05f, 0.85f, warp: 0.15f, vertical: true,
				speckles: Hexes("#e08030", "#1e1a1a"), speckleCount: 700, speckleSize: 0.005f),
			R(Ground.GravelOutwashBraided, 2f, 0.03f, "#4a4c4e", "#74787a", "#9ea2a2", 8, SurfaceMotif.Cobbles, 48, 0.12f, 0.85f, "#2a2a2c", "#8a6e68", 0.3f, 0.15f, 0.85f,
				speckles: Hexes("#b8bab4"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.SiltThermokarst, 4f, 0.03f, "#2e302c", "#4a4c46", "#686a62", 4, SurfaceMotif.Clumps, 8, 0.3f, 0.4f, "#3a3a22", "#5a5a3a", 0.5f, 0.3f, 0.7f),
			R(Ground.PathDroveRoad, 4f, 0.02f, "#4e4a44", "#706a60", "#948c80", 8, SurfaceMotif.Ridges, 12, 0.03f, 0.6f, "#2e2a26", "#b8b48c", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#b8b48c", "#2e2a26"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.PathTundraTrod, 3f, 0.015f, "#3e3828", "#5e5640", "#807658", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#2a2018", "#c8c09a", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#c8c09a", "#2a2018"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.FlagstoneGneissSplit, 3f, 0.04f, "#5e5658", "#847a7a", "#aaa09c", 6, SurfaceMotif.Cobbles, 8, 0.05f, 0.8f, "#3e4a24", "#b4aaa6", 0.2f, 0.1f, 0.8f),
			R(Ground.PathSlabCauseway, 4f, 0.04f, "#4a4446", "#6a6062", "#8a807e", 6, SurfaceMotif.Cobbles, 6, 0.06f, 0.8f, "#1e2420", "#9a908e", 0.3f, 0.2f, 0.8f),
			R(Ground.SandGlacialFlour, 4f, 0.02f, "#5a5a58", "#7e7e7a", "#a2a29c", 6, SurfaceMotif.Ripples, 14, 0.1f, 0.5f, "#4e4e4c", "#b0b0aa", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#e8e8e0"), speckleCount: 600, speckleSize: 0.0025f),
			R(Ground.GravelOutwashBark, 2f, 0.03f, "#545658", "#7a7e80", "#a2a6a6", 8, SurfaceMotif.Cobbles, 44, 0.12f, 0.85f, "#2a2a2c", "#8e7270", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#6a5a48"), speckleCount: 200, speckleSize: 0.004f),
			// ── Ice Sheet (biome round, 2026-10-26) ── The draft's palettes and grains; the Thin band's own (layered deposits, Swiss-cheese slab, spiral
			// scarps, red-dust rock).
			R(Ground.SnowKatabatic, 6f, 0.04f, "#a8b4c4", "#d4dde8", "#f4f7fb", 4, SurfaceMotif.Ridges, 6, 0.04f, 0.55f, "#8e9cb0", "#b8c4d4", 0.5f, 0.1f, 0.45f,
				speckles: Hexes("#ffffff"), speckleCount: 700, speckleSize: 0.002f),
			R(Ground.IceLayeredDeposit, 8f, 0.06f, "#8a7466", "#b8a090", "#e2d6cc", 4, SurfaceMotif.Strata, 18, 0.1f, 0.7f, "#6a5448", "#ece4dc", 0.5f, 0.1f, 0.5f),
			R(Ground.IceWindGlaze, 8f, 0.02f, "#9aaabc", "#cad6e2", "#eef4fa", 4, SurfaceMotif.Cracks, 3, 0.02f, 0.3f, "#dde6ee", "#ffffff", 0.75f, 0.1f, 0.5f,
				speckles: Hexes("#f8fbfd"), speckleCount: 300, speckleSize: 0.002f),
			R(Ground.FrostSwissCheese, 8f, 0.05f, "#a8a49e", "#d6d2cc", "#f6f2ee", 4, SurfaceMotif.Clumps, 12, 0.4f, 0.6f, "#8a7c70", "#f6f2ee", 0.3f, 0.1f, 0.5f),
			R(Ground.IceBlueField, 8f, 0.03f, "#2e5a86", "#5a8cba", "#a6cae4", 3, SurfaceMotif.Ripples, 14, 0.08f, 0.4f, "#7aa6cc", "#dce8f2", 0.6f, 0.1f, 0.5f,
				speckles: Hexes("#1a1816", "#c8dcec"), speckleCount: 120, speckleSize: 0.004f),
			R(Ground.RockCharnockiteFell, 6f, 0.3f, "#3a362c", "#6a6250", "#9a907a", 4, SurfaceMotif.Cobbles, 14, 0.12f, 0.75f, "#4e5a42", "#8a6a3e", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#8aa0b8", "#c8c8c0", "#1a1a1a", "#c8d020", "#ece8dc", "#1e140c"), speckleCount: 900, speckleSize: 0.004f),
			R(Ground.RockCharnockiteDust, 6f, 0.3f, "#4a3a30", "#7a6250", "#a88e78", 4, SurfaceMotif.Cobbles, 14, 0.12f, 0.75f, "#3a2e26", "#9a6a4a", 0.12f, 0.1f, 0.8f),
			R(Ground.FrostSurfaceHoar, 4f, 0.02f, "#bcc6d2", "#e0e7ef", "#fbfdff", 4, SurfaceMotif.Clumps, 22, 0.25f, 0.5f, "#aab8c8", "#ffffff", 0.55f, 0.1f, 0.4f,
				speckles: Hexes("#ffffff"), speckleCount: 1400, speckleSize: 0.002f),
			R(Ground.GravelBlueIceMoraine, 4f, 0.05f, "#3e3a34", "#686254", "#948a78", 8, SurfaceMotif.Cobbles, 22, 0.14f, 0.85f, "#6a96bc", "#9a8e7a", 0.3f, 0.2f, 0.85f),
			R(Ground.IceSteppedEscarpment, 8f, 0.08f, "#4e6e8e", "#8eaec8", "#d8e6f2", 4, SurfaceMotif.Strata, 14, 0.1f, 0.6f, "#8a8478", "#e2ecf4", 0.55f, 0.1f, 0.5f),
			R(Ground.IceSpiralScarp, 8f, 0.08f, "#6a5448", "#9a8070", "#cab4a4", 4, SurfaceMotif.Strata, 24, 0.1f, 0.7f, "#4a3a30", "#e0d4c8", 0.45f, 0.1f, 0.5f),
			R(Ground.IceTidewater, 10f, 0.3f, "#1c4a72", "#3e7aaa", "#c4dcee", 3, SurfaceMotif.Fractured, 5, 0.04f, 0.6f, "#e8eef2", "#ffffff", 0.7f, 0.1f, 0.5f, vertical: true),
			R(Ground.CliffCharnockite, 10f, 0.6f, "#2e2a22", "#5a5240", "#8e8268", 4, SurfaceMotif.Fractured, 4, 0.08f, 0.75f, "#7a5a30", "#a89878", 0.15f, 0.05f, 0.85f, vertical: true,
				speckles: Hexes("#e8e4da", "#eef3f8"), speckleCount: 600, speckleSize: 0.006f),
			R(Ground.CliffRedDust, 10f, 0.6f, "#3e3028", "#6a5444", "#94806c", 4, SurfaceMotif.Fractured, 4, 0.08f, 0.75f, "#9a6a4a", "#8a7060", 0.12f, 0.05f, 0.85f, vertical: true),
			R(Ground.IceMeltRunnel, 4f, 0.03f, "#24506e", "#5a8eb0", "#b4d4e8", 3, SurfaceMotif.Ridges, 3, 0.05f, 0.4f, "#3a6a8a", "#e0ecf4", 0.5f, 0.1f, 0.5f),
			R(Ground.IceBubbleStack, 6f, 0.02f, "#0c1a24", "#26404e", "#6a8a9a", 3, SurfaceMotif.Cracks, 7, 0.02f, 0.6f, "#e8f0f4", "#c8dce6", 0.85f, 0.05f, 0.5f,
				speckles: Hexes("#c8dce6"), speckleCount: 600, speckleSize: 0.008f),
			R(Ground.PathIceRoad, 4f, 0.02f, "#8a96a4", "#b4c0cc", "#dce4ec", 4, SurfaceMotif.Ridges, 2, 0.04f, 0.5f, "#7a8694", "#c8d4e0", 0.55f, 0.15f, 0.5f,
				speckles: Hexes("#4a3a2a", "#c0a868"), speckleCount: 100, speckleSize: 0.004f),
			R(Ground.PathSnowHorseTrace, 3f, 0.015f, "#8c98a6", "#b8c4d0", "#e0e8f0", 4, SurfaceMotif.Clumps, 40, 0.15f, 0.45f, "#a4b0bc", "#e8eef4", 0.5f, 0.15f, 0.5f),
			R(Ground.SnowBlockYard, 3f, 0.04f, "#9ca8b6", "#c6d0dc", "#eaf0f6", 4, SurfaceMotif.Cobbles, 6, 0.06f, 0.7f, "#e8eef4", "#b8c4d0", 0.45f, 0.1f, 0.5f),
			R(Ground.SandCharnockiteGrus, 4f, 0.02f, "#5e5444", "#8a7c64", "#b4a68a", 6, SurfaceMotif.Granular, 22, 0.1f, 0.5f, "#4e4434", "#c8a878", 0.12f, 0.1f, 0.5f,
				speckles: Hexes("#8aa0b8", "#c8a878"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.GravelNunatakShingle, 2f, 0.03f, "#4a443a", "#786e5c", "#a4987e", 8, SurfaceMotif.Cobbles, 24, 0.13f, 0.85f, "#8a6a3e", "#b0a488", 0.15f, 0.1f, 0.85f),
			// ── Glacier (biome round, 2026-10-26) ── The draft's palettes and grains; the Thin air band's dust-mantled lobate ice and rimed dust.
			R(Ground.IceAblation, 8f, 0.05f, "#4a6e8c", "#8fb4cc", "#dce9f1", 3, SurfaceMotif.Fractured, 5, 0.03f, 0.5f, "#5f92b4", "#eef4f8", 0.6f, 0.15f, 0.5f, warp: 0.15f,
				speckles: Hexes("#2a2420", "#5ab0e0", "#6a645c", "#8a7a88"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.SnowFirnRunnels, 6f, 0.03f, "#a8b6c6", "#d4dee8", "#f2f6fa", 4, SurfaceMotif.Ridges, 14, 0.03f, 0.5f, "#c0cad6", "#fbfdfe", 0.45f, 0.1f, 0.45f,
				speckles: Hexes("#a8a49c", "#e8eef4"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.IceIcefall, 8f, 0.15f, "#1e4e7c", "#4a86b8", "#b4d6ec", 3, SurfaceMotif.Cracks, 6, 0.04f, 0.8f, "#10304e", "#f4f8fb", 0.65f, 0.1f, 0.5f),
			R(Ground.RockGreenstonePolished, 6f, 0.2f, "#34403a", "#5e6e62", "#8e9e90", 4, SurfaceMotif.Fractured, 6, 0.05f, 0.55f, "#2a322e", "#c4ccc0", 0.4f, 0.1f, 0.8f,
				speckles: Hexes("#d8e0d0", "#b8c43a"), speckleCount: 1050, speckleSize: 0.003f),
			R(Ground.GravelTill, 3f, 0.05f, "#4a4844", "#74706a", "#a29c92", 8, SurfaceMotif.Cobbles, 26, 0.13f, 0.85f, "#4a5a4a", "#c0b8a6", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#a89890", "#3a3a3e"), speckleCount: 600, speckleSize: 0.005f),
			R(Ground.GravelDebrisTongue, 3f, 0.06f, "#2a2826", "#46423e", "#6a645e", 8, SurfaceMotif.Cobbles, 18, 0.14f, 0.85f, "#1e1c1a", "#9ac0d8", 0.3f, 0.2f, 0.85f,
				speckles: Hexes("#9ac0d8", "#6a6e6c"), speckleCount: 300, speckleSize: 0.006f),
			R(Ground.SiltForefield, 4f, 0.03f, "#56564e", "#828074", "#aeaa9c", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#4a4a42", "#b8b4a6", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#8a8a6a", "#c02a8a", "#7a7a6a"), speckleCount: 580, speckleSize: 0.003f),
			R(Ground.CliffGreenstone, 10f, 0.6f, "#2e3832", "#56645a", "#869488", 4, SurfaceMotif.Strata, 4, 0.1f, 0.75f, "#4a6a50", "#a8b8a4", 0.2f, 0.05f, 0.85f, vertical: true,
				speckles: Hexes("#9aa69a", "#1e2420"), speckleCount: 600, speckleSize: 0.006f),
			R(Ground.GravelGreenBoulderBed, 2f, 0.04f, "#4e5a52", "#7a887e", "#a8b4aa", 8, SurfaceMotif.Cobbles, 20, 0.14f, 0.85f, "#3e4a42", "#e8e6de", 0.3f, 0.15f, 0.85f,
				speckles: Hexes("#b8bcb8"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.SiltVarvedClay, 4f, 0.02f, "#4e5a62", "#78848c", "#a2aeb4", 4, SurfaceMotif.Strata, 22, 0.1f, 0.5f, "#3e4a52", "#aeb8bc", 0.35f, 0.2f, 0.6f,
				speckles: Hexes("#3a3a3a"), speckleCount: 60, speckleSize: 0.008f),
			R(Ground.PathPitchedTrail, 4f, 0.03f, "#4a5448", "#727e70", "#9aa496", 6, SurfaceMotif.Cobbles, 40, 0.08f, 0.7f, "#3a4238", "#a8b0a2", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#3e3a34", "#d88a1a"), speckleCount: 240, speckleSize: 0.003f),
			R(Ground.PathMoraineTrod, 3f, 0.02f, "#5a6258", "#828a7e", "#aab2a4", 8, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#4a5248", "#b4bcae", 0.15f, 0.1f, 0.8f),
			R(Ground.FlagstoneTempleSlab, 4f, 0.03f, "#6e6c66", "#9a9890", "#c4c2b8", 6, SurfaceMotif.Cobbles, 5, 0.03f, 0.8f, "#4a4842", "#d0cec4", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#8a887e"), speckleCount: 120, speckleSize: 0.01f),
			R(Ground.SandPlacerFlour, 4f, 0.02f, "#6a7068", "#929890", "#bcc2b8", 6, SurfaceMotif.Ripples, 8, 0.1f, 0.5f, "#262626", "#c8ccc4", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#262626", "#d8b040"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.GravelBraidedShingle, 2f, 0.03f, "#56605a", "#808a80", "#a8b2a6", 8, SurfaceMotif.Cobbles, 36, 0.12f, 0.85f, "#4a5a4a", "#c0c4be", 0.15f, 0.1f, 0.85f),
			R(Ground.PathRubbleTrod, 3f, 0.02f, "#3e3c38", "#5a5852", "#7e7a72", 8, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#2a2826", "#3a4a5a", 0.2f, 0.15f, 0.8f,
				speckles: Hexes("#3a4a5a"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.IceDustMantled, 8f, 0.05f, "#6a5a4a", "#8e7a66", "#b4a08a", 4, SurfaceMotif.Ridges, 10, 0.06f, 0.6f, "#5a4a3a", "#c4b49e", 0.3f, 0.1f, 0.6f),
			R(Ground.FrostDustRimed, 6f, 0.03f, "#8a7a6a", "#b4a492", "#dcd2c4", 4, SurfaceMotif.Granular, 20, 0.1f, 0.4f, "#6a5a4a", "#f0e8e0", 0.4f, 0.1f, 0.5f),
			// ── Alpine Meadow (biome round, 2026-10-26) ── The draft's palettes (dark/mid/light, motif dark/light) and grains; tiles as the
			// draft gives them. LATER (I9): the sledge track's two polished skids and the trod's terracette risers as motifs of their own.
			R(Ground.GrassMeadowPolonyna, 3f, 0.04f, "#3a4424", "#66703a", "#9aa060", 4, SurfaceMotif.Blades, 8, 0.02f, 0.8f, "#4e5a2c", "#a8a86a", 0.15f, motifCount: 32000,
				speckles: Hexes("#8a6a7a", "#e8a830", "#3a5ac0", "#6a6c5e", "#2a2218"), speckleCount: 1000, speckleSize: 0.003f),
			R(Ground.GrassFlowerHay, 4f, 0.04f, "#3e4a22", "#6a7a34", "#a4a858", 4, SurfaceMotif.Blades, 8, 0.04f, 0.8f, "#4a5a28", "#b4b468", 0.15f, motifCount: 26000,
				speckles: Hexes("#c8b878", "#e8e0ec", "#b04a6a", "#6a4aa0"), speckleCount: 770, speckleSize: 0.003f),
			R(Ground.HeathMugo, 6f, 0.05f, "#22281a", "#40482c", "#6a6c48", 4, SurfaceMotif.Blades, 8, 0.02f, 0.8f, "#2e3420", "#74744c", 0.12f, motifCount: 18000,
				speckles: Hexes("#c02a4a", "#8a7a4a", "#7a4a2a", "#d8d0b0"), speckleCount: 1000, speckleSize: 0.003f),
			R(Ground.RockFlyschRibs, 10f, 0.3f, "#44463c", "#6e7062", "#9a9a88", 4, SurfaceMotif.Strata, 14, 0.08f, 0.5f, "#3a3c34", "#8e9080", 0.12f, 0.05f, 0.8f, warp: 0.1f,
				speckles: Hexes("#b8b840", "#d8d8cc", "#4a5a2a"), speckleCount: 550, speckleSize: 0.004f),
			R(Ground.SnowLatePatch, 8f, 0.03f, "#a8b0b8", "#d0d6dc", "#eef2f4", 4, SurfaceMotif.Granular, 8, 0.1f, 0.5f, "#8a8a7a", "#f4f6f8", 0.45f, 0.1f, 0.5f,
				speckles: Hexes("#8a8a7a", "#9aa8b4"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.GrassDryMeltTurf, 5f, 0.03f, "#5a5232", "#8a7e52", "#b8aa7a", 6, SurfaceMotif.Fibres, 18, 0.1f, 0.7f, "#6a603a", "#c4b48a", 0.12f, 0.1f, 0.75f,
				speckles: Hexes("#8a5ac0", "#f0eef4", "#9a7ad8", "#c8c8c0", "#4a3a2a"), speckleCount: 970, speckleSize: 0.003f),
			R(Ground.MossSpringFlush, 4f, 0.04f, "#2e4a16", "#5a8a24", "#a0c444", 6, SurfaceMotif.Clumps, 36, 0.45f, 0.75f, "#4a7a1a", "#b8d84a", 0.4f, 0.2f, 0.8f,
				speckles: Hexes("#f0c020", "#a8c0c8", "#a8602a"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.LimestoneKarren, 6f, 0.15f, "#5e5e58", "#a0a096", "#d2d2c8", 4, SurfaceMotif.Cracks, 10, 0.05f, 0.7f, "#464640", "#dcdcd2", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#6a7a6a", "#e8e8e0", "#e06a9a"), speckleCount: 720, speckleSize: 0.004f),
			R(Ground.CliffFlyschRib, 16f, 0.7f, "#3e4036", "#686a5c", "#96968a", 4, SurfaceMotif.Strata, 8, 0.1f, 0.8f, "#2e302a", "#a0a290", 0.12f, 0.05f, 0.85f, vertical: true,
				speckles: Hexes("#1e201a", "#4a5a2a"), speckleCount: 700, speckleSize: 0.006f),
			R(Ground.PebblesFlyschSlab, 2f, 0.03f, "#3e4038", "#646658", "#8e9082", 8, SurfaceMotif.Cobbles, 26, 0.12f, 0.85f, "#4e5046", "#9a9c8e", 0.35f, 0.15f, 0.85f,
				speckles: Hexes("#2e302a", "#8a5a34", "#4a5a2a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SiltTarn, 4f, 0.02f, "#2a2c28", "#4a4c46", "#6e706a", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#22241e", "#7a7c74", 0.45f, 0.25f, 0.6f,
				speckles: Hexes("#6a4a2a", "#6a6c5e"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.PathSledgeTrack, 4f, 0.015f, "#4e4a32", "#6e6a48", "#9a9468", 6, SurfaceMotif.Granular, 36, 0.1f, 0.45f, "#5a4a34", "#a8a870", 0.25f, 0.2f, 0.75f,
				speckles: Hexes("#5a6432", "#6a6c5e", "#3a2e20"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.PathSheepTrod, 3f, 0.015f, "#4a4232", "#6a5e48", "#928468", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#3a3428", "#a09070", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#e8e0d0", "#2a2218"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.FlagstoneFlyschCrazy, 3f, 0.04f, "#4e5046", "#6e7264", "#94988a", 6, SurfaceMotif.Cracks, 6, 0.05f, 0.8f, "#4a5a2a", "#9ea294", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#5a5e50", "#4a5a2a"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.SandFlysch, 2f, 0.02f, "#6a6c60", "#8e9082", "#b0b2a4", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.4f, "#5e6054", "#bcbeb0", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#d8d8d0", "#2e302a"), speckleCount: 500, speckleSize: 0.0025f),
			R(Ground.GravelFlyschShingle, 2f, 0.03f, "#4a4c44", "#74766a", "#9ea094", 8, SurfaceMotif.Cobbles, 40, 0.12f, 0.85f, "#3a3a3e", "#d0d0c8", 0.2f, 0.1f, 0.85f),
			R(Ground.PathKnieholzCut, 3f, 0.015f, "#4a2e1c", "#7a4a2a", "#9a6a44", 6, SurfaceMotif.Needles, 8, 0.02f, 0.6f, "#5a3a20", "#8a8478", 0.15f, 0.1f, 0.8f, motifCount: 5000,
				speckles: Hexes("#8a8478", "#3a2a1a"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.PathKarrenSteps, 4f, 0.03f, "#6a6a62", "#9a9a90", "#c8c8be", 6, SurfaceMotif.Cracks, 8, 0.05f, 0.6f, "#4a4a44", "#d4d4ca", 0.25f, 0.1f, 0.8f,
				speckles: Hexes("#7a7a70"), speckleCount: 500, speckleSize: 0.003f),
			// ── Mountain Slope (biome round, 2026-10-26) ── The draft's palettes and grains. LATER (I9): the mule road's braided runnels and
			// the root stair's root bars as motifs of their own.
			R(Ground.ForestFloorCloudHumus, 3f, 0.04f, "#1a160e", "#33291a", "#54442a", 6, SurfaceMotif.Leaves, 8, 0.02f, 0.85f, "#5a3e1e", "#8a6a34", 0.25f, 0.2f, 0.85f,
				motifCount: 2400, speckles: Hexes("#b8c4a0", "#4a6a22", "#7a5a30", "#7a3a22"), speckleCount: 1200, speckleSize: 0.003f),
			R(Ground.MossBlanket, 3f, 0.04f, "#1a2a0e", "#3a5a1a", "#7a9a30", 6, SurfaceMotif.Clumps, 30, 0.4f, 0.75f, "#2a4a14", "#8aaa38", 0.3f, 0.2f, 0.8f,
				speckles: Hexes("#2e5a2e", "#5a8a3a", "#12180c"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.GrassMeadowKikuyu, 3f, 0.04f, "#2a3a16", "#4e6a26", "#8a9a46", 4, SurfaceMotif.Blades, 8, 0.02f, 0.8f, "#3a5a1e", "#8aa04a", 0.15f, motifCount: 26000,
				speckles: Hexes("#8a4a2a", "#e8c020", "#4a7a3a", "#3a2e1e"), speckleCount: 980, speckleSize: 0.003f),
			R(Ground.SoilNitisol, 4f, 0.04f, "#3e160c", "#74321c", "#a85a34", 8, SurfaceMotif.Granular, 48, 0.1f, 0.6f, "#5a2a14", "#b86a40", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#7a8070", "#5a2a14", "#c8b080", "#2a1a10"), speckleCount: 1200, speckleSize: 0.003f),
			R(Ground.HeathEricaMoor, 6f, 0.05f, "#1e1a12", "#3e3a24", "#6a6440", 4, SurfaceMotif.Blades, 8, 0.025f, 0.8f, "#3a3a22", "#8a8458", 0.12f, motifCount: 16000,
				speckles: Hexes("#6a5a30", "#f0eee4", "#a8b4a8", "#120e0a", "#e8ecf0"), speckleCount: 1200, speckleSize: 0.003f),
			R(Ground.RockPhonoliteLedges, 8f, 0.25f, "#3a3e34", "#646a5a", "#9aa08c", 4, SurfaceMotif.Fractured, 6, 0.06f, 0.6f, "#2e322a", "#b0b4a0", 0.15f, 0.05f, 0.8f,
				speckles: Hexes("#4a6a22", "#c0c0b0", "#1e201a", "#e8e8dc"), speckleCount: 1200, speckleSize: 0.004f),
			R(Ground.ForestFloorCoffee, 4f, 0.04f, "#2a1c10", "#4e3620", "#7a5834", 6, SurfaceMotif.Leaves, 8, 0.06f, 0.85f, "#6a4a26", "#a07a3c", 0.2f, 0.2f, 0.85f,
				motifCount: 1400, speckles: Hexes("#a8201a", "#8a4428", "#9a5a34"), speckleCount: 480, speckleSize: 0.003f),
			R(Ground.CliffPhonolite, 16f, 0.7f, "#3a3c34", "#5e6256", "#8e9282", 4, SurfaceMotif.Strata, 8, 0.1f, 0.8f, "#2a2c26", "#3e6a22", 0.15f, 0.05f, 0.85f, vertical: true,
				speckles: Hexes("#181a14", "#3e6a22", "#8a4a2a"), speckleCount: 700, speckleSize: 0.006f),
			R(Ground.PebblesRedSilted, 2f, 0.03f, "#2a2620", "#4e4a40", "#7a7464", 8, SurfaceMotif.Cobbles, 24, 0.12f, 0.85f, "#2a2a28", "#6a705e", 0.35f, 0.15f, 0.85f,
				speckles: Hexes("#8a4a2a", "#8a5a3a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.MudRedClay, 4f, 0.02f, "#2e1a10", "#54301c", "#7a4a2e", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#22140c", "#8a5a3a", 0.5f, 0.3f, 0.6f,
				speckles: Hexes("#4a3a20"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.PathMuleGully, 4f, 0.02f, "#4a1e10", "#7a3a20", "#a8603a", 6, SurfaceMotif.Granular, 36, 0.12f, 0.5f, "#3a160c", "#b8704a", 0.2f, 0.15f, 0.75f,
				speckles: Hexes("#5a2a16", "#6a7060", "#3a2a1a"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.PathRootStair, 3f, 0.02f, "#1e160e", "#3a2c1c", "#5a4630", 6, SurfaceMotif.Fibres, 30, 0.1f, 0.6f, "#5a3e24", "#8a6a44", 0.3f, 0.2f, 0.8f,
				speckles: Hexes("#3e5a1e", "#2a1e14"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.FlagstoneSlabStair, 3f, 0.04f, "#3e4238", "#646a5c", "#8e9484", 6, SurfaceMotif.Cobbles, 6, 0.05f, 0.8f, "#22241e", "#a0a494", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#3a5a1e"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.SandPhonolite, 2f, 0.02f, "#2e2c28", "#4e4a42", "#76705e", 6, SurfaceMotif.Ripples, 8, 0.1f, 0.4f, "#26241e", "#8a846e", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#8a4a2a", "#e0e0d4"), speckleCount: 600, speckleSize: 0.0025f),
			R(Ground.GravelRedBlack, 2f, 0.03f, "#2a2824", "#504c44", "#7e786a", 8, SurfaceMotif.Cobbles, 40, 0.12f, 0.85f, "#2a2a28", "#8a4a2a", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#e8e4dc"), speckleCount: 100, speckleSize: 0.004f),
			R(Ground.PathBundleCauseway, 3f, 0.03f, "#120e0a", "#4a4236", "#8a7a62", 6, SurfaceMotif.Fibres, 20, 0.1f, 0.7f, "#5a4e3e", "#a8987a", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#3a4a1e"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.PathPhonoliteSteps, 3f, 0.03f, "#2a3a16", "#646a5a", "#8a9080", 6, SurfaceMotif.Cobbles, 10, 0.06f, 0.75f, "#3a5a1a", "#a0a494", 0.25f, 0.1f, 0.8f,
				speckles: Hexes("#3a5a1a"), speckleCount: 600, speckleSize: 0.003f),
			// ── Scree (biome round, 2026-10-26) ── The draft's palettes and grains; the nets on the sorted-circle motif. LATER (I9): the fine
			// scree's slide streaks along the fall line and the nets' SortedStripes motif on 4–14° slopes.
			R(Ground.GravelFineScree, 6f, 0.05f, "#4e5052", "#7c7d7a", "#aeaca4", 8, SurfaceMotif.Cobbles, 50, 0.1f, 0.85f, "#3a3c3e", "#b8b0a0", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#1a1a1a", "#e4e0d6", "#b8b0a0"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.RockOpenworkTalus, 10f, 0.4f, "#2a2b2c", "#5e5f5c", "#8e8d86", 4, SurfaceMotif.Cobbles, 7, 0.2f, 0.9f, "#0c0c0e", "#9a9990", 0.12f, 0.05f, 0.9f,
				speckles: Hexes("#e8ecea", "#1a1a1a"), speckleCount: 520, speckleSize: 0.005f),
			R(Ground.RockStoneRiver, 12f, 0.35f, "#3e403e", "#6a6c66", "#9a9a90", 4, SurfaceMotif.Cobbles, 5, 0.2f, 0.85f, "#101210", "#a8a8a0", 0.15f, 0.05f, 0.9f,
				speckles: Hexes("#d8dcd4", "#9aa494"), speckleCount: 500, speckleSize: 0.006f),
			R(Ground.GravelRockGlacierSkin, 10f, 0.15f, "#34363a", "#5a5c5c", "#888884", 6, SurfaceMotif.Ridges, 18, 0.08f, 0.7f, "#6e685a", "#949490", 0.15f, 0.1f, 0.85f),
			R(Ground.SoilFellNets, 8f, 0.06f, "#3e3a2e", "#5e5846", "#8a8268", 6, SurfaceMotif.SortedCircles, 6, 0.15f, 0.85f, "#4a5a2a", "#9a9a92", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#4a5a2a"), speckleCount: 200, speckleSize: 0.004f),
			R(Ground.SnowPatchScree, 6f, 0.03f, "#9aa0a6", "#c8ccd0", "#eceef0", 4, SurfaceMotif.Clumps, 20, 0.2f, 0.4f, "#3a3640", "#f4f6f8", 0.45f, 0.1f, 0.5f,
				speckles: Hexes("#3a3640", "#8a9096"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.SoilConeToe, 5f, 0.04f, "#3a2e22", "#5e4a34", "#8a7254", 6, SurfaceMotif.Granular, 40, 0.1f, 0.6f, "#4a3a2a", "#9a8264", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#4a3a2a", "#4a2a4a", "#b0aca4"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.RockBreathingTalus, 8f, 0.35f, "#2e3236", "#5a6066", "#a8b4bc", 4, SurfaceMotif.Cobbles, 7, 0.2f, 0.85f, "#1e2a1e", "#eef4f8", 0.3f, 0.15f, 0.9f,
				speckles: Hexes("#eef4f8", "#1e2a1e"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.GravelWedgeSpoil, 4f, 0.08f, "#5a5a56", "#86847e", "#b8b4aa", 8, SurfaceMotif.Cobbles, 20, 0.14f, 0.85f, "#3a3a38", "#e8e4dc", 0.2f, 0.1f, 0.85f),
			R(Ground.GravelRustScree, 6f, 0.05f, "#3a2420", "#6a4434", "#9a6a52", 8, SurfaceMotif.Cobbles, 50, 0.1f, 0.85f, "#2a1a16", "#b08a60", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#e8e4e0", "#b08a60"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.RegolithVacuumTalus, 8f, 0.3f, "#262628", "#444446", "#6e6e72", 4, SurfaceMotif.Cobbles, 9, 0.18f, 0.85f, "#1a1a1c", "#a0a0a6", 0.08f, 0.05f, 0.9f),
			R(Ground.CliffGranodiorite, 16f, 0.7f, "#4a4c4c", "#7e7e7a", "#b4b2aa", 4, SurfaceMotif.Strata, 6, 0.1f, 0.6f, "#2e2e30", "#e8e4dc", 0.15f, 0.05f, 0.85f, warp: 0.3f,
				vertical: true, speckles: Hexes("#2e2e30", "#e8e4dc", "#1a1c1c", "#e8ecea"), speckleCount: 800, speckleSize: 0.006f),
			R(Ground.GravelStepPool, 2f, 0.04f, "#3a3c3c", "#686a66", "#a09e94", 8, SurfaceMotif.Cobbles, 14, 0.15f, 0.85f, "#2e302e", "#c8c0ae", 0.4f, 0.15f, 0.85f),
			R(Ground.SiltRampartTarn, 4f, 0.02f, "#22241e", "#3e4036", "#686a5a", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#1a1c18", "#7a7c6a", 0.5f, 0.3f, 0.6f,
				speckles: Hexes("#4a4c48", "#3a2e20"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.PathFortressCauseway, 4f, 0.03f, "#6a6a64", "#8e8c84", "#b4b0a6", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#5a5a56", "#c4c0b6", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#5a5a56"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.PathTalusTrace, 2f, 0.02f, "#5a5a56", "#7e7c76", "#a6a49c", 8, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#4a4a46", "#b4b2aa", 0.15f, 0.1f, 0.8f),
			R(Ground.FlagstoneWedge, 3f, 0.04f, "#5e5e5a", "#8a8882", "#b6b2a8", 6, SurfaceMotif.Cobbles, 6, 0.05f, 0.8f, "#3a3a38", "#c4c0b6", 0.25f, 0.1f, 0.8f),
			R(Ground.SandGrus, 2f, 0.02f, "#8a8478", "#b4ac9c", "#d8d0c0", 6, SurfaceMotif.Granular, 34, 0.1f, 0.4f, "#7a7468", "#e0d8c8", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#1e1e1e"), speckleCount: 500, speckleSize: 0.0025f),
			R(Ground.GravelTorrentShingle, 2f, 0.03f, "#4a4c4a", "#767874", "#a4a49c", 8, SurfaceMotif.Cobbles, 22, 0.12f, 0.85f, "#2e2e30", "#b4b4ac", 0.25f, 0.1f, 0.85f),
			R(Ground.PathSteppingBlocks, 3f, 0.04f, "#4a4a48", "#76746e", "#a09e96", 6, SurfaceMotif.Cobbles, 8, 0.06f, 0.8f, "#2a2a2a", "#b0aea6", 0.2f, 0.1f, 0.85f),

			// ── Scrubland (biome round, 2026-10-26) ── The draft's palettes (dark/mid/light, motif dark/light) and grains; tiles as the draft's.
			// LATER (I9 T13): the caliche bench's nodular-knob motif and the wash's braid-lens bars (cobbles and ripples stand in), and the
			// loam's downslope rill lines.
			R(Ground.SoilBajadaLoam, 5f, 0.04f, "#7a5e44", "#a88462", "#ccae88", 6, SurfaceMotif.Granular, 40, 0.1f, 0.6f, "#6a4e38", "#d4b894", 0.15f, 0.1f, 0.75f,
				speckles: Hexes("#c8907a", "#e8e4dc", "#e4dccc"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.SiltCreosoteFlat, 6f, 0.02f, "#8e8070", "#b8a88e", "#dcd0b8", 6, SurfaceMotif.Granular, 18, 0.1f, 0.4f, "#7e7262", "#e4d8c2", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#4a4038", "#a8a898"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.CalicheBench, 6f, 0.08f, "#b0a48c", "#d4c8ae", "#f0e8d6", 5, SurfaceMotif.Cobbles, 14, 0.12f, 0.75f, "#9a8e76", "#f6f0e2", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#c09078", "#e8e0cc"), speckleCount: 400, speckleSize: 0.006f),
			R(Ground.GravelGrusBajada, 8f, 0.06f, "#6a5244", "#9a7a66", "#c8a892", 6, SurfaceMotif.Cobbles, 60, 0.08f, 0.8f, "#5a4436", "#d0b29c", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#b8c83a", "#e8e4dc"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.BasaltMalpais, 10f, 0.3f, "#141210", "#2e2a26", "#524a42", 4, SurfaceMotif.Fractured, 10, 0.1f, 0.85f, "#0c0a08", "#5e564c", 0.15f, 0.05f, 0.9f,
				speckles: Hexes("#7a4a26", "#6a7a3a", "#b8a07a"), speckleCount: 600, speckleSize: 0.005f),
			R(Ground.LitterMesquiteBosque, 5f, 0.03f, "#3e3022", "#5e4a34", "#86704e", 6, SurfaceMotif.Leaves, 8, 0.02f, 0.7f, "#4e5a2c", "#c8a870", 0.15f, 0.15f, 0.85f,
				motifCount: 2000, speckles: Hexes("#c8a870", "#6a7a3a", "#e8e0d0"), speckleCount: 700, speckleSize: 0.004f),
			R(Ground.CliffMylonite, 18f, 0.7f, "#3e3c3a", "#76706a", "#b8b0a6", 4, SurfaceMotif.Strata, 16, 0.1f, 0.6f, "#2e2c2a", "#c09a8a", 0.15f, 0.05f, 0.85f, warp: 0.12f,
				vertical: true, speckles: Hexes("#b8c83a", "#8a5a3a", "#e8e4dc"), speckleCount: 500, speckleSize: 0.006f),
			R(Ground.SandWashArkose, 2f, 0.02f, "#7a6450", "#a88c70", "#d0b898", 6, SurfaceMotif.Ripples, 3, 0.1f, 0.5f, "#6a5444", "#dcc8aa", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#ece4d8", "#6a5444"), speckleCount: 700, speckleSize: 0.0025f),
			R(Ground.ClayTank, 3f, 0.02f, "#4a3a2a", "#6e5840", "#94785a", 6, SurfaceMotif.Cracks, 10, 0.03f, 0.5f, "#3a2c20", "#a08462", 0.4f, 0.2f, 0.7f,
				speckles: Hexes("#5a6a3a", "#3a2e22"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.PathCanalBank, 6f, 0.02f, "#8a7254", "#b09474", "#d4ba98", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#7a6448", "#dcc4a4", 0.2f, 0.1f, 0.7f,
				speckles: Hexes("#e4dccc", "#7a6448"), speckleCount: 500, speckleSize: 0.006f),
			R(Ground.PathClearedTrail, 3f, 0.015f, "#9a8a74", "#c0ae94", "#e0d0b4", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#8a7a66", "#e8dcc4", 0.2f, 0.1f, 0.7f,
				speckles: Hexes("#f0e8dc", "#9a3a24", "#5a4e44"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.FlagstoneCalicheSetts, 3f, 0.04f, "#a89880", "#cebea2", "#ece0c8", 6, SurfaceMotif.Cobbles, 7, 0.06f, 0.8f, "#8a5a3e", "#f2e8d2", 0.25f, 0.1f, 0.8f),
			R(Ground.PathMalpaisTrail, 3f, 0.03f, "#3a342e", "#5a524a", "#7e746a", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#2a2622", "#8a8074", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#141210", "#b8a07a"), speckleCount: 600, speckleSize: 0.005f),
			R(Ground.SandWashBar, 2f, 0.02f, "#9a8068", "#c4a888", "#e4d0b0", 6, SurfaceMotif.Ripples, 8, 0.1f, 0.5f, "#8a7058", "#ecdabc", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#2a2420"), speckleCount: 500, speckleSize: 0.0025f),
			R(Ground.GravelWashCobble, 2f, 0.03f, "#4a3e36", "#8a7a6a", "#c8baa8", 8, SurfaceMotif.Cobbles, 30, 0.12f, 0.85f, "#2a2420", "#e0d8c4", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#c8907a", "#e8e4dc", "#2a2420"), speckleCount: 300, speckleSize: 0.005f),

			// ── Savanna (biome round, 2026-10-26) ── The draft's palettes and grains. The burnt veld (the draft's d5) is not painted: the burn
			// mosaic draws it on the GPU from the shared clock (I2t). LATER (I9 T1): the dwala's onion-skin sheets and gnamma pans as motifs.
			R(Ground.GrassDryTawnyVeld, 6f, 0.04f, "#4a3220", "#86643c", "#b8945c", 6, SurfaceMotif.Blades, 30, 0.03f, 0.7f, "#7a4a2a", "#c8a868", 0.15f, 0.1f, 0.75f,
				motifCount: 24000, speckles: Hexes("#e8e2d6", "#a07a48", "#7a4a2a"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.SandGraniteSandveld, 4f, 0.02f, "#6a6052", "#a49686", "#d2c6b4", 6, SurfaceMotif.Granular, 60, 0.1f, 0.5f, "#5a5044", "#dcd0be", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#c89a86", "#f0ece4", "#2a2622"), speckleCount: 900, speckleSize: 0.0025f),
			R(Ground.ClayMopane, 5f, 0.03f, "#3e3428", "#6a5a46", "#948066", 6, SurfaceMotif.Cracks, 14, 0.04f, 0.6f, "#2e2620", "#a08a6e", 0.25f, 0.15f, 0.75f,
				speckles: Hexes("#4a3e30", "#8a5a2a", "#b07a3a"), speckleCount: 800, speckleSize: 0.004f),
			R(Ground.SoilGreenstoneRed, 5f, 0.03f, "#4a1e14", "#7a3624", "#a85a3a", 6, SurfaceMotif.Granular, 40, 0.1f, 0.6f, "#3a160e", "#b46a48", 0.15f, 0.1f, 0.75f,
				speckles: Hexes("#2a1a12", "#5a6a5a"), speckleCount: 700, speckleSize: 0.0035f),
			R(Ground.RockDwalaSlab, 10f, 0.15f, "#5a524a", "#9a8e82", "#cac0b4", 4, SurfaceMotif.Fractured, 10, 0.1f, 0.6f, "#4a443e", "#d4cabe", 0.3f, 0.1f, 0.85f,
				speckles: Hexes("#d07a2a", "#b8c040", "#8a9a7a"), speckleCount: 600, speckleSize: 0.008f),
			R(Ground.MudVleiTurf, 4f, 0.02f, "#1e1a16", "#3a342c", "#5e564a", 6, SurfaceMotif.Cracks, 8, 0.02f, 0.4f, "#141210", "#6a6252", 0.45f, 0.25f, 0.7f,
				speckles: Hexes("#5a5a3a", "#3a3a24"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.CliffMatopo, 14f, 0.6f, "#4e4842", "#8a8076", "#c4b8aa", 4, SurfaceMotif.Fractured, 18, 0.1f, 0.7f, "#3a3430", "#d0c4b6", 0.15f, 0.05f, 0.85f,
				vertical: true, speckles: Hexes("#d07a2a", "#b8c040", "#8a9a7a", "#a0805a"), speckleCount: 700, speckleSize: 0.007f),
			R(Ground.SandRiverGruss, 5f, 0.03f, "#8a7a68", "#c4b2a0", "#ece2d4", 6, SurfaceMotif.Ripples, 6, 0.1f, 0.5f, "#7a6a58", "#f2eadc", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#5a4a3a", "#c89a86"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.MudPanWallow, 4f, 0.02f, "#2e2c24", "#4e4a3c", "#7a7460", 6, SurfaceMotif.Granular, 24, 0.1f, 0.4f, "#3a3628", "#86806a", 0.5f, 0.25f, 0.65f,
				speckles: Hexes("#3a3628", "#5a6a3a"), speckleCount: 800, speckleSize: 0.004f),
			R(Ground.PathCattleRoad, 4f, 0.02f, "#4a2a1a", "#7a4a30", "#a87250", 6, SurfaceMotif.Granular, 20, 0.1f, 0.5f, "#3a2214", "#b47e5c", 0.2f, 0.1f, 0.75f,
				speckles: Hexes("#3a2a1a", "#5a5a2e"), speckleCount: 560, speckleSize: 0.005f),
			R(Ground.PathSingleFile, 2f, 0.01f, "#8a7a64", "#b4a48a", "#d8cab0", 6, SurfaceMotif.Granular, 40, 0.1f, 0.4f, "#7a6a56", "#e2d6bc", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#f0ece4"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.FlagstoneDagaFloor, 3f, 0.01f, "#6a4426", "#9a6a40", "#c49a68", 6, SurfaceMotif.Granular, 12, 0.1f, 0.3f, "#5a3a22", "#d0a874", 0.5f, 0.15f, 0.6f,
				speckles: Hexes("#5a3a22"), speckleCount: 160, speckleSize: 0.008f),
			R(Ground.PathGameTrail, 3f, 0.02f, "#3a3228", "#5e5242", "#807058", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#2e2820", "#8a7a62", 0.2f, 0.1f, 0.75f,
				speckles: Hexes("#3a2a1a", "#4a3e30"), speckleCount: 600, speckleSize: 0.005f),
			R(Ground.PathSlabSteps, 3f, 0.04f, "#5a524a", "#8a8076", "#b4aa9e", 6, SurfaceMotif.Cobbles, 6, 0.05f, 0.8f, "#3e3832", "#c4bab0", 0.3f, 0.1f, 0.8f),
			R(Ground.SandPinkGruss, 2f, 0.02f, "#9a8270", "#c8b09c", "#ead8c6", 6, SurfaceMotif.Granular, 90, 0.1f, 0.4f, "#8a7260", "#f2e2d0", 0.15f, 0.1f, 0.55f,
				speckles: Hexes("#c89a86", "#f0ece4"), speckleCount: 600, speckleSize: 0.0025f),
			R(Ground.GravelReefIronstone, 2f, 0.03f, "#2a2420", "#7a6e62", "#e6e0d6", 8, SurfaceMotif.Cobbles, 26, 0.12f, 0.85f, "#1a1612", "#f0ece4", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#f0ece4", "#1a1612", "#5a6a5a"), speckleCount: 300, speckleSize: 0.005f),
			// ── Grassland (biome round, 2026-10-26) ── The draft's palettes and grains; the loess wall and bluff vertical, the szik's polygon cracks.
			R(Ground.GrassPusztaSward, 3f, 0.04f, "#3a3c1c", "#6a6a32", "#a09a58", 4, SurfaceMotif.Blades, 8, 0.035f, 0.8f, "#5a6030", "#b0a460", 0.15f, 0.1f, 0.8f, motifCount: 32000,
				speckles: Hexes("#c0aa6a", "#8a4a30", "#2a2018", "#ece4d4"), speckleCount: 1980, speckleSize: 0.003f),
			R(Ground.GrassDryGoldbeard, 4f, 0.05f, "#4a3a22", "#8a6a3a", "#c09a5a", 4, SurfaceMotif.Blades, 8, 0.045f, 0.8f, "#7a4a3a", "#d0a858", 0.12f, 0.1f, 0.8f, motifCount: 26000,
				speckles: Hexes("#6a3a4a", "#d8c088"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.SoilChernozem, 4f, 0.03f, "#161210", "#2a2420", "#463a30", 8, SurfaceMotif.Granular, 48, 0.1f, 0.5f, "#0e0c0a", "#3a3028", 0.12f, 0.1f, 0.75f,
				speckles: Hexes("#a08a5a", "#3a3028", "#0e0c0a", "#b8a060"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.SandPuszta, 4f, 0.02f, "#8a7a58", "#b8a47a", "#dcc8a0", 6, SurfaceMotif.Ripples, 8, 0.1f, 0.35f, "#7a6a4c", "#c8b48a", 0.12f, 0.05f, 0.4f,
				speckles: Hexes("#8a9a92", "#c8a040", "#a89a70"), speckleCount: 1300, speckleSize: 0.003f),
			R(Ground.SaltCrustSzik, 6f, 0.03f, "#5e5a50", "#a29a8a", "#ece8de", 6, SurfaceMotif.Cracks, 10, 0.04f, 0.7f, "#4a463e", "#d8d4ca", 0.3f, 0.15f, 0.7f,
				speckles: Hexes("#f6f4ee", "#8a2a3a", "#7a8a5a"), speckleCount: 2300, speckleSize: 0.003f),
			R(Ground.LoessBank, 6f, 0.05f, "#8a7450", "#b89a6a", "#dcc496", 6, SurfaceMotif.Granular, 56, 0.08f, 0.5f, "#6a5034", "#e0d0b0", 0.1f, 0.05f, 0.75f,
				speckles: Hexes("#f0e8d8", "#e0d4b8", "#6a5034"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.LoessWall, 8f, 0.2f, "#806844", "#ae9064", "#d4bc8e", 4, SurfaceMotif.Strata, 4, 0.12f, 0.6f, "#6a5636", "#d8c498", 0.1f, 0.05f, 0.8f, vertical: true,
				speckles: Hexes("#2a2018", "#6a4a30"), speckleCount: 500, speckleSize: 0.008f),
			R(Ground.CliffLoessBluff, 10f, 0.5f, "#7e6642", "#b09264", "#d8c090", 4, SurfaceMotif.Strata, 5, 0.12f, 0.7f, "#6a4a2e", "#e4dccb", 0.1f, 0.05f, 0.85f, vertical: true,
				speckles: Hexes("#e4dccb", "#2a2018"), speckleCount: 600, speckleSize: 0.005f),
			R(Ground.SiltTisza, 2f, 0.02f, "#4a4436", "#6e6650", "#908870", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#3a3428", "#a09878", 0.35f, 0.2f, 0.6f,
				speckles: Hexes("#d8d0c0", "#b8c0c8", "#a89060"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.MudSodaOoze, 4f, 0.03f, "#58584e", "#7e7c70", "#aaa89a", 6, SurfaceMotif.Granular, 28, 0.1f, 0.4f, "#48483e", "#c0beb2", 0.45f, 0.3f, 0.6f,
				speckles: Hexes("#eeece6", "#c09088", "#e8e4dc"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.PathBlackEarthTrack, 4f, 0.03f, "#1e1a16", "#3a3228", "#5a4e40", 8, SurfaceMotif.Ridges, 12, 0.03f, 0.6f, "#4a4034", "#6a6a32", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#6a665c", "#b8a060", "#0e0c0a"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.PathHerdTrail, 3f, 0.015f, "#5a4a34", "#7e6a4a", "#a8906a", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#3a2e20", "#6a6a32", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#2e261c", "#3a2e20"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.PathSandTrackPuszta, 4f, 0.02f, "#a8946a", "#c8b48a", "#e0d0a8", 6, SurfaceMotif.Ridges, 10, 0.03f, 0.55f, "#8a7a58", "#d8c8a0", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#a89a70"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.BrickHerringbone, 3f, 0.03f, "#5e2a1c", "#9a4a30", "#c07050", 6, SurfaceMotif.Cobbles, 22, 0.05f, 0.85f, "#c8b088", "#c87a58", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#3a2a22", "#5a6a3a"), speckleCount: 200, speckleSize: 0.004f),
			R(Ground.SandMicaRiver, 2f, 0.02f, "#8a8270", "#b0a68a", "#d0c6aa", 6, SurfaceMotif.Ripples, 12, 0.1f, 0.5f, "#7a7262", "#dcd4bc", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#ecece4", "#e0d8c8"), speckleCount: 600, speckleSize: 0.0025f),
			R(Ground.GravelConcretionGrit, 2f, 0.03f, "#7a6a50", "#a08c6c", "#c8b490", 8, SurfaceMotif.Cobbles, 40, 0.1f, 0.85f, "#6a5a44", "#d8ccb0", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#d0c8b8", "#9a4a30"), speckleCount: 300, speckleSize: 0.004f),

			// ── Desert (biome round, 2026-10-26) ── The draft's palettes (dark/mid/light, motif dark/light) and grains; tiles as the draft's.
			// LATER (I9 T3): the varnish skin as its own channel on the sandstone and the reg; (I9 T1) the caravan piste's 6–14 braided
			// trails and the slip face's grainflow tongues as motifs of their own (granular grain and ridges stand in).
			R(Ground.SandErg, 4f, 0.03f, "#b0743a", "#d49a5a", "#ecc48a", 6, SurfaceMotif.Ripples, 30, 0.1f, 0.6f, "#6a4a32", "#f0c890", 0.12f, 0.05f, 0.4f,
				speckles: Hexes("#6a4a32", "#f6eedc"), speckleCount: 1200, speckleSize: 0.002f),
			R(Ground.SandSlipFace, 6f, 0.03f, "#b8804a", "#d8a466", "#f0cc94", 5, SurfaceMotif.Ridges, 10, 0.05f, 0.5f, "#a87440", "#f4d4a0", 0.15f, 0.05f, 0.4f),
			R(Ground.RegPavement, 6f, 0.05f, "#3a2a20", "#6a5038", "#b89a74", 8, SurfaceMotif.Cobbles, 40, 0.12f, 0.85f, "#2a1e18", "#ece6d8", 0.35f, 0.15f, 0.85f,
				speckles: Hexes("#5a3a24", "#9a3a24", "#7a7470"), speckleCount: 900, speckleSize: 0.004f),
			R(Ground.GravelGassi, 6f, 0.03f, "#9a7a58", "#c0a07a", "#dcc4a0", 6, SurfaceMotif.Ripples, 10, 0.12f, 0.6f, "#5a4632", "#c8907a", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#c8907a", "#4a3a2a"), speckleCount: 1200, speckleSize: 0.003f),
			R(Ground.SandstoneVarnished, 8f, 0.25f, "#2e221a", "#6a4a34", "#c88a5a", 4, SurfaceMotif.Fractured, 6, 0.08f, 0.75f, "#241a14", "#d08a58", 0.25f, 0.1f, 0.8f,
				speckles: Hexes("#d8b07a"), speckleCount: 600, speckleSize: 0.004f),
			R(Ground.ClayPaleolakeMarl, 8f, 0.03f, "#8a8070", "#b8ae98", "#e2dac6", 6, SurfaceMotif.Cracks, 8, 0.03f, 0.6f, "#6a6258", "#f0ece2", 0.12f, 0.1f, 0.7f,
				speckles: Hexes("#f0ece2", "#ece4d0"), speckleCount: 400, speckleSize: 0.005f),
			R(Ground.SandRedSheet, 5f, 0.02f, "#8a4a2a", "#b06a3e", "#d0925e", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#7a3e22", "#dca070", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#8a8a6a"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.CliffNubian, 18f, 0.7f, "#3a2a20", "#a0603a", "#d89a64", 4, SurfaceMotif.Strata, 14, 0.1f, 0.8f, "#241a14", "#e0a870", 0.12f, 0.05f, 0.85f, warp: 0.15f,
				vertical: true, speckles: Hexes("#241a14"), speckleCount: 500, speckleSize: 0.006f),
			R(Ground.GravelWadi, 2f, 0.03f, "#6a5a48", "#9a8468", "#c8b08e", 8, SurfaceMotif.Cobbles, 30, 0.12f, 0.85f, "#2a1e18", "#ece6d8", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#b0703e", "#8a7a64"), speckleCount: 300, speckleSize: 0.005f),
			R(Ground.ClayGueltaOoze, 4f, 0.02f, "#2a2a20", "#4a4836", "#6a6650", 6, SurfaceMotif.Granular, 24, 0.1f, 0.4f, "#4a5a2a", "#c8a070", 0.5f, 0.25f, 0.65f),
			R(Ground.PathCaravanTrack, 6f, 0.02f, "#a07a50", "#c49c6c", "#e0c294", 6, SurfaceMotif.Granular, 24, 0.1f, 0.45f, "#8a6a42", "#ead0a4", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#5a4430"), speckleCount: 400, speckleSize: 0.006f),
			R(Ground.PathCamelPad, 3f, 0.015f, "#a8804e", "#c8a06c", "#e4c696", 6, SurfaceMotif.Granular, 24, 0.1f, 0.4f, "#8a6a40", "#ecd0a0", 0.15f, 0.1f, 0.6f),
			R(Ground.FlagstoneSlabPaving, 3f, 0.04f, "#9a6a44", "#c08a5a", "#dcb084", 6, SurfaceMotif.Cobbles, 6, 0.05f, 0.8f, "#7a4e30", "#d8b07a", 0.25f, 0.1f, 0.8f),
			R(Ground.SandWadiFlood, 2f, 0.02f, "#b8a080", "#d4bc98", "#ecdcc0", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.5f, "#9a8468", "#f0e4cc", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#9a8468"), speckleCount: 200, speckleSize: 0.005f),
			R(Ground.GravelWadiBar, 2f, 0.03f, "#3a3028", "#8a7058", "#c8b098", 8, SurfaceMotif.Cobbles, 34, 0.12f, 0.85f, "#2a1e18", "#ece6d8", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#9a3a24", "#b0703e"), speckleCount: 200, speckleSize: 0.005f),
			R(Ground.PathRegTrack, 4f, 0.02f, "#8a7458", "#b49c7c", "#d8c4a2", 6, SurfaceMotif.Granular, 30, 0.1f, 0.45f, "#7a6448", "#e2d0b0", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#2a1e18", "#5a3a24"), speckleCount: 500, speckleSize: 0.006f),
			R(Ground.SandFrostErg, 4f, 0.03f, "#9a8a7a", "#c8b8a4", "#e8eaec", 6, SurfaceMotif.Ripples, 30, 0.1f, 0.6f, "#6a5a4a", "#f4f6f8", 0.35f, 0.1f, 0.45f,
				speckles: Hexes("#e8eaec", "#f6eedc"), speckleCount: 1500, speckleSize: 0.002f),

			// ── Badlands (biome round, 2026-10-26) ── The draft's palettes and grains. LATER (I9 T4): the Rills motif on the bare tuff and the
			// rut-cut road's twin ruts (strata and granular grain stand in).
			R(Ground.AshLapilliSteppe, 4f, 0.02f, "#8a7a62", "#b8a688", "#dccdb0", 6, SurfaceMotif.Granular, 48, 0.1f, 0.5f, "#6a5e4a", "#e8dcc4", 0.1f, 0.05f, 0.6f,
				speckles: Hexes("#f0ece4", "#4a443c", "#8a8e78", "#1e1c1e"), speckleCount: 1540, speckleSize: 0.0025f),
			R(Ground.TuffRilled, 8f, 0.2f, "#9a7a6a", "#c8a894", "#ead8c8", 4, SurfaceMotif.Strata, 7, 0.1f, 0.75f, "#c88a7a", "#f2ece2", 0.12f, 0.05f, 0.8f,
				speckles: Hexes("#c8a060", "#c88a7a"), speckleCount: 400, speckleSize: 0.006f),
			R(Ground.ClayWashFloor, 6f, 0.03f, "#7a6252", "#a68a74", "#c8b09a", 6, SurfaceMotif.Cracks, 10, 0.04f, 0.7f, "#5a4436", "#d8c4b0", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#e8e2d6"), speckleCount: 300, speckleSize: 0.005f),
			R(Ground.SoilAshLoam, 5f, 0.03f, "#5a4a3a", "#806a52", "#a68e72", 6, SurfaceMotif.Granular, 36, 0.1f, 0.55f, "#4a3e30", "#b49a7e", 0.12f, 0.1f, 0.75f,
				speckles: Hexes("#d8d0c4", "#4a3424", "#e08a30"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.GravelLapilliDrift, 6f, 0.04f, "#bab4a8", "#d8d2c6", "#f0ece4", 6, SurfaceMotif.Cobbles, 40, 0.08f, 0.8f, "#c8a090", "#f2eee6", 0.15f, 0.1f, 0.75f,
				speckles: Hexes("#3a3634", "#c8a090"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.BasaltCaprock, 8f, 0.25f, "#2e2c2a", "#4a4642", "#6e6862", 4, SurfaceMotif.Fractured, 14, 0.06f, 0.75f, "#1e1c1a", "#7a746c", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#c8783a", "#b8a688"), speckleCount: 450, speckleSize: 0.006f),
			R(Ground.TuffRedValley, 8f, 0.2f, "#6a3428", "#9a5440", "#c8806a", 4, SurfaceMotif.Strata, 5, 0.1f, 0.75f, "#5a2a20", "#f0e8e0", 0.12f, 0.05f, 0.8f,
				speckles: Hexes("#f0e8e0"), speckleCount: 300, speckleSize: 0.005f),
			R(Ground.CliffIgnimbrite, 18f, 0.7f, "#8a6a5a", "#c8a894", "#ecdcc8", 4, SurfaceMotif.Strata, 6, 0.1f, 0.8f, "#4a3e38", "#f2ece2", 0.12f, 0.05f, 0.85f,
				vertical: true, speckles: Hexes("#4a3e38", "#c88a7a"), speckleCount: 600, speckleSize: 0.008f),
			R(Ground.SandLapilliStream, 2f, 0.02f, "#6a6058", "#9a8e80", "#c4b8aa", 6, SurfaceMotif.Ripples, 6, 0.1f, 0.5f, "#2a2826", "#c8a090", 0.3f, 0.15f, 0.6f,
				speckles: Hexes("#2a2826", "#c8a090"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.ClayTuffOoze, 4f, 0.02f, "#5a4a42", "#7a665a", "#9a8676", 6, SurfaceMotif.Granular, 24, 0.1f, 0.4f, "#4a3c36", "#a89484", 0.45f, 0.25f, 0.65f,
				speckles: Hexes("#e0dad0"), speckleCount: 60, speckleSize: 0.008f),
			R(Ground.PathRutCut, 6f, 0.03f, "#8a7a66", "#b4a48a", "#d8cab0", 6, SurfaceMotif.Granular, 24, 0.1f, 0.45f, "#9a8a74", "#dccdb0", 0.2f, 0.1f, 0.75f,
				speckles: Hexes("#6a5a48"), speckleCount: 300, speckleSize: 0.005f),
			R(Ground.PathDonkeyAsh, 3f, 0.015f, "#8a7a62", "#ae9e82", "#d0c2a6", 6, SurfaceMotif.Granular, 28, 0.1f, 0.4f, "#7a6a54", "#dacdb2", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#5a4a38"), speckleCount: 40, speckleSize: 0.006f),
			R(Ground.FlagstoneTuffSetts, 3f, 0.04f, "#a8927a", "#cdb89c", "#eadcc4", 6, SurfaceMotif.Cobbles, 7, 0.05f, 0.8f, "#c8a090", "#e8e2d6", 0.2f, 0.1f, 0.8f),
			R(Ground.SandLapilliBar, 2f, 0.02f, "#a89e90", "#ccc2b4", "#ebe4d8", 6, SurfaceMotif.Ripples, 12, 0.1f, 0.5f, "#988e80", "#f2ece2", 0.15f, 0.1f, 0.55f,
				speckles: Hexes("#1e1c1e", "#ece8e0"), speckleCount: 200, speckleSize: 0.0025f),
			R(Ground.GravelVolcanicBar, 2f, 0.03f, "#4e4844", "#847a70", "#bab0a4", 8, SurfaceMotif.Cobbles, 34, 0.12f, 0.85f, "#3a3634", "#f0ece4", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#c8a090", "#141214"), speckleCount: 200, speckleSize: 0.005f),
			R(Ground.PathTuffSteps, 3f, 0.05f, "#b8a48c", "#d8c8b0", "#f0e4d0", 6, SurfaceMotif.Cobbles, 6, 0.05f, 0.75f, "#9a8670", "#f4ecdc", 0.2f, 0.1f, 0.8f),
			R(Ground.PathWashTrace, 4f, 0.015f, "#9a826c", "#bea690", "#dac6b0", 6, SurfaceMotif.Granular, 20, 0.1f, 0.4f, "#8a7260", "#e4d2bc", 0.15f, 0.1f, 0.6f),
			R(Ground.AshSnowOn, 4f, 0.03f, "#a8a294", "#d8d6d0", "#e8eaec", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#8a8478", "#f6f8fa", 0.35f, 0.1f, 0.45f,
				speckles: Hexes("#f0ece4", "#4a443c"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.CrackedEarthSaltBaked, 4f, 0.04f, "#9a8670", "#c0a888", "#e0d0b8", 6, SurfaceMotif.Cracks, 12, 0.03f, 0.7f, "#7a6a56", "#f4f0ea", 0.12f, 0.05f, 0.7f,
				speckles: Hexes("#f4f0ea"), speckleCount: 600, speckleSize: 0.004f),

			// ── Salt Flat (biome round, 2026-10-26) ── The draft's palettes and grains; the amole crust on the polygon motif (its metre plates).
			// LATER (I9 T1): every ground as Salt Flat's own variant of its family (SaltCrust, Mud, CrackedEarth, Reg, Sand, Gravel, PathSalt,
			// Flagstone); the hopper rims and the mirror morning's wet sheen (WATER C21) are not drawn.
			R(Ground.SaltAmoleCrust, 6f, 0.03f, "#b8b2a6", "#dedad0", "#f8f6f0", 6, SurfaceMotif.Polygons, 5, 0.04f, 0.7f, "#c4bca8", "#ffffff", 0.55f, 0.1f, 0.6f,
				speckles: Hexes("#8a7458", "#ffffff"), speckleCount: 600, speckleSize: 0.002f),
			R(Ground.SaltEtchedCrust, 4f, 0.05f, "#8e8a80", "#b6b0a4", "#d6d0c4", 6, SurfaceMotif.Clumps, 22, 0.35f, 0.7f, "#6a5a46", "#e0dace", 0.25f, 0.1f, 0.75f,
				speckles: Hexes("#6a5a46"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.MudBrineEdge, 3f, 0.02f, "#3a342c", "#5e564a", "#8a8070", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#b87a7a", "#f0ece4", 0.6f, 0.25f, 0.65f,
				speckles: Hexes("#f0ece4", "#b87a7a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.ClayKhakiPan, 4f, 0.03f, "#6e604a", "#9a8a6e", "#c4b496", 6, SurfaceMotif.Cracks, 12, 0.04f, 0.7f, "#4a3e2e", "#d8ccb0", 0.12f, 0.05f, 0.7f,
				speckles: Hexes("#ece6da"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.RegAfarBasalt, 6f, 0.06f, "#1e1a18", "#3a3430", "#6a6058", 8, SurfaceMotif.Cobbles, 36, 0.12f, 0.85f, "#2a2624", "#a89a86", 0.3f, 0.15f, 0.85f,
				speckles: Hexes("#7a4a2a", "#e8e4dc"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.SandSaltSoot, 4f, 0.02f, "#2e2c2a", "#56524c", "#8a847a", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.4f, "#242220", "#9a948a", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#f2f0ea"), speckleCount: 500, speckleSize: 0.0025f),
			R(Ground.SaltSpringCrust, 3f, 0.05f, "#8a7a64", "#c8bca6", "#ece6d8", 6, SurfaceMotif.Clumps, 18, 0.4f, 0.75f, "#a0602a", "#f4f0e6", 0.35f, 0.15f, 0.7f,
				speckles: Hexes("#a0602a"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.SaltAlkaliPlaya, 6f, 0.02f, "#7a7466", "#a8a092", "#ccc6b8", 6, SurfaceMotif.Ridges, 6, 0.03f, 0.4f, "#8a8274", "#e6e2da", 0.2f, 0.1f, 0.6f,
				speckles: Hexes("#e6e2da"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.CliffSaltrock, 10f, 0.5f, "#6a5e52", "#b0a698", "#e6e0d6", 4, SurfaceMotif.Strata, 12, 0.1f, 0.8f, "#a8604a", "#f0ece4", 0.3f, 0.1f, 0.8f, warp: 0.12f,
				vertical: true, speckles: Hexes("#a8604a"), speckleCount: 400, speckleSize: 0.006f),
			R(Ground.GravelWadiBasalt, 3f, 0.03f, "#2a2622", "#4e4840", "#847a6e", 8, SurfaceMotif.Cobbles, 30, 0.12f, 0.85f, "#1a1816", "#e8e4dc", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#e8e4dc"), speckleCount: 300, speckleSize: 0.005f),
			R(Ground.SaltBrineLakeFloor, 4f, 0.03f, "#a8b4b0", "#d4dcd8", "#f4f6f4", 6, SurfaceMotif.Cobbles, 40, 0.05f, 0.7f, "#4a4a44", "#ffffff", 0.6f, 0.15f, 0.6f),
			R(Ground.PathBarRoad, 4f, 0.02f, "#6e665a", "#9a9286", "#c4beb2", 6, SurfaceMotif.Granular, 24, 0.1f, 0.45f, "#5a5246", "#ece8e0", 0.2f, 0.1f, 0.7f,
				speckles: Hexes("#5a5246", "#ece8e0", "#4a3e30"), speckleCount: 620, speckleSize: 0.005f),
			R(Ground.PathCuttersTrace, 2f, 0.02f, "#807868", "#aaa294", "#d0cabe", 6, SurfaceMotif.Cracks, 10, 0.04f, 0.6f, "#6a5e4c", "#e0dace", 0.25f, 0.1f, 0.7f),
			R(Ground.FlagstoneBasaltSetts, 3f, 0.04f, "#1e1c1a", "#3a3632", "#5e5852", 6, SurfaceMotif.Cobbles, 6, 0.05f, 0.8f, "#141210", "#e4e0d6", 0.3f, 0.1f, 0.8f),
			R(Ground.SandSaltPepper, 2f, 0.02f, "#4a4640", "#8a847a", "#d8d4ca", 6, SurfaceMotif.Granular, 80, 0.1f, 0.5f, "#2a2826", "#f2f0ea", 0.2f, 0.1f, 0.6f,
				speckles: Hexes("#f2f0ea", "#1e1c1a"), speckleCount: 800, speckleSize: 0.0025f),
			R(Ground.GravelCrustedPebbles, 2f, 0.03f, "#2e2a26", "#6a645a", "#dcd8ce", 8, SurfaceMotif.Cobbles, 26, 0.12f, 0.85f, "#1e1c1a", "#f0ece4", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#f0ece4"), speckleCount: 300, speckleSize: 0.005f),
			R(Ground.PathCrustCauseway, 3f, 0.03f, "#a8a296", "#ccc6ba", "#ece8e0", 6, SurfaceMotif.Cobbles, 6, 0.05f, 0.8f, "#5a4e3e", "#f4f2ec", 0.3f, 0.1f, 0.75f),
			R(Ground.PathBasaltTrack, 4f, 0.02f, "#8a7e6c", "#a89a86", "#c8bca8", 6, SurfaceMotif.Granular, 30, 0.1f, 0.45f, "#7a6e5c", "#d4c8b4", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#2a2624"), speckleCount: 400, speckleSize: 0.006f),
			// ── Plains (biome round, 2026-10-26) ── The draft's palettes and grains; the red beds' strata, the playa's polygon cracks, the travois trace's grooves as ridges.
			R(Ground.GrassDryMixedPrairie, 3f, 0.04f, "#4a4a30", "#7e7a52", "#b0a878", 4, SurfaceMotif.Blades, 8, 0.03f, 0.8f, "#7a8a82", "#c8b888", 0.12f, 0.1f, 0.8f, motifCount: 28000,
				speckles: Hexes("#7a8a82", "#a05a30", "#e0dccc", "#4a3a28"), speckleCount: 1660, speckleSize: 0.003f),
			R(Ground.GrassShortgrassSod, 3f, 0.03f, "#4e5236", "#7a7a56", "#a8a480", 4, SurfaceMotif.Blades, 8, 0.008f, 0.75f, "#5a6a48", "#b8b080", 0.12f, 0.1f, 0.8f, motifCount: 40000,
				speckles: Hexes("#7a5a6a", "#9a8a6a"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.GrassMeadowTallgrass, 3f, 0.05f, "#3a4626", "#6a7438", "#9a9656", 4, SurfaceMotif.Blades, 8, 0.06f, 0.8f, "#4a5a2e", "#a8a060", 0.15f, 0.1f, 0.8f, motifCount: 22000,
				speckles: Hexes("#6a2a4a", "#c89a3a", "#3a2a1a"), speckleCount: 560, speckleSize: 0.004f),
			R(Ground.SandSandhills, 4f, 0.02f, "#9a8a62", "#c8b488", "#e6d6b0", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.45f, "#8a7a56", "#d8c8a0", 0.12f, 0.05f, 0.4f,
				speckles: Hexes("#a89a62", "#7a8a7a", "#d8ccaa"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.SoilBuffaloWallow, 4f, 0.04f, "#3a3026", "#5a4a38", "#806c56", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#2a2018", "#6a5a44", 0.3f, 0.2f, 0.7f,
				speckles: Hexes("#2a2018", "#6a5a44", "#a8906a", "#7a7a70"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.ClayPlayaFloor, 6f, 0.03f, "#6a6250", "#948a74", "#bab09a", 6, SurfaceMotif.Cracks, 14, 0.04f, 0.7f, "#50483a", "#c4baa4", 0.2f, 0.1f, 0.7f,
				speckles: Hexes("#8a3a4a", "#4a6a3a", "#e8e4dc"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.RedbedGypSlope, 8f, 0.1f, "#6a2a1c", "#9a4a30", "#c8785a", 4, SurfaceMotif.Strata, 6, 0.1f, 0.15f, "#5a2418", "#d08a6a", 0.12f, 0.05f, 0.8f,
				speckles: Hexes("#f0ece4", "#7a8a6a", "#7a7a56"), speckleCount: 820, speckleSize: 0.004f),
			R(Ground.RedbedRimrockTalus, 8f, 0.25f, "#5a2418", "#8a3e28", "#e8e2d6", 4, SurfaceMotif.Fractured, 12, 0.08f, 0.75f, "#3a1a12", "#e8e2d6", 0.12f, 0.05f, 0.85f),
			R(Ground.CliffRedbedGypsum, 10f, 0.5f, "#5e2618", "#8e4430", "#ece6da", 4, SurfaceMotif.Strata, 5, 0.12f, 0.75f, "#6a7a5a", "#ece6da", 0.1f, 0.05f, 0.85f, vertical: true,
				speckles: Hexes("#ece6da"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SandPlatteBraided, 2f, 0.02f, "#7a6a4c", "#a8926a", "#ccb890", 6, SurfaceMotif.Ripples, 6, 0.1f, 0.5f, "#4a3e2e", "#d8c8a0", 0.3f, 0.2f, 0.6f,
				speckles: Hexes("#4a3e2e", "#f0ece4"), speckleCount: 240, speckleSize: 0.003f),
			R(Ground.ClayOxbowOoze, 4f, 0.03f, "#4a4436", "#6a6250", "#8e8670", 6, SurfaceMotif.Granular, 28, 0.1f, 0.4f, "#3a3428", "#9a907a", 0.45f, 0.3f, 0.6f,
				speckles: Hexes("#d8d0c0", "#8a7a3a"), speckleCount: 250, speckleSize: 0.003f),
			R(Ground.PathTravoisTrace, 6f, 0.03f, "#5a4c36", "#84704e", "#ae9a74", 6, SurfaceMotif.Ridges, 9, 0.03f, 0.6f, "#4a3c2c", "#7e7a52", 0.12f, 0.1f, 0.8f,
				speckles: Hexes("#b8a47a", "#4a3a28"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.PathBisonTrail, 3f, 0.02f, "#4a3c2c", "#6e5a42", "#94806a", 6, SurfaceMotif.Granular, 28, 0.1f, 0.5f, "#2e2418", "#8a845a", 0.12f, 0.1f, 0.8f,
				speckles: Hexes("#2e2418", "#8a845a"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.PathSandTrace, 4f, 0.02f, "#a8946a", "#c8b488", "#e0d0a8", 6, SurfaceMotif.Ridges, 8, 0.03f, 0.5f, "#8a7a56", "#d8c8a0", 0.12f, 0.05f, 0.5f),
			R(Ground.PathRedTrail, 3f, 0.02f, "#6a2e20", "#8e4a34", "#b06a50", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#5a2418", "#ece6da", 0.12f, 0.1f, 0.8f,
				speckles: Hexes("#ece6da"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.FlagstoneRedbed, 3f, 0.04f, "#6a2e20", "#9a5038", "#c07a5a", 6, SurfaceMotif.Cobbles, 8, 0.06f, 0.8f, "#7a7a56", "#c88a6a", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#ece6da", "#b07a30"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.SandFeldsparBar, 2f, 0.02f, "#a08a70", "#c4ac8e", "#e2d0b6", 6, SurfaceMotif.Ripples, 12, 0.1f, 0.5f, "#8a7660", "#e8d8c0", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#c89a8a", "#ecece4"), speckleCount: 600, speckleSize: 0.0025f),
			R(Ground.GravelOgallala, 2f, 0.03f, "#6a5a4a", "#9a8470", "#c8b498", 8, SurfaceMotif.Cobbles, 36, 0.12f, 0.85f, "#b07a6a", "#d8d0c8", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#7a5a3a", "#e0d8c4"), speckleCount: 200, speckleSize: 0.004f),
			R(Ground.SnowCrustSward, 4f, 0.03f, "#a8a8a0", "#d0d2d0", "#eceef0", 4, SurfaceMotif.Ridges, 8, 0.04f, 0.5f, "#8a7a52", "#e8ecf0", 0.4f, 0.1f, 0.5f,
				speckles: Hexes("#8a7a52"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.CrackedDustPlain, 5f, 0.04f, "#6a4a32", "#8a6a4a", "#b08e6a", 6, SurfaceMotif.Cracks, 10, 0.04f, 0.7f, "#4a3222", "#c8a87a", 0.12f, 0.05f, 0.7f,
				speckles: Hexes("#c8b480"), speckleCount: 400, speckleSize: 0.003f),

			// ── Estuary (biome round, 2026-10-26) ── The draft's palettes (dark/mid/light, motif dark/light) and grains; tiles as the draft's.
			// LATER (I9 T1): every ground as Estuary's own variant of its family (Mud, Sand, Grass, SaltCrust, Silt, Gravel, PathMud, Till);
			// LATER (I9 T7): the lugworm-cast and combed-strand motifs (Clumps and Fibres stand in).
			R(Ground.MudSchlickFlat, 4f, 0.02f, "#3a3a34", "#5a5a50", "#7e7c6e", 6, SurfaceMotif.Ripples, 6, 0.1f, 0.4f, "#2a2a24", "#8a8a7c", 0.75f, 0.2f, 0.6f,
				speckles: Hexes("#4a3e30", "#e0dace", "#6a6e66"), speckleCount: 1000, speckleSize: 0.003f),
			R(Ground.SandSandwatt, 4f, 0.03f, "#5e5a48", "#8a8468", "#b4ac8c", 6, SurfaceMotif.Clumps, 40, 0.3f, 0.5f, "#3a362c", "#7a7058", 0.5f, 0.15f, 0.6f,
				speckles: Hexes("#d8d0c0", "#3a362c"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.MudZosteraFlat, 3f, 0.02f, "#2e3226", "#484e38", "#646a4c", 6, SurfaceMotif.Fibres, 60, 0.12f, 0.6f, "#2a4a22", "#4a6a2a", 0.7f, 0.2f, 0.65f,
				speckles: Hexes("#6aaa3a", "#3a3a2a"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.MudPioneerSalting, 4f, 0.03f, "#403830", "#605448", "#847466", 6, SurfaceMotif.Clumps, 30, 0.2f, 0.55f, "#6a8a3a", "#8a2a3a", 0.6f, 0.2f, 0.65f,
				speckles: Hexes("#4a5a3a", "#2e2a24"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.GrassLowMarsh, 3f, 0.03f, "#3a4a3a", "#5a6a56", "#7e8c78", 4, SurfaceMotif.Blades, 8, 0.022f, 0.8f, "#4a5a46", "#9ab0a0", 0.3f, motifCount: 22000,
				speckles: Hexes("#8a8670", "#4a6a4a", "#c8b888"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.GrassHighMarsh, 3f, 0.03f, "#2e3a24", "#4a5a34", "#6e7a4c", 4, SurfaceMotif.Blades, 8, 0.012f, 0.8f, "#3a4a2a", "#7a8a58", 0.15f, motifCount: 30000,
				speckles: Hexes("#7a3a3a", "#2a3220", "#4a6a3a", "#e8e4d8"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.SaltPanneCrust, 5f, 0.02f, "#5a5850", "#8a887e", "#dcdad2", 6, SurfaceMotif.Cracks, 16, 0.04f, 0.7f, "#3e3c36", "#f2f2ec", 0.45f, 0.2f, 0.6f,
				speckles: Hexes("#f2f2ec", "#8a5a7a"), speckleCount: 600, speckleSize: 0.004f),
			R(Ground.SiltCreekBank, 4f, 0.05f, "#3e3a32", "#5e584a", "#847c68", 6, SurfaceMotif.Strata, 22, 0.08f, 0.6f, "#2e2a24", "#9a927c", 0.5f, 0.15f, 0.8f,
				speckles: Hexes("#1e1c18", "#5a4a30"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.TillKliffFace, 10f, 0.6f, "#5a5244", "#8a6a4e", "#b88a5e", 4, SurfaceMotif.Strata, 6, 0.08f, 0.6f, "#6a6458", "#a8603a", 0.12f, 0.05f, 0.85f,
				vertical: true, speckles: Hexes("#b08070", "#7a3a32", "#2a2a2c", "#e8e4dc"), speckleCount: 500, speckleSize: 0.006f),
			R(Ground.MudFluidSulphide, 2f, 0.01f, "#1e1e1c", "#34342e", "#4e4c44", 6, SurfaceMotif.Granular, 80, 0.06f, 0.4f, "#2a2a28", "#6a6a5e", 0.85f, 0.15f, 0.55f,
				speckles: Hexes("#2a2a28", "#6a6a5e"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.MudSubtidal, 3f, 0.02f, "#26282a", "#3e4042", "#5a5c5a", 6, SurfaceMotif.Granular, 60, 0.08f, 0.45f, "#1e2228", "#5a6a7a", 0.8f, 0.15f, 0.55f,
				speckles: Hexes("#1e2228", "#5a6a7a", "#3a5a2a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.ClayKleiweg, 4f, 0.03f, "#4a4c48", "#6e7068", "#9a9a8e", 6, SurfaceMotif.Cracks, 20, 0.05f, 0.6f, "#3a3a34", "#9a9a8e", 0.35f, 0.15f, 0.7f,
				speckles: Hexes("#e0dace", "#5a6460"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.PathMudSheepTrack, 3f, 0.02f, "#3a3a30", "#56564a", "#767666", 6, SurfaceMotif.Granular, 30, 0.08f, 0.45f, "#2a2a22", "#5a6a4a", 0.45f, 0.2f, 0.6f,
				speckles: Hexes("#2a2a22", "#5a6a4a"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.PathMudWattweg, 3f, 0.02f, "#34342c", "#52524a", "#74746a", 6, SurfaceMotif.Granular, 28, 0.08f, 0.45f, "#2a2a24", "#8a7a52", 0.5f, 0.2f, 0.6f,
				speckles: Hexes("#2a2a24", "#8a7a52"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.FlagstoneMoppen, 3f, 0.03f, "#5a2a1e", "#8a4430", "#b06a4e", 6, SurfaceMotif.Cobbles, 3, 0.04f, 0.9f, "#4a2a2a", "#a89a7a", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#4a2a2a", "#4a5a2a"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.SandShoalCockle, 2f, 0.02f, "#6a6450", "#948c70", "#bcb294", 6, SurfaceMotif.Granular, 36, 0.06f, 0.4f, "#8a8264", "#cac2a4", 0.3f, 0.1f, 0.5f,
				speckles: Hexes("#e8e0d0", "#e8b0b8"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.GravelSchill, 2f, 0.03f, "#4a4a4c", "#8a8680", "#d4cec4", 8, SurfaceMotif.Cobbles, 50, 0.1f, 0.85f, "#6a6458", "#ece6da", 0.3f, 0.15f, 0.8f,
				speckles: Hexes("#1e2228", "#e8a8b0"), speckleCount: 400, speckleSize: 0.004f),
			// ── Coral Reef, Beach and Abyss (biome round, 2026-10-26) ── The draft's palettes and grains; one motif a recipe (the draft's second
			// motif is a speckle or left to I9 T7).
			R(Ground.SandAtoll, 4f, 0.03f, "#b4ac94", "#dcd4bc", "#f4eedc", 6, SurfaceMotif.Ripples, 7, 0.1f, 0.4f, "#c4bca4", "#f8f4e6", 0.15f, 0.05f, 0.5f,
				speckles: Hexes("#e6e2c8", "#d89090", "#c8b898"), speckleCount: 980, speckleSize: 0.003f),
			R(Ground.ReefFramework, 6f, 0.12f, "#5e5646", "#8e826a", "#bcae90", 6, SurfaceMotif.Clumps, 28, 0.4f, 0.75f, "#8a5a8a", "#d08a9a", 0.25f, 0.1f, 0.8f,
				speckles: Hexes("#8a7a4a", "#6a8a5a", "#e8e0cc"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.RubbleBranchRods, 4f, 0.08f, "#7e7a6a", "#aaa490", "#d8d2c0", 8, SurfaceMotif.Cobbles, 40, 0.15f, 0.85f, "#6a5e3a", "#f0ece0", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#f0ece0", "#6a5e3a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SandLagoon, 5f, 0.02f, "#a4a08c", "#ccc8b2", "#eae6d4", 6, SurfaceMotif.Clumps, 10, 0.12f, 0.4f, "#6a6656", "#b8b4a0", 0.2f, 0.1f, 0.5f),
			R(Ground.ReefFlatPavement, 5f, 0.1f, "#746c5c", "#a29884", "#cac0a8", 6, SurfaceMotif.Cracks, 18, 0.06f, 0.7f, "#4a6a6a", "#c88090", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#f0e4b0", "#2a2420", "#c88090"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.ReefBleached, 6f, 0.12f, "#a8a498", "#d4d0c4", "#f6f4ee", 6, SurfaceMotif.Clumps, 28, 0.4f, 0.75f, "#8a8a5a", "#f8f6f0", 0.25f, 0.1f, 0.8f,
				speckles: Hexes("#8a8a5a", "#f8f6f0"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.SandForeReef, 5f, 0.04f, "#6a6a62", "#908e82", "#b8b4a6", 6, SurfaceMotif.Cobbles, 8, 0.1f, 0.45f, "#5a5a54", "#c0bcae", 0.15f, 0.05f, 0.6f),
			R(Ground.CliffDropOff, 12f, 0.6f, "#3e3a32", "#6a6252", "#968a74", 4, SurfaceMotif.Fractured, 8, 0.08f, 0.7f, "#1a1612", "#e090b0", 0.3f, 0.1f, 0.85f,
				vertical: true, speckles: Hexes("#e07030", "#8040a0", "#e090b0", "#e0c040", "#1a1612"), speckleCount: 700, speckleSize: 0.005f),
			R(Ground.SandLagoonPowder, 3f, 0.02f, "#c4c0ac", "#e8e4d4", "#fbf9f0", 6, SurfaceMotif.Granular, 90, 0.1f, 0.4f, "#b4b09c", "#ffffff", 0.15f, 0.05f, 0.4f,
				speckles: Hexes("#fffef8"), speckleCount: 400, speckleSize: 0.002f),
			R(Ground.GravelReefGrit, 2f, 0.03f, "#8a8470", "#b6ae98", "#ded6c2", 8, SurfaceMotif.Cobbles, 34, 0.12f, 0.85f, "#7a7460", "#d8a0a0", 0.3f, 0.1f, 0.85f),
			R(Ground.FlagstoneHirigaa, 4f, 0.05f, "#9a907a", "#c8bea6", "#e8e0cc", 6, SurfaceMotif.Cobbles, 5, 0.05f, 0.9f, "#8a826e", "#f4f0e4", 0.2f, 0.1f, 0.8f),
			R(Ground.PathSweptSand, 3f, 0.02f, "#c4bca6", "#e6e0ce", "#faf6ea", 6, SurfaceMotif.Ripples, 4, 0.08f, 0.5f, "#b4ac96", "#fffcf0", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#8a7a4a"), speckleCount: 150, speckleSize: 0.003f),
			R(Ground.FlagstoneRagSett, 3f, 0.05f, "#b4aa92", "#d6ceb8", "#f0ead8", 6, SurfaceMotif.Cobbles, 12, 0.05f, 0.85f, "#d8d0bc", "#f6f2e8", 0.2f, 0.1f, 0.8f),
			R(Ground.SandForamBar, 2f, 0.02f, "#c4a49c", "#e2cac2", "#f6e8e2", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#b09488", "#fff0ea", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#d88080", "#e8e4cc"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.GravelCayShingle, 2f, 0.03f, "#aca694", "#d4cebc", "#f2eee2", 8, SurfaceMotif.Cobbles, 30, 0.12f, 0.85f, "#9a9482", "#fbfaf4", 0.25f, 0.1f, 0.85f),
			R(Ground.SandSinging, 3f, 0.03f, "#b8ac94", "#d8ccb2", "#f0e8d8", 6, SurfaceMotif.Ripples, 18, 0.08f, 0.25f, "#c8bca4", "#f8f2e4", 0.1f, 0.05f, 0.5f,
				speckles: Hexes("#b06a6a", "#2a2622", "#f4f0e8"), speckleCount: 800, speckleSize: 0.0025f),
			R(Ground.SandSwash, 3f, 0.02f, "#6e675a", "#918874", "#b4aa94", 6, SurfaceMotif.Ripples, 10, 0.08f, 0.5f, "#3a342c", "#e8e6dc", 0.6f, 0.1f, 0.5f,
				speckles: Hexes("#e8e6dc", "#3a342c"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.LitterWrackAmber, 3f, 0.06f, "#1e1a12", "#3e3424", "#6a5a40", 6, SurfaceMotif.Fibres, 30, 0.1f, 0.6f, "#2a2418", "#5a4a30", 0.3f, 0.1f, 0.7f,
				speckles: Hexes("#141210", "#d88a20", "#ece8e0"), speckleCount: 480, speckleSize: 0.003f),
			R(Ground.LichenGreyDune, 4f, 0.05f, "#5e5e50", "#8e8e7c", "#c4c4b0", 6, SurfaceMotif.Clumps, 40, 0.3f, 0.65f, "#a8b098", "#c8ccb8", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#2a3a26", "#d8ccb2"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.SandWarmBackdune, 4f, 0.03f, "#9a7a50", "#c4a474", "#e6cca0", 6, SurfaceMotif.Ripples, 14, 0.08f, 0.4f, "#8a6a40", "#f0dcb4", 0.1f, 0.05f, 0.5f,
				speckles: Hexes("#8a3a2a", "#6a7a3a", "#e8dcc0"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.SandSlipTongues, 4f, 0.05f, "#a0927a", "#c4b698", "#e2d6bc", 6, SurfaceMotif.Ridges, 8, 0.04f, 0.5f, "#8a7c64", "#ece2cc", 0.1f, 0.05f, 0.5f,
				speckles: Hexes("#2a2826", "#b08a7a"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.BeachrockPavement, 6f, 0.15f, "#6a6252", "#968c76", "#c0b69c", 4, SurfaceMotif.Strata, 10, 0.1f, 0.6f, "#4a4236", "#e8e0cc", 0.35f, 0.1f, 0.8f,
				speckles: Hexes("#e8e0cc", "#4a5a3a"), speckleCount: 600, speckleSize: 0.004f),
			R(Ground.CliffBlueEarth, 10f, 0.5f, "#2a3634", "#4a5a56", "#8a8670", 4, SurfaceMotif.Strata, 20, 0.1f, 0.7f, "#1a1614", "#c8bca0", 0.3f, 0.1f, 0.85f,
				vertical: true, speckles: Hexes("#c8801e", "#1a1614", "#3e4e4a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SandBeachStream, 2f, 0.02f, "#4e483c", "#766c5a", "#a0947c", 6, SurfaceMotif.Ripples, 8, 0.08f, 0.5f, "#24201c", "#b8ac94", 0.5f, 0.1f, 0.5f),
			R(Ground.SandSwashBar, 3f, 0.03f, "#7a7462", "#9e9682", "#c0b8a2", 6, SurfaceMotif.Ripples, 6, 0.1f, 0.6f, "#6a6452", "#d8d0bc", 0.2f, 0.05f, 0.5f,
				speckles: Hexes("#e8e0d0"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.SandTrough, 4f, 0.04f, "#4e4c40", "#6e6a58", "#908a74", 6, SurfaceMotif.Ripples, 4, 0.12f, 0.6f, "#2a2a1e", "#a0987e", 0.2f, 0.05f, 0.5f,
				speckles: Hexes("#2a2a1e"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.SiltOffshore, 6f, 0.03f, "#3a3c34", "#545648", "#6e7060", 6, SurfaceMotif.Granular, 70, 0.1f, 0.4f, "#2e3028", "#80826e", 0.2f, 0.1f, 0.5f,
				speckles: Hexes("#d8d0c0"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.GravelShellRoad, 3f, 0.04f, "#8a8270", "#b0a890", "#d4ccb8", 8, SurfaceMotif.Cobbles, 80, 0.08f, 0.7f, "#6a6454", "#f0ece2", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#f0ece2"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.PathDuneTrod, 3f, 0.03f, "#a49880", "#c6baa0", "#e2d8c2", 6, SurfaceMotif.Granular, 20, 0.1f, 0.4f, "#8a7e66", "#e8dec8", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#6a5a44"), speckleCount: 150, speckleSize: 0.004f),
			R(Ground.PathBrushwoodMat, 3f, 0.06f, "#3a2e1e", "#5a4a34", "#7e6a4c", 6, SurfaceMotif.Fibres, 24, 0.1f, 0.7f, "#2a2014", "#8a7a5a", 0.1f, 0.05f, 0.8f,
				speckles: Hexes("#a49880"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.FlagstoneErraticCobbles, 3f, 0.05f, "#5e504a", "#8e7e74", "#b8aaa0", 6, SurfaceMotif.Cobbles, 10, 0.05f, 0.85f, "#c8bca0", "#b07a6a", 0.2f, 0.1f, 0.8f),
			R(Ground.SandGarnetBar, 2f, 0.02f, "#9a8c76", "#c0b49a", "#e0d6c0", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#8a7c66", "#ece2cc", 0.12f, 0.05f, 0.5f,
				speckles: Hexes("#b07070", "#2a2622"), speckleCount: 700, speckleSize: 0.003f),
			R(Ground.GravelFlintShingle, 2f, 0.03f, "#3a3836", "#6a6460", "#a09890", 8, SurfaceMotif.Cobbles, 36, 0.12f, 0.85f, "#1e1e20", "#e8e4dc", 0.3f, 0.1f, 0.85f,
				speckles: Hexes("#b08a7a", "#e8e4dc"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.SiltHadalOoze, 4f, 0.03f, "#2a2620", "#463e34", "#66594a", 6, SurfaceMotif.Granular, 48, 0.1f, 0.45f, "#1e1a16", "#8a8070", 0.2f, 0.1f, 0.5f,
				speckles: Hexes("#d8d4c8", "#c8c0b0"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.PeridotiteTrenchWall, 8f, 0.3f, "#161a14", "#2e3628", "#4e5a42", 4, SurfaceMotif.Fractured, 12, 0.08f, 0.7f, "#7a4a26", "#6a8a3a", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#7a4a26", "#6a8a3a", "#463e34"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.BasaltPillowTalus, 6f, 0.15f, "#121416", "#262a2e", "#44484c", 6, SurfaceMotif.Cobbles, 14, 0.25f, 0.8f, "#8a6a3a", "#5a5e62", 0.6f, 0.1f, 0.8f,
				speckles: Hexes("#8a6a3a", "#463e34"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.MudSeepSediment, 3f, 0.03f, "#0e0e0c", "#22201a", "#3a362c", 6, SurfaceMotif.Clumps, 20, 0.3f, 0.5f, "#080806", "#ece8dc", 0.5f, 0.1f, 0.5f,
				speckles: Hexes("#ece8dc", "#d88a3a"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.ChemohermSeepPavement, 5f, 0.12f, "#4a463a", "#7a7462", "#a8a28c", 6, SurfaceMotif.Cracks, 26, 0.06f, 0.7f, "#1e1c18", "#8a7a5a", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#141210", "#8a7a5a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.AsphaltFlow, 6f, 0.1f, "#070707", "#141412", "#262420", 6, SurfaceMotif.Ridges, 10, 0.05f, 0.5f, "#070707", "#3a3448", 0.5f, 0.1f, 0.7f,
				speckles: Hexes("#3a3448", "#463e34"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.HydrateCrust, 3f, 0.08f, "#8a8060", "#c8bc8a", "#ece4c4", 6, SurfaceMotif.Clumps, 22, 0.35f, 0.65f, "#c87a7a", "#f4eed4", 0.5f, 0.1f, 0.7f,
				speckles: Hexes("#c87a7a", "#463e34"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.CliffSerpentineScarp, 14f, 0.6f, "#10140f", "#262e22", "#46523c", 4, SurfaceMotif.Fractured, 8, 0.06f, 0.7f, "#5a7a4a", "#7a4a22", 0.4f, 0.1f, 0.85f,
				vertical: true, speckles: Hexes("#5a7a4a", "#7a4a22"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SandTurbiditeChannel, 2f, 0.03f, "#1e1e1c", "#3a3a36", "#5c5a54", 6, SurfaceMotif.Ripples, 10, 0.08f, 0.5f, "#141412", "#6a6e74", 0.2f, 0.05f, 0.5f,
				speckles: Hexes("#6a6e74"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.SiltOozeRim, 4f, 0.03f, "#22201a", "#3a342a", "#564c40", 6, SurfaceMotif.Granular, 44, 0.1f, 0.4f, "#1a1814", "#6a6050", 0.2f, 0.1f, 0.5f),
			R(Ground.FlagstoneCyclopeanCauseway, 6f, 0.1f, "#141618", "#2a2e32", "#4a4e52", 6, SurfaceMotif.Cobbles, 4, 0.05f, 0.9f, "#463e34", "#d8d4c8", 0.15f, 0.1f, 0.8f),
			R(Ground.FlagstoneProcessional, 4f, 0.06f, "#1a1c1e", "#34383a", "#565a5c", 6, SurfaceMotif.Cobbles, 7, 0.05f, 0.9f, "#0e0e10", "#6a6e70", 0.15f, 0.1f, 0.8f),
			R(Ground.PebblesPillowGlass, 3f, 0.05f, "#0e1012", "#22262a", "#3e4246", 8, SurfaceMotif.Cobbles, 16, 0.1f, 0.85f, "#463e34", "#5a6068", 0.6f, 0.1f, 0.85f),
			R(Ground.SandHyaloclastite, 2f, 0.02f, "#1a1a18", "#34322e", "#524e48", 6, SurfaceMotif.Granular, 40, 0.1f, 0.45f, "#121210", "#7a8088", 0.2f, 0.05f, 0.5f,
				speckles: Hexes("#7a8088"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.GravelPeridotiteBar, 2f, 0.03f, "#141612", "#2c3226", "#4a5040", 8, SurfaceMotif.Cobbles, 40, 0.12f, 0.85f, "#7a4a26", "#4a6a3a", 0.25f, 0.1f, 0.85f),
			// ── Steppe (biome round, 2026-10-26) ── The draft's palettes and grains; the takyr's polygon plates, the sor's puffy crust domes, the Horde road's braided ruts as ridges.
			R(Ground.GrassDryKovylSward, 4f, 0.04f, "#5a5236", "#8e8458", "#c4b888", 4, SurfaceMotif.Blades, 8, 0.03f, 0.75f, "#6a4e34", "#e0dccc", 0.12f, 0.1f, 0.8f, motifCount: 22000,
				speckles: Hexes("#e0dccc", "#2a2620", "#a8a490"), speckleCount: 1250, speckleSize: 0.003f),
			R(Ground.SoilChestnut, 4f, 0.03f, "#3e2c1e", "#6a4e34", "#946e4c", 8, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#2a1e14", "#a07a56", 0.12f, 0.1f, 0.75f,
				speckles: Hexes("#d8d0c0", "#2a1e14", "#3a2e20"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.GrassDryWormwoodSteppe, 4f, 0.04f, "#5a5a4a", "#8a8a74", "#b8b8a0", 4, SurfaceMotif.Blades, 8, 0.025f, 0.6f, "#6a5a44", "#c8c8b0", 0.12f, 0.1f, 0.8f, motifCount: 9000,
				speckles: Hexes("#a0a490", "#9a6a3a", "#a06a7a"), speckleCount: 1900, speckleSize: 0.004f),
			R(Ground.ClayTakyr, 10f, 0.02f, "#8a7058", "#b49478", "#d8bea0", 6, SurfaceMotif.Cracks, 14, 0.03f, 0.7f, "#6a5440", "#e0c8aa", 0.25f, 0.1f, 0.7f),
			R(Ground.SaltCrustSor, 8f, 0.05f, "#2a2826", "#8a8478", "#f2eee6", 6, SurfaceMotif.Clumps, 18, 0.3f, 0.7f, "#1e1c1a", "#f6f4ee", 0.35f, 0.25f, 0.65f,
				speckles: Hexes("#1e1c1a", "#6a5a40", "#9a2a2a"), speckleCount: 700, speckleSize: 0.004f),
			R(Ground.GravelPorphyryGrit, 4f, 0.04f, "#4a2e2e", "#7a4e48", "#a87a70", 8, SurfaceMotif.Cobbles, 26, 0.1f, 0.85f, "#3a2224", "#ece8e0", 0.18f, 0.1f, 0.85f,
				speckles: Hexes("#d8c8b8"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.SandKum, 4f, 0.02f, "#8a7c5a", "#b4a47a", "#d4c8a0", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.5f, "#7a6c4c", "#dcd0a8", 0.12f, 0.05f, 0.4f,
				speckles: Hexes("#6a6a4a", "#4a3a2a"), speckleCount: 660, speckleSize: 0.003f),
			R(Ground.SoilBobakMound, 6f, 0.03f, "#7a6a52", "#a8946e", "#cfbc94", 6, SurfaceMotif.Granular, 34, 0.1f, 0.5f, "#5a4c38", "#dccaa2", 0.12f, 0.1f, 0.75f,
				speckles: Hexes("#1e1a14", "#5a6a3a"), speckleCount: 360, speckleSize: 0.004f),
			R(Ground.GrassDryLogMeadow, 3f, 0.04f, "#3e4426", "#66703e", "#9aa060", 4, SurfaceMotif.Blades, 8, 0.035f, 0.8f, "#4a5a2e", "#a8a868", 0.15f, 0.1f, 0.8f, motifCount: 26000,
				speckles: Hexes("#8a7a3a", "#6a3a5a"), speckleCount: 320, speckleSize: 0.003f),
			R(Ground.GrassDryColdSteppe, 4f, 0.03f, "#4a4434", "#6e6650", "#9a9078", 4, SurfaceMotif.Blades, 8, 0.02f, 0.65f, "#3a3024", "#a89c80", 0.12f, 0.1f, 0.8f, motifCount: 12000,
				speckles: Hexes("#1e1a16", "#3a3024"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.CliffPorphyrySopka, 10f, 0.5f, "#3a2224", "#6a3e3a", "#9a6a60", 4, SurfaceMotif.Fractured, 10, 0.08f, 0.8f, "#1e1416", "#d8c4b0", 0.12f, 0.05f, 0.85f,
				speckles: Hexes("#d8c4b0", "#d87a2a", "#1a1818"), speckleCount: 1700, speckleSize: 0.004f),
			R(Ground.GravelSteppeRiver, 2f, 0.03f, "#4a4232", "#6e6450", "#988c74", 8, SurfaceMotif.Cobbles, 18, 0.12f, 0.8f, "#3a3428", "#a0906a", 0.3f, 0.2f, 0.8f),
			R(Ground.MudBrackishOoze, 4f, 0.03f, "#1e1e1c", "#3a3a34", "#5e5c52", 6, SurfaceMotif.Granular, 24, 0.1f, 0.4f, "#141412", "#8a887c", 0.5f, 0.3f, 0.6f,
				speckles: Hexes("#d8d0c0", "#8a5a3a"), speckleCount: 260, speckleSize: 0.003f),
			R(Ground.PathHordeRoad, 6f, 0.03f, "#4a3a2a", "#7a6448", "#a48c6c", 6, SurfaceMotif.Ridges, 10, 0.03f, 0.6f, "#3a2c1e", "#8e8458", 0.12f, 0.1f, 0.8f,
				speckles: Hexes("#3a2e20", "#b8a888"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.PathSheepTrodSteppe, 3f, 0.015f, "#6a5a44", "#8e7c60", "#b4a080", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#4a3c2c", "#c0ac8a", 0.12f, 0.1f, 0.8f,
				speckles: Hexes("#4a3c2c"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.PathSandTrackKum, 4f, 0.02f, "#a89a72", "#c8b88e", "#e0d4ae", 6, SurfaceMotif.Ridges, 8, 0.03f, 0.5f, "#8a7c58", "#dcd0a8", 0.12f, 0.05f, 0.5f),
			R(Ground.PathTakyrPolish, 8f, 0.01f, "#9a8068", "#c0a888", "#dcc6a6", 6, SurfaceMotif.Ridges, 6, 0.02f, 0.3f, "#8a7058", "#e0caaa", 0.35f, 0.1f, 0.6f),
			R(Ground.BrickSquareHorde, 3f, 0.03f, "#8a6a48", "#b8946a", "#dcc094", 6, SurfaceMotif.Cobbles, 12, 0.04f, 0.85f, "#e8e0cc", "#c8a478", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#6a5038", "#c8b48a"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.SandGreyRiver, 2f, 0.02f, "#8a8070", "#aaa08c", "#ccc2ae", 6, SurfaceMotif.Ripples, 12, 0.1f, 0.5f, "#7a7062", "#d8d0bc", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#b07a6a"), speckleCount: 300, speckleSize: 0.0025f),
			R(Ground.GravelPorphyryQuartz, 2f, 0.03f, "#4a3a38", "#7a6460", "#a8948e", 8, SurfaceMotif.Cobbles, 34, 0.12f, 0.85f, "#6a3e3a", "#ece8e0", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#8a8480"), speckleCount: 200, speckleSize: 0.004f),
			// ── Hills (biome round, 2026-10-26) ── The draft's palettes and grains; the schist crenulated and warped, the cliff's quartz veins vertical.
			R(Ground.GrassHillPasture, 3f, 0.04f, "#34361c", "#5e6232", "#9a965a", 4, SurfaceMotif.Blades, 8, 0.025f, 0.8f, "#4a5028", "#a8a060", 0.15f, 0.1f, 0.8f, motifCount: 30000,
				speckles: Hexes("#8a887a", "#e0e0d4", "#8a5a34", "#b07aa0"), speckleCount: 1630, speckleSize: 0.003f),
			R(Ground.SoilTerraceTilth, 4f, 0.03f, "#3e2e1e", "#6a5034", "#96784e", 8, SurfaceMotif.Granular, 52, 0.1f, 0.5f, "#7a7668", "#d8d8cc", 0.12f, 0.1f, 0.75f,
				speckles: Hexes("#7a7668", "#d8d8cc", "#5a4030", "#6a7458"), speckleCount: 2150, speckleSize: 0.003f),
			R(Ground.GrassDryBurntSlope, 4f, 0.04f, "#4a3e24", "#8a7442", "#c4a868", 4, SurfaceMotif.Blades, 8, 0.035f, 0.8f, "#6a5a30", "#d0b46c", 0.12f, 0.1f, 0.8f, motifCount: 24000,
				speckles: Hexes("#5a6a3a", "#7e7a6c", "#ece4d4"), speckleCount: 980, speckleSize: 0.003f),
			R(Ground.HeathBroomFell, 4f, 0.04f, "#2e2a1c", "#54503a", "#847c5a", 4, SurfaceMotif.Blades, 8, 0.02f, 0.75f, "#3e3a26", "#8a8458", 0.12f, 0.1f, 0.8f, motifCount: 18000,
				speckles: Hexes("#e0b820", "#7a5aa8", "#e8e4dc", "#8a9078"), speckleCount: 850, speckleSize: 0.003f),
			R(Ground.RockSchistShatter, 6f, 0.3f, "#3e3c36", "#6a675e", "#9a968a", 4, SurfaceMotif.Strata, 12, 0.1f, 0.7f, "#565248", "#a8a498", 0.18f, 0.1f, 0.8f, warp: 0.15f,
				speckles: Hexes("#6e2a26", "#8a5a34", "#c8c040", "#e0802a"), speckleCount: 700, speckleSize: 0.004f),
			R(Ground.GravelCrestQuartz, 4f, 0.04f, "#6a665c", "#9a968a", "#d8d6ce", 8, SurfaceMotif.Cobbles, 40, 0.1f, 0.85f, "#7a786e", "#ecebe4", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#f0f0e8"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.GrassFellTurf, 4f, 0.04f, "#3a3a26", "#6a6a48", "#a8a47a", 4, SurfaceMotif.Blades, 8, 0.02f, 0.75f, "#5a5a3a", "#c8c4a0", 0.12f, 0.1f, 0.8f, motifCount: 20000,
				speckles: Hexes("#e0c420", "#d8d4c4"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.SnowHillCap, 4f, 0.03f, "#a8aaa8", "#d0d2d4", "#eef0f2", 4, SurfaceMotif.Ridges, 8, 0.04f, 0.5f, "#6a675e", "#f0f2f4", 0.4f, 0.1f, 0.5f,
				speckles: Hexes("#6a675e"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.CliffSchistCrenulated, 10f, 0.5f, "#3a3832", "#66625a", "#9a968c", 4, SurfaceMotif.Strata, 10, 0.1f, 0.75f, "#e4e2da", "#8a5030", 0.15f, 0.05f, 0.85f, warp: 0.2f, vertical: true,
				speckles: Hexes("#c8c040", "#8a5030"), speckleCount: 600, speckleSize: 0.005f),
			R(Ground.PebblesSchistSlab, 2f, 0.03f, "#3a3a34", "#64625a", "#8e8a7e", 8, SurfaceMotif.Cobbles, 24, 0.12f, 0.85f, "#4e4c44", "#e4e2dc", 0.3f, 0.2f, 0.85f,
				speckles: Hexes("#d8b040", "#e0e0d8"), speckleCount: 140, speckleSize: 0.003f),
			R(Ground.SiltMica, 4f, 0.02f, "#4a4842", "#6e6c64", "#92908a", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#3a3832", "#a8a8a0", 0.35f, 0.2f, 0.6f,
				speckles: Hexes("#e0e0d8", "#5a6044"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.PathRockCart, 4f, 0.03f, "#4a4842", "#76726a", "#a8a498", 8, SurfaceMotif.Ridges, 10, 0.03f, 0.6f, "#5a5850", "#6a6a3a", 0.15f, 0.1f, 0.8f,
				speckles: Hexes("#d8d6cc"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.PathSchistShard, 3f, 0.02f, "#4a4234", "#706656", "#9a8e78", 6, SurfaceMotif.Granular, 30, 0.1f, 0.55f, "#3a362e", "#b0aca0", 0.18f, 0.1f, 0.8f,
				speckles: Hexes("#8a8678", "#b0aca0"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.PathDrove, 6f, 0.03f, "#5a4630", "#8a6a44", "#b08e62", 6, SurfaceMotif.Granular, 36, 0.1f, 0.5f, "#2a221a", "#e8e0d0", 0.12f, 0.1f, 0.8f,
				speckles: Hexes("#2a221a", "#e8e0d0"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.FlagstoneSchistEdge, 3f, 0.04f, "#3a3832", "#605c54", "#8e8a80", 6, SurfaceMotif.Cobbles, 40, 0.04f, 0.85f, "#6a604c", "#a8a296", 0.25f, 0.1f, 0.8f,
				speckles: Hexes("#a8a838"), speckleCount: 200, speckleSize: 0.004f),
			R(Ground.SandMicaSchist, 2f, 0.02f, "#6a665a", "#948e7e", "#bcb6a4", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.5f, "#5a564a", "#c8c2b0", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#e8e6dc", "#d8c070", "#6e2a26"), speckleCount: 1050, speckleSize: 0.0025f),
			R(Ground.GravelSchistShingle, 2f, 0.03f, "#4a4842", "#77746a", "#a8a49a", 8, SurfaceMotif.Cobbles, 44, 0.1f, 0.85f, "#5a584e", "#ecebe4", 0.15f, 0.1f, 0.85f,
				speckles: Hexes("#8a5a34"), speckleCount: 200, speckleSize: 0.004f),

			// ── Wetlands (biome round, 2026-10-26) ── The draft's palettes (dark/mid/light, motif dark/light) and grains; tiles as the draft's.
			// LATER (I9 T1): every ground as Wetlands' own variant of its family (Mud, Limestone, Grass, SaltCrust, Peat, Sand, Silt, Gypsum, Clay,
			// PathMud, Flagstone); the winter reedbed's frost-silvered band (a band of the main ground) paints the same silt.
			R(Ground.MudReedbedSilt, 4f, 0.03f, "#2e3020", "#4a4c34", "#6e6e50", 6, SurfaceMotif.Fibres, 34, 0.06f, 0.55f, "#8a7a52", "#c8b88a", 0.5f, 0.3f, 0.65f,
				speckles: Hexes("#b0a070", "#d8d4c8", "#8a3a22"), speckleCount: 900, speckleSize: 0.003f),
			R(Ground.MarlShellFlat, 4f, 0.02f, "#8a8a7a", "#b8b6a4", "#dcdaca", 6, SurfaceMotif.Granular, 52, 0.04f, 0.4f, "#a8a692", "#f0ece0", 0.35f, 0.1f, 0.55f,
				speckles: Hexes("#f0ece0", "#6a6a60", "#4a4a52", "#7a9a6a"), speckleCount: 1200, speckleSize: 0.003f),
			R(Ground.GrassBuffaloLawn, 3f, 0.03f, "#2e3e1e", "#4e6230", "#78884a", 4, SurfaceMotif.Blades, 8, 0.02f, 0.8f, "#3e5222", "#98aa5a", 0.25f, motifCount: 26000,
				speckles: Hexes("#2a2a1e", "#4a3e2a", "#f4f2ec"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.SaltSabkhaLevee, 6f, 0.02f, "#6a6250", "#9a927c", "#e4e0d6", 6, SurfaceMotif.Cracks, 12, 0.04f, 0.7f, "#4a4436", "#f4f2ec", 0.45f, 0.2f, 0.6f,
				speckles: Hexes("#f4f2ec", "#a03a3a"), speckleCount: 700, speckleSize: 0.004f),
			R(Ground.PeatRaftMat, 3f, 0.05f, "#1e1a12", "#3a3020", "#5a4c34", 6, SurfaceMotif.Fibres, 48, 0.12f, 0.65f, "#6a4a2a", "#c8c0a0", 0.5f, 0.2f, 0.75f,
				speckles: Hexes("#5a7a34", "#3a4a3a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SandDibinSilt, 4f, 0.03f, "#6a5e48", "#9a8c70", "#c8baa0", 6, SurfaceMotif.Ripples, 18, 0.05f, 0.35f, "#7a6e56", "#d8ccb4", 0.15f, 0.1f, 0.5f,
				speckles: Hexes("#c89a6a", "#3a9a9a", "#8a7a4a", "#c8b88a"), speckleCount: 450, speckleSize: 0.003f),
			R(Ground.SiltRiverBank, 6f, 0.06f, "#4a4636", "#6e6a54", "#948e76", 6, SurfaceMotif.Strata, 8, 0.1f, 0.6f, "#3a382a", "#a8a088", 0.4f, 0.15f, 0.8f,
				speckles: Hexes("#6a5a3a", "#1e1c16"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.GypsumAlabaster, 14f, 0.6f, "#6a6a60", "#a8a69a", "#e4e2d8", 4, SurfaceMotif.Strata, 14, 0.08f, 0.6f, "#6a7a6a", "#8a5a4a", 0.3f, 0.1f, 0.8f,
				vertical: true, speckles: Hexes("#f8f6ee", "#6a7a6a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SiltChannelShell, 2f, 0.02f, "#343626", "#545640", "#7a7a5e", 6, SurfaceMotif.Ripples, 10, 0.1f, 0.35f, "#2a2c1e", "#8a8a6a", 0.5f, 0.2f, 0.6f,
				speckles: Hexes("#d8d4c8", "#8a7a52"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.MarlStonewort, 4f, 0.02f, "#6a7a62", "#9aa490", "#c8ccbc", 6, SurfaceMotif.Clumps, 26, 0.3f, 0.5f, "#5a7a52", "#8aa07a", 0.45f, 0.15f, 0.6f,
				speckles: Hexes("#ecebe0"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.ClayDykeCrown, 4f, 0.03f, "#5a5444", "#827a64", "#aaa28a", 6, SurfaceMotif.Cracks, 14, 0.05f, 0.5f, "#44402e", "#8a7a52", 0.3f, 0.15f, 0.7f,
				speckles: Hexes("#c8a870", "#141210", "#2a2a1e"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.PathMudBuffaloTrack, 3f, 0.02f, "#2a2a1e", "#444232", "#62604a", 6, SurfaceMotif.Granular, 30, 0.08f, 0.45f, "#1e1e14", "#8a7a52", 0.55f, 0.25f, 0.6f,
				speckles: Hexes("#3a4a3a", "#8a7a52"), speckleCount: 600, speckleSize: 0.004f),
			R(Ground.PathSaltTrace, 4f, 0.01f, "#8a8472", "#b0aa98", "#e4e0d6", 6, SurfaceMotif.Granular, 30, 0.1f, 0.4f, "#7a7462", "#f4f2ec", 0.3f, 0.1f, 0.5f,
				speckles: Hexes("#9a927c", "#f4f2ec"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.FlagstoneBitumenBrick, 3f, 0.03f, "#8a7450", "#b49a6a", "#d8c294", 6, SurfaceMotif.Cobbles, 4, 0.05f, 0.9f, "#141210", "#c8b07a", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#6a5a3a", "#3a9a9a"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.SandShellHash, 2f, 0.02f, "#6a6450", "#949078", "#bab6a0", 6, SurfaceMotif.Granular, 36, 0.06f, 0.4f, "#8a8670", "#d8d4c8", 0.3f, 0.1f, 0.5f,
				speckles: Hexes("#e0dcd0", "#d8d4c8"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.GravelShellBar, 2f, 0.03f, "#5a5446", "#8a8270", "#c8c2b0", 8, SurfaceMotif.Cobbles, 42, 0.1f, 0.85f, "#3a3a42", "#ecebe0", 0.3f, 0.15f, 0.8f,
				speckles: Hexes("#8a7a5a", "#b88a5a"), speckleCount: 300, speckleSize: 0.004f),

			// ── Mangrove (biome round, 2026-10-26) ── The draft's palettes (dark/mid/light, motif dark/light) and grains; tiles as the draft's.
			// LATER (I9 T1): every ground as Mangrove's own variant of its family (Mud, ForestFloor, SaltCrust, Soil, Sand, Gravel, PathMud);
			// LATER (I9 T7): a CoralRag family with phytokarst pits (Fractured and Clumps stand in); the road and the chokaa floor's motifs.
			R(Ground.MudMkokoBrown, 3f, 0.02f, "#1e1c18", "#3a342a", "#5a5040", 6, SurfaceMotif.Granular, 70, 0.06f, 0.4f, "#0e0c0a", "#7a4a2a", 0.85f, 0.2f, 0.55f,
				speckles: Hexes("#0e0c0a", "#7a4a2a", "#b8a040"), speckleCount: 800, speckleSize: 0.003f),
			R(Ground.MudFiddlerFlat, 4f, 0.02f, "#4a463a", "#6e6852", "#948c70", 6, SurfaceMotif.Clumps, 50, 0.2f, 0.5f, "#2a2620", "#8a826a", 0.6f, 0.2f, 0.6f,
				speckles: Hexes("#8a826a", "#5a6458"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.MudPencilRoot, 3f, 0.02f, "#2e3026", "#4a4c38", "#6a6a4e", 6, SurfaceMotif.Granular, 60, 0.06f, 0.4f, "#6a6a2a", "#a89e88", 0.8f, 0.2f, 0.55f,
				speckles: Hexes("#6a6a2a", "#c8c0a8", "#a89e88"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.ForestFloorMangal, 4f, 0.03f, "#2e2218", "#4e3a26", "#6e5434", 6, SurfaceMotif.Fibres, 30, 0.08f, 0.6f, "#6a3a1e", "#a86a2a", 0.5f, 0.2f, 0.7f,
				speckles: Hexes("#3a2a1e", "#6a4a2a"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SaltUkandaCrust, 6f, 0.02f, "#7a6446", "#a88e66", "#dccfae", 6, SurfaceMotif.Cracks, 22, 0.05f, 0.7f, "#5a4630", "#3e2e1e", 0.4f, 0.2f, 0.6f,
				speckles: Hexes("#f2ece0", "#3e2e1e"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.RagPavement, 5f, 0.08f, "#2e2e2a", "#545248", "#8a8676", 6, SurfaceMotif.Fractured, 10, 0.15f, 0.6f, "#1a1a18", "#d8d0bc", 0.3f, 0.15f, 0.85f,
				speckles: Hexes("#9a9282", "#8a3e22", "#d8d0bc"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.SoilShambaRed, 4f, 0.03f, "#5a2e1c", "#8a4a2c", "#b4744a", 6, SurfaceMotif.Granular, 40, 0.06f, 0.4f, "#6a4a2a", "#8a7040", 0.25f, 0.1f, 0.6f,
				speckles: Hexes("#8a7040", "#6a4a2a", "#5a5852"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.MudCreekBankMangal, 4f, 0.05f, "#262420", "#423c32", "#625a4a", 6, SurfaceMotif.Strata, 10, 0.1f, 0.6f, "#1a1814", "#5a3a24", 0.7f, 0.2f, 0.8f,
				speckles: Hexes("#0e0c0a", "#5a3a24"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.RagCliffNotched, 10f, 0.6f, "#222220", "#48463e", "#7e7a6a", 4, SurfaceMotif.Fractured, 6, 0.12f, 0.6f, "#1a1a16", "#c8c0aa", 0.25f, 0.1f, 0.85f,
				vertical: true, speckles: Hexes("#8a3e22", "#c8c0aa"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.MudCreekBedBlack, 2f, 0.01f, "#141412", "#2a2620", "#423a2e", 6, SurfaceMotif.Granular, 80, 0.05f, 0.4f, "#0e0c0a", "#5a3a1e", 0.9f, 0.15f, 0.5f,
				speckles: Hexes("#5a3a1e", "#4a5a2a", "#8a8270"), speckleCount: 500, speckleSize: 0.004f),
			R(Ground.SandLagoonMuddy, 3f, 0.02f, "#3a3a32", "#5e5c4e", "#848070", 6, SurfaceMotif.Ripples, 4, 0.1f, 0.35f, "#2a2a24", "#6a6a58", 0.8f, 0.2f, 0.55f,
				speckles: Hexes("#6a5a4a", "#4a5a2a"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.LateriteRagRoad, 4f, 0.03f, "#5e3420", "#8a5232", "#b47c52", 6, SurfaceMotif.Granular, 50, 0.08f, 0.4f, "#5a5852", "#c8bca0", 0.35f, 0.15f, 0.6f,
				speckles: Hexes("#5a5852", "#c8bca0"), speckleCount: 600, speckleSize: 0.004f),
			R(Ground.PathMudCreekPath, 3f, 0.02f, "#2e2a22", "#4a4232", "#6a604a", 6, SurfaceMotif.Granular, 40, 0.08f, 0.45f, "#1e1a14", "#6a604a", 0.6f, 0.2f, 0.6f,
				speckles: Hexes("#1e1a14", "#5a4a30"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.PathMudPoleWalk, 3f, 0.03f, "#2a2620", "#6a3a24", "#8a5a3a", 6, SurfaceMotif.Ridges, 6, 0.1f, 0.6f, "#4a2a18", "#a06a44", 0.4f, 0.15f, 0.7f,
				speckles: Hexes("#2a2620", "#6a3a24"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.FlagstoneChokaa, 3f, 0.02f, "#b8ae96", "#dcd4c0", "#f4f0e4", 6, SurfaceMotif.Cracks, 6, 0.04f, 0.5f, "#9a4a2a", "#8a8272", 0.5f, 0.15f, 0.6f,
				speckles: Hexes("#8a8272", "#9a4a2a"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.SandCreekMouth, 2f, 0.02f, "#6a6452", "#948c74", "#c0b69a", 6, SurfaceMotif.Granular, 36, 0.06f, 0.4f, "#8a8264", "#cac2a4", 0.3f, 0.1f, 0.5f,
				speckles: Hexes("#3a2a1e", "#e8e0cc"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.GravelRagShell, 2f, 0.03f, "#2e2c28", "#6a665a", "#b4ac98", 8, SurfaceMotif.Cobbles, 40, 0.1f, 0.85f, "#1a1a18", "#d8d0c0", 0.3f, 0.15f, 0.8f,
				speckles: Hexes("#d8d0c0", "#4a5a2a"), speckleCount: 300, speckleSize: 0.004f),

			// ── Lake and River (biome round, 2026-10-26) ── The draft's palettes (dark/mid/light, motif dark/light) and grains; tiles as the draft's.
			// LATER (I9 T1): every ground as the biome's own variant of its family; the mud Prints motif (River wet margin) is a granular stand-in.
			R(Ground.PebblesStrandShingle, 3f, 0.03f, "#5a5a52", "#8a887c", "#b4b0a2", 8, SurfaceMotif.Cobbles, 46, 0.1f, 0.85f, "#3e3e38", "#c8c4b6", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#6a7a4a", "#8a7a5a"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.SandIsoetidFlat, 3f, 0.02f, "#7a7462", "#a8a28c", "#d0cab4", 6, SurfaceMotif.Granular, 52, 0.03f, 0.4f, "#6a6450", "#c8c2ac", 0.25f, 0.1f, 0.5f,
				speckles: Hexes("#4a6a3a", "#e0dccc"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.LitterGeschwemmsel, 3f, 0.03f, "#3a3024", "#5e4e3a", "#8a785a", 6, SurfaceMotif.Fibres, 34, 0.06f, 0.6f, "#4a3a26", "#a08a5a", 0.2f, 0.1f, 0.7f,
				speckles: Hexes("#2a2016", "#e8e4dc"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.MolasseLedge, 8f, 0.2f, "#4e5448", "#727a6a", "#9aa090", 4, SurfaceMotif.Strata, 14, 0.1f, 0.6f, "#3a4036", "#b0b4a0", 0.1f, 0.05f, 0.8f,
				speckles: Hexes("#4a6a4a", "#c8c2b0"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.GravelPushRampart, 4f, 0.05f, "#4a4a46", "#767470", "#a29e96", 8, SurfaceMotif.Ridges, 6, 0.1f, 0.6f, "#3a3a38", "#b0aca4", 0.1f, 0.1f, 0.8f,
				speckles: Hexes("#9a8a62"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.MolasseTafoniCliff, 12f, 0.5f, "#4a5046", "#6e7666", "#949a8a", 4, SurfaceMotif.Strata, 18, 0.08f, 0.7f, "#2e322a", "#a0a690", 0.1f, 0.05f, 0.8f,
				vertical: true, speckles: Hexes("#3a4034"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.SandDeltaGlacialFlour, 2f, 0.02f, "#6a6a62", "#9a9a90", "#c4c4ba", 6, SurfaceMotif.Ripples, 12, 0.06f, 0.5f, "#5a5a52", "#d0d0c6", 0.3f, 0.1f, 0.5f,
				speckles: Hexes("#b8bcc0"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.LimestoneFurrowShelf, 4f, 0.04f, "#6a7466", "#9aa492", "#c4ccba", 8, SurfaceMotif.Cobbles, 22, 0.1f, 0.8f, "#4a6a5a", "#7a8a6a", 0.5f, 0.1f, 0.8f,
				speckles: Hexes("#2e3a32"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.SiltPondweedHalde, 4f, 0.03f, "#3a4232", "#56604a", "#78806a", 6, SurfaceMotif.Granular, 40, 0.06f, 0.5f, "#2e3a26", "#a8a898", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#a8a898"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.MudVarvedGyttja, 6f, 0.02f, "#141a14", "#222a20", "#343e30", 6, SurfaceMotif.Ridges, 3, 0.05f, 0.4f, "#1a2018", "#3e4836", 0.35f, 0.1f, 0.6f,
				speckles: Hexes("#4a3a26"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.GravelTowpath, 3f, 0.03f, "#5e5c56", "#8a8880", "#b2aea4", 8, SurfaceMotif.Cobbles, 70, 0.05f, 0.6f, "#4a4842", "#6a7a4a", 0.15f, 0.1f, 0.7f,
				speckles: Hexes("#6a7a4a"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.PathStrandTrod, 3f, 0.02f, "#6a6860", "#929084", "#bab6a8", 6, SurfaceMotif.Cobbles, 60, 0.05f, 0.7f, "#a8a28c", "#bab6a8", 0.2f, 0.1f, 0.7f,
				speckles: Hexes("#a8a28c"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.FlagstoneBollenstein, 3f, 0.04f, "#5a5a54", "#8c8a80", "#b8b4a8", 6, SurfaceMotif.Cobbles, 9, 0.08f, 0.9f, "#a8a290", "#4a5a3a", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#4a5a3a"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.SandDeltaFlourBar, 2f, 0.02f, "#7a7a72", "#a8a8a0", "#d0d0c8", 6, SurfaceMotif.Granular, 40, 0.06f, 0.4f, "#6a6a62", "#d0d0c8", 0.2f, 0.1f, 0.5f,
				speckles: Hexes("#e8e8e0", "#3a3a3a"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.GravelErraticBar, 2f, 0.03f, "#4e4c48", "#7a7670", "#a8a29a", 8, SurfaceMotif.Cobbles, 40, 0.1f, 0.85f, "#6a7a62", "#e0dcd4", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#b08a7a", "#6a7a62", "#e0dcd4"), speckleCount: 400, speckleSize: 0.004f),
			R(Ground.MudFloodLoam, 3f, 0.03f, "#4e4636", "#786c54", "#a29478", 6, SurfaceMotif.Cracks, 30, 0.04f, 0.5f, "#3a3428", "#a29478", 0.25f, 0.1f, 0.7f,
				speckles: Hexes("#5a4630", "#c8c4b4"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.SandLeveeDrape, 3f, 0.02f, "#7a6e56", "#a89878", "#d0c2a0", 6, SurfaceMotif.Ripples, 14, 0.05f, 0.4f, "#6a5e48", "#dccfae", 0.2f, 0.1f, 0.5f,
				speckles: Hexes("#4a3a28"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.MudWetMargin, 3f, 0.02f, "#2e2a22", "#4a4436", "#6e6650", 6, SurfaceMotif.Granular, 30, 0.08f, 0.4f, "#241f18", "#6e6650", 0.6f, 0.1f, 0.6f,
				speckles: Hexes("#a8a090"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.GreywackeBankLedge, 6f, 0.3f, "#3a3e38", "#5c625a", "#868a80", 4, SurfaceMotif.Cracks, 50, 0.05f, 0.6f, "#2a2e2a", "#e0dcd0", 0.1f, 0.05f, 0.8f,
				vertical: true, speckles: Hexes("#7a4a2a"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.GravelScourBank, 4f, 0.05f, "#3e3c38", "#6a6660", "#96908a", 8, SurfaceMotif.Cobbles, 28, 0.1f, 0.8f, "#2e2c28", "#a8a29a", 0.1f, 0.1f, 0.8f,
				speckles: Hexes("#8a7658"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.GreywackeNarrowsCliff, 12f, 0.5f, "#2c302c", "#4a4e48", "#72766e", 4, SurfaceMotif.Strata, 22, 0.08f, 0.75f, "#24282a", "#e0dcd0", 0.1f, 0.05f, 0.8f,
				vertical: true, speckles: Hexes("#1e2420"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.GravelArmourRiffle, 3f, 0.04f, "#3a3c34", "#5e6052", "#8a8a78", 8, SurfaceMotif.Cobbles, 18, 0.1f, 0.85f, "#1e2418", "#3a4a2a", 0.4f, 0.1f, 0.8f,
				speckles: Hexes("#1e2418"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.GravelRhineRun, 3f, 0.04f, "#3e3c36", "#6a665a", "#9c9686", 8, SurfaceMotif.Cobbles, 34, 0.1f, 0.85f, "#2a2a2c", "#d8d4c8", 0.4f, 0.1f, 0.8f,
				speckles: Hexes("#d8d4c8", "#2a2a2c", "#8a4a3a", "#5a5e56"), speckleCount: 700, speckleSize: 0.004f),
			R(Ground.SandScourPool, 5f, 0.02f, "#222420", "#3a3c34", "#56584c", 6, SurfaceMotif.Ripples, 6, 0.05f, 0.3f, "#2a2418", "#56584c", 0.5f, 0.1f, 0.6f,
				speckles: Hexes("#2a2418"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.GreywackeReefRibs, 6f, 0.3f, "#2a2e2a", "#464a44", "#6a6e66", 4, SurfaceMotif.Strata, 8, 0.1f, 0.7f, "#1a1e1a", "#6a6e66", 0.5f, 0.1f, 0.8f,
				speckles: Hexes("#1e2418"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.GreywackeFordPaving, 3f, 0.04f, "#3e423a", "#646a60", "#8e9288", 6, SurfaceMotif.Cobbles, 5, 0.06f, 0.9f, "#6a665a", "#3e4a2e", 0.4f, 0.1f, 0.8f,
				speckles: Hexes("#3e4a2e"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.PathBarTrack, 3f, 0.02f, "#5e584a", "#8a826e", "#b4aa92", 6, SurfaceMotif.Granular, 44, 0.08f, 0.5f, "#b8aa88", "#8a826e", 0.2f, 0.1f, 0.6f,
				speckles: Hexes("#b8aa88"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.BasaltRhenishSetts, 3f, 0.03f, "#222224", "#3c3c40", "#626266", 6, SurfaceMotif.Cobbles, 10, 0.06f, 0.9f, "#8a8270", "#626266", 0.4f, 0.1f, 0.8f,
				speckles: Hexes("#8a8270"), speckleCount: 200, speckleSize: 0.003f),
			R(Ground.SandPointBar, 2f, 0.02f, "#8a7a5a", "#b8a680", "#ddd0aa", 6, SurfaceMotif.Ripples, 12, 0.05f, 0.4f, "#7a6a4a", "#ece8dc", 0.2f, 0.1f, 0.5f,
				speckles: Hexes("#ece8dc", "#2e2a26"), speckleCount: 500, speckleSize: 0.003f),
			R(Ground.GravelBleachedBar, 2f, 0.03f, "#6a665e", "#9a948a", "#ccc6ba", 8, SurfaceMotif.Cobbles, 30, 0.1f, 0.85f, "#8a8478", "#e8e4da", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#e8e4da", "#6a5a42"), speckleCount: 300, speckleSize: 0.004f),

			// Rocky Terrain (biome round, 2026-10-26): the shattered high cold desert of the Pamir's own grounds.
			R(Ground.RockHornstoneShatter, 8f, 0.3f, "#2a2830", "#46424a", "#6a6470", 4, SurfaceMotif.Fractured, 12, 0.1f, 0.85f, "#18161c", "#8a5a3a", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#3a3640", "#d8b030", "#d8d8cc"), speckleCount: 900, speckleSize: 0.004f),
			R(Ground.GravelSplinterGrit, 4f, 0.05f, "#2e2c32", "#4e4a52", "#747078", 8, SurfaceMotif.Cobbles, 30, 0.1f, 0.85f, "#1c1a20", "#d8d4cc", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#d8d4cc"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.GrassDrySazTurf, 10f, 0.04f, "#3a3826", "#5a5638", "#7e7a52", 6, SurfaceMotif.Clumps, 24, 0.15f, 0.5f, "#2e2c1c", "#e8e4d8", 0.2f, 0.15f, 0.8f,
				speckles: Hexes("#e8e4d8", "#4a3a24"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.GravelTereskenReg, 8f, 0.05f, "#6a6050", "#8a8068", "#aaa08a", 8, SurfaceMotif.Cobbles, 20, 0.08f, 0.7f, "#3a3630", "#c4b898", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#4e4a52"), speckleCount: 200, speckleSize: 0.004f),
			R(Ground.RockSplitBlockField, 10f, 0.4f, "#1c1a1e", "#3a3438", "#5e5650", 4, SurfaceMotif.Cobbles, 6, 0.2f, 0.9f, "#0e0e10", "#6a4030", 0.15f, 0.05f, 0.9f,
				speckles: Hexes("#d8b030", "#6a4030"), speckleCount: 300, speckleSize: 0.005f),
			R(Ground.SoilFanSilt, 6f, 0.03f, "#5a4a38", "#7a6a52", "#a08c70", 6, SurfaceMotif.Granular, 30, 0.1f, 0.6f, "#4a3c2e", "#b0a08a", 0.2f, 0.1f, 0.7f,
				speckles: Hexes("#6e6a70"), speckleCount: 120, speckleSize: 0.004f),
			R(Ground.ClaySunBaked, 8f, 0.06f, "#6a4a30", "#9a6e48", "#c09a70", 6, SurfaceMotif.Cracks, 6, 0.05f, 0.5f, "#4e4a52", "#c8a478", 0.3f, 0.1f, 0.75f,
				speckles: Hexes("#4e4a52"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.RockRustLeeHornstone, 8f, 0.3f, "#3a2a24", "#6a4a38", "#9a7058", 4, SurfaceMotif.Ripples, 18, 0.1f, 0.6f, "#4a3426", "#b08a60", 0.25f, 0.1f, 0.85f,
				speckles: Hexes("#b08a60"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.RockZapShatter, 8f, 0.3f, "#26262a", "#3e3e44", "#66666e", 4, SurfaceMotif.Fractured, 12, 0.1f, 0.85f, "#16161a", "#9a9aa2", 0.15f, 0.05f, 0.9f,
				speckles: Hexes("#9a9aa2"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.CliffHornfelsSplinter, 14f, 0.7f, "#1e1c22", "#3a363e", "#5e5862", 4, SurfaceMotif.Fractured, 20, 0.1f, 0.6f, "#141216", "#e0dcd4", 0.15f, 0.05f, 0.85f,
				vertical: true, speckles: Hexes("#7a4a2a", "#e0dcd4", "#141216"), speckleCount: 500, speckleSize: 0.005f),
			R(Ground.CliffMarbleLajvard, 14f, 0.6f, "#8a8a8e", "#b8b8bc", "#e0e0e2", 4, SurfaceMotif.Strata, 10, 0.1f, 0.6f, "#2a4a9a", "#e8e8ec", 0.3f, 0.1f, 0.8f,
				warp: 0.2f, vertical: true, speckles: Hexes("#2a4a9a", "#c8a040"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.GravelPanjMilk, 2f, 0.03f, "#5a5a5e", "#7e7e82", "#a8a8aa", 8, SurfaceMotif.Cobbles, 22, 0.12f, 0.85f, "#a8a49a", "#e0dcd8", 0.3f, 0.1f, 0.85f,
				speckles: Hexes("#c8c0b8", "#e0dcd8"), speckleCount: 200, speckleSize: 0.004f),
			R(Ground.SiltDragonMarl, 3f, 0.02f, "#3e4440", "#5e645a", "#868a7c", 6, SurfaceMotif.Granular, 40, 0.1f, 0.5f, "#2e342e", "#6a7a5a", 0.6f, 0.2f, 0.6f,
				speckles: Hexes("#6a7a5a"), speckleCount: 80, speckleSize: 0.004f),
			R(Ground.GravelCaravanTrack, 4f, 0.04f, "#5a5248", "#7a7064", "#a09482", 8, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#4a4238", "#a89c88", 0.4f, 0.1f, 0.8f,
				speckles: Hexes("#4a3a28"), speckleCount: 40, speckleSize: 0.004f),
			R(Ground.PathPackTrail, 3f, 0.03f, "#4a4238", "#6a5e50", "#8e8270", 6, SurfaceMotif.Granular, 26, 0.1f, 0.5f, "#3a342c", "#9a8e7c", 0.5f, 0.1f, 0.75f,
				speckles: Hexes("#4e4a52"), speckleCount: 200, speckleSize: 0.004f),
			R(Ground.FlagstoneFortStreet, 4f, 0.04f, "#3e3c40", "#66646a", "#8e8c92", 6, SurfaceMotif.Cobbles, 12, 0.08f, 0.8f, "#6a5a48", "#e0dcd8", 0.4f, 0.1f, 0.8f),
			R(Ground.SandMicaPamir, 2f, 0.02f, "#7a7670", "#9e9a92", "#c4beb4", 6, SurfaceMotif.Granular, 44, 0.1f, 0.4f, "#6a665e", "#e8e8f0", 0.2f, 0.1f, 0.5f,
				speckles: Hexes("#e8e8f0"), speckleCount: 300, speckleSize: 0.0025f),
			R(Ground.GravelBraid, 2f, 0.03f, "#4a4a4e", "#6e6c70", "#949296", 8, SurfaceMotif.Cobbles, 34, 0.1f, 0.85f, "#3a3a3e", "#a8a6aa", 0.25f, 0.1f, 0.85f),

			// Geyser Basin (biome round, 2026-10-26): the hot ground of the Icelandic interior's own grounds.
			R(Ground.GravelMelurFell, 6f, 0.06f, "#4a4642", "#6e6a62", "#9a948a", 8, SurfaceMotif.Cobbles, 30, 0.1f, 0.85f, "#2e2c2a", "#c49a88", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#d8cbb0", "#101014", "#e4dccb"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.SinterGeyseriteApron, 4f, 0.1f, "#9a968c", "#c8c4b8", "#ecebe4", 6, SurfaceMotif.Ridges, 8, 0.1f, 0.7f, "#8a867c", "#f4f0ea", 0.4f, 0.1f, 0.7f,
				speckles: Hexes("#d8b8b0"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.SinterThermophileMat, 3f, 0.04f, "#5a3a1e", "#c8641e", "#e0b040", 6, SurfaceMotif.Ripples, 12, 0.1f, 0.7f, "#6a7a2a", "#f0e0b0", 0.6f, 0.15f, 0.6f,
				speckles: Hexes("#f0e0b0", "#6a7a2a"), speckleCount: 400, speckleSize: 0.003f),
			R(Ground.ClaySolfataraClay, 4f, 0.06f, "#8a6a52", "#c8a07a", "#e8dccb", 6, SurfaceMotif.Cracks, 18, 0.05f, 0.6f, "#c88a7a", "#f4f0e8", 0.2f, 0.1f, 0.7f,
				speckles: Hexes("#e8d060"), speckleCount: 150, speckleSize: 0.003f),
			R(Ground.MudPotFlat, 3f, 0.05f, "#4e4a46", "#7a7570", "#a8a29a", 6, SurfaceMotif.Clumps, 20, 0.15f, 0.6f, "#3a3632", "#b0aaa0", 0.6f, 0.2f, 0.6f),
			R(Ground.MossWarmHalo, 4f, 0.03f, "#1e3a14", "#3e6a24", "#78a848", 6, SurfaceMotif.Clumps, 26, 0.1f, 0.7f, "#2e5a1a", "#9ac85a", 0.3f, 0.15f, 0.85f,
				speckles: Hexes("#4a8a2a", "#9ac85a"), speckleCount: 600, speckleSize: 0.003f),
			R(Ground.AshTephraSheet, 5f, 0.04f, "#a8a090", "#ccc4b2", "#ece6d6", 6, SurfaceMotif.Granular, 50, 0.1f, 0.5f, "#8a8272", "#f0eadc", 0.15f, 0.1f, 0.6f,
				speckles: Hexes("#6a6660"), speckleCount: 300, speckleSize: 0.004f),
			R(Ground.SnowFonnPatch, 6f, 0.04f, "#b8bcc0", "#dadde0", "#f4f6f8", 4, SurfaceMotif.Ripples, 10, 0.2f, 0.4f, "#8a8a88", "#f8fafc", 0.45f, 0.1f, 0.5f,
				speckles: Hexes("#8a8a88"), speckleCount: 300, speckleSize: 0.003f),
			R(Ground.GravelPaintedScree, 6f, 0.1f, "#5a4640", "#a07868", "#d4c0a8", 8, SurfaceMotif.Cobbles, 24, 0.1f, 0.8f, "#7a8a78", "#d4c0a8", 0.2f, 0.1f, 0.85f,
				speckles: Hexes("#121216"), speckleCount: 150, speckleSize: 0.003f),
			R(Ground.CliffRhyolitePainted, 14f, 0.7f, "#6a5048", "#b08070", "#e0cdb4", 4, SurfaceMotif.Strata, 14, 0.1f, 0.6f, "#7a8a78", "#c8a050", 0.2f, 0.05f, 0.85f,
				warp: 0.3f, vertical: true, speckles: Hexes("#8a8090", "#ece4d8"), speckleCount: 400, speckleSize: 0.005f),
			R(Ground.GravelWarmStream, 2f, 0.03f, "#3a3632", "#6a6058", "#a09484", 8, SurfaceMotif.Cobbles, 28, 0.1f, 0.85f, "#c86a28", "#5a7a2a", 0.35f, 0.1f, 0.8f),
			R(Ground.SinterHotPoolOoze, 4f, 0.02f, "#8aa0a8", "#c8d0cc", "#eaeeea", 6, SurfaceMotif.Granular, 40, 0.1f, 0.4f, "#d89040", "#f4f6f4", 0.6f, 0.2f, 0.6f),
			R(Ground.PathReidgataRuts, 6f, 0.06f, "#4a4440", "#6e6860", "#948c80", 6, SurfaceMotif.Ridges, 12, 0.1f, 0.6f, "#4a5a2e", "#948c80", 0.3f, 0.1f, 0.8f,
				speckles: Hexes("#4a3a28"), speckleCount: 80, speckleSize: 0.004f),
			R(Ground.PathPonyTrod, 3f, 0.03f, "#5a5650", "#7e7870", "#a8a298", 6, SurfaceMotif.Granular, 30, 0.1f, 0.5f, "#4a4640", "#b0a89c", 0.35f, 0.1f, 0.8f),
			R(Ground.FlagstoneHella, 3f, 0.04f, "#2e2e30", "#4e4c4a", "#76726c", 6, SurfaceMotif.Cobbles, 8, 0.06f, 0.8f, "#3e5a24", "#6a665e", 0.4f, 0.1f, 0.8f),
			R(Ground.SandBlackSandur, 2f, 0.02f, "#1e1e20", "#3a3836", "#5e5a56", 6, SurfaceMotif.Granular, 40, 0.1f, 0.4f, "#141416", "#8a867e", 0.2f, 0.1f, 0.5f,
				speckles: Hexes("#e0d8c4", "#101014"), speckleCount: 400, speckleSize: 0.0025f),
			R(Ground.GravelRhyoliteObsidian, 2f, 0.03f, "#2a2826", "#7a6a60", "#cbb8a4", 8, SurfaceMotif.Cobbles, 34, 0.1f, 0.85f, "#121216", "#e4dccb", 0.25f, 0.1f, 0.85f),
		};

		/// <summary>
		/// The path ground a biome's footpaths and trails wear down to, by the ground it is painted with: what lies under its
		/// cover, packed. A grassland's is loam, a forest's a littered path, a bog's mud, a desert's packed sand, a
		/// snowfield's trampled snow, rock's grit. A biome's own Small Path layer, when it has one, wins over this.
		/// </summary>
		public static string PathGroundFor(string ground)
		{
			switch (ground)
			{
				case Ground.GrassDry:
				case Ground.Soil:
				case Ground.Dust:
				case Ground.Lichen:
					return Ground.PathDryEarth;
				case Ground.Clay:
				case Ground.CrackedEarth:
				case Ground.Sandstone:
				case Ground.Tholin:
					return Ground.PathRedEarth;
				case Ground.ForestFloor:
				case Ground.NeedleLitter:
					return Ground.PathLitter;
				case Ground.JungleFloor:
				case Ground.Mud:
				case Ground.Peat:
				case Ground.Sphagnum:
				case Ground.Silt:
					return Ground.PathMud;
				case Ground.Sand:
				case Ground.SandBeach:
				case Ground.TholinDune:
				case Ground.Coral:
					return Ground.PathSand;
				case Ground.Gravel:
				case Ground.Pebbles:
				case Ground.Reg:
				case Ground.Rock:
				case Ground.CliffRock:
				case Ground.Limestone:
				case Ground.Basalt:
				case Ground.Pahoehoe:
				case Ground.Regolith:
				case Ground.Lineae:
				case Ground.MetalFrost:
				case Ground.Flagstone:
					return Ground.PathGrit;
				case Ground.Snow:
				case Ground.Frost:
				case Ground.Ice:
					return Ground.PathSnow;
				case Ground.Ash:
				case Ground.Cinder:
				case Ground.Lava:
				case Ground.Sulphur:
					return Ground.PathAsh;
				case Ground.SaltCrust:
				case Ground.Sinter:
					return Ground.PathSalt;
				default:
					// Grass, meadow, moss, heath, and anything unknown.
					return Ground.PathLoam;
			}
		}

		/// <summary>
		/// The ground a biome's roads are made of: gravel, except where the country makes its roads of what it has: packed
		/// sand in the desert, packed snow on the ice, ash on the volcano, salt on the pan. A biome's own Road layer wins.
		/// </summary>
		public static string RoadGroundFor(string ground)
		{
			switch (PathGroundFor(ground))
			{
				case Ground.PathSand: return Ground.PathSand;
				case Ground.PathSnow: return Ground.PathSnow;
				case Ground.PathAsh: return Ground.PathAsh;
				case Ground.PathSalt: return Ground.PathSalt;
				default: return Ground.Gravel;
			}
		}

		// ── What each ground is made of (2026-10-15) ─────────────────

		/// <summary>Wholly mineral: rock, sand, soil, ash (takes a thin-aired world's own ground colour).</summary>
		private static readonly Vector3 Mineral = new Vector3(0f, 0f, 1f);
		/// <summary>Snow, ice and frost: mineral, but keeping most of its own white (a frost on a rust world is still white).</summary>
		private static readonly Vector3 Frozen = new Vector3(0f, 0f, (1f - 0f) * 0.3f);

		/// <summary>Living to <paramref name="share"/>, the rest mineral (dead leaves and peat keep some of their colour).</summary>
		private static Vector3 Living(float share) => new Vector3(share, 0f, (1f - share) * 1f);

		/// <summary>
		/// What each generated ground is made of, as the planet's looks see it (x living, y lichen, z mineral): a property of the
		/// surface, written by the biome art authoring onto every template slot holding it (TerrainTextureLayer.GroundMakeup)
		/// and baked per terrain array layer from there. Until 2026-10-15 the runtime read it from the layer's NAME
		/// (BiomeLooks.GroundShareOf); every value here is what that reading gave the ground's layer, Ground_&lt;Name&gt;, so
		/// nothing changed (BiomeLooksTests pins it).
		/// </summary>
		private static readonly Dictionary<string, Vector3> Makeup = new Dictionary<string, Vector3>
		{
			{ Ground.Grass, Living(1f) },
			{ Ground.GrassDry, Living(0.55f) },
			{ Ground.GrassMeadow, Living(1f) },
			{ Ground.Moss, Living(1f) },
			{ Ground.ForestFloor, Living(0.4f) },
			{ Ground.NeedleLitter, Living(0.3f) },
			{ Ground.JungleFloor, Living(0.7f) },
			{ Ground.Soil, Mineral },
			{ Ground.Mud, Mineral },
			{ Ground.Clay, Mineral },
			{ Ground.Sandstone, Mineral },
			{ Ground.Sand, Mineral },
			{ Ground.SandBeach, Mineral },
			{ Ground.Gravel, Mineral },
			{ Ground.Pebbles, Mineral },
			{ Ground.Rock, Mineral },
			{ Ground.CliffRock, Mineral },
			{ Ground.Basalt, Mineral },
			// Lava glows its own colour: nothing of the planet's leaves, lichen or ground.
			{ Ground.Lava, Vector3.zero },
			{ Ground.Ash, Mineral },
			{ Ground.Snow, Frozen },
			{ Ground.Ice, Frozen },
			{ Ground.SaltCrust, Mineral },
			{ Ground.Regolith, Mineral },
			{ Ground.Sulphur, Mineral },
			{ Ground.Peat, Living(0.3f) },
			{ Ground.Limestone, Mineral },
			{ Ground.Lichen, new Vector3(0f, 1f, 0f) },
			{ Ground.Silt, Mineral },
			{ Ground.Coral, new Vector3(0f, 0.6f, 0.4f) },
			{ Ground.Tholin, Mineral },
			{ Ground.Frost, Frozen },
			{ Ground.CrackedEarth, Mineral },
			{ Ground.Flagstone, Mineral },
			{ Ground.Sphagnum, Living(1f) },
			{ Ground.Heath, Living(0.85f) },
			{ Ground.Sinter, Mineral },
			{ Ground.Cinder, Mineral },
			{ Ground.Pahoehoe, Vector3.zero },
			{ Ground.Reg, Mineral },
			{ Ground.Dust, Mineral },
			{ Ground.Lineae, Mineral },
			{ Ground.MetalFrost, Frozen },
			{ Ground.TholinDune, Mineral },
			{ Ground.PathLoam, Mineral },
			{ Ground.PathDryEarth, Mineral },
			{ Ground.PathRedEarth, Mineral },
			{ Ground.PathSand, Mineral },
			{ Ground.PathGrit, Mineral },
			{ Ground.PathSnow, Frozen },
			{ Ground.PathMud, Mineral },
			{ Ground.PathAsh, Mineral },
			{ Ground.PathSalt, Mineral },
			{ Ground.PathLitter, Living(0.25f) },
			// Jungle + Bamboo Forest (biome rounds, 2026-10-26).
			{ Ground.JungleFloorBronze, Living(0.7f) },
			{ Ground.SoilRedForest, Mineral },
			{ Ground.MossRavine, Living(1f) },
			{ Ground.MudRaffiaSwamp, Mineral },
			{ Ground.LateriteIronstone, Mineral },
			{ Ground.GraniteWhaleback, Mineral },
			{ Ground.CliffGneissGorge, Mineral },
			{ Ground.SandQuartzStream, Mineral },
			{ Ground.MudLeafSilt, Mineral },
			{ Ground.LateriteAvenue, Mineral },
			{ Ground.PathRootTrail, Mineral },
			{ Ground.PotsherdPavement, Mineral },
			{ Ground.PathFrondMat, Mineral },
			{ Ground.SandMicaGold, Mineral },
			{ Ground.GravelPisolite, Mineral },
			{ Ground.ForestFloorBambooGrove, Living(0.4f) },
			{ Ground.MossSugigoke, Living(1f) },
			{ Ground.NeedleLitterSugi, Living(0.3f) },
			{ Ground.SoilKuroboku, Mineral },
			{ Ground.RockAndesiteTalus, Mineral },
			{ Ground.ForestFloorLaurel, Living(0.4f) },
			{ Ground.CliffAndesitePlated, Mineral },
			{ Ground.GravelAndesiteBed, Mineral },
			{ Ground.SiltKurobokuPond, Mineral },
			{ Ground.RoadMasa, Mineral },
			{ Ground.PathBambooLeaf, Mineral },
			{ Ground.FlagstoneIshidatami, Mineral },
			{ Ground.PathTobiishi, Mineral },
			{ Ground.PathIshidan, Mineral },
			{ Ground.SandMasaGrus, Mineral },
			{ Ground.GravelArc, Mineral },
			// Forest (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.ForestFloorBeech, Living(0.4f) },
			{ Ground.MossCushion, Living(1f) },
			{ Ground.ForestFloorRamsons, Living(0.4f) },
			{ Ground.NeedleLitterFir, Living(0.3f) },
			{ Ground.SoilRootHumus, Mineral },
			{ Ground.SandstoneForest, Mineral },
			{ Ground.SandstoneForestFace, Mineral },
			{ Ground.MudLeafMould, Mineral },
			{ Ground.GravelBrook, Mineral },
			{ Ground.PathBeechLitter, Living(0.25f) },
			{ Ground.PathHolloway, Mineral },
			{ Ground.FlagstoneSandstone, Mineral },
			{ Ground.SandHumic, Mineral },
			{ Ground.GravelSandstoneFlint, Mineral },
			// Woodland (biome round, 2026-10-26).
			{ Ground.GrassWoodPasture, Living(1f) },
			{ Ground.ForestFloorCopse, Living(0.4f) },
			{ Ground.ForestFloorBluebell, Living(0.4f) },
			{ Ground.GrassDryBracken, Living(0.55f) },
			{ Ground.GrassMeadowGlade, Living(1f) },
			{ Ground.SoilRendzina, Mineral },
			{ Ground.ChalkWoodland, Mineral },
			{ Ground.ChalkFace, Mineral },
			{ Ground.GravelChalkStream, Mineral },
			{ Ground.MudMarl, Mineral },
			{ Ground.PathChalkTrack, Mineral },
			{ Ground.PathGreenLane, Mineral },
			{ Ground.FlagstoneFlint, Mineral },
			{ Ground.SandChalky, Mineral },
			{ Ground.GravelFlint, Mineral },
			// Coastal Water and Ocean (biome rounds, 2026-10-26): sea floors, mineral (no grass grows on them) but for the drowned peat
			// and the coral grounds, which take what their names read as (the runtime reading this table replaced).
			{ Ground.SandShellGravel, Mineral },
			{ Ground.DoleriteKelpReef, Mineral },
			{ Ground.Maerl, Mineral },
			{ Ground.CobbleTideSwept, Mineral },
			{ Ground.SiltBrittlestar, Mineral },
			{ Ground.SandHoneycomb, Mineral },
			{ Ground.PeatLyonesse, Living(0.3f) },
			{ Ground.CoralBarren, new Vector3(0f, 0.6f, 0.4f) },
			{ Ground.DoleriteSeaCliff, Mineral },
			{ Ground.SandSurfSorted, Mineral },
			{ Ground.GravelSlateQuartz, Mineral },
			{ Ground.FlagstoneSarn, Mineral },
			{ Ground.RockSkerry, Mineral },
			{ Ground.FlagstonePave, Mineral },
			{ Ground.SandMaerlBar, Mineral },
			{ Ground.GravelKillas, Mineral },
			{ Ground.MudNephrops, Mineral },
			{ Ground.SandShelfEdge, Mineral },
			{ Ground.CoralRubble, new Vector3(0f, 0.6f, 0.4f) },
			{ Ground.MudPockmark, Mineral },
			{ Ground.SpiculeMat, Mineral },
			{ Ground.GravelDiamicton, Mineral },
			{ Ground.SandstoneIgueldo, Mineral },
			{ Ground.MudstoneScarp, Mineral },
			{ Ground.GravelGullyChip, Mineral },
			{ Ground.MudShelfRim, Mineral },
			{ Ground.FlagstoneGaltzada, Mineral },
			{ Ground.PebblesCoralGrit, new Vector3(0f, 0.6f, 0.4f) },
			{ Ground.PebblesArriBiribil, Mineral },
			{ Ground.SandCoralGrit, new Vector3(0f, 0.6f, 0.4f) },
			{ Ground.GravelSandstoneChip, Mineral },
			// Tundra (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.LichenTundraMat, new Vector3(0f, 1f, 0f) },
			{ Ground.MossPolygonMire, Living(1f) },
			{ Ground.GravelSortedCircles, Mineral },
			{ Ground.GrassTussockTundra, Living(1f) },
			{ Ground.RockFellField, Mineral },
			{ Ground.SnowWatermelon, Frozen },
			{ Ground.RockGneissOutcrop, Mineral },
			{ Ground.GravelHeaveScree, Mineral },
			{ Ground.CliffGneissBanded, Mineral },
			{ Ground.GravelOutwashBraided, Mineral },
			{ Ground.SiltThermokarst, Mineral },
			{ Ground.PathDroveRoad, Mineral },
			{ Ground.PathTundraTrod, Mineral },
			{ Ground.FlagstoneGneissSplit, Mineral },
			{ Ground.PathSlabCauseway, Mineral },
			{ Ground.SandGlacialFlour, Mineral },
			{ Ground.GravelOutwashBark, Mineral },
			// Ice Sheet (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.SnowKatabatic, Frozen },
			{ Ground.IceLayeredDeposit, Frozen },
			{ Ground.IceWindGlaze, Frozen },
			{ Ground.FrostSwissCheese, Frozen },
			{ Ground.IceBlueField, Frozen },
			{ Ground.RockCharnockiteFell, Mineral },
			{ Ground.RockCharnockiteDust, Mineral },
			{ Ground.FrostSurfaceHoar, Frozen },
			{ Ground.GravelBlueIceMoraine, Frozen },
			{ Ground.IceSteppedEscarpment, Frozen },
			{ Ground.IceSpiralScarp, Frozen },
			{ Ground.IceTidewater, Frozen },
			{ Ground.CliffCharnockite, Mineral },
			{ Ground.CliffRedDust, Mineral },
			{ Ground.IceMeltRunnel, Frozen },
			{ Ground.IceBubbleStack, Frozen },
			{ Ground.PathIceRoad, Frozen },
			{ Ground.PathSnowHorseTrace, Frozen },
			{ Ground.SnowBlockYard, Frozen },
			{ Ground.SandCharnockiteGrus, Mineral },
			{ Ground.GravelNunatakShingle, Mineral },
			// Glacier (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.IceAblation, Frozen },
			{ Ground.SnowFirnRunnels, Frozen },
			{ Ground.IceIcefall, Frozen },
			{ Ground.RockGreenstonePolished, Mineral },
			{ Ground.GravelTill, Mineral },
			{ Ground.GravelDebrisTongue, Mineral },
			{ Ground.SiltForefield, Mineral },
			{ Ground.CliffGreenstone, Mineral },
			{ Ground.GravelGreenBoulderBed, Mineral },
			{ Ground.SiltVarvedClay, Mineral },
			{ Ground.PathPitchedTrail, Mineral },
			{ Ground.PathMoraineTrod, Mineral },
			{ Ground.FlagstoneTempleSlab, Mineral },
			{ Ground.SandPlacerFlour, Mineral },
			{ Ground.GravelBraidedShingle, Mineral },
			{ Ground.PathRubbleTrod, Mineral },
			{ Ground.IceDustMantled, Frozen },
			{ Ground.FrostDustRimed, Frozen },
			// Alpine Meadow (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.GrassMeadowPolonyna, Living(1f) },
			{ Ground.GrassFlowerHay, Living(1f) },
			{ Ground.HeathMugo, Living(0.85f) },
			{ Ground.RockFlyschRibs, Mineral },
			{ Ground.SnowLatePatch, Frozen },
			{ Ground.GrassDryMeltTurf, Living(0.55f) },
			{ Ground.MossSpringFlush, Living(1f) },
			{ Ground.LimestoneKarren, Mineral },
			{ Ground.CliffFlyschRib, Mineral },
			{ Ground.PebblesFlyschSlab, Mineral },
			{ Ground.SiltTarn, Mineral },
			{ Ground.PathSledgeTrack, Mineral },
			{ Ground.PathSheepTrod, Mineral },
			{ Ground.FlagstoneFlyschCrazy, Mineral },
			{ Ground.SandFlysch, Mineral },
			{ Ground.GravelFlyschShingle, Mineral },
			{ Ground.PathKnieholzCut, Mineral },
			{ Ground.PathKarrenSteps, Mineral },
			// Mountain Slope (biome round, 2026-10-26).
			{ Ground.ForestFloorCloudHumus, Living(0.4f) },
			{ Ground.MossBlanket, Living(1f) },
			{ Ground.GrassMeadowKikuyu, Living(1f) },
			{ Ground.SoilNitisol, Mineral },
			{ Ground.HeathEricaMoor, Living(0.85f) },
			{ Ground.RockPhonoliteLedges, Mineral },
			{ Ground.ForestFloorCoffee, Living(0.4f) },
			{ Ground.CliffPhonolite, Mineral },
			{ Ground.PebblesRedSilted, Mineral },
			{ Ground.MudRedClay, Mineral },
			{ Ground.PathMuleGully, Mineral },
			{ Ground.PathRootStair, Mineral },
			{ Ground.FlagstoneSlabStair, Mineral },
			{ Ground.SandPhonolite, Mineral },
			{ Ground.GravelRedBlack, Mineral },
			{ Ground.PathBundleCauseway, Mineral },
			{ Ground.PathPhonoliteSteps, Mineral },
			// Scree (biome round, 2026-10-26).
			{ Ground.GravelFineScree, Mineral },
			{ Ground.RockOpenworkTalus, Mineral },
			{ Ground.RockStoneRiver, Mineral },
			{ Ground.GravelRockGlacierSkin, Mineral },
			{ Ground.SoilFellNets, Mineral },
			{ Ground.SnowPatchScree, Frozen },
			{ Ground.SoilConeToe, Mineral },
			{ Ground.RockBreathingTalus, Mineral },
			{ Ground.GravelWedgeSpoil, Mineral },
			{ Ground.GravelRustScree, Mineral },
			{ Ground.RegolithVacuumTalus, Mineral },
			{ Ground.CliffGranodiorite, Mineral },
			{ Ground.GravelStepPool, Mineral },
			{ Ground.SiltRampartTarn, Mineral },
			{ Ground.PathFortressCauseway, Mineral },
			{ Ground.PathTalusTrace, Mineral },
			{ Ground.FlagstoneWedge, Mineral },
			{ Ground.SandGrus, Mineral },
			{ Ground.GravelTorrentShingle, Mineral },
			{ Ground.PathSteppingBlocks, Mineral },
			// Scrubland (biome round, 2026-10-26).
			{ Ground.SoilBajadaLoam, Mineral },
			{ Ground.SiltCreosoteFlat, Mineral },
			{ Ground.CalicheBench, Mineral },
			{ Ground.GravelGrusBajada, Mineral },
			{ Ground.BasaltMalpais, Mineral },
			{ Ground.LitterMesquiteBosque, Living(0.25f) },
			{ Ground.CliffMylonite, Mineral },
			{ Ground.SandWashArkose, Mineral },
			{ Ground.ClayTank, Mineral },
			{ Ground.PathCanalBank, Mineral },
			{ Ground.PathClearedTrail, Mineral },
			{ Ground.FlagstoneCalicheSetts, Mineral },
			{ Ground.PathMalpaisTrail, Mineral },
			{ Ground.SandWashBar, Mineral },
			{ Ground.GravelWashCobble, Mineral },
			// Savanna (biome round, 2026-10-26).
			{ Ground.GrassDryTawnyVeld, Living(0.55f) },
			{ Ground.SandGraniteSandveld, Mineral },
			{ Ground.ClayMopane, Mineral },
			{ Ground.SoilGreenstoneRed, Mineral },
			{ Ground.RockDwalaSlab, Mineral },
			{ Ground.MudVleiTurf, Mineral },
			{ Ground.CliffMatopo, Mineral },
			{ Ground.SandRiverGruss, Mineral },
			{ Ground.MudPanWallow, Mineral },
			{ Ground.PathCattleRoad, Mineral },
			{ Ground.PathSingleFile, Mineral },
			{ Ground.FlagstoneDagaFloor, Mineral },
			{ Ground.PathGameTrail, Mineral },
			{ Ground.PathSlabSteps, Mineral },
			{ Ground.SandPinkGruss, Mineral },
			{ Ground.GravelReefIronstone, Mineral },
			// Grassland (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.GrassPusztaSward, Living(1f) },
			{ Ground.GrassDryGoldbeard, Living(0.55f) },
			{ Ground.SoilChernozem, Mineral },
			{ Ground.SandPuszta, Mineral },
			{ Ground.SaltCrustSzik, Mineral },
			{ Ground.LoessBank, Mineral },
			{ Ground.LoessWall, Mineral },
			{ Ground.CliffLoessBluff, Mineral },
			{ Ground.SiltTisza, Mineral },
			{ Ground.MudSodaOoze, Mineral },
			{ Ground.PathBlackEarthTrack, Mineral },
			{ Ground.PathHerdTrail, Mineral },
			{ Ground.PathSandTrackPuszta, Mineral },
			{ Ground.BrickHerringbone, Mineral },
			{ Ground.SandMicaRiver, Mineral },
			{ Ground.GravelConcretionGrit, Mineral },
			// Desert (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf); the frost-glazed erg keeps its white.
			{ Ground.SandErg, Mineral },
			{ Ground.SandSlipFace, Mineral },
			{ Ground.RegPavement, Mineral },
			{ Ground.GravelGassi, Mineral },
			{ Ground.SandstoneVarnished, Mineral },
			{ Ground.ClayPaleolakeMarl, Mineral },
			{ Ground.SandRedSheet, Mineral },
			{ Ground.CliffNubian, Mineral },
			{ Ground.GravelWadi, Mineral },
			{ Ground.ClayGueltaOoze, Mineral },
			{ Ground.PathCaravanTrack, Mineral },
			{ Ground.PathCamelPad, Mineral },
			{ Ground.FlagstoneSlabPaving, Mineral },
			{ Ground.SandWadiFlood, Mineral },
			{ Ground.GravelWadiBar, Mineral },
			{ Ground.PathRegTrack, Mineral },
			{ Ground.SandFrostErg, Frozen },
			// Badlands (biome round, 2026-10-26); the snow on the ash keeps its white.
			{ Ground.AshLapilliSteppe, Mineral },
			{ Ground.TuffRilled, Mineral },
			{ Ground.ClayWashFloor, Mineral },
			{ Ground.SoilAshLoam, Mineral },
			{ Ground.GravelLapilliDrift, Mineral },
			{ Ground.BasaltCaprock, Mineral },
			{ Ground.TuffRedValley, Mineral },
			{ Ground.CliffIgnimbrite, Mineral },
			{ Ground.SandLapilliStream, Mineral },
			{ Ground.ClayTuffOoze, Mineral },
			{ Ground.PathRutCut, Mineral },
			{ Ground.PathDonkeyAsh, Mineral },
			{ Ground.FlagstoneTuffSetts, Mineral },
			{ Ground.SandLapilliBar, Mineral },
			{ Ground.GravelVolcanicBar, Mineral },
			{ Ground.PathTuffSteps, Mineral },
			{ Ground.PathWashTrace, Mineral },
			{ Ground.AshSnowOn, Frozen },
			{ Ground.CrackedEarthSaltBaked, Mineral },
			// Salt Flat (biome round, 2026-10-26).
			{ Ground.SaltAmoleCrust, Mineral },
			{ Ground.SaltEtchedCrust, Mineral },
			{ Ground.MudBrineEdge, Mineral },
			{ Ground.ClayKhakiPan, Mineral },
			{ Ground.RegAfarBasalt, Mineral },
			{ Ground.SandSaltSoot, Mineral },
			{ Ground.SaltSpringCrust, Mineral },
			{ Ground.SaltAlkaliPlaya, Mineral },
			{ Ground.CliffSaltrock, Mineral },
			{ Ground.GravelWadiBasalt, Mineral },
			{ Ground.SaltBrineLakeFloor, Mineral },
			{ Ground.PathBarRoad, Mineral },
			{ Ground.PathCuttersTrace, Mineral },
			{ Ground.FlagstoneBasaltSetts, Mineral },
			{ Ground.SandSaltPepper, Mineral },
			{ Ground.GravelCrustedPebbles, Mineral },
			{ Ground.PathCrustCauseway, Mineral },
			{ Ground.PathBasaltTrack, Mineral },
			// Plains (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.GrassDryMixedPrairie, Living(0.55f) },
			{ Ground.GrassShortgrassSod, Living(1f) },
			{ Ground.GrassMeadowTallgrass, Living(1f) },
			{ Ground.SandSandhills, Mineral },
			{ Ground.SoilBuffaloWallow, Mineral },
			{ Ground.ClayPlayaFloor, Mineral },
			{ Ground.RedbedGypSlope, Mineral },
			{ Ground.RedbedRimrockTalus, Mineral },
			{ Ground.CliffRedbedGypsum, Mineral },
			{ Ground.SandPlatteBraided, Mineral },
			{ Ground.ClayOxbowOoze, Mineral },
			{ Ground.PathTravoisTrace, Mineral },
			{ Ground.PathBisonTrail, Mineral },
			{ Ground.PathSandTrace, Mineral },
			{ Ground.PathRedTrail, Mineral },
			{ Ground.FlagstoneRedbed, Mineral },
			{ Ground.SandFeldsparBar, Mineral },
			{ Ground.GravelOgallala, Mineral },
			{ Ground.SnowCrustSward, Frozen },
			{ Ground.CrackedDustPlain, Mineral },
			// Estuary (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.MudSchlickFlat, Mineral },
			{ Ground.SandSandwatt, Mineral },
			{ Ground.MudZosteraFlat, Mineral },
			{ Ground.MudPioneerSalting, Mineral },
			{ Ground.GrassLowMarsh, Living(1f) },
			{ Ground.GrassHighMarsh, Living(1f) },
			{ Ground.SaltPanneCrust, Mineral },
			{ Ground.SiltCreekBank, Mineral },
			{ Ground.TillKliffFace, Mineral },
			{ Ground.MudFluidSulphide, Mineral },
			{ Ground.MudSubtidal, Mineral },
			{ Ground.ClayKleiweg, Mineral },
			{ Ground.PathMudSheepTrack, Mineral },
			{ Ground.PathMudWattweg, Mineral },
			{ Ground.FlagstoneMoppen, Mineral },
			{ Ground.SandShoalCockle, Mineral },
			{ Ground.GravelSchill, Mineral },
			// Coral Reef, Beach and Abyss (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.SandAtoll, Mineral },
			{ Ground.ReefFramework, Mineral },
			{ Ground.RubbleBranchRods, Mineral },
			{ Ground.SandLagoon, Mineral },
			{ Ground.ReefFlatPavement, Mineral },
			{ Ground.ReefBleached, Mineral },
			{ Ground.SandForeReef, Mineral },
			{ Ground.CliffDropOff, Mineral },
			{ Ground.SandLagoonPowder, Mineral },
			{ Ground.GravelReefGrit, Mineral },
			{ Ground.FlagstoneHirigaa, Mineral },
			{ Ground.PathSweptSand, Mineral },
			{ Ground.FlagstoneRagSett, Mineral },
			{ Ground.SandForamBar, Mineral },
			{ Ground.GravelCayShingle, Mineral },
			{ Ground.SandSinging, Mineral },
			{ Ground.SandSwash, Mineral },
			{ Ground.LitterWrackAmber, Living(0.25f) },
			{ Ground.LichenGreyDune, new Vector3(0f, 1f, 0f) },
			{ Ground.SandWarmBackdune, Mineral },
			{ Ground.SandSlipTongues, Mineral },
			{ Ground.BeachrockPavement, Mineral },
			{ Ground.CliffBlueEarth, Mineral },
			{ Ground.SandBeachStream, Mineral },
			{ Ground.SandSwashBar, Mineral },
			{ Ground.SandTrough, Mineral },
			{ Ground.SiltOffshore, Mineral },
			{ Ground.GravelShellRoad, Mineral },
			{ Ground.PathDuneTrod, Mineral },
			{ Ground.PathBrushwoodMat, Mineral },
			{ Ground.FlagstoneErraticCobbles, Mineral },
			{ Ground.SandGarnetBar, Mineral },
			{ Ground.GravelFlintShingle, Mineral },
			{ Ground.SiltHadalOoze, Mineral },
			{ Ground.PeridotiteTrenchWall, Mineral },
			{ Ground.BasaltPillowTalus, Mineral },
			{ Ground.MudSeepSediment, Mineral },
			{ Ground.ChemohermSeepPavement, Mineral },
			{ Ground.AsphaltFlow, Mineral },
			{ Ground.HydrateCrust, Mineral },
			{ Ground.CliffSerpentineScarp, Mineral },
			{ Ground.SandTurbiditeChannel, Mineral },
			{ Ground.SiltOozeRim, Mineral },
			{ Ground.FlagstoneCyclopeanCauseway, Mineral },
			{ Ground.FlagstoneProcessional, Mineral },
			{ Ground.PebblesPillowGlass, Mineral },
			{ Ground.SandHyaloclastite, Mineral },
			{ Ground.GravelPeridotiteBar, Mineral },
			// Steppe (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.GrassDryKovylSward, Living(0.55f) },
			{ Ground.SoilChestnut, Mineral },
			{ Ground.GrassDryWormwoodSteppe, Living(0.55f) },
			{ Ground.ClayTakyr, Mineral },
			{ Ground.SaltCrustSor, Mineral },
			{ Ground.GravelPorphyryGrit, Mineral },
			{ Ground.SandKum, Mineral },
			{ Ground.SoilBobakMound, Mineral },
			{ Ground.GrassDryLogMeadow, Living(0.55f) },
			{ Ground.GrassDryColdSteppe, Living(0.55f) },
			{ Ground.CliffPorphyrySopka, Mineral },
			{ Ground.GravelSteppeRiver, Mineral },
			{ Ground.MudBrackishOoze, Mineral },
			{ Ground.PathHordeRoad, Mineral },
			{ Ground.PathSheepTrodSteppe, Mineral },
			{ Ground.PathSandTrackKum, Mineral },
			{ Ground.PathTakyrPolish, Mineral },
			{ Ground.BrickSquareHorde, Mineral },
			{ Ground.SandGreyRiver, Mineral },
			{ Ground.GravelPorphyryQuartz, Mineral },
			// Hills (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.GrassHillPasture, Living(1f) },
			{ Ground.SoilTerraceTilth, Mineral },
			{ Ground.GrassDryBurntSlope, Living(0.55f) },
			{ Ground.HeathBroomFell, Living(0.85f) },
			{ Ground.RockSchistShatter, Mineral },
			{ Ground.GravelCrestQuartz, Mineral },
			{ Ground.GrassFellTurf, Living(1f) },
			{ Ground.SnowHillCap, Frozen },
			{ Ground.CliffSchistCrenulated, Mineral },
			{ Ground.PebblesSchistSlab, Mineral },
			{ Ground.SiltMica, Mineral },
			{ Ground.PathRockCart, Mineral },
			{ Ground.PathSchistShard, Mineral },
			{ Ground.PathDrove, Mineral },
			{ Ground.FlagstoneSchistEdge, Mineral },
			{ Ground.SandMicaSchist, Mineral },
			{ Ground.GravelSchistShingle, Mineral },
			// Wetlands (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.MudReedbedSilt, Mineral },
			{ Ground.MarlShellFlat, Mineral },
			{ Ground.GrassBuffaloLawn, Living(1f) },
			{ Ground.SaltSabkhaLevee, Mineral },
			{ Ground.PeatRaftMat, Living(0.3f) },
			{ Ground.SandDibinSilt, Mineral },
			{ Ground.SiltRiverBank, Mineral },
			{ Ground.GypsumAlabaster, Mineral },
			{ Ground.SiltChannelShell, Mineral },
			{ Ground.MarlStonewort, Mineral },
			{ Ground.ClayDykeCrown, Mineral },
			{ Ground.PathMudBuffaloTrack, Mineral },
			{ Ground.PathSaltTrace, Mineral },
			{ Ground.FlagstoneBitumenBrick, Mineral },
			{ Ground.SandShellHash, Mineral },
			{ Ground.GravelShellBar, Mineral },
			// Mangrove (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.MudMkokoBrown, Mineral },
			{ Ground.MudFiddlerFlat, Mineral },
			{ Ground.MudPencilRoot, Mineral },
			{ Ground.ForestFloorMangal, Living(0.4f) },
			{ Ground.SaltUkandaCrust, Mineral },
			{ Ground.RagPavement, Mineral },
			{ Ground.SoilShambaRed, Mineral },
			{ Ground.MudCreekBankMangal, Mineral },
			{ Ground.RagCliffNotched, Mineral },
			{ Ground.MudCreekBedBlack, Mineral },
			{ Ground.SandLagoonMuddy, Mineral },
			{ Ground.LateriteRagRoad, Mineral },
			{ Ground.PathMudCreekPath, Mineral },
			{ Ground.PathMudPoleWalk, Mineral },
			{ Ground.FlagstoneChokaa, Mineral },
			{ Ground.SandCreekMouth, Mineral },
			{ Ground.GravelRagShell, Mineral },
			// Lake and River (biome round, 2026-10-26): what each ground's name gives (BiomeLooks.GroundShareOf).
			{ Ground.PebblesStrandShingle, Mineral },
			{ Ground.SandIsoetidFlat, Mineral },
			{ Ground.LitterGeschwemmsel, Living(0.25f) },
			{ Ground.MolasseLedge, Mineral },
			{ Ground.GravelPushRampart, Mineral },
			{ Ground.MolasseTafoniCliff, Mineral },
			{ Ground.SandDeltaGlacialFlour, Mineral },
			{ Ground.LimestoneFurrowShelf, Mineral },
			{ Ground.SiltPondweedHalde, Mineral },
			{ Ground.MudVarvedGyttja, Mineral },
			{ Ground.GravelTowpath, Mineral },
			{ Ground.PathStrandTrod, Mineral },
			{ Ground.FlagstoneBollenstein, Mineral },
			{ Ground.SandDeltaFlourBar, Mineral },
			{ Ground.GravelErraticBar, Mineral },
			{ Ground.MudFloodLoam, Mineral },
			{ Ground.SandLeveeDrape, Mineral },
			{ Ground.MudWetMargin, Mineral },
			{ Ground.GreywackeBankLedge, Mineral },
			{ Ground.GravelScourBank, Mineral },
			{ Ground.GreywackeNarrowsCliff, Mineral },
			{ Ground.GravelArmourRiffle, Mineral },
			{ Ground.GravelRhineRun, Mineral },
			{ Ground.SandScourPool, Mineral },
			{ Ground.GreywackeReefRibs, Mineral },
			{ Ground.GreywackeFordPaving, Mineral },
			{ Ground.PathBarTrack, Mineral },
			{ Ground.BasaltRhenishSetts, Mineral },
			{ Ground.SandPointBar, Mineral },
			{ Ground.GravelBleachedBar, Mineral },
			// Rocky Terrain (biome round, 2026-10-26): the shattered high cold desert of the Pamir's own grounds.
			{ Ground.RockHornstoneShatter, Mineral },
			{ Ground.GravelSplinterGrit, Mineral },
			{ Ground.GrassDrySazTurf, Living(0.55f) },
			{ Ground.GravelTereskenReg, Mineral },
			{ Ground.RockSplitBlockField, Mineral },
			{ Ground.SoilFanSilt, Mineral },
			{ Ground.ClaySunBaked, Mineral },
			{ Ground.RockRustLeeHornstone, Mineral },
			{ Ground.RockZapShatter, Mineral },
			{ Ground.CliffHornfelsSplinter, Mineral },
			{ Ground.CliffMarbleLajvard, Mineral },
			{ Ground.GravelPanjMilk, Mineral },
			{ Ground.SiltDragonMarl, Mineral },
			{ Ground.GravelCaravanTrack, Mineral },
			{ Ground.PathPackTrail, Mineral },
			{ Ground.FlagstoneFortStreet, Mineral },
			{ Ground.SandMicaPamir, Mineral },
			{ Ground.GravelBraid, Mineral },
			// Geyser Basin (biome round, 2026-10-26): the hot ground of the Icelandic interior's own grounds.
			{ Ground.GravelMelurFell, Mineral },
			{ Ground.SinterGeyseriteApron, Mineral },
			{ Ground.SinterThermophileMat, Mineral },
			{ Ground.ClaySolfataraClay, Mineral },
			{ Ground.MudPotFlat, Mineral },
			{ Ground.MossWarmHalo, Living(1f) },
			{ Ground.AshTephraSheet, Mineral },
			{ Ground.SnowFonnPatch, Frozen },
			{ Ground.GravelPaintedScree, Mineral },
			{ Ground.CliffRhyolitePainted, Mineral },
			{ Ground.GravelWarmStream, Mineral },
			{ Ground.SinterHotPoolOoze, Mineral },
			{ Ground.PathReidgataRuts, Mineral },
			{ Ground.PathPonyTrod, Mineral },
			{ Ground.FlagstoneHella, Mineral },
			{ Ground.SandBlackSandur, Mineral },
			{ Ground.GravelRhyoliteObsidian, Mineral },
		};

		/// <summary>What a generated ground is made of (<see cref="Makeup"/>); false for a name that is no generated ground.</summary>
		public static bool TryMakeupOf(string ground, out Vector3 makeup) => Makeup.TryGetValue(ground ?? string.Empty, out makeup);

		/// <summary>Every ground with a makeup, for tests.</summary>
		public static IEnumerable<string> GroundsWithMakeup => Makeup.Keys;

		/// <summary>Every bark family. Bark tiles are one metre: a trunk's UVs run in metres round and along it.</summary>
		public static readonly SurfaceRecipe[] BarkRecipes =
		{
			R(Bark.Brown, 1f, 0.02f, "#2e2218", "#4a3828", "#6a5440", 4, SurfaceMotif.Fibres, 24, 0.1f, 0.8f, "#20180f", "#5a4632", 0.1f, vertical: true),
			R(Bark.Pine, 1f, 0.025f, "#4a2e1c", "#6e4428", "#8e5c38", 4, SurfaceMotif.Cracks, 10, 0.05f, 0.8f, "#24160c", "#3a2414", 0.1f),
			R(Bark.Birch, 1f, 0.01f, "#c8c4b8", "#e0ddd2", "#f2f0e8", 4, SurfaceMotif.Fibres, 32, 0.1f, 0.5f, "#2a2622", "#4a4640", 0.2f,
				speckles: Hexes("#2a2622", "#3a3630"), speckleCount: 500, speckleSize: 0.004f),
			R(Bark.Palm, 1f, 0.02f, "#5a4a34", "#7a664a", "#9a8664", 4, SurfaceMotif.Strata, 12, 0.1f, 0.7f, "#4a3a26", "#a89470", 0.1f),
			R(Bark.Cactus, 1f, 0.015f, "#2e5a2a", "#3e7238", "#5a8a48", 4, SurfaceMotif.Fibres, 16, 0.1f, 0.7f, "#24481f", "#6a9a58", 0.35f, vertical: true,
				speckles: Hexes("#e8e0c0"), speckleCount: 800, speckleSize: 0.0025f),
			R(Bark.Bamboo, 1f, 0.01f, "#4a7a2a", "#6a9a3a", "#8ab45a", 4, SurfaceMotif.Strata, 4, 0.1f, 0.5f, "#3a6020", "#a0c070", 0.5f),
			R(Bark.Dead, 1f, 0.02f, "#5a564e", "#7a766c", "#9a968a", 4, SurfaceMotif.Fibres, 24, 0.1f, 0.8f, "#3a3630", "#aaa69a", 0.1f, vertical: true),
			// ── The vegetation expansion (2026-10-10) ──
			// Smooth: a beech's silver-grey skin, near flat, with faint wrinkles running round the stem and blotches
			// of pale and greenish lichen; worn by kapok, baobab and dragon tree too.
			R(Bark.Smooth, 1f, 0.006f, "#6a6a64", "#8e8e86", "#aeada4", 4, SurfaceMotif.Fibres, 20, 0.1f, 0.3f, "#5e5e58", "#b8b6ac", 0.25f,
				speckles: Hexes("#9aa088", "#c8c8b8", "#5a5c50"), speckleCount: 300, speckleSize: 0.006f),
			// Aspen: smooth, chalky green-white, with black lenticel bands running round the stem and black scars.
			R(Bark.Aspen, 1f, 0.008f, "#b4bca4", "#d0d6c0", "#e6e8da", 4, SurfaceMotif.Fibres, 20, 0.1f, 0.4f, "#2e2e26", "#5a5a4c", 0.2f,
				speckles: Hexes("#1e1e1a", "#3a3a30"), speckleCount: 260, speckleSize: 0.007f),
			// Olive: grey-brown, deeply and irregularly furrowed up the stem, a few pale lichen flecks.
			R(Bark.Olive, 1f, 0.03f, "#4a4438", "#6e6656", "#948a78", 4, SurfaceMotif.Fibres, 18, 0.1f, 0.85f, "#2a261e", "#8a826e", 0.1f, vertical: true,
				speckles: Hexes("#a8a890"), speckleCount: 120, speckleSize: 0.005f),
			// Date palm: the stubs of old frond bases, a hand across, packed in a diamond lattice up the stem.
			R(Bark.DatePalm, 1f, 0.04f, "#3e3224", "#6e5c44", "#8e7a5e", 4, SurfaceMotif.Cobbles, 8, 0.12f, 0.85f, "#3a2e20", "#a08a68", 0.1f, 0.1f, 0.85f),
			// Fibrous: shaggy reddish-brown bark in long, fine, peeling strips.
			R(Bark.Fibrous, 1f, 0.03f, "#3a2a20", "#5e4634", "#80644c", 4, SurfaceMotif.Fibres, 40, 0.1f, 0.85f, "#24180f", "#9a7a5a", 0.08f, vertical: true),
			// ── Biome rounds (2026-10-26) ── The stone pine's scaly plates with resin streaks; the yellowwood's and the kosso's peeling
			// plates (grey and red); the giant heather's moss sleeve.
			R(Bark.CembraPlate, 1f, 0.025f, "#4a3e34", "#6a5a4c", "#8a7a6a", 4, SurfaceMotif.Cracks, 12, 0.05f, 0.75f, "#2a221c", "#a89880", 0.12f,
				speckles: Hexes("#c8b890", "#5a4a3e"), speckleCount: 200, speckleSize: 0.004f),
			R(Bark.Flaky, 1f, 0.025f, "#4a4236", "#6a5e50", "#a89a86", 4, SurfaceMotif.Cobbles, 10, 0.08f, 0.7f, "#3a3228", "#b8aa96", 0.1f),
			R(Bark.FlakyRed, 1f, 0.025f, "#4a2a1c", "#7a4a32", "#a87a5a", 4, SurfaceMotif.Cobbles, 10, 0.08f, 0.7f, "#3a2014", "#b8886a", 0.1f),
			R(Bark.MossSleeve, 1f, 0.03f, "#2a3218", "#3e4a26", "#6a7a3a", 4, SurfaceMotif.Clumps, 20, 0.4f, 0.7f, "#2e3a1e", "#7a8a44", 0.15f, vertical: true,
				speckles: Hexes("#c4ccb0", "#5a4a30"), speckleCount: 300, speckleSize: 0.005f),
			// Scrubland + Savanna (biome rounds, 2026-10-26): the palo verde's smooth green skin, the ironwood's grey shreds, the
			// elephant tree's paper over copper; the fever tree's lime powder, the leadwood's small grey plates.
			R(Bark.GreenBark, 1f, 0.006f, "#6a7a38", "#8a9a48", "#a8b45a", 4, SurfaceMotif.Fibres, 20, 0.1f, 0.3f, "#7a7a62", "#b8c068", 0.3f,
				speckles: Hexes("#8a8a7a", "#6a6a5a"), speckleCount: 200, speckleSize: 0.008f),
			R(Bark.ShredGrey, 1f, 0.03f, "#2a2622", "#5a5650", "#8e8a82", 4, SurfaceMotif.Fibres, 44, 0.1f, 0.85f, "#1a1614", "#a8a49c", 0.08f, vertical: true),
			R(Bark.PaperPeel, 1f, 0.015f, "#8a3a22", "#b0603a", "#d8d0c4", 4, SurfaceMotif.Cracks, 14, 0.06f, 0.7f, "#c8c0b4", "#ece8e0", 0.2f,
				speckles: Hexes("#e8e4dc", "#a04a2a"), speckleCount: 400, speckleSize: 0.008f),
			R(Bark.Powder, 1f, 0.005f, "#a8a840", "#c8c860", "#dcdc80", 4, SurfaceMotif.Fibres, 16, 0.1f, 0.25f, "#8a8a34", "#e4e49a", 0.35f,
				speckles: Hexes("#6a6a2a", "#e8e8b0"), speckleCount: 260, speckleSize: 0.006f),
			R(Bark.SnakeSkin, 1f, 0.025f, "#4a4640", "#8a867c", "#b4b0a6", 4, SurfaceMotif.Cobbles, 30, 0.06f, 0.85f, "#3a3630", "#c0bcb2", 0.1f, 0.1f, 0.85f),
			// Grassland (biome round, 2026-10-26).
			R(Bark.Checker, 1f, 0.02f, "#2a2622", "#3a3430", "#5a524a", 4, SurfaceMotif.Cracks, 18, 0.05f, 0.8f, "#1a1614", "#6a625a", 0.1f),
			R(Bark.Cerris, 1f, 0.03f, "#2a2420", "#3a3028", "#5a4c40", 4, SurfaceMotif.Fibres, 22, 0.1f, 0.85f, "#b05a2a", "#6a5a4c", 0.08f, vertical: true),
			R(Bark.WhitePoplar, 1f, 0.01f, "#9a9a8e", "#c8c8bc", "#e0e0d6", 4, SurfaceMotif.Fibres, 28, 0.1f, 0.5f, "#2a2622", "#4a4640", 0.2f,
				speckles: Hexes("#1e1c1a", "#3a3632"), speckleCount: 300, speckleSize: 0.012f),
			// Plains (biome round, 2026-10-26).
			R(Bark.CottonwoodRidge, 1f, 0.035f, "#4a4640", "#5a5650", "#8a867c", 4, SurfaceMotif.Fibres, 20, 0.1f, 0.85f, "#2a2622", "#9a968c", 0.08f, vertical: true),
			R(Bark.CorkRidge, 1f, 0.04f, "#3a342c", "#4a443a", "#7a7262", 4, SurfaceMotif.Fibres, 16, 0.12f, 0.85f, "#241e18", "#8a826e", 0.08f, vertical: true),
			R(Bark.OsageStrip, 1f, 0.03f, "#6a4222", "#8a5a2a", "#c07a3a", 4, SurfaceMotif.Fibres, 36, 0.1f, 0.85f, "#3a2414", "#d08a48", 0.08f, vertical: true),
			// Steppe (biome round, 2026-10-26).
			R(Bark.Karagach, 1f, 0.035f, "#2a221c", "#3a3028", "#5a4c40", 4, SurfaceMotif.Fibres, 26, 0.1f, 0.85f, "#1a1410", "#6a5c4e", 0.08f, vertical: true),
			R(Bark.SaxaulTwist, 1f, 0.03f, "#4a3c2e", "#5a4a3a", "#8a7a64", 4, SurfaceMotif.Fibres, 18, 0.1f, 0.8f, "#2a2018", "#9a8a74", 0.1f, warp: 0.25f, vertical: true),
			// Desert + Badlands (biome rounds, 2026-10-26): the desert date's fissures with green in the cracks, the atil's pale smooth
			// skin; the apricot's lenticels and gum, the oleaster's shaggy strips, the plane's flaking camouflage.
			R(Bark.BalanitesFurrow, 1f, 0.03f, "#3a2e24", "#5a4a3a", "#7a6a56", 4, SurfaceMotif.Fibres, 22, 0.1f, 0.85f, "#6a7a4a", "#8a7a64", 0.08f, vertical: true),
			R(Bark.AtilPale, 1f, 0.008f, "#a8a090", "#c8c0b0", "#dcd6c8", 4, SurfaceMotif.Fibres, 20, 0.1f, 0.3f, "#5a4a3a", "#e4dece", 0.25f,
				speckles: Hexes("#3a2e24", "#6a5a4a"), speckleCount: 160, speckleSize: 0.01f),
			R(Bark.ApricotScale, 1f, 0.015f, "#4a2a1e", "#6a3e2a", "#8a5a3e", 4, SurfaceMotif.Cracks, 14, 0.04f, 0.6f, "#2e1a12", "#9a6a4a", 0.2f,
				speckles: Hexes("#c88a3a", "#c8b8a0"), speckleCount: 300, speckleSize: 0.005f),
			R(Bark.OleasterStrip, 1f, 0.03f, "#4a3e30", "#6a5a48", "#9a8a72", 4, SurfaceMotif.Fibres, 34, 0.1f, 0.85f, "#2e261e", "#a89880", 0.08f, vertical: true),
			R(Bark.PlaneFlake, 1f, 0.012f, "#6a6a5a", "#8a8a6a", "#e0d8c0", 4, SurfaceMotif.Cobbles, 12, 0.1f, 0.75f, "#5a5a4a", "#ece4cc", 0.2f),
			// Hills (biome round, 2026-10-26).
			R(Bark.Cork, 1f, 0.05f, "#6a6458", "#8a8274", "#b0a898", 4, SurfaceMotif.Cracks, 10, 0.06f, 0.85f, "#4a443a", "#c0b8a8", 0.06f),
			R(Bark.PinePlate, 1f, 0.03f, "#5a4a40", "#a8502a", "#c87a4a", 4, SurfaceMotif.Cracks, 12, 0.05f, 0.8f, "#3a2e28", "#d08a5a", 0.1f),
		};

		/// <summary>The ground recipe with this name, or false.</summary>
		public static bool TryGround(string name, out SurfaceRecipe recipe) => TryFind(GroundRecipes, name, out recipe);

		/// <summary>The bark recipe with this name, or false.</summary>
		public static bool TryBark(string name, out SurfaceRecipe recipe) => TryFind(BarkRecipes, name, out recipe);

		private static bool TryFind(SurfaceRecipe[] recipes, string name, out SurfaceRecipe recipe)
		{
			for (int i = 0; i < recipes.Length; i++)
			{
				if (recipes[i].Name == name)
				{
					recipe = recipes[i];
					return true;
				}
			}
			recipe = default;
			return false;
		}

		/// <summary>Every ground family's name, in catalogue order.</summary>
		public static IEnumerable<string> GroundNames()
		{
			foreach (SurfaceRecipe r in GroundRecipes)
			{
				yield return r.Name;
			}
		}
	}
}
#endif
