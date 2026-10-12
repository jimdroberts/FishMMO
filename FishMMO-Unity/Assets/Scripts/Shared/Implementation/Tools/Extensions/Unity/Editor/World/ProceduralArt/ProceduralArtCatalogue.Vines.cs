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
