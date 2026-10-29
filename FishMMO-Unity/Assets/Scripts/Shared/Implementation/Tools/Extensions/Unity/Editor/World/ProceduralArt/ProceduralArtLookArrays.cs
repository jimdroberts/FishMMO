#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The look arrays (2026-10-28): the props' generated surface maps written as Texture2DArray assets, one set of three arrays per
	/// surface family — albedo and emission (sRGB), normals, gloss and occlusion (linear) — and the families' materials naming their
	/// slices instead of 2D maps.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The props wear one material per texture set and tint (1816 Structure_*, 115 Rock_*, …): ~30 materials in view at
	/// Moron 2, each a material switch outside the SRP Batcher. The GPU-driven renderer draws a family of them through one material
	/// (TerrainGpuRenderer.Looks.cs) — which needs every member's maps in the same textures. Copying them into arrays at run time
	/// kept two copies of every map in memory; written here, there is one: the arrays are the only textures these maps exist as,
	/// and every user — the GPU-driven family, a prop's own MeshRenderer, a gathering node's tinted copy, a cave's rock — samples
	/// the same slices.
	/// </para>
	/// <para>
	/// <b>The sets.</b> <see cref="Sets"/>: the structure kit's surfaces (each with its finishes' albedos), the cave glow art's
	/// ramps, the rock types' surfaces and the ice surfaces. A set is cut into chunks of whole surfaces so no array passes
	/// <see cref="MaxLayers"/> (WebGPU's default <c>maxTextureArrayLayers</c>) or a source image <see cref="MaxSourceSide"/>; a
	/// chunk's arrays are <c>LookArray_{Set}_{Kind}[_{chunk}].png</c> in the textures folder (<see cref="ArrayPath"/>), each a
	/// flipbook of its slices — left to right, top to bottom, <see cref="Grid"/> columns by rows — that
	/// <see cref="ProceduralArtTextureImport"/> imports as a <see cref="Texture2DArray"/> with exactly the settings the single maps
	/// had (compression, mips, filtering, wrap; sRGB colour, normal-map normals, linear masks). A grid's spare cells are blank
	/// layers (at most a row's worth short of square, chosen to waste the fewest).
	/// </para>
	/// <para>
	/// <b>The materials.</b> <see cref="Bind"/> puts a map's array in the slot's array property, its slice in
	/// <c>_FishLookSlices</c> (albedo, normal, gloss, occlusion) or <c>_FishLookEmissionSlice</c>, leaves the 2D slot empty, and
	/// turns <see cref="Keyword"/> on (FishWeatherLitInput.hlsl). A map that is no set's (a legacy stone on a ground surface) stays a
	/// 2D texture. Nothing alpha-tested is array-backed: the depth and shadow passes read only the 2D albedo.
	/// </para>
	/// <para>
	/// <b>Memory.</b> An array is loaded whole: a scene that wears one slice of a set loads all of the set's layers. The single maps
	/// it replaces are no longer written, referenced or shipped (their old files are left on disk, unreferenced, for the owner to
	/// delete; ProceduralArtOrphans lists the rock ones).
	/// </para>
	/// </remarks>
	public static class ProceduralArtLookArrays
	{
		/// <summary>The shader keyword of an array-backed material (FishMMO/Weather Lit; FishMMO.Client TerrainLookMath.Keyword).</summary>
		public const string Keyword = "_FISH_LOOK_ARRAY";

		/// <summary>The file name prefix of an array's flipbook.</summary>
		public const string FilePrefix = "LookArray_";

		/// <summary>The most layers an array holds: WebGPU's default <c>maxTextureArrayLayers</c>.</summary>
		public const int MaxLayers = 256;

		/// <summary>The largest side of a flipbook's source image the texture importer takes.</summary>
		public const int MaxSourceSide = 16384;

		/// <summary>The three arrays of a set.</summary>
		public enum Kind
		{
			/// <summary>Albedo and emission maps, sRGB.</summary>
			Colour,
			/// <summary>Normal maps.</summary>
			Normal,
			/// <summary>Masks (metallic, occlusion, height, smoothness), linear.</summary>
			Mask,
		}

		public static readonly Kind[] Kinds = { Kind.Colour, Kind.Normal, Kind.Mask };

		/// <summary>The material property each kind's array is bound to (FishWeatherLit.shader).</summary>
		public static string ArrayProperty(Kind kind) => kind == Kind.Colour ? "_FishLookColour" : kind == Kind.Normal ? "_FishLookNormal" : "_FishLookMask";

		/// <summary>The material's slices (albedo, normal, gloss, occlusion; −1 empty) and its emission slice.</summary>
		public const string SlicesProperty = "_FishLookSlices", EmissionSliceProperty = "_FishLookEmissionSlice";

		/// <summary>The texture slots an array-backed material fills from its arrays, in <c>_FishLookSlices</c>' order, then emission.</summary>
		public static readonly string[] Slots = { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" };

		/// <summary>The 2D map properties an array-backed material leaves empty (the slots, the specular workflow's gloss map, and URP's legacy main texture).</summary>
		public static readonly string[] EmptiedProperties = { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_EmissionMap", "_MainTex" };

		/// <summary>A slot's component of <c>_FishLookSlices</c> (0..3), 4 for emission, −1 for a property no array fills.</summary>
		public static int SlotOf(string property)
		{
			switch (property)
			{
				case "_BaseMap": return 0;
				case "_BumpMap": return 1;
				case "_MetallicGlossMap":
				case "_SpecGlossMap": return 2;
				case "_OcclusionMap": return 3;
				case "_EmissionMap": return 4;
				default: return -1;
			}
		}

		/// <summary>The array a slot's map lies in: albedo and emission colour, normal its own, gloss and occlusion the mask.</summary>
		public static Kind KindOfSlot(int slot) => slot == 0 || slot == 4 ? Kind.Colour : slot == 1 ? Kind.Normal : Kind.Mask;

		/// <summary>One surface of a set: its colour maps (albedo, a finish's albedo, an emission ramp), normal and mask (null: none).</summary>
		public sealed class Surface
		{
			public string Name;
			public string[] Colour;
			public string Normal;
			public string Mask;

			public string MapOf(Kind kind, int i) => kind == Kind.Colour ? Colour[i] : kind == Kind.Normal ? Normal : Mask;
			public int CountOf(Kind kind) => kind == Kind.Colour ? Colour.Length : (kind == Kind.Normal ? Normal : Mask) != null ? 1 : 0;
		}

		/// <summary>A surface family: its name (in the file names), its maps' size, and its surfaces in slice order.</summary>
		public sealed class Set
		{
			public string Name;
			public int Size;
			public Surface[] Surfaces;
		}

		/// <summary>One array: its flipbook's path, set, kind, chunk, slice size, grid and members (single map names) in slice order.</summary>
		public sealed class Layout
		{
			public string Path;
			public string Set;
			public Kind Kind;
			public int Chunk;
			public int Size;
			public int Columns, Rows;
			public readonly List<string> Members = new List<string>();
			public int Width => Columns * Size;
			public int Height => Rows * Size;
			public int Layers => Columns * Rows;
		}

		/// <summary>A map's place: the array's flipbook path and the slice.</summary>
		public readonly struct Slice
		{
			public readonly string Array;
			public readonly int Index;
			public readonly Kind Kind;

			public Slice(string array, int index, Kind kind)
			{
				Array = array;
				Index = index;
				Kind = kind;
			}
		}

		/// <summary>The structure kit's surfaces, the cave glow art's maps, the rock types' and the ice surfaces, in catalogue order.</summary>
		public static Set[] BuildSets()
		{
			var structure = new List<Surface>();
			foreach (string surface in StructureSurfaces.Names)
			{
				var colour = new List<string>();
				foreach (StructureFinish finish in StructurePieces.Finishes)
				{
					colour.Add(ProceduralArtCatalogue.StructureTexture(surface, "Albedo", finish));
				}
				structure.Add(new Surface
				{
					Name = surface,
					Colour = colour.ToArray(),
					Normal = ProceduralArtCatalogue.StructureTexture(surface, "Normal"),
					Mask = ProceduralArtCatalogue.StructureTexture(surface, "Mask"),
				});
			}
			var caveGlow = new[]
			{
				new Surface { Name = "CaveGlow", Colour = new[] { ProceduralArtCatalogue.CaveGlowAlbedoPath, ProceduralArtCatalogue.CaveGlowEmissionPath } },
				new Surface { Name = "CaveOre", Colour = new[] { ProceduralArtCatalogue.CaveOreAlbedoPath } },
			};
			var rock = new List<Surface>();
			foreach (RockType type in RockTypes.All)
			{
				rock.Add(new Surface
				{
					Name = type.Name,
					Colour = new[] { RockArtNames.RockSurfaceTexture(type.Name, "Albedo") },
					Normal = RockArtNames.RockSurfaceTexture(type.Name, "Normal"),
					Mask = RockArtNames.RockSurfaceTexture(type.Name, "Mask"),
				});
			}
			var ice = new List<Surface>();
			foreach (SurfaceRecipe recipe in IceSurfaces.Recipes)
			{
				ice.Add(new Surface
				{
					Name = recipe.Name,
					Colour = new[] { RockArtNames.IceSurfaceTexture(recipe.Name, "Albedo") },
					Normal = RockArtNames.IceSurfaceTexture(recipe.Name, "Normal"),
					Mask = RockArtNames.IceSurfaceTexture(recipe.Name, "Mask"),
				});
			}
			return new[]
			{
				new Set { Name = StructureSetName, Size = StructureSurfaces.Size, Surfaces = structure.ToArray() },
				new Set { Name = CaveGlowSetName, Size = ProceduralArtCatalogue.CaveGlowTextureSize, Surfaces = caveGlow },
				new Set { Name = RockSetName, Size = ProceduralArtCatalogue.RockSurfaceSize, Surfaces = rock.ToArray() },
				new Set { Name = IceSetName, Size = ProceduralArtCatalogue.RockSurfaceSize, Surfaces = ice.ToArray() },
			};
		}

		public const string StructureSetName = "Structure", CaveGlowSetName = "CaveGlow", RockSetName = "RockSurface", IceSetName = "IceSurface";

		private static Set[] sets;
		private static List<Layout> layouts;
		private static Dictionary<string, Slice> slices;

		/// <summary>The sets, built once a domain (the catalogues are code).</summary>
		public static Set[] Sets => sets ??= BuildSets();

		/// <summary>Every array, set by set, chunk by chunk, kind by kind (a kind no surface of a chunk has makes none).</summary>
		public static List<Layout> Layouts
		{
			get
			{
				if (layouts == null)
				{
					var all = new List<Layout>();
					foreach (Set set in Sets)
					{
						all.AddRange(LayoutsOf(set));
					}
					layouts = all;
				}
				return layouts;
			}
		}

		/// <summary>A set's arrays: whole surfaces to a chunk while every kind's layer count stays within <see cref="ChunkCapacity"/>.</summary>
		public static List<Layout> LayoutsOf(Set set)
		{
			int capacity = ChunkCapacity(set.Size);
			var chunks = new List<List<Surface>>();
			var current = new List<Surface>();
			var counts = new int[Kinds.Length];
			foreach (Surface surface in set.Surfaces)
			{
				bool fits = true;
				foreach (Kind kind in Kinds)
				{
					fits &= counts[(int)kind] + surface.CountOf(kind) <= capacity;
				}
				if (!fits && current.Count > 0)
				{
					chunks.Add(current);
					current = new List<Surface>();
					Array.Clear(counts, 0, counts.Length);
				}
				current.Add(surface);
				foreach (Kind kind in Kinds)
				{
					counts[(int)kind] += surface.CountOf(kind);
				}
			}
			if (current.Count > 0)
			{
				chunks.Add(current);
			}
			var result = new List<Layout>();
			for (int chunk = 0; chunk < chunks.Count; chunk++)
			{
				foreach (Kind kind in Kinds)
				{
					var layout = new Layout { Path = ArrayPath(set.Name, kind, chunk), Set = set.Name, Kind = kind, Chunk = chunk, Size = set.Size };
					foreach (Surface surface in chunks[chunk])
					{
						for (int i = 0; i < surface.CountOf(kind); i++)
						{
							layout.Members.Add(surface.MapOf(kind, i));
						}
					}
					if (layout.Members.Count == 0)
					{
						continue;
					}
					if (!Grid(layout.Members.Count, set.Size, out layout.Columns, out layout.Rows))
					{
						throw new InvalidOperationException($"{layout.Path}: {layout.Members.Count} maps of {set.Size}² do not fit one flipbook");
					}
					result.Add(layout);
				}
			}
			return result;
		}

		/// <summary>The most layers a chunk of maps this size takes: the layer limit, or what a flipbook of <see cref="MaxSourceSide"/> holds.</summary>
		public static int ChunkCapacity(int size)
		{
			int side = Math.Max(1, MaxSourceSide / Math.Max(1, size));
			return Math.Min(MaxLayers, side * side);
		}

		/// <summary>
		/// The flipbook grid for <paramref name="count"/> maps of <paramref name="size"/>²: the columns and rows whose cells hold them with
		/// the fewest spare (each a blank layer of GPU memory), the squarer grid on a tie, neither side past <see cref="MaxSourceSide"/>.
		/// False when they cannot fit.
		/// </summary>
		public static bool Grid(int count, int size, out int columns, out int rows)
		{
			columns = rows = 0;
			if (count <= 0 || size <= 0 || count > MaxLayers)
			{
				return false;
			}
			int max = MaxSourceSide / size;
			int bestWaste = int.MaxValue, bestSkew = int.MaxValue;
			for (int c = 1; c <= max; c++)
			{
				int r = (count + c - 1) / c;
				if (r > max)
				{
					continue;
				}
				int waste = c * r - count, skew = Math.Abs(c - r);
				if (waste < bestWaste || (waste == bestWaste && skew < bestSkew))
				{
					bestWaste = waste;
					bestSkew = skew;
					columns = c;
					rows = r;
				}
			}
			return columns > 0;
		}

		/// <summary>An array's flipbook path: <c>LookArray_{set}_{kind}</c>, <c>_{chunk}</c> after the first chunk.</summary>
		public static string ArrayPath(string set, Kind kind, int chunk)
		{
			return $"{ProceduralArtCatalogue.TexturesFolder}/{FilePrefix}{set}_{kind}{(chunk > 0 ? "_" + chunk : string.Empty)}.png";
		}

		/// <summary>The set and kind of an array's file name (no folder, no extension); false for any other name.</summary>
		public static bool TryParseArrayName(string fileName, out string set, out Kind kind)
		{
			set = null;
			kind = Kind.Colour;
			if (string.IsNullOrEmpty(fileName) || !fileName.StartsWith(FilePrefix, StringComparison.Ordinal))
			{
				return false;
			}
			string[] parts = fileName.Substring(FilePrefix.Length).Split('_');
			if (parts.Length < 2 || !Enum.TryParse(parts[1], false, out kind))
			{
				return false;
			}
			set = parts[0];
			return true;
		}

		/// <summary>A set's slice size by name, or 0.</summary>
		public static int SizeOf(string set)
		{
			foreach (Set s in Sets)
			{
				if (s.Name == set)
				{
					return s.Size;
				}
			}
			return 0;
		}

		/// <summary>Where a single map's texels now lie, when its family is array-backed.</summary>
		public static bool TryGetSlice(string singlePath, out Slice slice)
		{
			if (slices == null)
			{
				var index = new Dictionary<string, Slice>(StringComparer.Ordinal);
				foreach (Layout layout in Layouts)
				{
					for (int i = 0; i < layout.Members.Count; i++)
					{
						index[layout.Members[i]] = new Slice(layout.Path, i, layout.Kind);
					}
				}
				slices = index;
			}
			return slices.TryGetValue(singlePath ?? string.Empty, out slice);
		}

		/// <summary>Every array flipbook the generator writes (payload).</summary>
		public static IEnumerable<string> ArrayPaths()
		{
			foreach (Layout layout in Layouts)
			{
				yield return layout.Path;
			}
		}

		/// <summary>The arrays of one set.</summary>
		public static IEnumerable<Layout> LayoutsOfSet(string set)
		{
			foreach (Layout layout in Layouts)
			{
				if (layout.Set == set)
				{
					yield return layout;
				}
			}
		}

		/// <summary>
		/// A cell's top-left texel in a flipbook stored bottom row first (Unity's texture layout): slice <paramref name="slice"/> sits
		/// at column slice % columns of row slice / columns counted from the TOP, as the importer reads cells.
		/// </summary>
		public static void CellOrigin(Layout layout, int slice, out int x, out int y)
		{
			int column = slice % layout.Columns, rowFromTop = slice / layout.Columns;
			x = column * layout.Size;
			y = (layout.Rows - 1 - rowFromTop) * layout.Size;
		}

		/// <summary>
		/// Fills slot <paramref name="property"/> of an array-backed material from <paramref name="array"/> at <paramref name="slice"/>:
		/// the kind's array property, the slice's component, the 2D slot emptied, the keyword on. Returns a problem, or null.
		/// </summary>
		public static string Bind(Material material, string property, Texture2DArray array, int slice)
		{
			int slot = SlotOf(property);
			if (material == null || slot < 0)
			{
				return $"{property} is no look slot";
			}
			if (array == null)
			{
				return $"{property}'s look array did not load";
			}
			string arrayProperty = ArrayProperty(KindOfSlot(slot));
			Texture bound = material.HasTexture(arrayProperty) ? material.GetTexture(arrayProperty) : null;
			if (bound != null && bound != array)
			{
				return $"{property} is in {array.name}, but the material's {arrayProperty} is already {bound.name}";
			}
			material.SetTexture(arrayProperty, array);
			if (slot == 4)
			{
				material.SetFloat(EmissionSliceProperty, slice);
			}
			else
			{
				Vector4 s = material.GetVector(SlicesProperty);
				s[slot] = slice;
				material.SetVector(SlicesProperty, s);
			}
			if (material.HasTexture(property))
			{
				material.SetTexture(property, null);
			}
			material.EnableKeyword(Keyword);
			return null;
		}

		/// <summary>
		/// Whether an array-backed material's slot is filled: its array bound and its slice set (a pure read of what
		/// <see cref="Bind"/> wrote, for <see cref="ProceduralArtWrapperCheck"/>).
		/// </summary>
		public static bool SlotFilled(int slot, bool arrayBound, Vector4 slicesValue, float emissionSlice)
		{
			if (slot < 0 || !arrayBound)
			{
				return false;
			}
			return (slot == 4 ? emissionSlice : slicesValue[slot]) >= 0f;
		}
	}
}
#endif
