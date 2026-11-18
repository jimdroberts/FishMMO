#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The vines (<see cref="VineMeshes"/>): their species, the sizes they are built at, the trees they climb, names and paths.</summary>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>The vine atlas's file name (<see cref="VineAtlas"/>); imported as the foliage atlas is (ProceduralArtTextureImport).</summary>
		public const string VineAtlasName = "Vine_Atlas";

		public static string VineAtlasPath => $"{TexturesFolder}/{VineAtlasName}.png";

		/// <summary>Every vine mesh, material and prefab name starts with this.</summary>
		public const string VinePrefix = "Vine_";

		/// <summary>The sub-folder of the generated prefabs every vine prefab is written to.</summary>
		public const string VinePrefabFolderName = "Vines";

		public const string VinePrefabsFolder = PrefabsFolder + "/" + VinePrefabFolderName;

		/// <summary>
		/// Every vine species: one for each climate a vine is part of the look of (2026-10-12: "temperate ivy, jungle
		/// lianas, autumn creeper; none in deserts/ice"); which biomes grow which is <see cref="VineBiomes"/>.
		/// </summary>
		/// <remarks>
		/// <list type="bullet">
		/// <item><b>Ivy</b> (common ivy, <i>Hedera helix</i>): the evergreen of old walls, ruins, cliffs and oak trunks across
		/// temperate and oceanic lands; dark glossy lobed leaves 5–10 cm, pale-veined; fine stems clinging by rootlets, hanging
		/// in curtains from any edge it reaches over.</item>
		/// <item><b>Creeper</b> (Virginia creeper, <i>Parthenocissus quinquefolia</i>, standing for Boston ivy too): deciduous,
		/// five toothed leaflets, mid green in summer and a deep crimson in autumn (its own autumn colour), bare in winter.</item>
		/// <item><b>Liana</b> (the jungle's woody climbers, with the heart-leaved philodendrons and pothos that clothe its
		/// trunks): rope-thick stems hung in loops from the canopy and the temple walls, aerial roots, leaves 15–30 cm.</item>
		/// </list>
		/// </remarks>
		public static readonly VineSpecies[] Vines =
		{
			new VineSpecies { Name = "Ivy", Leaf = VineLeaf.Ivy, LeafA = H("#1f3a1a"), LeafB = H("#36562a"), Stem = H("#5a4a3a"),
				LeafScale = 0.32f, MassScale = 0.46f, StrandsPerMetre = 6.5f, Autumn = Color.white },
			new VineSpecies { Name = "Creeper", Leaf = VineLeaf.Palmate, LeafA = H("#3c6226"), LeafB = H("#587c30"), Stem = H("#6a4a3a"),
				LeafScale = 0.36f, MassScale = 0.5f, StrandsPerMetre = 5f, Deciduous = true, Autumn = new Color(2.1f, 0.42f, 0.24f, 1f) },
			new VineSpecies { Name = "Liana", Leaf = VineLeaf.Heart, LeafA = H("#2a5020"), LeafB = H("#4a742c"), Stem = H("#6a5a44"),
				LeafScale = 0.46f, MassScale = 0.62f, StrandsPerMetre = 2.4f, Woody = true, Autumn = Color.white },
			// ── Forest (biome round, 2026-10-26) ── Honeysuckle (Lonicera periclymenum): twining, heart-ish leaves, deciduous.
			// LATER (I9b): its cream-pink flowers, and the HangingRoots root curtains (a strand-only leaf).
			new VineSpecies { Name = "Honeysuckle", Leaf = VineLeaf.Heart, LeafA = H("#3a6228"), LeafB = H("#50782e"), Stem = H("#7a5a44"),
				LeafScale = 0.24f, MassScale = 0.4f, StrandsPerMetre = 5f, Deciduous = true, Autumn = new Color(1.4f, 1.1f, 0.5f, 1f) },
			// ── Woodland (biome round, 2026-10-26) ── Traveller's joy (old man's beard) sheeting the chalk and hedges, hop, black
			// bryony. LATER (I9b): traveller's joy's fluffy seed-head curtains, the hop's pale cones, bryony's red berry chains.
			new VineSpecies { Name = "TravellersJoy", Leaf = VineLeaf.Palmate, LeafA = H("#4a6a2a"), LeafB = H("#6a8a3a"), Stem = H("#7a6a52"),
				LeafScale = 0.3f, MassScale = 0.5f, StrandsPerMetre = 4f, Deciduous = true, Autumn = new Color(1.5f, 1.45f, 1.3f, 1f) },
			new VineSpecies { Name = "Hop", Leaf = VineLeaf.Palmate, LeafA = H("#3e6a26"), LeafB = H("#5a8030"), Stem = H("#5a6a3a"),
				LeafScale = 0.36f, MassScale = 0.46f, StrandsPerMetre = 4f, Deciduous = true, Autumn = new Color(1.3f, 1.2f, 0.6f, 1f) },
			new VineSpecies { Name = "BlackBryony", Leaf = VineLeaf.Heart, LeafA = H("#2e5a22"), LeafB = H("#3e6a2a"), Stem = H("#4a5a2a"),
				LeafScale = 0.26f, MassScale = 0.4f, StrandsPerMetre = 4f, Deciduous = true, Autumn = new Color(1.6f, 1.3f, 0.4f, 1f) },
			// ── Alpine Meadow (biome round, 2026-10-26) ── Houseleek mats on the old walls and crags (the lightning charm of every
			// roof). LATER (I9b): a rosette-mat leaf (grey-green rosettes with red tips, pink stars on short stalks); small dense
			// palmate leaves in its colours stand in.
			new VineSpecies { Name = "Houseleek", Leaf = VineLeaf.Palmate, LeafA = H("#6a8a5a"), LeafB = H("#a04a4a"), Stem = H("#5a6a4a"),
				LeafScale = 0.2f, MassScale = 0.3f, StrandsPerMetre = 5f, Autumn = Color.white },
			// ── Mountain Slope (biome round, 2026-10-26) ── Moss sleeves on trunks, limbs and walls (LATER (I9b): a moss-mat leaf; tiny ivy
			// leaves in moss greens stand in), and the woody highland jasmine with its white stars (LATER (I9b): its flowers).
			new VineSpecies { Name = "MossSleeve", Leaf = VineLeaf.Ivy, LeafA = H("#3e6a1e"), LeafB = H("#6a9a2a"), Stem = H("#4a5a2a"),
				LeafScale = 0.3f, MassScale = 0.36f, StrandsPerMetre = 3.5f, Autumn = Color.white },
			new VineSpecies { Name = "AbyssinianJasmine", Leaf = VineLeaf.Heart, LeafA = H("#2e5224"), LeafB = H("#46682e"), Stem = H("#6a5a40"),
				LeafScale = 0.34f, MassScale = 0.42f, StrandsPerMetre = 2.6f, Woody = true, Autumn = Color.white },
			// ── Jungle (biome round, 2026-10-26) ── Rattan whips draping the crowns (LATER (I9b): hooked whip tips) and staghorn-and-orchid
			// epiphytes clinging to trunks and limbs (LATER (I9b): plate-fern and spray leaves; small ivy leaves in fern greens stand in).
			new VineSpecies { Name = "RattanWhip", Leaf = VineLeaf.Heart, LeafA = H("#3a5a2a"), LeafB = H("#5a7a34"), Stem = H("#5a6a3a"),
				LeafScale = 0.2f, MassScale = 0.3f, StrandsPerMetre = 2.4f, Woody = true, Autumn = Color.white },
			new VineSpecies { Name = "Epiphytes", Leaf = VineLeaf.Ivy, LeafA = H("#4a7a3a"), LeafB = H("#e8d8a0"), Stem = H("#5a4a3a"),
				LeafScale = 0.3f, MassScale = 0.34f, StrandsPerMetre = 3f, Autumn = Color.white },
			// ── Bamboo Forest (biome round, 2026-10-26) ── Wisteria (fuji): woody twining rope stems, lilac racemes in May (LATER (I9b):
			// the hanging racemes), and kudzu (kuzu) smothering the edges in huge three-lobed leaves.
			new VineSpecies { Name = "Fuji", Leaf = VineLeaf.Heart, LeafA = H("#4a7a34"), LeafB = H("#a888d8"), Stem = H("#6a5a4a"),
				LeafScale = 0.26f, MassScale = 0.4f, StrandsPerMetre = 3.5f, Woody = true, Deciduous = true, Autumn = new Color(1.5f, 1.3f, 0.5f, 1f) },
			new VineSpecies { Name = "Kuzu", Leaf = VineLeaf.Palmate, LeafA = H("#3a6a2a"), LeafB = H("#5a8a34"), Stem = H("#6a5a3a"),
				LeafScale = 0.42f, MassScale = 0.54f, StrandsPerMetre = 3f, Deciduous = true, Autumn = new Color(1.4f, 1.2f, 0.5f, 1f) },
			// ── Scrubland (biome round, 2026-10-26) ── Desert mistletoe (Phoradendron californicum): leafless reddish stem clumps in the
			// legume trees' crowns, pink-red berries. LATER (I9b): a clump habit of its own; a small-leaved reddish climber stands in.
			new VineSpecies { Name = "DesertMistletoe", Leaf = VineLeaf.Palmate, LeafA = H("#8a5a4a"), LeafB = H("#a06a54"), Stem = H("#7a4a3e"),
				LeafScale = 0.12f, MassScale = 0.3f, StrandsPerMetre = 3f, Autumn = Color.white },
			// ── Savanna (biome round, 2026-10-26) ── The rock fig's pale grey root lattice splitting granite and wall courses under small
			// leaf tufts, and the flame lily scrambling through the thickets. LATER (I9b): the fig's root-lattice habit and the flame lily's
			// red-and-yellow flowers on the climber.
			new VineSpecies { Name = "RockFigRoots", Leaf = VineLeaf.Heart, LeafA = H("#2e4a22"), LeafB = H("#3e5a2a"), Stem = H("#b0aaa0"),
				LeafScale = 0.28f, MassScale = 0.42f, StrandsPerMetre = 2.6f, Woody = true, Autumn = Color.white },
			new VineSpecies { Name = "Gloriosa", Leaf = VineLeaf.Heart, LeafA = H("#3a5a2a"), LeafB = H("#5a7a30"), Stem = H("#5a6a3a"),
				LeafScale = 0.2f, MassScale = 0.36f, StrandsPerMetre = 3.5f, Deciduous = true, Autumn = new Color(1.6f, 1.2f, 0.5f, 1f) },
			// ── Grassland (biome round, 2026-10-26) ── Wild grape (Vitis sylvestris) on the poplars, ruins and cellar fronts, scarlet in
			// autumn; field bindweed twining low on the fences and walls. LATER (I9b): the grape's black clusters and bindweed's trumpets.
			new VineSpecies { Name = "WildGrape", Leaf = VineLeaf.Palmate, LeafA = H("#3e6a2a"), LeafB = H("#6a8a3a"), Stem = H("#6a5a44"),
				LeafScale = 0.4f, MassScale = 0.5f, StrandsPerMetre = 3.5f, Woody = true, Deciduous = true, Autumn = new Color(2f, 0.5f, 0.3f, 1f) },
			new VineSpecies { Name = "BindweedField", Leaf = VineLeaf.Heart, LeafA = H("#4a7a34"), LeafB = H("#5a8a3e"), Stem = H("#5a7a3a"),
				LeafScale = 0.18f, MassScale = 0.3f, StrandsPerMetre = 5f, Deciduous = true, Autumn = new Color(1.3f, 1.2f, 0.6f, 1f) },
			// ── Plains (biome round, 2026-10-26) ── American bittersweet in the galleries, wild cucumber's lacy sprays, groundnut twining
			// low. LATER (I9b): bittersweet's orange-and-scarlet capsules, the cucumber's white sprays and balloon fruit, groundnut's maroon clusters.
			new VineSpecies { Name = "Bittersweet", Leaf = VineLeaf.Heart, LeafA = H("#3e6a2a"), LeafB = H("#4e7a32"), Stem = H("#6a5a44"),
				LeafScale = 0.26f, MassScale = 0.42f, StrandsPerMetre = 3.5f, Woody = true, Deciduous = true, Autumn = new Color(1.8f, 1.2f, 0.4f, 1f) },
			new VineSpecies { Name = "WildCucumber", Leaf = VineLeaf.Palmate, LeafA = H("#4a7a3a"), LeafB = H("#5a8a44"), Stem = H("#5a7a3a"),
				LeafScale = 0.3f, MassScale = 0.4f, StrandsPerMetre = 4f, Deciduous = true, Autumn = new Color(1.4f, 1.3f, 0.6f, 1f) },
			new VineSpecies { Name = "Groundnut", Leaf = VineLeaf.Heart, LeafA = H("#4a6a34"), LeafB = H("#5a7a3e"), Stem = H("#5a6a3a"),
				LeafScale = 0.2f, MassScale = 0.32f, StrandsPerMetre = 4.5f, Deciduous = true, Autumn = new Color(1.3f, 1.2f, 0.6f, 1f) },
			// ── Estuary (biome round, 2026-10-26) ── Woody nightshade (Solanum dulcamara): scrambling stems, purple star flowers, glossy red
			// berries. LATER (I9b): its flowers and berry clusters.
			new VineSpecies { Name = "Nightshade", Leaf = VineLeaf.Heart, LeafA = H("#3a5a2a"), LeafB = H("#4e7034"), Stem = H("#5a4a3a"),
				LeafScale = 0.24f, MassScale = 0.34f, StrandsPerMetre = 3f, Deciduous = true, Autumn = new Color(1.3f, 1.2f, 0.6f, 1f) },
			// ── Beach (biome round, 2026-10-26) ── Sea bindweed (Calystegia soldanella) scrambling over the dunes' ruins and snags: kidney leaves,
			// pink-white striped trumpets. LATER (I9b): its flowers; the heart leaf stands in.
			new VineSpecies { Name = "SeaBindweed", Leaf = VineLeaf.Heart, LeafA = H("#3e6a3a"), LeafB = H("#548a4a"), Stem = H("#6a7a4a"),
				LeafScale = 0.2f, MassScale = 0.36f, StrandsPerMetre = 3.2f, Autumn = Color.white },
			// ── Desert (biome round, 2026-10-26) ── Pergularia, the twining desert milkweed: grey-green heart leaves. LATER (I9b): its
			// cream-green flower stars and paired horned pods.
			new VineSpecies { Name = "Pergularia", Leaf = VineLeaf.Heart, LeafA = H("#6a7a5a"), LeafB = H("#7a8a6a"), Stem = H("#7a7a5a"),
				LeafScale = 0.22f, MassScale = 0.34f, StrandsPerMetre = 3f, Autumn = Color.white },
			// ── Badlands (biome round, 2026-10-26) ── Wild madder scrambling over the walls (small rough whorled leaves; LATER (I9b): its
			// black berries), and white bryony with palmate leaves (LATER (I9b): its scarlet berry strings).
			new VineSpecies { Name = "WildMadder", Leaf = VineLeaf.Ivy, LeafA = H("#4a6a3a"), LeafB = H("#5a7a44"), Stem = H("#6a6a4a"),
				LeafScale = 0.18f, MassScale = 0.34f, StrandsPerMetre = 4f, Deciduous = true, Autumn = new Color(1.3f, 1.1f, 0.6f, 1f) },
			new VineSpecies { Name = "WhiteBryony", Leaf = VineLeaf.Palmate, LeafA = H("#4a7a3a"), LeafB = H("#5a8a44"), Stem = H("#5a7a3a"),
				LeafScale = 0.28f, MassScale = 0.4f, StrandsPerMetre = 3.5f, Deciduous = true, Autumn = new Color(1.4f, 1.3f, 0.6f, 1f) },
			// ── Hills (biome round, 2026-10-26) ── Caper trailing from the wall cracks, the cultivated pergola vine on the house fronts and
			// granite-post ramadas, Etruscan honeysuckle. LATER (I9b): the caper's white-and-purple flowers, the vine's purple clusters, the
			// honeysuckle's cream-pink trumpets.
			new VineSpecies { Name = "CaperHills", Leaf = VineLeaf.Heart, LeafA = H("#6a8a6a"), LeafB = H("#7a9a7a"), Stem = H("#6a6a4a"),
				LeafScale = 0.18f, MassScale = 0.32f, StrandsPerMetre = 3f, Autumn = Color.white },
			new VineSpecies { Name = "PergolaVine", Leaf = VineLeaf.Palmate, LeafA = H("#4a7a2c"), LeafB = H("#6a8a34"), Stem = H("#5a4632"),
				LeafScale = 0.42f, MassScale = 0.5f, StrandsPerMetre = 3f, Woody = true, Deciduous = true, Autumn = new Color(1.6f, 1.3f, 0.5f, 1f) },
			new VineSpecies { Name = "EtruscanHoneysuckle", Leaf = VineLeaf.Heart, LeafA = H("#5a7a4a"), LeafB = H("#6a8a54"), Stem = H("#7a5a44"),
				LeafScale = 0.24f, MassScale = 0.38f, StrandsPerMetre = 4f, Deciduous = true, Autumn = new Color(1.3f, 1.2f, 0.6f, 1f) },
			// ── Wetlands (biome round, 2026-10-26) ── Bindweed (Calystegia sepium) twining up the reed edges and trees, and the caper (Capparis)
			// trailing on the brick ruins. LATER (I9b): the bindweed's white trumpets and the caper's stamen-brush flowers.
			new VineSpecies { Name = "Bindweed", Leaf = VineLeaf.Heart, LeafA = H("#3e6a2a"), LeafB = H("#5a8a38"), Stem = H("#6a8a4a"),
				LeafScale = 0.26f, MassScale = 0.36f, StrandsPerMetre = 3f, Deciduous = true, Autumn = new Color(1.3f, 1.25f, 0.6f, 1f) },
			new VineSpecies { Name = "Caper", Leaf = VineLeaf.Heart, LeafA = H("#5a7a6a"), LeafB = H("#6a8a78"), Stem = H("#7a6a56"),
				LeafScale = 0.22f, MassScale = 0.34f, StrandsPerMetre = 3f, Autumn = Color.white },
			// ── Mangrove (biome round, 2026-10-26) ── Derris, the woody twiner over the landward mangroves, and pencil vine (Cynanchum), leafless
			// green stems trailing over rag and ruin walls. LATER (I9b): derris's pink pea-sprays and pods, the pencil vine's cream flowers and
			// a leafless pencil-strand leaf (a sparse small leaf stands in).
			new VineSpecies { Name = "Derris", Leaf = VineLeaf.Heart, LeafA = H("#3a5a2a"), LeafB = H("#4e7234"), Stem = H("#5a4a3a"),
				LeafScale = 0.24f, MassScale = 0.34f, StrandsPerMetre = 3f, Autumn = Color.white },
			new VineSpecies { Name = "Pencilvine", Leaf = VineLeaf.Heart, LeafA = H("#5a7a3a"), LeafB = H("#6a8a46"), Stem = H("#5a7a3a"),
				LeafScale = 0.3f, MassScale = 0.3f, StrandsPerMetre = 2f, Autumn = Color.white },
			// ── River (biome round, 2026-10-26) ── Dodder (Cuscuta): orange-pink leafless thread nets over willow rods and ruins. LATER (I9b): its
			// leafless thread-net leaf and bead flowers (ivy-shaped leaves in its colours stand in). (Lake's bittersweet is Plains' species.)
			new VineSpecies { Name = "Dodder", Leaf = VineLeaf.Ivy, LeafA = H("#d88a6a"), LeafB = H("#e8a888"), Stem = H("#d88a6a"),
				LeafScale = 0.32f, MassScale = 0.4f, StrandsPerMetre = 4f, Autumn = Color.white },
		};

		/// <summary>
		/// The sizes every species is built at, two variants each: curtains for a cave's lintel and a ledge (a fringe for a low
		/// opening, a drape, and a long fall for a jamb or a cliff), mats for a gravestone, a wall and a tower's face, and spill
		/// for the foot of either.
		/// </summary>
		public static readonly VinePiece[] VinePieces =
		{
			new VinePiece { Form = VineForm.Curtain, Size = "Fringe", Width = 2.2f, Length = 1.4f, Variants = 2 },
			new VinePiece { Form = VineForm.Curtain, Size = "Drape", Width = 3f, Length = 2.8f, Variants = 2 },
			new VinePiece { Form = VineForm.Curtain, Size = "Fall", Width = 1.8f, Length = 5f, Variants = 2 },
			new VinePiece { Form = VineForm.Mat, Size = "Patch", Width = 1f, Length = 1.2f, Variants = 2 },
			new VinePiece { Form = VineForm.Mat, Size = "Mat", Width = 2.2f, Length = 2.4f, Variants = 2 },
			new VinePiece { Form = VineForm.Mat, Size = "Wall", Width = 3.6f, Length = 3.4f, Variants = 2 },
			new VinePiece { Form = VineForm.Spill, Size = "Small", Width = 1.3f, Length = 0f, Variants = 2 },
			new VinePiece { Form = VineForm.Spill, Size = "Large", Width = 2.6f, Length = 0f, Variants = 2 },
		};

		/// <summary>A tree species a vine climbs: the vines it carries, by the trunks they really grow on.</summary>
		public struct VineHost
		{
			public string Tree;
			public string[] Vines;
		}

		/// <summary>
		/// The trees vines climb, and which: ivy on the broadleaves of temperate woods (oak, beech, ash-like alder and poplar,
		/// holm oak, olive, willow, birch), on Scots pine in the Atlantic woods and on dead snags, which it often all but
		/// buries; Virginia creeper on oak, poplar and aspen; lianas on the jungle's trees and its palms. Conifers of the
		/// boreal forest, desert and dry-country trees, palms of the coast and swamp trees carry none.
		/// </summary>
		public static readonly VineHost[] VineHosts =
		{
			new VineHost { Tree = "Oak", Vines = new[] { "Ivy", "Creeper" } },
			new VineHost { Tree = "Beech", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Alder", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Poplar", Vines = new[] { "Ivy", "Creeper" } },
			new VineHost { Tree = "LombardyPoplar", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "HolmOak", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Olive", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "WeepingWillow", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Birch", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Aspen", Vines = new[] { "Creeper" } },
			new VineHost { Tree = "Pine", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Dead", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Jungle", Vines = new[] { "Liana" } },
			new VineHost { Tree = "Kapok", Vines = new[] { "Liana" } },
			new VineHost { Tree = "Palm", Vines = new[] { "Liana" } },
			// Forest (biome round, 2026-10-26): ivy columns up the beech and oak boles, honeysuckle twining in the sub-canopy hornbeams.
			new VineHost { Tree = "BeechForest", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "OakForest", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Lime", Vines = new[] { "Ivy" } },
			new VineHost { Tree = "Hornbeam", Vines = new[] { "Ivy", "Honeysuckle" } },
			new VineHost { Tree = "Ash", Vines = new[] { "Ivy" } },
			// Woodland (biome round, 2026-10-26): traveller's joy and hop over the hedge trees, bryony in them.
			new VineHost { Tree = "OakWoodland", Vines = new[] { "TravellersJoy", "Hop" } },
			new VineHost { Tree = "FieldMaple", Vines = new[] { "TravellersJoy", "BlackBryony" } },
			new VineHost { Tree = "CrabApple", Vines = new[] { "TravellersJoy", "BlackBryony" } },
			// Mountain Slope (biome round, 2026-10-26): moss sleeves on the cloud forest's trunks, the jasmine in the kosso. LATER (I9b): the
			// emergent yellowwoods' moss-sleeved boles and the jasmine up the meeting figs (climbers up their great trunks are over budget).
			new VineHost { Tree = "Hagenia", Vines = new[] { "MossSleeve", "AbyssinianJasmine" } },
			new VineHost { Tree = "GiantHeather", Vines = new[] { "MossSleeve" } },
			// Scrubland (biome round, 2026-10-26): desert mistletoe in the ironwood's crowns. LATER (I9b): the palo verde's and the velvet
			// mesquite's too (their trunks, as the generator builds them, give the climber none to follow).
			new VineHost { Tree = "Ironwood", Vines = new[] { "DesertMistletoe" } },
			// Jungle (biome round, 2026-10-26): lianas and epiphytes in the canopy's smaller trees, rattan in the kola and the forest canopy.
			// LATER (budget): the emergents' climbers (iroko, obeche, mahogany) are over the LOD budget on their great trunks.
			new VineHost { Tree = "Kola", Vines = new[] { "Liana", "Epiphytes" } },
			new VineHost { Tree = "ForestCanopy", Vines = new[] { "Liana" } },
			// Bamboo Forest (biome round, 2026-10-26): wisteria up the red pines, kudzu over the shrine forest's shii.
			new VineHost { Tree = "Akamatsu", Vines = new[] { "Fuji" } },
			new VineHost { Tree = "Shii", Vines = new[] { "Kuzu" } },
			// Savanna (biome round, 2026-10-26): the flame lily up the termite islands' jackalberries.
			new VineHost { Tree = "Jackalberry", Vines = new[] { "Gloriosa" } },
			// Desert (biome round, 2026-10-26): the milkweed twining up the desert dates. LATER (budget): Badlands' white bryony up the
			// oriental planes (the great trunk's climbers are over the LOD budget, as Mountain Slope's were).
			new VineHost { Tree = "Balanites", Vines = new[] { "Pergularia" } },
			// River (biome round, 2026-10-26): dodder netting the crack willows' rods.
			new VineHost { Tree = "CrackWillow", Vines = new[] { "Dodder" } },
		};

		/// <summary>How far up its clear trunk each climber variant reaches: a skirt about the foot, and up to the crown.</summary>
		public static readonly float[] ClimberReach = { 0.4f, 0.85f };

		/// <summary>The most each climber variant climbs, metres, whatever the trunk: ivy rarely clothes more than ten metres of bole.</summary>
		public static readonly float[] ClimberMaxMetres = { 4.5f, 10f };

		/// <summary>The vine species with this name.</summary>
		public static bool TryVine(string name, out VineSpecies species)
		{
			foreach (VineSpecies v in Vines)
			{
				if (v.Name == name)
				{
					species = v;
					return true;
				}
			}
			species = default;
			return false;
		}

		/// <summary>The size of a form with this name, or false.</summary>
		public static bool TryVinePiece(VineForm form, string size, out VinePiece piece)
		{
			foreach (VinePiece p in VinePieces)
			{
				if (p.Form == form && p.Size == size)
				{
					piece = p;
					return true;
				}
			}
			piece = default;
			return false;
		}

		/// <summary>The vines that climb a tree species (empty for none).</summary>
		public static string[] VinesOn(string tree)
		{
			foreach (VineHost h in VineHosts)
			{
				if (h.Tree == tree)
				{
					return h.Vines;
				}
			}
			return System.Array.Empty<string>();
		}

		// ── Names ─────────────────────────────────────────────────

		/// <summary>A piece's base name: <c>Vine_Ivy_CurtainDrape_0</c>.</summary>
		public static string VineName(string species, in VinePiece piece, int variant) => $"{VinePrefix}{species}_{piece.Form}{piece.Size}_{variant}";

		/// <summary>A climber's base name: <c>Vine_Ivy_Climber_Oak_0</c>.</summary>
		public static string VineClimberName(string species, string tree, int variant) => $"{VinePrefix}{species}_{VineForm.Climber}_{tree}_{variant}";

		/// <summary>A level's payload mesh name.</summary>
		public static string VineMesh(string baseName, int lod) => $"{baseName}_LOD{lod}";

		/// <summary>A prefab's name under the generated prefabs folder, with its sub-folder.</summary>
		public static string VinePrefab(string baseName) => $"{VinePrefabFolderName}/{baseName}";

		/// <summary>The hanging, mat and spill material of a species; and the material its climbers wear (the trees' variation figures).</summary>
		public static string VineMaterial(string species, bool climber = false) => climber ? $"{VinePrefix}{species}_Climber" : $"{VinePrefix}{species}";

		/// <summary>Every vine payload file: the atlas, and three meshes a variant of every piece and every climber.</summary>
		private static IEnumerable<string> VinePayloadPaths()
		{
			yield return VineAtlasPath;
			foreach (string name in VineBaseNames())
			{
				for (int lod = 0; lod < VineMeshes.Levels; lod++)
				{
					yield return MeshPath(VineMesh(name, lod));
				}
			}
		}

		/// <summary>Every vine material: two a species.</summary>
		private static IEnumerable<string> VineWrapperPaths()
		{
			foreach (VineSpecies v in Vines)
			{
				yield return MaterialPath(VineMaterial(v.Name));
				yield return MaterialPath(VineMaterial(v.Name, true));
			}
		}

		private static IEnumerable<string> VinePrefabNames()
		{
			foreach (string name in VineBaseNames())
			{
				yield return VinePrefab(name);
			}
		}

		/// <summary>Every vine prefab's base name, pieces then climbers.</summary>
		public static IEnumerable<string> VineBaseNames()
		{
			foreach (VineSpecies v in Vines)
			{
				foreach (VinePiece p in VinePieces)
				{
					for (int variant = 0; variant < p.Variants; variant++)
					{
						yield return VineName(v.Name, in p, variant);
					}
				}
			}
			foreach (VineHost h in VineHosts)
			{
				foreach (string vine in h.Vines)
				{
					for (int variant = 0; variant < ClimberReach.Length; variant++)
					{
						yield return VineClimberName(vine, h.Tree, variant);
					}
				}
			}
		}
	}
}
#endif
