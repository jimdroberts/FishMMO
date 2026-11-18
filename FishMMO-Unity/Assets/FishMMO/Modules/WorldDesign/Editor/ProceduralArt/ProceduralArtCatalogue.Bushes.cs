#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The shrubs (<see cref="BushMeshes"/>): their species, names and paths.</summary>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>
		/// Every shrub prefab's name starts with this. The client's GPU detail scatter takes every prototype so named
		/// as a bush (FishMMO.Client DetailScatterSettings.BushPrefix: the two must match): drawn only on the GPU, at
		/// a draw distance and density no client setting changes, so nobody can thin the cover another player hides in.
		/// </summary>
		public const string BushPrefix = "Detail_Bush_";

		/// <summary>A shrub's prefab, material and level-0 mesh name.</summary>
		public static string BushPrefab(string name) => BushPrefix + name;

		/// <summary>A shrub's mesh at a level of detail: level 0 is the prefab's own name.</summary>
		public static string BushMesh(string name, int lod) => lod == 0 ? BushPrefab(name) : $"{BushPrefab(name)}_LOD{lod}";

		/// <summary>The child of a shrub prefab that carries a level's mesh (a MeshFilter only: nothing renders it but the GPU scatter).</summary>
		public static string BushLevelChild(int lod) => "LOD" + lod;

		/// <summary>
		/// Every shrub species, at the size of a mature plant of its kind, chosen so that each biome's climate has the
		/// shrubs that really grow in it (BiomeArtSpec) and most stand taller than a person.
		/// </summary>
		/// <remarks>
		/// <list type="bullet">
		/// <item><b>Hazel</b> (<i>Corylus avellana</i>): the woodland understory's coppice stool, a goblet of a dozen
		/// smooth grey-brown stems 4 m tall, broad soft leaves; deciduous.</item>
		/// <item><b>Laurel</b> (cherry laurel, <i>Prunus laurocerasus</i>): a dense evergreen dome 3 m tall and wider,
		/// large dark glossy leaves to the ground.</item>
		/// <item><b>Box</b> (<i>Buxus sempervirens</i>): a tight dome of tiny dark leaves, 1.8 m; wild on limestone, clipped
		/// in gardens.</item>
		/// <item><b>Juniper</b> (common juniper, <i>Juniperus communis</i>): a narrow grey-green flame 3 m tall, the shrub
		/// of the boreal forest floor, heath and mountain; stunted to a mat on tundra (the rule's scale).</item>
		/// <item><b>Mountain pine</b> (<i>Pinus mugo</i>): the dwarf pine above the tree line, sprawling stems 1.8 m tall
		/// and twice as wide.</item>
		/// <item><b>Gorse</b> (<i>Ulex europaeus</i>): a spiny dark cushion 1.8 m tall, yellow with flowers, on heath, hill
		/// and coast.</item>
		/// <item><b>Bramble</b> (<i>Rubus fruticosus</i>): arching purple canes in a mound 1.6 m tall and twice as wide, at
		/// wood edges, in hedges and over ruins.</item>
		/// <item><b>Rhododendron</b> (<i>R. ponticum</i>; the Himalayan forests' own): a broad leathery evergreen 3.2 m tall
		/// with pink-purple trusses.</item>
		/// <item><b>Sagebrush</b> (<i>Artemisia tridentata</i>): the cold desert's silver-grey shrub, a gnarled trunk
		/// under an irregular crown, 1.3 m.</item>
		/// <item><b>Creosote</b> (<i>Larrea tridentata</i>): the hot desert's open vase of grey stems and small dark-olive
		/// leaves, 1.9 m, airy (it hides poorly, as the real one does); a few yellow flowers.</item>
		/// <item><b>Hibiscus</b> (<i>H. rosa-sinensis</i>): a glossy tropical evergreen 2.6 m tall with large red
		/// flowers.</item>
		/// <item><b>Willow</b> (grey willow, <i>Salix cinerea</i>): a many-stemmed goblet of narrow grey-green leaves
		/// 3.5 m tall on wet ground; dwarfed on tundra; deciduous.</item>
		/// </list>
		/// Added by the vegetation expansion (2026-10-10), so every biome's shrub layer has its real species:
		/// <list type="bullet">
		/// <item><b>Heather</b> (ling, <i>Calluna vulgaris</i>): the moorland and bog's dwarf shrub, a wiry cushion half a
		/// metre tall under purple flower spikes.</item>
		/// <item><b>Dwarf birch</b> (<i>Betula nana</i>): the tundra's sprawling knee-high birch with small round leaves,
		/// red in autumn.</item>
		/// <item><b>Bilberry</b> (<i>Vaccinium myrtillus</i>, standing for crowberry and lingonberry too): the boreal forest
		/// floor's low mound of green stems and bright leaves, dotted with dark berries.</item>
		/// <item><b>Holly</b> (<i>Ilex aquifolium</i>): a 5 m evergreen column of dark glossy leaves with red berries, under
		/// oak and beech.</item>
		/// <item><b>Hawthorn</b> (<i>Crataegus monogyna</i>): the hedgerow and scrub shrub, a 5 m goblet of small lobed leaves
		/// hung with red haws (haws, not blossom: a flower card cannot change with the season).</item>
		/// <item><b>Rabbitbrush</b> (<i>Ericameria nauseosa</i>): the cold desert's grey-green mound under a mass of yellow
		/// flower heads.</item>
		/// <item><b>Saltbush</b> (<i>Atriplex</i>): a silver-grey mound wider than tall, on salt and dry ground.</item>
		/// <item><b>Tamarisk</b> (<i>Tamarix</i>): a feathery grey-green vase 4 m tall with pink plumes, at desert springs
		/// and salt margins.</item>
		/// <item><b>Ocotillo</b> (<i>Fouquieria splendens</i>): a fountain of straight grey canes 4.5 m tall from one foot,
		/// small leaves all along them after rain and a red flower spike at each tip (<see cref="BushHabit.Whip"/>;
		/// open: it hides no one).</item>
		/// <item><b>Oleander</b> (<i>Nerium oleander</i>): a 3 m evergreen mound of narrow leathery leaves with pink and white
		/// flowers, at oases and in Mediterranean gardens.</item>
		/// <item><b>Cistus</b> (rock-rose): the maquis's grey-green metre-high mound with white and pink flowers.</item>
		/// <item><b>Dwarf bamboo</b> (<i>Sasa</i>): the bamboo forest's understory, a thicket of twenty thin green culms
		/// with bamboo leaves, 1.5 m.</item>
		/// <item><b>Thornbush</b> (<i>Vachellia</i>/<i>Senegalia</i> scrub): the savanna's sprawling, crooked, sparse-leaved
		/// thicket (open: its cover is thin).</item>
		/// <item><b>Palmetto</b> (<i>Sabal minor</i>, <i>Serenoa</i>): a clump of fan leaves on long petioles, 1.5 m, in warm
		/// swamps (<see cref="BushSpecies.FanLeaves"/>).</item>
		/// </list>
		/// Leaf sizes are set so the atlas cell's leaves come out near their real size: broad leaves 8–15 cm, small
		/// ones 3–6 cm. Shrubs under 0.6 m (heather, bilberry) are ground cover and owe nobody cover.
		/// </remarks>
		public static readonly BushSpecies[] Bushes =
		{
			new BushSpecies { Name = "Hazel", Habit = BushHabit.Vase, Height = 4f, Width = 3.6f, Stems = 12, StemRadius = 0.035f, Branches = 3,
				Bark = H("#7a6a58"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.5f, LeafA = H("#4a7a2c"), LeafB = H("#6a9438"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.22f, Irregularity = 0.18f, Deciduous = true },
			new BushSpecies { Name = "Laurel", Habit = BushHabit.Mound, Height = 3f, Width = 3.6f, Stems = 7, StemRadius = 0.05f, Branches = 4,
				Bark = H("#4f463e"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.6f, LeafA = H("#1f4a1e"), LeafB = H("#2f5e26"),
				Shell = 0.4f, Fullness = 4.2f, BareBase = 0f, Irregularity = 0.15f },
			new BushSpecies { Name = "Box", Habit = BushHabit.Mound, Height = 1.8f, Width = 2f, Stems = 6, StemRadius = 0.03f, Branches = 4,
				Bark = H("#8a7a62"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#2e5428"), LeafB = H("#3e6a30"),
				Shell = 0.3f, Fullness = 4.5f, BareBase = 0f, Irregularity = 0.1f },
			new BushSpecies { Name = "Juniper", Habit = BushHabit.Upright, Height = 3f, Width = 1.5f, Stems = 8, StemRadius = 0.04f, Branches = 5,
				Bark = H("#6a4a3a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.45f, LeafA = H("#3c5a40"), LeafB = H("#527050"),
				Shell = 0.35f, Fullness = 5.2f, BareBase = 0.02f, Irregularity = 0.22f },
			new BushSpecies { Name = "MountainPine", Habit = BushHabit.Spreading, Height = 1.8f, Width = 3.4f, Stems = 8, StemRadius = 0.06f, Branches = 3,
				Bark = H("#4a3c32"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.55f, LeafA = H("#2a4a2a"), LeafB = H("#3c5e34"),
				Shell = 0.4f, Fullness = 4.4f, BareBase = 0.05f, Irregularity = 0.25f },
			new BushSpecies { Name = "Gorse", Habit = BushHabit.Cushion, Height = 1.8f, Width = 2.2f, Stems = 9, StemRadius = 0.025f, Branches = 3,
				Bark = H("#6a5a3e"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.4f, LeafA = H("#34502a"), LeafB = H("#46642e"),
				Shell = 0.35f, Fullness = 4.2f, BareBase = 0f, Irregularity = 0.15f,
				Flowers = new[] { H("#f0c020"), H("#f5d23a") }, FlowerShare = 0.9f, FlowerSize = 0.1f },
			new BushSpecies { Name = "Bramble", Habit = BushHabit.Arching, Height = 1.6f, Width = 3.2f, Stems = 14, StemRadius = 0.012f, Branches = 2,
				Bark = H("#6a3a42"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.4f, LeafA = H("#2e5426"), LeafB = H("#46682e"),
				Shell = 0.5f, Fullness = 3.6f, BareBase = 0f, Irregularity = 0.25f },
			new BushSpecies { Name = "Rhododendron", Habit = BushHabit.Mound, Height = 3.2f, Width = 3.8f, Stems = 6, StemRadius = 0.06f, Branches = 4,
				Bark = H("#5a4a40"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.6f, LeafA = H("#22441e"), LeafB = H("#335a28"),
				Shell = 0.4f, Fullness = 4f, BareBase = 0f, Irregularity = 0.2f,
				Flowers = new[] { H("#b0508e"), H("#c868a8") }, FlowerShare = 0.12f, FlowerSize = 0.28f },
			new BushSpecies { Name = "Sagebrush", Habit = BushHabit.Mound, Height = 1.3f, Width = 1.6f, Stems = 5, StemRadius = 0.04f, Branches = 3,
				Bark = H("#6a5e50"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.32f, LeafA = H("#8a9478"), LeafB = H("#a2aa8c"),
				Shell = 0.35f, Fullness = 3.6f, BareBase = 0.18f, Irregularity = 0.3f },
			new BushSpecies { Name = "Creosote", Habit = BushHabit.Vase, Height = 1.9f, Width = 2.2f, Stems = 10, StemRadius = 0.02f, Branches = 3,
				Bark = H("#6a6460"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#4a5a2a"), LeafB = H("#5e6e34"),
				Shell = 0.4f, Fullness = 2.6f, BareBase = 0.3f, Irregularity = 0.25f,
				Flowers = new[] { H("#e8c838") }, FlowerShare = 0.05f, FlowerSize = 0.06f },
			new BushSpecies { Name = "Hibiscus", Habit = BushHabit.Mound, Height = 2.6f, Width = 2.4f, Stems = 6, StemRadius = 0.035f, Branches = 4,
				Bark = H("#6a5a48"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.5f, LeafA = H("#2a5a22"), LeafB = H("#3c7428"),
				Shell = 0.4f, Fullness = 3.8f, BareBase = 0.05f, Irregularity = 0.2f,
				Flowers = new[] { H("#d8282a"), H("#e04a3a") }, FlowerShare = 0.06f, FlowerSize = 0.18f },
			new BushSpecies { Name = "Willow", Habit = BushHabit.Vase, Height = 3.5f, Width = 3.2f, Stems = 10, StemRadius = 0.03f, Branches = 3,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.4f, LeafA = H("#5a7a48"), LeafB = H("#7a9460"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.18f, Irregularity = 0.2f, Deciduous = true },

			// ── Vegetation expansion (2026-10-10) ──
			new BushSpecies { Name = "Heather", Habit = BushHabit.Cushion, Height = 0.5f, Width = 0.9f, Stems = 10, StemRadius = 0.008f, Branches = 3,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.15f, LeafA = H("#3a4a2a"), LeafB = H("#4e5e34"),
				Shell = 0.35f, Fullness = 4.2f, BareBase = 0f, Irregularity = 0.18f,
				Flowers = new[] { H("#9a5a9a"), H("#b070a8") }, FlowerShare = 0.7f, FlowerSize = 0.05f },
			new BushSpecies { Name = "DwarfBirch", Habit = BushHabit.Spreading, Height = 0.8f, Width = 1.4f, Stems = 9, StemRadius = 0.012f, Branches = 3,
				Bark = H("#4a3a30"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#4a7a2c"), LeafB = H("#5e8a36"),
				Shell = 0.4f, Fullness = 4.6f, BareBase = 0.04f, Irregularity = 0.25f, Deciduous = true },
			new BushSpecies { Name = "Bilberry", Habit = BushHabit.Mound, Height = 0.4f, Width = 0.7f, Stems = 12, StemRadius = 0.005f, Branches = 3,
				Bark = H("#4e7a34"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.12f, LeafA = H("#4e8a2e"), LeafB = H("#6aa03a"),
				Shell = 0.35f, Fullness = 4f, BareBase = 0.05f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#2a2a4a"), H("#3a3a5e") }, FlowerShare = 0.05f, FlowerSize = 0.025f },
			new BushSpecies { Name = "Holly", Habit = BushHabit.Upright, Height = 5f, Width = 3f, Stems = 6, StemRadius = 0.06f, Branches = 5,
				Bark = H("#6a6a5a"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.55f, LeafA = H("#1e3e1c"), LeafB = H("#2a5226"),
				Shell = 0.4f, Fullness = 4.6f, BareBase = 0.05f, Irregularity = 0.2f,
				Flowers = new[] { H("#c01818"), H("#d82a20") }, FlowerShare = 0.05f, FlowerSize = 0.06f },
			new BushSpecies { Name = "Hawthorn", Habit = BushHabit.Vase, Height = 5f, Width = 4.5f, Stems = 8, StemRadius = 0.06f, Branches = 4,
				Bark = H("#6a5a4a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.5f, LeafA = H("#3e6a28"), LeafB = H("#507a30"),
				Shell = 0.45f, Fullness = 3.8f, BareBase = 0.2f, Irregularity = 0.22f, Deciduous = true,
				Flowers = new[] { H("#a01818"), H("#b82a20") }, FlowerShare = 0.15f, FlowerSize = 0.06f },
			new BushSpecies { Name = "Rabbitbrush", Habit = BushHabit.Mound, Height = 1.2f, Width = 1.3f, Stems = 10, StemRadius = 0.02f, Branches = 3,
				Bark = H("#8a8a74"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.28f, LeafA = H("#7a8a60"), LeafB = H("#8e9a70"),
				Shell = 0.35f, Fullness = 5.2f, BareBase = 0.1f, Irregularity = 0.22f,
				Flowers = new[] { H("#e8c020"), H("#f0d040") }, FlowerShare = 0.5f, FlowerSize = 0.12f },
			new BushSpecies { Name = "Saltbush", Habit = BushHabit.Mound, Height = 1f, Width = 1.6f, Stems = 8, StemRadius = 0.025f, Branches = 3,
				Bark = H("#8a8070"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#8a9688"), LeafB = H("#a6b0a0"),
				Shell = 0.35f, Fullness = 4.4f, BareBase = 0.04f, Irregularity = 0.25f },
			new BushSpecies { Name = "Tamarisk", Habit = BushHabit.Vase, Height = 4f, Width = 4f, Stems = 12, StemRadius = 0.04f, Branches = 4,
				Bark = H("#6a4a3e"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.5f, LeafA = H("#6a8a6a"), LeafB = H("#7e9a7a"),
				Shell = 0.45f, Fullness = 3.8f, BareBase = 0.18f, Irregularity = 0.22f, Deciduous = true,
				Flowers = new[] { H("#e0a0b8"), H("#d08aa8") }, FlowerShare = 0.25f, FlowerSize = 0.2f },
			new BushSpecies { Name = "Ocotillo", Habit = BushHabit.Whip, Height = 4.5f, Width = 2.6f, Stems = 12, StemRadius = 0.03f, Branches = 0,
				Bark = H("#7a7a64"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.15f, LeafA = H("#4a7a2a"), LeafB = H("#5e8a34"),
				Shell = 0f, Fullness = 0.8f, BareBase = 0.08f, Irregularity = 0.1f, Deciduous = true, Open = true,
				Flowers = new[] { H("#d8381e"), H("#e84a24") }, FlowerShare = 0.04f, FlowerSize = 0.22f },
			new BushSpecies { Name = "Oleander", Habit = BushHabit.Mound, Height = 3f, Width = 3f, Stems = 10, StemRadius = 0.03f, Branches = 4,
				Bark = H("#6a6a58"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.5f, LeafA = H("#2e5228"), LeafB = H("#3e6230"),
				Shell = 0.4f, Fullness = 4.2f, BareBase = 0.06f, Irregularity = 0.2f,
				Flowers = new[] { H("#e070a0"), H("#f4eef0") }, FlowerShare = 0.1f, FlowerSize = 0.12f },
			new BushSpecies { Name = "Cistus", Habit = BushHabit.Mound, Height = 1f, Width = 1.3f, Stems = 8, StemRadius = 0.015f, Branches = 3,
				Bark = H("#6a5a48"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.26f, LeafA = H("#5a6a48"), LeafB = H("#6e7c58"),
				Shell = 0.35f, Fullness = 4.4f, BareBase = 0.04f, Irregularity = 0.2f,
				Flowers = new[] { H("#f4f2ee"), H("#e0a0c0") }, FlowerShare = 0.15f, FlowerSize = 0.07f },
			new BushSpecies { Name = "DwarfBamboo", Habit = BushHabit.Upright, Height = 1.5f, Width = 2.2f, Stems = 20, StemRadius = 0.006f, Branches = 2,
				Bark = H("#6a8a40"), LeafCell = FoliageCell.BambooLeaves, LeafSize = 0.35f, LeafA = H("#4a7a2a"), LeafB = H("#5e8e34"),
				Shell = 0.45f, Fullness = 7.5f, BareBase = 0.1f, Irregularity = 0.2f },
			new BushSpecies { Name = "Thornbush", Habit = BushHabit.Spreading, Height = 2.5f, Width = 3f, Stems = 7, StemRadius = 0.03f, Branches = 4,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.35f, LeafA = H("#5a6a30"), LeafB = H("#6e7a3a"),
				Shell = 0.4f, Fullness = 3f, BareBase = 0.12f, Irregularity = 0.3f, Open = true },
			new BushSpecies { Name = "Palmetto", Habit = BushHabit.Mound, Height = 1.5f, Width = 2.5f, Stems = 14, StemRadius = 0.012f, Branches = 0,
				Bark = H("#6a6a48"), LeafCell = FoliageCell.Blade, LeafSize = 0.55f, LeafA = H("#3e6a34"), LeafB = H("#52804a"),
				Shell = 0.3f, Fullness = 5f, BareBase = 0.05f, Irregularity = 0.2f, FanLeaves = true },
			// ── Tundra (biome round, 2026-10-26): the knee-high arctic shrubs (decision 25's Prostrate habit for the mats) ──
			new BushSpecies { Name = "ArcticWillow", Habit = BushHabit.Prostrate, Height = 0.16f, Width = 1.4f, Stems = 7, StemRadius = 0.008f, Branches = 3,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.1f, LeafA = H("#4a6a3a"), LeafB = H("#6a8a4a"),
				Shell = 0.3f, Fullness = 2.6f, BareBase = 0f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#f0ecdc") }, FlowerShare = 0.2f, FlowerSize = 0.04f },
			new BushSpecies { Name = "Bearberry", Habit = BushHabit.Prostrate, Height = 0.13f, Width = 1f, Stems = 7, StemRadius = 0.006f, Branches = 3,
				Bark = H("#6a3a2a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.05f, LeafA = H("#c82a1e"), LeafB = H("#4a6a2a"),
				Shell = 0.3f, Fullness = 2.4f, BareBase = 0f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#141414") }, FlowerShare = 0.06f, FlowerSize = 0.02f },
			new BushSpecies { Name = "ArcticWhiteHeather", Habit = BushHabit.Cushion, Height = 0.15f, Width = 0.4f, Stems = 10, StemRadius = 0.005f, Branches = 3,
				Bark = H("#2e4a26"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.08f, LeafA = H("#2e4a26"), LeafB = H("#3e5a2e"),
				Shell = 0.35f, Fullness = 4.4f, BareBase = 0f, Irregularity = 0.18f,
				Flowers = new[] { H("#f4f0e8") }, FlowerShare = 0.4f, FlowerSize = 0.02f },
			new BushSpecies { Name = "LaplandRosebay", Habit = BushHabit.Mound, Height = 0.35f, Width = 0.5f, Stems = 8, StemRadius = 0.006f, Branches = 3,
				Bark = H("#4a3a2e"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.1f, LeafA = H("#3a4a2e"), LeafB = H("#4a5a34"),
				Shell = 0.35f, Fullness = 4.2f, BareBase = 0.02f, Irregularity = 0.2f,
				Flowers = new[] { H("#a03a90"), H("#b04aa0") }, FlowerShare = 0.35f, FlowerSize = 0.03f },
			// ── Glacier (biome round, 2026-10-26) ── Caragana jubata, "camel's tail": shaggy grey-white spine-sheathed stems on the outer
			// moraines. LATER (I9b): a Shaggy habit (persistent sheaths along each stem); Whip canes stand in.
			new BushSpecies { Name = "CaraganaJubata", Habit = BushHabit.Whip, Height = 1.2f, Width = 0.9f, Stems = 16, StemRadius = 0.02f, Branches = 0,
				Bark = H("#c8c0b0"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.12f, LeafA = H("#6a8a5a"), LeafB = H("#7a9a6a"),
				Shell = 0f, Fullness = 1.2f, BareBase = 0.05f, Irregularity = 0.1f, Open = true,
				Flowers = new[] { H("#f0d0d8") }, FlowerShare = 0.1f, FlowerSize = 0.04f },
			// ── Forest (biome round, 2026-10-26) ── The closed canopy's sparse evergreen-led understorey.
			// Forest holly grown up tree-form under the beech, glossy and dark, berries scarlet.
			new BushSpecies { Name = "HollyForest", Habit = BushHabit.Upright, Height = 6.5f, Width = 3.5f, Stems = 5, StemRadius = 0.08f, Branches = 5,
				Bark = H("#6a6a5a"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.55f, LeafA = H("#142e14"), LeafB = H("#1e3e1a"),
				Shell = 0.4f, Fullness = 3.8f, BareBase = 0.15f, Irregularity = 0.2f,
				Flowers = new[] { H("#c81818"), H("#d82a20") }, FlowerShare = 0.04f, FlowerSize = 0.06f },
			// Elder (Sambucus): a vase of corky stems under flat cream umbels, in the gaps and by settlements.
			new BushSpecies { Name = "Elder", Habit = BushHabit.Vase, Height = 4f, Width = 4f, Stems = 9, StemRadius = 0.05f, Branches = 4,
				Bark = H("#8a7a62"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.45f, LeafA = H("#3e6a28"), LeafB = H("#5a8434"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.25f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#f2eee0"), H("#e8e2cc") }, FlowerShare = 0.15f, FlowerSize = 0.18f },
			// Spindle (Euonymus): upright, its pink-orange fruit in autumn.
			new BushSpecies { Name = "Spindle", Habit = BushHabit.Upright, Height = 4f, Width = 2.5f, Stems = 6, StemRadius = 0.035f, Branches = 4,
				Bark = H("#5a6a4a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#3a6228"), LeafB = H("#4e7a30"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.2f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#e0508a"), H("#f08a20") }, FlowerShare = 0.08f, FlowerSize = 0.03f },
			// Butcher's broom: a low dark cushion of spiny cladodes under yew and fir, red berries.
			new BushSpecies { Name = "ButchersBroom", Habit = BushHabit.Cushion, Height = 0.55f, Width = 0.8f, Stems = 12, StemRadius = 0.01f, Branches = 3,
				Bark = H("#3e5a2e"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.2f, LeafA = H("#2e4a22"), LeafB = H("#3a5a28"),
				Shell = 0.35f, Fullness = 4f, BareBase = 0f, Irregularity = 0.2f,
				Flowers = new[] { H("#c02020") }, FlowerShare = 0.03f, FlowerSize = 0.03f },
			// ── Woodland (biome round, 2026-10-26) ── The managed wood's coppice, thickets and hedges. LATER (I9b P27): true cut stools;
			// a just-cut stool stands in as a low spread of short shoots.
			// Coppiced hazel: a vase of straight rods, catkins in spring.
			new BushSpecies { Name = "HazelCoppice", Habit = BushHabit.Vase, Height = 5f, Width = 4f, Stems = 16, StemRadius = 0.03f, Branches = 2,
				Bark = H("#7a6a58"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.5f, LeafA = H("#4a7a2c"), LeafB = H("#6a9438"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.28f, Irregularity = 0.15f, Deciduous = true,
				Flowers = new[] { H("#d8c060") }, FlowerShare = 0.08f, FlowerSize = 0.05f },
			// A hazel stool cut this winter: a ring of stubs with a foot of new shoots.
			new BushSpecies { Name = "HazelStoolCut", Habit = BushHabit.Spreading, Height = 0.55f, Width = 1.4f, Stems = 20, StemRadius = 0.012f, Branches = 1,
				Bark = H("#7a6a58"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.25f, LeafA = H("#5a8a34"), LeafB = H("#7aa040"),
				Shell = 0.3f, Fullness = 2.4f, BareBase = 0.3f, Irregularity = 0.3f, Deciduous = true },
			// A sweet-chestnut stool: eight thick rods under long serrated leaves.
			new BushSpecies { Name = "ChestnutStool", Habit = BushHabit.Vase, Height = 6f, Width = 4f, Stems = 8, StemRadius = 0.06f, Branches = 3,
				Bark = H("#6a5a48"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.6f, LeafA = H("#3a6a24"), LeafB = H("#5a8a30"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.35f, Irregularity = 0.2f, Deciduous = true },
			// Woodland bramble: blackberries and pink-white flowers on the edges.
			new BushSpecies { Name = "BrambleWoodland", Habit = BushHabit.Arching, Height = 1.6f, Width = 3.2f, Stems = 14, StemRadius = 0.012f, Branches = 2,
				Bark = H("#6a3a42"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.4f, LeafA = H("#2e5426"), LeafB = H("#46682e"),
				Shell = 0.5f, Fullness = 3.6f, BareBase = 0f, Irregularity = 0.25f,
				Flowers = new[] { H("#2a1a2a"), H("#f0d8e0") }, FlowerShare = 0.06f, FlowerSize = 0.04f },
			// Woodland hawthorn in May blossom, haws among it.
			new BushSpecies { Name = "HawthornWoodland", Habit = BushHabit.Vase, Height = 5f, Width = 4.5f, Stems = 8, StemRadius = 0.06f, Branches = 4,
				Bark = H("#6a5a4a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.5f, LeafA = H("#3e6a28"), LeafB = H("#507a30"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.2f, Irregularity = 0.22f, Deciduous = true,
				Flowers = new[] { H("#f4f2ea"), H("#a01818") }, FlowerShare = 0.25f, FlowerSize = 0.05f },
			// Blackthorn (Prunus spinosa): suckering black thickets, white blossom before the leaves, sloes.
			new BushSpecies { Name = "Blackthorn", Habit = BushHabit.Spreading, Height = 3f, Width = 3.5f, Stems = 9, StemRadius = 0.025f, Branches = 3,
				Bark = H("#2a2422"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.4f, LeafA = H("#2a4222"), LeafB = H("#3a5228"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.1f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#f6f4f0"), H("#3a3a6a") }, FlowerShare = 0.15f, FlowerSize = 0.02f },
			// Dog rose (Rosa canina): arching, pale pink flowers, scarlet hips.
			new BushSpecies { Name = "DogRose", Habit = BushHabit.Arching, Height = 2.5f, Width = 3f, Stems = 10, StemRadius = 0.015f, Branches = 2,
				Bark = H("#5a6a3a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#3e6a2a"), LeafB = H("#507a34"),
				Shell = 0.45f, Fullness = 3.2f, BareBase = 0.05f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#f0c0c8"), H("#c02818") }, FlowerShare = 0.08f, FlowerSize = 0.06f },
			// Dogwood (Cornus sanguinea): red stems, white cymes, on the chalk and in the hedges.
			new BushSpecies { Name = "Dogwood", Habit = BushHabit.Vase, Height = 3f, Width = 2.5f, Stems = 12, StemRadius = 0.02f, Branches = 3,
				Bark = H("#9a2a22"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.35f, LeafA = H("#3e6a2a"), LeafB = H("#507a34"),
				Shell = 0.4f, Fullness = 3.2f, BareBase = 0.2f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#f2eee0") }, FlowerShare = 0.1f, FlowerSize = 0.06f },
			// Wayfaring tree (Viburnum lantana), the chalk shrub: felted grey-green, flat cymes, berries red to black.
			new BushSpecies { Name = "WayfaringTree", Habit = BushHabit.Vase, Height = 4f, Width = 3f, Stems = 8, StemRadius = 0.035f, Branches = 4,
				Bark = H("#7a6a58"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.45f, LeafA = H("#5a7040"), LeafB = H("#7a8a5a"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.2f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#f2eee0"), H("#c02818") }, FlowerShare = 0.15f, FlowerSize = 0.1f },
			// ── Alpine Meadow (biome round, 2026-10-26) ── The knieholz: the high pastures' dwarf mugo pine, broader and darker than
			// Alpine's (its own species until I9's per-biome variants). The Carpathian rhododendron's crimson cushions on the north
			// faces, and the upright Silesian willow by the streams (Tundra's willow is prostrate).
			new BushSpecies { Name = "Knieholz", Habit = BushHabit.Spreading, Height = 2.2f, Width = 4.5f, Stems = 10, StemRadius = 0.07f, Branches = 3,
				Bark = H("#3a2e26"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.55f, LeafA = H("#1e3a22"), LeafB = H("#34522e"),
				Shell = 0.4f, Fullness = 4.6f, BareBase = 0.04f, Irregularity = 0.35f },
			new BushSpecies { Name = "CarpathianRhododendron", Habit = BushHabit.Cushion, Height = 0.4f, Width = 0.9f, Stems = 14, StemRadius = 0.01f, Branches = 3,
				Bark = H("#5a4a40"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.12f, LeafA = H("#2a4a24"), LeafB = H("#3a5a2e"),
				Shell = 0.35f, Fullness = 4.2f, BareBase = 0f, Irregularity = 0.2f,
				Flowers = new[] { H("#d0305a"), H("#e0507a") }, FlowerShare = 0.5f, FlowerSize = 0.025f },
			new BushSpecies { Name = "SilesianWillow", Habit = BushHabit.Vase, Height = 2.2f, Width = 2f, Stems = 10, StemRadius = 0.025f, Branches = 3,
				Bark = H("#7a3a2a"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.3f, LeafA = H("#4a6a34"), LeafB = H("#5e7e40"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.15f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#d8d8c8") }, FlowerShare = 0.1f, FlowerSize = 0.03f },
			// ── Mountain Slope (biome round, 2026-10-26) ── Tree hypericum with its gold cups in the glades, wild coffee in the warm
			// forest, the frost-moor's silver alchemilla mounds, and gesho, the mead's bittering shrub of the hedges.
			new BushSpecies { Name = "TreeHypericum", Habit = BushHabit.Vase, Height = 4.5f, Width = 3.2f, Stems = 6, StemRadius = 0.05f, Branches = 4,
				Bark = H("#8a5a3a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.4f, LeafA = H("#3a5a26"), LeafB = H("#5a7a32"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.15f, Irregularity = 0.4f,
				Flowers = new[] { H("#f0c020") }, FlowerShare = 0.12f, FlowerSize = 0.07f },
			new BushSpecies { Name = "Coffee", Habit = BushHabit.Upright, Height = 3.5f, Width = 2.2f, Stems = 5, StemRadius = 0.03f, Branches = 5,
				Bark = H("#8a7a62"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.45f, LeafA = H("#1c3a1a"), LeafB = H("#2c5024"),
				Shell = 0.4f, Fullness = 3.8f, BareBase = 0.25f, Irregularity = 0.2f,
				Flowers = new[] { H("#f4f0e6"), H("#b8201a") }, FlowerShare = 0.06f, FlowerSize = 0.03f },
			new BushSpecies { Name = "SilverAlchemilla", Habit = BushHabit.Mound, Height = 0.7f, Width = 1.4f, Stems = 12, StemRadius = 0.01f, Branches = 3,
				Bark = H("#6a6a5a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.15f, LeafA = H("#8a9a8c"), LeafB = H("#b4c0b4"),
				Shell = 0.35f, Fullness = 4.5f, BareBase = 0f, Irregularity = 0.2f },
			new BushSpecies { Name = "Gesho", Habit = BushHabit.Mound, Height = 3f, Width = 3f, Stems = 6, StemRadius = 0.04f, Branches = 4,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.4f, LeafA = H("#22401e"), LeafB = H("#34562a"),
				Shell = 0.4f, Fullness = 3.8f, BareBase = 0.1f, Irregularity = 0.25f,
				Flowers = new[] { H("#a82a22") }, FlowerShare = 0.05f, FlowerSize = 0.03f },
			// ── Scree (biome round, 2026-10-26) ── Jindallae azalea: slender grey-brown twigs, magenta-pink flowers on the warm cones.
			new BushSpecies { Name = "Jindallae", Habit = BushHabit.Upright, Height = 1.5f, Width = 1.2f, Stems = 8, StemRadius = 0.015f, Branches = 3,
				Bark = H("#7a6a5a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#4a6a2a"), LeafB = H("#5a7a34"),
				Shell = 0.4f, Fullness = 3.8f, BareBase = 0.08f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#c83a9a"), H("#d85aaa") }, FlowerShare = 0.4f, FlowerSize = 0.05f },

			// ── Scrubland (biome round, 2026-10-26) ── The Sonoran desert scrub's matrix shrubs (bursage on the bajadas, white bursage on
			// the creosote flats), the brittlebush of the hot slopes, jojoba, the washes' fairy duster and chuparosa, and quailbush on the
			// canal banks. LATER (I9): Creosote and Ocotillo as Scrubland's own textured variants (the catalogue's stand in).
			new BushSpecies { Name = "Bursage", Habit = BushHabit.Mound, Height = 0.55f, Width = 0.8f, Stems = 10, StemRadius = 0.008f, Branches = 3,
				Bark = H("#6a6050"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#7a8a6a"), LeafB = H("#94a080"),
				Shell = 0.3f, Fullness = 3.6f, BareBase = 0.06f, Irregularity = 0.25f },
			new BushSpecies { Name = "BursageWhite", Habit = BushHabit.Mound, Height = 0.55f, Width = 0.8f, Stems = 10, StemRadius = 0.008f, Branches = 3,
				Bark = H("#7a7062"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#a8aa98"), LeafB = H("#c0c0b0"),
				Shell = 0.3f, Fullness = 3.4f, BareBase = 0.08f, Irregularity = 0.25f },
			new BushSpecies { Name = "Brittlebush", Habit = BushHabit.Cushion, Height = 1f, Width = 1.3f, Stems = 6, StemRadius = 0.02f, Branches = 3,
				Bark = H("#7a7262"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#a8b0a0"), LeafB = H("#c8ccc0"),
				Shell = 0.35f, Fullness = 4f, BareBase = 0.05f, Irregularity = 0.25f,
				Flowers = new[] { H("#f0c818"), H("#f4d430") }, FlowerShare = 0.35f, FlowerSize = 0.07f },
			new BushSpecies { Name = "Jojoba", Habit = BushHabit.Upright, Height = 1.8f, Width = 2f, Stems = 12, StemRadius = 0.02f, Branches = 3,
				Bark = H("#6a6458"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.3f, LeafA = H("#6a8078"), LeafB = H("#8a9a90"),
				Shell = 0.4f, Fullness = 4f, BareBase = 0.04f, Irregularity = 0.2f,
				Flowers = new[] { H("#6a4a2e") }, FlowerShare = 0.03f, FlowerSize = 0.06f },
			new BushSpecies { Name = "FairyDuster", Habit = BushHabit.Spreading, Height = 0.55f, Width = 1f, Stems = 10, StemRadius = 0.008f, Branches = 3,
				Bark = H("#6a5a4a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.18f, LeafA = H("#5a7a3a"), LeafB = H("#6e8a46"),
				Shell = 0.35f, Fullness = 3f, BareBase = 0.06f, Irregularity = 0.3f,
				Flowers = new[] { H("#e070a0"), H("#f0a0c8") }, FlowerShare = 0.2f, FlowerSize = 0.07f },
			new BushSpecies { Name = "Chuparosa", Habit = BushHabit.Mound, Height = 1.2f, Width = 1.4f, Stems = 9, StemRadius = 0.008f, Branches = 2,
				Bark = H("#6a8a5a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.12f, LeafA = H("#6a8a5a"), LeafB = H("#7a9a64"),
				Shell = 0.3f, Fullness = 1.8f, BareBase = 0.04f, Irregularity = 0.25f, Open = true,
				Flowers = new[] { H("#d8301e"), H("#e04a2a") }, FlowerShare = 0.1f, FlowerSize = 0.05f },
			new BushSpecies { Name = "Quailbush", Habit = BushHabit.Mound, Height = 2f, Width = 2.6f, Stems = 10, StemRadius = 0.03f, Branches = 4,
				Bark = H("#8a8070"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#94a0a0"), LeafB = H("#aab4b2"),
				Shell = 0.35f, Fullness = 3.6f, BareBase = 0.06f, Irregularity = 0.25f },

			// ── Jungle + Bamboo Forest (biome rounds, 2026-10-26) ── The rainforest's understorey and the bamboo hills' shade shrubs.
			// Dracaena (the boundary peace plant): ringed grey canes tufted with strap leaves, cream panicles.
			new BushSpecies { Name = "Dracaena", Habit = BushHabit.Upright, Height = 4f, Width = 1.8f, Stems = 8, StemRadius = 0.04f, Branches = 4,
				Bark = H("#8a8070"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.9f, LeafA = H("#3a6a2a"), LeafB = H("#5a8a3a"),
				Shell = 0.4f, Fullness = 5f, BareBase = 0.1f, Irregularity = 0.3f,
				Flowers = new[] { H("#f0ece0") }, FlowerShare = 0.05f, FlowerSize = 0.1f },
			// Wild coffee: glossy dark leaves, white star flowers, red cherries.
			new BushSpecies { Name = "WildCoffee", Habit = BushHabit.Vase, Height = 3f, Width = 2.4f, Stems = 6, StemRadius = 0.025f, Branches = 4,
				Bark = H("#7a6a52"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.45f, LeafA = H("#1e3a18"), LeafB = H("#2e5222"),
				Shell = 0.4f, Fullness = 3.6f, BareBase = 0.2f, Irregularity = 0.2f,
				Flowers = new[] { H("#f4f0e8"), H("#b02a1a") }, FlowerShare = 0.07f, FlowerSize = 0.04f },
			// Ixora: leathery leaves under scarlet-orange heads.
			new BushSpecies { Name = "Ixora", Habit = BushHabit.Mound, Height = 1.6f, Width = 1.6f, Stems = 8, StemRadius = 0.015f, Branches = 3,
				Bark = H("#6a5a4a"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.3f, LeafA = H("#2a4a22"), LeafB = H("#3a5a2a"),
				Shell = 0.4f, Fullness = 3.6f, BareBase = 0.05f, Irregularity = 0.2f,
				Flowers = new[] { H("#e0401e") }, FlowerShare = 0.15f, FlowerSize = 0.07f },
			// Rattan (climbing-palm thicket): spiny green whips with barbed pinnate tips, an impassable tangle.
			new BushSpecies { Name = "Rattan", Habit = BushHabit.Whip, Height = 6f, Width = 3f, Stems = 5, StemRadius = 0.015f, Branches = 3,
				Bark = H("#5a6a3a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.7f, LeafA = H("#3a5a2a"), LeafB = H("#4a6a34"),
				Shell = 0.4f, Fullness = 2.4f, BareBase = 0.1f, Irregularity = 0.4f,
				Flowers = new[] { H("#4a6a34") }, FlowerShare = 0.02f, FlowerSize = 0.04f },
			// Bitterleaf (Vernonia): a matte compound-leaved herb shrub, grey-white flower tufts.
			new BushSpecies { Name = "Bitterleaf", Habit = BushHabit.Upright, Height = 2.5f, Width = 1.8f, Stems = 6, StemRadius = 0.02f, Branches = 4,
				Bark = H("#6a6a4a"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.35f, LeafA = H("#4a6a2e"), LeafB = H("#5a7a38"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.15f, Irregularity = 0.25f,
				Flowers = new[] { H("#e8e8e0") }, FlowerShare = 0.05f, FlowerSize = 0.04f },
			// Kuma-zasa (Sasa veitchii): the understorey dwarf bamboo, broad leaves with white withered margins.
			new BushSpecies { Name = "KumaZasa", Habit = BushHabit.Upright, Height = 1.0f, Width = 2.4f, Stems = 26, StemRadius = 0.006f, Branches = 2,
				Bark = H("#5a7a30"), LeafCell = FoliageCell.BambooLeaves, LeafSize = 0.35f, LeafA = H("#2e5a22"), LeafB = H("#e8e4c8"),
				Shell = 0.4f, Fullness = 7f, BareBase = 0.05f, Irregularity = 0.2f,
				Flowers = new[] { H("#e8e4c8") }, FlowerShare = 0.1f, FlowerSize = 0.03f },
			// Ajisai (hydrangea): mophead flowers in blue, mauve and white in the rains.
			new BushSpecies { Name = "Ajisai", Habit = BushHabit.Mound, Height = 1.5f, Width = 1.8f, Stems = 12, StemRadius = 0.012f, Branches = 3,
				Bark = H("#6a5a42"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.3f, LeafA = H("#3a6a2a"), LeafB = H("#4e7e34"),
				Shell = 0.4f, Fullness = 4.2f, BareBase = 0.1f, Irregularity = 0.2f,
				Flowers = new[] { H("#6a7ad0"), H("#b88ac8"), H("#e8e4f0") }, FlowerShare = 0.45f, FlowerSize = 0.18f },
			// Yamabuki (Kerria): arching green stems wreathed in golden flowers.
			new BushSpecies { Name = "Yamabuki", Habit = BushHabit.Arching, Height = 1.6f, Width = 2f, Stems = 18, StemRadius = 0.01f, Branches = 3,
				Bark = H("#4a7a2a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#4a7a34"), LeafB = H("#5a8a3a"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.1f, Irregularity = 0.25f,
				Flowers = new[] { H("#f0b020") }, FlowerShare = 0.35f, FlowerSize = 0.04f },
			// Asebi (Pieris): glossy leaves, new growth red, white bell sprays.
			new BushSpecies { Name = "Asebi", Habit = BushHabit.Mound, Height = 2.5f, Width = 2f, Stems = 6, StemRadius = 0.025f, Branches = 4,
				Bark = H("#6a5a48"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#2a4a24"), LeafB = H("#3a5a2a"),
				Shell = 0.4f, Fullness = 3.6f, BareBase = 0.15f, Irregularity = 0.2f,
				Flowers = new[] { H("#f4f0e8"), H("#b03a2a") }, FlowerShare = 0.4f, FlowerSize = 0.04f },
			// Yama-tsutsuji (Rhododendron kaempferi): salmon-vermilion flowers on the ridges.
			new BushSpecies { Name = "YamaTsutsuji", Habit = BushHabit.Spreading, Height = 1.8f, Width = 2.2f, Stems = 10, StemRadius = 0.015f, Branches = 3,
				Bark = H("#6a5a48"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#3a5a2a"), LeafB = H("#4a6a34"),
				Shell = 0.4f, Fullness = 4f, BareBase = 0.1f, Irregularity = 0.3f,
				Flowers = new[] { H("#e05a3a") }, FlowerShare = 0.5f, FlowerSize = 0.05f },
			// Chanoki (tea bush): clipped glossy mounds, white flowers; the field rows.
			new BushSpecies { Name = "Chanoki", Habit = BushHabit.Mound, Height = 1.0f, Width = 1.4f, Stems = 10, StemRadius = 0.012f, Branches = 3,
				Bark = H("#6a5a42"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.25f, LeafA = H("#2a4a20"), LeafB = H("#3a5a28"),
				Shell = 0.4f, Fullness = 4.2f, BareBase = 0f, Irregularity = 0.1f,
				Flowers = new[] { H("#f6f2e4") }, FlowerShare = 0.05f, FlowerSize = 0.03f },

			// ── Savanna (biome round, 2026-10-26) ── The veld's thickets: sickle bush with its two-tone lantern catkins, the raisin bush of
			// the termite islands, and red bushwillow on the granite edges.
			new BushSpecies { Name = "SickleBush", Habit = BushHabit.Spreading, Height = 2.5f, Width = 2.5f, Stems = 7, StemRadius = 0.03f, Branches = 4,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#5a6a30"), LeafB = H("#6a7a3a"),
				Shell = 0.4f, Fullness = 3f, BareBase = 0.12f, Irregularity = 0.3f, Open = true,
				Flowers = new[] { H("#e080a8"), H("#f0d040") }, FlowerShare = 0.12f, FlowerSize = 0.08f },
			new BushSpecies { Name = "Grewia", Habit = BushHabit.Mound, Height = 2f, Width = 2.5f, Stems = 9, StemRadius = 0.025f, Branches = 4,
				Bark = H("#6a5a48"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.35f, LeafA = H("#5a6a40"), LeafB = H("#6e7a4e"),
				Shell = 0.4f, Fullness = 3.8f, BareBase = 0.08f, Irregularity = 0.25f,
				Flowers = new[] { H("#b0602a") }, FlowerShare = 0.08f, FlowerSize = 0.05f },
			new BushSpecies { Name = "Bushwillow", Habit = BushHabit.Vase, Height = 4f, Width = 3.5f, Stems = 8, StemRadius = 0.04f, Branches = 4,
				Bark = H("#6a5e50"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.45f, LeafA = H("#5a7a2e"), LeafB = H("#a8602a"),
				Shell = 0.45f, Fullness = 3.2f, BareBase = 0.2f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#b08a5a") }, FlowerShare = 0.2f, FlowerSize = 0.07f },
			// ── Grassland (biome round, 2026-10-26) ── The forest-steppe scrub on the loess slopes, kurgans and grove edges.
			// Dwarf almond (Prunus tenella): suckering, narrow leaves, pink spring blossom.
			new BushSpecies { Name = "DwarfAlmond", Habit = BushHabit.Spreading, Height = 1.2f, Width = 1.6f, Stems = 14, StemRadius = 0.012f, Branches = 3,
				Bark = H("#5a3a2e"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#3e6a2a"), LeafB = H("#5a7a34"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.08f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#e890b0"), H("#f0a8c4") }, FlowerShare = 0.35f, FlowerSize = 0.03f },
			// Steppe cherry (Prunus fruticosa): a low glossy mound, white blossom, dark red cherries.
			new BushSpecies { Name = "SteppeCherry", Habit = BushHabit.Mound, Height = 1f, Width = 1.6f, Stems = 12, StemRadius = 0.012f, Branches = 3,
				Bark = H("#4a2e26"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.25f, LeafA = H("#2e5a22"), LeafB = H("#3e6a28"),
				Shell = 0.4f, Fullness = 4f, BareBase = 0.02f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#f6f2ec"), H("#6a1020") }, FlowerShare = 0.2f, FlowerSize = 0.03f },
			// Spiraea hypericifolia: arching wiry grey-green sprays under white flowers.
			new BushSpecies { Name = "Spiraea", Habit = BushHabit.Arching, Height = 1.4f, Width = 1.8f, Stems = 10, StemRadius = 0.008f, Branches = 2,
				Bark = H("#6a5a4a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#6a8058"), LeafB = H("#8a9a6a"),
				Shell = 0.45f, Fullness = 3.6f, BareBase = 0.05f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#f4f2ea") }, FlowerShare = 0.45f, FlowerSize = 0.02f },
			// Austrian broom (Chamaecytisus austriacus): green twiggy uprights under yellow pea flowers on the sand.
			new BushSpecies { Name = "SteppeBroom", Habit = BushHabit.Upright, Height = 0.8f, Width = 0.9f, Stems = 12, StemRadius = 0.008f, Branches = 2,
				Bark = H("#5a7040"), LeafCell = FoliageCell.Twigs, LeafSize = 0.2f, LeafA = H("#5a7040"), LeafB = H("#6a8048"),
				Shell = 0.35f, Fullness = 3f, BareBase = 0.05f, Irregularity = 0.2f, Open = true,
				Flowers = new[] { H("#f0c020") }, FlowerShare = 0.4f, FlowerSize = 0.03f },
			// Tatarian maple (Acer tataricum), the bush-tree of the grove edges: three-lobed leaves and a summer haze of red samaras. LATER (budget):
			// its 5 m spread; the chestnut stool's in-budget proportions (4 m) stand in.
			new BushSpecies { Name = "TatarianMaple", Habit = BushHabit.Vase, Height = 6f, Width = 4f, Stems = 8, StemRadius = 0.06f, Branches = 3,
				Bark = H("#5a4e44"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.6f, LeafA = H("#3e6a2a"), LeafB = H("#5a8034"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.35f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#c03028") }, FlowerShare = 0.2f, FlowerSize = 0.05f },
			// ── Plains (biome round, 2026-10-26) ── Shrubs only in the draws, the sand and the breaks.
			// Sand plum: a suckering thicket of red twigs, white spring blossom, red-orange plums.
			new BushSpecies { Name = "SandPlum", Habit = BushHabit.Mound, Height = 1.8f, Width = 3f, Stems = 14, StemRadius = 0.015f, Branches = 3,
				Bark = H("#7a3a2a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#3e6a2a"), LeafB = H("#4e7a32"),
				Shell = 0.4f, Fullness = 4f, BareBase = 0.05f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#f6f2ec"), H("#d04a2a") }, FlowerShare = 0.3f, FlowerSize = 0.03f },
			// Chokecherry: upright, white pendant racemes, purple-black fruit chains.
			new BushSpecies { Name = "Chokecherry", Habit = BushHabit.Upright, Height = 4f, Width = 3f, Stems = 8, StemRadius = 0.035f, Branches = 4,
				Bark = H("#5a4a40"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.4f, LeafA = H("#3a5e2a"), LeafB = H("#4e7232"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.2f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#f2f0e6"), H("#2a1028") }, FlowerShare = 0.2f, FlowerSize = 0.05f },
			// Silver buffaloberry: thorny grey stems, silver rounded leaves, scarlet berries.
			new BushSpecies { Name = "BuffaloBerry", Habit = BushHabit.Upright, Height = 3.5f, Width = 3f, Stems = 8, StemRadius = 0.03f, Branches = 4,
				Bark = H("#8a8478"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.36f, LeafA = H("#a8b0a0"), LeafB = H("#c8ccc0"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.15f, Irregularity = 0.22f, Deciduous = true,
				Flowers = new[] { H("#c8201a") }, FlowerShare = 0.2f, FlowerSize = 0.03f },
			// Leadplant: the prairie's hoary subshrub, violet spikes with orange anthers.
			new BushSpecies { Name = "Leadplant", Habit = BushHabit.Spreading, Height = 0.8f, Width = 1f, Stems = 12, StemRadius = 0.006f, Branches = 2,
				Bark = H("#6a6a5a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.15f, LeafA = H("#8a9488"), LeafB = H("#a8ac9e"),
				Shell = 0.35f, Fullness = 3f, BareBase = 0.05f, Irregularity = 0.2f, Deciduous = true, Open = true,
				Flowers = new[] { H("#5a3a9a"), H("#e08a20") }, FlowerShare = 0.3f, FlowerSize = 0.03f },
			// Smooth sumac: a colony of stout stems, pinnate leaves going scarlet, upright crimson fruit cones.
			new BushSpecies { Name = "SmoothSumac", Habit = BushHabit.Vase, Height = 2.5f, Width = 3f, Stems = 7, StemRadius = 0.035f, Branches = 3,
				Bark = H("#6a5a48"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.45f, LeafA = H("#3e6a2a"), LeafB = H("#c8201a"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.25f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#8a1a20") }, FlowerShare = 0.2f, FlowerSize = 0.08f },
			// Shinnery oak: a knee-high rhizomatous oak "forest" on the southern sand.
			new BushSpecies { Name = "ShinneryOak", Habit = BushHabit.Spreading, Height = 0.9f, Width = 2.5f, Stems = 14, StemRadius = 0.012f, Branches = 3,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.25f, LeafA = H("#4a6a3a"), LeafB = H("#5a7a42"),
				Shell = 0.35f, Fullness = 3.2f, BareBase = 0.05f, Irregularity = 0.25f, Deciduous = true },
			// Sandbar willow: wand-like stems, linear grey-green leaves, on the bars and banks.
			new BushSpecies { Name = "SandbarWillow", Habit = BushHabit.Upright, Height = 3f, Width = 2f, Stems = 14, StemRadius = 0.015f, Branches = 2,
				Bark = H("#8a6a4a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.3f, LeafA = H("#7a8a6a"), LeafB = H("#8a9a7a"),
				Shell = 0.4f, Fullness = 3.2f, BareBase = 0.15f, Irregularity = 0.2f, Deciduous = true },
			// ── Steppe (biome round, 2026-10-26) ── The salt tree on the sor rims and river terraces, Atraphaxis on the grit.
			new BushSpecies { Name = "SaltTree", Habit = BushHabit.Arching, Height = 1.6f, Width = 2f, Stems = 8, StemRadius = 0.015f, Branches = 2,
				Bark = H("#7a7068"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#a8b0a0"), LeafB = H("#c0c8b8"),
				Shell = 0.45f, Fullness = 3f, BareBase = 0.08f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#b06aa0") }, FlowerShare = 0.3f, FlowerSize = 0.03f },
			new BushSpecies { Name = "Atraphaxis", Habit = BushHabit.Upright, Height = 0.9f, Width = 1f, Stems = 12, StemRadius = 0.008f, Branches = 2,
				Bark = H("#7a4a3a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.15f, LeafA = H("#8a9a7a"), LeafB = H("#9aa88a"),
				Shell = 0.35f, Fullness = 3f, BareBase = 0.05f, Irregularity = 0.2f, Deciduous = true, Open = true,
				Flowers = new[] { H("#f0d0d8") }, FlowerShare = 0.45f, FlowerSize = 0.02f },
			// ── Estuary (biome round, 2026-10-26) ── The saltings' two shrubs: sea purslane along the creek rims, shrubby sea-blite in the warm reach.
			// Sea purslane (Halimione): a mealy silver-grey mound along every creek edge.
			new BushSpecies { Name = "SeaPurslane", Habit = BushHabit.Mound, Height = 0.5f, Width = 1.2f, Stems = 10, StemRadius = 0.01f, Branches = 3,
				Bark = H("#8a8a7a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.12f, LeafA = H("#8a9a88"), LeafB = H("#b4c0b0"),
				Shell = 0.4f, Fullness = 3f, BareBase = 0f, Irregularity = 0.25f },
			// Shrubby sea-blite (Suaeda vera): dark blue-green succulent twigs on the high marsh and the schill bars.
			new BushSpecies { Name = "ShrubbySeablite", Habit = BushHabit.Mound, Height = 1f, Width = 1.2f, Stems = 10, StemRadius = 0.012f, Branches = 3,
				Bark = H("#6a6a5a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.15f, LeafA = H("#3a5a4a"), LeafB = H("#4a6a58"),
				Shell = 0.5f, Fullness = 4f, BareBase = 0f, Irregularity = 0.25f },
			// ── Desert (biome round, 2026-10-26) ── Arta's leafless jointed switches over its nebkha with red bristly fruit balls, markh's
			// rush-like green rope stems, and the toothbrush tree's drooping fleshy-leaved sprays with pink-red berries at the wells.
			new BushSpecies { Name = "Arta", Habit = BushHabit.Whip, Height = 2f, Width = 2f, Stems = 12, StemRadius = 0.012f, Branches = 0,
				Bark = H("#8a8a72"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.12f, LeafA = H("#8a9a7a"), LeafB = H("#9aa888"),
				Shell = 0f, Fullness = 1.2f, BareBase = 0.05f, Irregularity = 0.2f, Open = true,
				Flowers = new[] { H("#c8502a") }, FlowerShare = 0.3f, FlowerSize = 0.05f },
			new BushSpecies { Name = "Markh", Habit = BushHabit.Upright, Height = 2.5f, Width = 1.8f, Stems = 24, StemRadius = 0.01f, Branches = 2,
				Bark = H("#5a7a34"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.2f, LeafA = H("#6a8a3a"), LeafB = H("#7a9a44"),
				Shell = 0.35f, Fullness = 2.4f, BareBase = 0.05f, Irregularity = 0.2f, Open = true,
				Flowers = new[] { H("#c8c860") }, FlowerShare = 0.05f, FlowerSize = 0.02f },
			// LATER (budget): the toothbrush tree's full drooping crown hides a player; in budget its far level is thinner, so it is Open.
			new BushSpecies { Name = "Irak", Habit = BushHabit.Spreading, Height = 3f, Width = 4f, Stems = 4, StemRadius = 0.05f, Branches = 3,
				Bark = H("#8a8070"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.35f, LeafA = H("#7a9a5a"), LeafB = H("#8aaa68"),
				Shell = 0.45f, Fullness = 2.6f, BareBase = 0.1f, Irregularity = 0.3f, Open = true,
				Flowers = new[] { H("#c84a5a") }, FlowerShare = 0.2f, FlowerSize = 0.04f },
			// ── Badlands (biome round, 2026-10-26) ── Geven, the plateau's spiny tragacanth cushion; Christ's thorn of the field hedges with
			// its coin-like winged fruit; the copper briar's copper-scarlet roses by the ruins and hans.
			new BushSpecies { Name = "Tragacanth", Habit = BushHabit.Cushion, Height = 0.5f, Width = 0.9f, Stems = 20, StemRadius = 0.008f, Branches = 3,
				Bark = H("#7a7262"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.15f, LeafA = H("#8a9a80"), LeafB = H("#9aa890"),
				Shell = 0.3f, Fullness = 4f, BareBase = 0f, Irregularity = 0.2f,
				Flowers = new[] { H("#f0e8b0") }, FlowerShare = 0.2f, FlowerSize = 0.025f },
			// LATER (budget): Christ's thorn's draft eight zigzag stems and fuller crown; four stems and a thinner crown keep its far level in budget, so it is Open.
			new BushSpecies { Name = "ChristsThorn", Habit = BushHabit.Spreading, Height = 2.5f, Width = 3f, Stems = 4, StemRadius = 0.025f, Branches = 3,
				Bark = H("#6a4a34"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.25f, LeafA = H("#4a6a2a"), LeafB = H("#5a7a34"),
				Shell = 0.4f, Fullness = 1.8f, BareBase = 0.1f, Irregularity = 0.35f, Deciduous = true, Open = true,
				Flowers = new[] { H("#c8a050") }, FlowerShare = 0.4f, FlowerSize = 0.04f },
			new BushSpecies { Name = "CopperBriar", Habit = BushHabit.Arching, Height = 1.8f, Width = 2f, Stems = 10, StemRadius = 0.012f, Branches = 2,
				Bark = H("#7a4a30"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.25f, LeafA = H("#4a6a34"), LeafB = H("#5a7a3e"),
				Shell = 0.45f, Fullness = 3.8f, BareBase = 0.05f, Irregularity = 0.25f, Deciduous = true,
				Flowers = new[] { H("#d8501a"), H("#f0c020") }, FlowerShare = 0.35f, FlowerSize = 0.06f },
			// ── Salt Flat (biome round, 2026-10-26) ── Saltwort's glaucous succulent mounds going wine-red with pink-buff winged fruit, the
			// grey thorny caper of the lava rim, and the myrrh's few spiny stems under sparse trifoliate leaves (mostly bare).
			// LATER (I9 P11): the myrrh's PaperPeel bark (bushes take a bark colour, not a family).
			new BushSpecies { Name = "Salsola", Habit = BushHabit.Mound, Height = 0.8f, Width = 1.2f, Stems = 10, StemRadius = 0.012f, Branches = 3,
				Bark = H("#7a6a5a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.18f, LeafA = H("#7a8a72"), LeafB = H("#8a3a3a"),
				Shell = 0.35f, Fullness = 3.6f, BareBase = 0.05f, Irregularity = 0.25f,
				Flowers = new[] { H("#e0b0a0") }, FlowerShare = 0.2f, FlowerSize = 0.03f },
			new BushSpecies { Name = "Cadaba", Habit = BushHabit.Mound, Height = 1.6f, Width = 2f, Stems = 6, StemRadius = 0.03f, Branches = 3,
				Bark = H("#8a8478"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.25f, LeafA = H("#6a7a5a"), LeafB = H("#7a8a68"),
				Shell = 0.4f, Fullness = 3.2f, BareBase = 0.1f, Irregularity = 0.3f },
			new BushSpecies { Name = "Commiphora", Habit = BushHabit.Spreading, Height = 3f, Width = 3f, Stems = 3, StemRadius = 0.06f, Branches = 4,
				Bark = H("#8a8a6a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#5a6a3a"), LeafB = H("#6a7a44"),
				Shell = 0.3f, Fullness = 1.4f, BareBase = 0.2f, Irregularity = 0.35f, Deciduous = true, Open = true },
			// ── Beach (biome round, 2026-10-26): the dune willow, burnet rose and crowberry of the cool grey dune; the warm strand's sea grape ──
			new BushSpecies { Name = "DuneWillow", Habit = BushHabit.Prostrate, Height = 0.5f, Width = 2f, Stems = 7, StemRadius = 0.01f, Branches = 3,
				Bark = H("#6a3a2a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.12f, LeafA = H("#a8b0a0"), LeafB = H("#c8d0c0"),
				Shell = 0.3f, Fullness = 2.5f, BareBase = 0f, Irregularity = 0.25f, Deciduous = true },
			new BushSpecies { Name = "BurnetRose", Habit = BushHabit.Mound, Height = 0.7f, Width = 1.2f, Stems = 9, StemRadius = 0.01f, Branches = 3,
				Bark = H("#4a3a2e"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.14f, LeafA = H("#3a4a2a"), LeafB = H("#4e6034"),
				Shell = 0.32f, Fullness = 3.8f, BareBase = 0.04f, Irregularity = 0.22f, Deciduous = true,
				Flowers = new[] { H("#f0ead0") }, FlowerShare = 0.1f, FlowerSize = 0.06f },
			new BushSpecies { Name = "Crowberry", Habit = BushHabit.Prostrate, Height = 0.15f, Width = 1.5f, Stems = 7, StemRadius = 0.005f, Branches = 3,
				Bark = H("#3a2a22"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.1f, LeafA = H("#2a3a22"), LeafB = H("#3a4a2a"),
				Shell = 0.3f, Fullness = 2.2f, BareBase = 0f, Irregularity = 0.2f,
				Flowers = new[] { H("#1a1418") }, FlowerShare = 0.1f, FlowerSize = 0.02f },
			// LATER (I9b): the sea grape's red-veined old leaves and its grape clusters; the round leathery leaf stands in.
			new BushSpecies { Name = "SeaGrape", Habit = BushHabit.Spreading, Height = 2.5f, Width = 4f, Stems = 7, StemRadius = 0.04f, Branches = 4,
				Bark = H("#6a5a46"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.4f, LeafA = H("#5a7a3a"), LeafB = H("#a83a2a"),
				Shell = 0.3f, Fullness = 4.3f, BareBase = 0.06f, Irregularity = 0.22f,
				Flowers = new[] { H("#6a2a4a") }, FlowerShare = 0.04f, FlowerSize = 0.1f },
			// ── Hills (biome round, 2026-10-26) ── The hills' scrub on the fells and abandoned terraces.
			// Tree-heath (Erica arborea), the briar: an upright needle-leaved bush under white honey flowers. LATER (budget):
			// its 3.5 m height; the juniper's in-budget proportions (3 m) stand in. Spanish broom on the ocotillo's whip proportions.
			new BushSpecies { Name = "TreeHeath", Habit = BushHabit.Upright, Height = 3f, Width = 1.5f, Stems = 8, StemRadius = 0.04f, Branches = 5,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.45f, LeafA = H("#3a4a2a"), LeafB = H("#4e5e34"),
				Shell = 0.35f, Fullness = 5.2f, BareBase = 0.02f, Irregularity = 0.22f,
				Flowers = new[] { H("#f2eee6") }, FlowerShare = 0.5f, FlowerSize = 0.02f },
			// Spanish broom: rush-green whips with few leaves and big yellow pea flowers.
			new BushSpecies { Name = "SpanishBroom", Habit = BushHabit.Whip, Height = 2.5f, Width = 2f, Stems = 12, StemRadius = 0.015f, Branches = 0,
				Bark = H("#4a7a34"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.15f, LeafA = H("#4a7a34"), LeafB = H("#5a8a3e"),
				Shell = 0f, Fullness = 0.8f, BareBase = 0.05f, Irregularity = 0.1f, Open = true,
				Flowers = new[] { H("#f2c418") }, FlowerShare = 0.6f, FlowerSize = 0.05f },
			// Cushion broom (Cytisus oromediterraneus): the cold fells' yellow, no spines.
			new BushSpecies { Name = "CushionBroom", Habit = BushHabit.Cushion, Height = 1f, Width = 1.6f, Stems = 9, StemRadius = 0.025f, Branches = 3,
				Bark = H("#4a6a3a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.4f, LeafA = H("#4a6a3a"), LeafB = H("#5a7a42"),
				Shell = 0.35f, Fullness = 4.2f, BareBase = 0f, Irregularity = 0.15f,
				Flowers = new[] { H("#f0c420"), H("#f5d23a") }, FlowerShare = 0.85f, FlowerSize = 0.06f },
			// Prickly juniper (Juniperus oxycedrus): grey-green, red-brown berries, the warm and temperate reach's.
			new BushSpecies { Name = "PricklyJuniper", Habit = BushHabit.Upright, Height = 4f, Width = 2.2f, Stems = 8, StemRadius = 0.04f, Branches = 5,
				Bark = H("#6a4a3a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.45f, LeafA = H("#5a6a48"), LeafB = H("#7a8466"),
				Shell = 0.35f, Fullness = 5.2f, BareBase = 0.05f, Irregularity = 0.22f,
				Flowers = new[] { H("#8a3a22") }, FlowerShare = 0.05f, FlowerSize = 0.03f },
			// Mat juniper: the cold fells' prostrate juniper.
			new BushSpecies { Name = "JuniperMat", Habit = BushHabit.Prostrate, Height = 0.4f, Width = 2.5f, Stems = 8, StemRadius = 0.015f, Branches = 3,
				Bark = H("#5a4a3a"), LeafCell = FoliageCell.BushNeedle, LeafSize = 0.2f, LeafA = H("#3c5a40"), LeafB = H("#4a6a48"),
				Shell = 0.3f, Fullness = 2.8f, BareBase = 0f, Irregularity = 0.25f },
			// Terebinth: glossy pinnate leaves going crimson, red-purple fruit, horn galls.
			new BushSpecies { Name = "Terebinth", Habit = BushHabit.Vase, Height = 4f, Width = 3.5f, Stems = 6, StemRadius = 0.05f, Branches = 4,
				Bark = H("#6a6058"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.4f, LeafA = H("#3e5a2a"), LeafB = H("#4e6a32"),
				Shell = 0.45f, Fullness = 3.4f, BareBase = 0.25f, Irregularity = 0.2f, Deciduous = true,
				Flowers = new[] { H("#9a2a4a") }, FlowerShare = 0.1f, FlowerSize = 0.04f },
			// A wild fig out of a wall, gorge or spring: grey stems, big lobed leaves.
			new BushSpecies { Name = "WildFig", Habit = BushHabit.Mound, Height = 4f, Width = 5f, Stems = 5, StemRadius = 0.06f, Branches = 4,
				Bark = H("#8a8478"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.7f, LeafA = H("#3a6a2a"), LeafB = H("#5a8034"),
				Shell = 0.4f, Fullness = 4f, BareBase = 0.05f, Irregularity = 0.2f, Deciduous = true },
			// ── Wetlands (biome round, 2026-10-26) ── The warm reach's thorn scrub of the levees: Syrian mesquite and the Christ's-thorn sidr.
			// Syrian mesquite (Prosopis farcta): a spiny low mound of bipinnate leaves with curled pods.
			new BushSpecies { Name = "Prosopis", Habit = BushHabit.Mound, Height = 0.8f, Width = 1.4f, Stems = 10, StemRadius = 0.012f, Branches = 3,
				Bark = H("#6a5a40"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.15f, LeafA = H("#6a8a5a"), LeafB = H("#7a9a68"),
				Shell = 0.45f, Fullness = 4f, BareBase = 0f, Irregularity = 0.25f },
			// Sidr (Ziziphus spina-christi): a spreading thorny tree-shrub, glossy leaves, gold-orange drupes.
			new BushSpecies { Name = "Sidr", Habit = BushHabit.Vase, Height = 5f, Width = 5f, Stems = 4, StemRadius = 0.07f, Branches = 4,
				Bark = H("#7a6a56"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.5f, LeafA = H("#4a6a34"), LeafB = H("#5e8040"),
				Shell = 0.4f, Fullness = 3.2f, BareBase = 0.2f, Irregularity = 0.25f,
				Flowers = new[] { H("#d88a2a") }, FlowerShare = 0.05f, FlowerSize = 0.03f },
			// ── Mangrove (biome round, 2026-10-26) ── The landward fringe's shrubs: sea hibiscus, pemphis on the rag, lumnitzera on the rims.
			// Sea hibiscus (Talipariti tiliaceum): a spreading thicket of heart leaves, yellow cups with a maroon eye.
			new BushSpecies { Name = "SeaHibiscus", Habit = BushHabit.Vase, Height = 5f, Width = 6f, Stems = 6, StemRadius = 0.05f, Branches = 4,
				Bark = H("#7a6a52"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.5f, LeafA = H("#3a6a2a"), LeafB = H("#5a8a38"),
				Shell = 0.45f, Fullness = 2.9f, BareBase = 0.15f, Irregularity = 0.25f,
				Flowers = new[] { H("#f0d030"), H("#e07020") }, FlowerShare = 0.1f, FlowerSize = 0.1f },
			// Pemphis (Pemphis acidula): a gnarled grey mound of tiny leaves on the rag, white-pink flowers.
			new BushSpecies { Name = "Pemphis", Habit = BushHabit.Mound, Height = 1.8f, Width = 2.5f, Stems = 10, StemRadius = 0.03f, Branches = 3,
				Bark = H("#8a867a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.25f, LeafA = H("#6a7a5a"), LeafB = H("#7a8a68"),
				Shell = 0.45f, Fullness = 3f, BareBase = 0.05f, Irregularity = 0.3f,
				Flowers = new[] { H("#f4ecee") }, FlowerShare = 0.06f, FlowerSize = 0.03f },
			// Lumnitzera (L. racemosa): fleshy spoon leaves crowded at the tips, white flower spikes.
			new BushSpecies { Name = "Lumnitzera", Habit = BushHabit.Upright, Height = 3f, Width = 2f, Stems = 6, StemRadius = 0.025f, Branches = 4,
				Bark = H("#6a5a46"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.3f, LeafA = H("#5a8a2a"), LeafB = H("#6a9a34"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.15f, Irregularity = 0.25f,
				Flowers = new[] { H("#f6f4ee") }, FlowerShare = 0.06f, FlowerSize = 0.04f },
			// ── Lake and River (biome round, 2026-10-26) ── Purple-willow scrub on the lake's gravel strand-top, and the dewberry trailing over the river's bars.
			new BushSpecies { Name = "LakeWillowScrub", Habit = BushHabit.Upright, Height = 1.2f, Width = 1.5f, Stems = 12, StemRadius = 0.012f, Branches = 3,
				Bark = H("#6a2a4a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.16f, LeafA = H("#6a8a7a"), LeafB = H("#7a9a8a"),
				Shell = 0.4f, Fullness = 3f, BareBase = 0.1f, Irregularity = 0.25f, Deciduous = true },
			new BushSpecies { Name = "Dewberry", Habit = BushHabit.Prostrate, Height = 0.5f, Width = 2.5f, Stems = 9, StemRadius = 0.012f, Branches = 3,
				Bark = H("#5a3a4a"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.28f, LeafA = H("#4a6a3a"), LeafB = H("#5e7e46"),
				Shell = 0.4f, Fullness = 2.6f, BareBase = 0.05f, Irregularity = 0.3f, Deciduous = true,
				Flowers = new[] { H("#4a5a8a") }, FlowerShare = 0.12f, FlowerSize = 0.03f },
			// ── Rocky Terrain (biome round, 2026-10-26) ── Teresken (winterfat) in the gravel, the Pamir rose along the streams, the Bukhara almond on the warm sun-slopes.
			new BushSpecies { Name = "Teresken", Habit = BushHabit.Mound, Height = 0.5f, Width = 0.75f, Stems = 20, StemRadius = 0.012f, Branches = 3,
				Bark = H("#b8b0a0"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.14f, LeafA = H("#a8a898"), LeafB = H("#c0c0b0"),
				Shell = 0.35f, Fullness = 3.6f, BareBase = 0.1f, Irregularity = 0.25f,
				Flowers = new[] { H("#ece8e0") }, FlowerShare = 0.3f, FlowerSize = 0.03f },
			new BushSpecies { Name = "PamirRose", Habit = BushHabit.Arching, Height = 1.5f, Width = 2f, Stems = 7, StemRadius = 0.014f, Branches = 3,
				Bark = H("#7a5a48"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.19f, LeafA = H("#7a8a7a"), LeafB = H("#8a9a8a"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.1f, Irregularity = 0.3f, Deciduous = true,
				Flowers = new[] { H("#f4f0e8"), H("#b02a1a") }, FlowerShare = 0.05f, FlowerSize = 0.05f },
			new BushSpecies { Name = "BukharaAlmond", Habit = BushHabit.Upright, Height = 2.5f, Width = 2f, Stems = 5, StemRadius = 0.02f, Branches = 3,
				Bark = H("#6a625a"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#6a7a4a"), LeafB = H("#7a8a58"),
				Shell = 0.4f, Fullness = 2.6f, BareBase = 0.2f, Irregularity = 0.3f, Deciduous = true,
				Flowers = new[] { H("#f0c0c8") }, FlowerShare = 0.15f, FlowerSize = 0.04f },
			// ── Geyser Basin (biome round, 2026-10-26) ── The woolly willow of the gullies and halos, the tea-leaf willow of the banks.
			new BushSpecies { Name = "WoollyWillow", Habit = BushHabit.Spreading, Height = 0.9f, Width = 1.4f, Stems = 10, StemRadius = 0.015f, Branches = 3,
				Bark = H("#6a5a48"), LeafCell = FoliageCell.BushSmall, LeafSize = 0.2f, LeafA = H("#a8b0a0"), LeafB = H("#c8ccc0"),
				Shell = 0.4f, Fullness = 3.4f, BareBase = 0.1f, Irregularity = 0.3f, Deciduous = true,
				Flowers = new[] { H("#e0c040") }, FlowerShare = 0.2f, FlowerSize = 0.03f },
			new BushSpecies { Name = "TeaLeafWillow", Habit = BushHabit.Mound, Height = 1.2f, Width = 1.5f, Stems = 9, StemRadius = 0.014f, Branches = 3,
				Bark = H("#7a3a2a"), LeafCell = FoliageCell.BushBroad, LeafSize = 0.22f, LeafA = H("#2e4a26"), LeafB = H("#8a9a88"),
				Shell = 0.4f, Fullness = 4f, BareBase = 0.03f, Irregularity = 0.25f, Deciduous = true },
		};

		/// <summary>The shrub species with this name.</summary>
		public static bool TryBush(string name, out BushSpecies species)
		{
			foreach (BushSpecies b in Bushes)
			{
				if (b.Name == name)
				{
					species = b;
					return true;
				}
			}
			species = default;
			return false;
		}

		/// <summary>Every shrub payload file: three meshes a species.</summary>
		private static IEnumerable<string> BushPayloadPaths()
		{
			foreach (BushSpecies b in Bushes)
			{
				for (int lod = 0; lod < BushMeshes.Levels; lod++)
				{
					yield return MeshPath(BushMesh(b.Name, lod));
				}
			}
		}

		/// <summary>Every shrub wrapper: a material and a prefab a species.</summary>
		private static IEnumerable<string> BushWrapperPaths()
		{
			foreach (BushSpecies b in Bushes)
			{
				yield return MaterialPath(BushPrefab(b.Name));
			}
		}

		private static IEnumerable<string> BushPrefabNames()
		{
			foreach (BushSpecies b in Bushes)
			{
				yield return BushPrefab(b.Name);
			}
		}
	}
}
#endif
