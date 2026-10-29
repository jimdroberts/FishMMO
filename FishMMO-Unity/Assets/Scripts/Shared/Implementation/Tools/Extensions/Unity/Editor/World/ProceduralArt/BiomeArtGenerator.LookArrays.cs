#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The look arrays' writing (<see cref="ProceduralArtLookArrays"/>): the surface steps hand each map's pixels to
	/// <see cref="PutLook"/> instead of writing a PNG of their own, <see cref="WriteLookArrays"/> writes each array's flipbook PNG
	/// (imported with the rest at the shared import step), and the material writers fill their slots through
	/// <see cref="SetSurfaceMap"/>, which names an array slice where the map is array-backed and a 2D texture where it is not.
	/// </summary>
	/// <remarks>
	/// An array is all-or-nothing: when it is due (its family rebuilt, or its file missing) every member is synthesised, since the
	/// flipbook is written whole; when it is not, nothing of its set is synthesised at all.
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>An array's flipbook while its members are put in: RGBA32, bottom row first, one cell a slice.</summary>
		private sealed class LookFlipbook
		{
			public ProceduralArtLookArrays.Layout Layout;
			public Color32[] Pixels;
			public bool[] Filled;
		}

		/// <summary>Whether any of a set's arrays is due this run: what decides whether its surfaces are synthesised.</summary>
		private static bool LookArraysDue(Context c, string set)
		{
			foreach (ProceduralArtLookArrays.Layout layout in ProceduralArtLookArrays.LayoutsOfSet(set))
			{
				if (ShouldWrite(c, layout.Path))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// A surface map's pixels into its array's flipbook, when the map is array-backed (true; nothing else is written for it) —
		/// false when it is not, and the caller writes its PNG as before.
		/// </summary>
		private static bool PutLook(Context c, string singlePath, Color32[] pixels, int width, int height)
		{
			if (!ProceduralArtLookArrays.TryGetSlice(singlePath, out ProceduralArtLookArrays.Slice slice))
			{
				return false;
			}
			if (!c.LookFlipbooks.TryGetValue(slice.Array, out LookFlipbook book))
			{
				ProceduralArtLookArrays.Layout layout = null;
				foreach (ProceduralArtLookArrays.Layout l in ProceduralArtLookArrays.Layouts)
				{
					if (l.Path == slice.Array)
					{
						layout = l;
						break;
					}
				}
				// Spare cells stay blank (transparent black): layers no material names.
				book = new LookFlipbook { Layout = layout, Pixels = new Color32[layout.Width * layout.Height], Filled = new bool[layout.Members.Count] };
				c.LookFlipbooks.Add(slice.Array, book);
			}
			int size = book.Layout.Size;
			if (pixels == null || width != size || height != size || pixels.Length < size * size)
			{
				c.Report.Problems.Add($"{singlePath}: {width}x{height} is not its look array's {size}² ({book.Layout.Path}); its slice is left blank.");
				return true;
			}
			ProceduralArtLookArrays.CellOrigin(book.Layout, slice.Index, out int x0, out int y0);
			int stride = book.Layout.Width;
			for (int y = 0; y < size; y++)
			{
				Array.Copy(pixels, y * size, book.Pixels, (y0 + y) * stride + x0, size);
			}
			book.Filled[slice.Index] = true;
			return true;
		}

		/// <summary>Writes each of a set's flipbooks that was filled this run (every member, or it is reported and not written).</summary>
		private static void WriteLookArrays(Context c, string set)
		{
			foreach (ProceduralArtLookArrays.Layout layout in ProceduralArtLookArrays.LayoutsOfSet(set))
			{
				if (!c.LookFlipbooks.TryGetValue(layout.Path, out LookFlipbook book))
				{
					continue;
				}
				c.LookFlipbooks.Remove(layout.Path);
				int missing = 0;
				foreach (bool filled in book.Filled)
				{
					missing += filled ? 0 : 1;
				}
				if (missing > 0)
				{
					c.Report.Problems.Add($"{layout.Path}: {missing} of its {layout.Members.Count} maps were not made this run; the array was not written.");
					continue;
				}
				WritePng(c, layout.Path, book.Pixels, layout.Width, layout.Height);
			}
		}

		/// <summary>
		/// Fills a material's map slot with a generated surface map: its look array and slice where the map is array-backed
		/// (<see cref="ProceduralArtLookArrays.Bind"/>), else the 2D texture at <paramref name="path"/>.
		/// </summary>
		private static void SetSurfaceMap(Context c, Material material, string property, string path)
		{
			if (!ProceduralArtLookArrays.TryGetSlice(path, out ProceduralArtLookArrays.Slice slice))
			{
				material.SetTexture(property, LoadTexture(path));
				return;
			}
			var array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(slice.Array);
			string problem = ProceduralArtLookArrays.Bind(material, property, array, slice.Index);
			if (problem != null)
			{
				c.Report.Problems.Add($"{material.name}: {problem}.");
			}
		}

		/// <summary>
		/// Every material in the project that still names a single map now in a look array — a generated one this run did not
		/// rewrite, a gathering node's tinted copy, a cave's rock (a sub-asset of its scene's caves asset) — pointed at the map's
		/// array slice instead, in place (references to it survive), so nothing loads the single maps any more. Only while the old
		/// files are on disk (nothing can name one that is gone). A material that would keep a 2D map beside its slices is left as
		/// it is and reported, unless it is cave rock (whose empty slots read its own 2D maps: an inner wall over its shell), as is
		/// an alpha-tested one (its depth and shadow passes read the 2D albedo).
		/// </summary>
		private static void RebindLookArrayUsers(Context c)
		{
			var singles = new HashSet<string>(StringComparer.Ordinal);
			foreach (ProceduralArtLookArrays.Layout layout in ProceduralArtLookArrays.Layouts)
			{
				foreach (string member in layout.Members)
				{
					if (File.Exists(member))
					{
						singles.Add(member);
					}
				}
			}
			if (singles.Count == 0)
			{
				return;
			}
			int rebound = 0, assets = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:Material"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
				{
					continue;
				}
				bool uses = false;
				foreach (string dependency in AssetDatabase.GetDependencies(path, false))
				{
					if (singles.Contains(dependency))
					{
						uses = true;
						break;
					}
				}
				if (!uses)
				{
					continue;
				}
				bool changed = false;
				foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
				{
					if (asset is Material material && RebindLookUser(c, material, path))
					{
						EditorUtility.SetDirty(material);
						changed = true;
						rebound++;
					}
				}
				if (changed)
				{
					assets++;
					AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));
				}
			}
			if (rebound > 0)
			{
				Debug.Log($"[Biome art] Look arrays: {rebound} material(s) in {assets} asset(s) that named single maps now read the arrays' slices.");
			}
		}

		/// <summary>One material's single maps onto their array slices (see <see cref="RebindLookArrayUsers"/>); true when it changed.</summary>
		private static bool RebindLookUser(Context c, Material material, string path)
		{
			Shader shader = material.shader;
			if (shader == null || !material.HasVector(ProceduralArtLookArrays.SlicesProperty) || !shader.keywordSpace.FindKeyword(ProceduralArtLookArrays.Keyword).isValid)
			{
				return false;
			}
			// A dry pass first: what would move, and what would stay 2D.
			bool any = false, mixed = false;
			foreach (string property in ProceduralArtLookArrays.EmptiedProperties)
			{
				Texture t = material.HasTexture(property) ? material.GetTexture(property) : null;
				if (t == null)
				{
					continue;
				}
				if (ProceduralArtLookArrays.TryGetSlice(AssetDatabase.GetAssetPath(t), out ProceduralArtLookArrays.Slice planned))
				{
					if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(planned.Array) == null)
					{
						c.Report.Problems.Add($"{path} ({material.name}): {planned.Array} did not load as a texture array; left on its single maps.");
						return false;
					}
					any = true;
				}
				else if (ProceduralArtLookArrays.SlotOf(property) >= 0)
				{
					mixed = true;
				}
			}
			if (!any)
			{
				return false;
			}
			bool caveRock = material.IsKeywordEnabled("_FISH_CAVE_ROCK");
			if ((mixed && !caveRock) || material.IsKeywordEnabled("_ALPHATEST_ON"))
			{
				c.Report.Problems.Add($"{path} ({material.name}): names single maps that are look array slices now, but {(mixed && !caveRock ? "keeps other 2D maps beside them" : "is alpha-tested")}; left as it is (the old maps are still on disk).");
				return false;
			}
			foreach (string property in ProceduralArtLookArrays.EmptiedProperties)
			{
				Texture t = material.HasTexture(property) ? material.GetTexture(property) : null;
				if (t == null || !ProceduralArtLookArrays.TryGetSlice(AssetDatabase.GetAssetPath(t), out ProceduralArtLookArrays.Slice slice))
				{
					continue;
				}
				if (ProceduralArtLookArrays.SlotOf(property) < 0)
				{
					// URP's legacy main texture: read by nothing, but it would keep the single map loaded.
					material.SetTexture(property, null);
					continue;
				}
				string problem = ProceduralArtLookArrays.Bind(material, property, AssetDatabase.LoadAssetAtPath<Texture2DArray>(slice.Array), slice.Index);
				if (problem != null)
				{
					c.Report.Problems.Add($"{path} ({material.name}): {problem}.");
				}
			}
			return true;
		}
	}
}
#endif
