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
