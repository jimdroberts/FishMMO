using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's paths rasterised for the GPU: a distance field half a metre a texel, kept only in the 16 m pages a path
	/// passes through. What the terrain, the grass and the details read to draw, thin and clear a path
	/// (FishGroundPaths.hlsl).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a distance field.</b> The splat is two metres a texel: a trail under a metre wide painted there is a smear
	/// four metres across. A distance to the nearest path's EDGE, bilinearly filtered, is exact along a straight stretch at
	/// any magnification, so the shader can cut a crisp edge, fray it with noise, lay two wheel ruts a gauge apart and
	/// leave grass between them — none of which a weight could say.
	/// </para>
	/// <para>
	/// <b>Why pages.</b> Paths cover a few percent of a scene. A whole-scene field at this resolution would be hundreds of
	/// megabytes; the pages a path touches are a few thousand, packed into one atlas. A page table (one texel a page)
	/// says where each page is in the atlas; a page holds 32 × 32 texels plus a one-texel border copied from its
	/// neighbours' ground, so a bilinear fetch never reads another page's texels.
	/// </para>
	/// <para>
	/// <b>A texel</b> (RGBA8, linear): r the signed distance to the nearest path's edge (−8 m inside … +3 m outside),
	/// g that path's half width (÷ 8 m), b its wear, a its surface code (<see cref="ScenePathStyle.SurfaceCode"/>).
	/// Where two paths meet, the texel takes the one whose edge is nearer — a road's verge, not the trail running into it.
	/// </para>
	/// <para>Pure data, built by the cut and baked to two textures beside the terrain (ScenePathBake), which
	/// <see cref="ScenePathSurfaceBinder"/> binds.</para>
	/// </remarks>
	public sealed class ScenePathField
	{
		public const float PageMetres = 16f;
		public const int PageTexels = 32;
		public const float TexelMetres = PageMetres / PageTexels;
		/// <summary>A page's stored side: its texels and a one-texel border.</summary>
		public const int StoredTexels = PageTexels + 2;
		/// <summary>The encoded range of the edge distance: InsideMetres inside the edge to OutsideMetres outside it.</summary>
		public const float InsideMetres = 8f;
		public const float OutsideMetres = 3f;
		public const float HalfWidthRange = 8f;
		/// <summary>The most pages one side of the atlas holds: its coordinates are 8-bit in the page table.</summary>
		public const int MaxAtlasPagesPerSide = 240;

		/// <summary>World x/z of page (0, 0)'s corner.</summary>
		public Vector2 Origin;
		/// <summary>The page table's size, pages.</summary>
		public int TableWidth, TableDepth;
		/// <summary>The atlas's size, pages a side (square).</summary>
		public int AtlasPages;
		/// <summary>Page table texels (RGBA32): r, g the page's atlas column and row, a 255 where the page is stored.</summary>
		public Color32[] Table = Array.Empty<Color32>();
		/// <summary>Atlas texels (RGBA32), AtlasPages × StoredTexels a side.</summary>
		public Color32[] Atlas = Array.Empty<Color32>();
		public int PageCount;
		/// <summary>Pages that did not fit the atlas (should be zero; reported, never silent).</summary>
		public int Dropped;

		public int AtlasTexels => AtlasPages * StoredTexels;

		/// <summary>Encodes an edge distance (metres, negative inside) as the r byte.</summary>
		public static byte EncodeEdge(float edge) => (byte)Mathf.RoundToInt(Mathf.Clamp01((edge + InsideMetres) / (InsideMetres + OutsideMetres)) * 255f);

		public static float DecodeEdge(byte r) => r / 255f * (InsideMetres + OutsideMetres) - InsideMetres;

		private sealed class Page
		{
			public readonly float[] Edge = new float[StoredTexels * StoredTexels];
			public readonly float[] Half = new float[StoredTexels * StoredTexels];
			public readonly float[] Wear = new float[StoredTexels * StoredTexels];
			public readonly float[] Surface = new float[StoredTexels * StoredTexels];

			public Page()
			{
				for (int i = 0; i < Edge.Length; i++)
				{
					Edge[i] = OutsideMetres;
				}
			}
		}

		/// <summary>
		/// Rasterises paths. Stretches on a bridge are left out (the deck is the way there, not the river bed under it).
		/// </summary>
		/// <param name="paths">Every path the scene draws.</param>
		public static ScenePathField Build(IEnumerable<ScenePath> paths)
		{
			var field = new ScenePathField();
			var list = new List<ScenePath>();
			float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
			if (paths != null)
			{
				foreach (ScenePath path in paths)
				{
					if (path == null || path.Count < 2 || path.HalfWidth == null || path.HalfWidth.Length != path.Count)
					{
						continue;
					}
					list.Add(path);
					foreach (Vector3 p in path.Points)
					{
						minX = Mathf.Min(minX, p.x);
						minZ = Mathf.Min(minZ, p.z);
						maxX = Mathf.Max(maxX, p.x);
						maxZ = Mathf.Max(maxZ, p.z);
					}
				}
			}
			if (list.Count == 0)
			{
				return field;
			}
			float pad = HalfWidthRange + OutsideMetres + PageMetres;
			field.Origin = new Vector2(Mathf.Floor((minX - pad) / PageMetres) * PageMetres, Mathf.Floor((minZ - pad) / PageMetres) * PageMetres);
			field.TableWidth = Mathf.Max(1, Mathf.CeilToInt((maxX + pad - field.Origin.x) / PageMetres));
			field.TableDepth = Mathf.Max(1, Mathf.CeilToInt((maxZ + pad - field.Origin.y) / PageMetres));

			var pages = new Dictionary<int, Page>();
			foreach (ScenePath path in list)
			{
				for (int i = 1; i < path.Count; i++)
				{
					if ((path.FlagsAt(i - 1) & ScenePathPointFlags.Bridge) != 0 && (path.FlagsAt(i) & ScenePathPointFlags.Bridge) != 0)
					{
						continue;
					}
					field.Segment(pages, path, i - 1, i);
				}
			}
			field.Pack(pages);
			return field;
		}

		/// <summary>Writes one segment's distance into every texel within its reach, keeping the nearest edge.</summary>
		private void Segment(Dictionary<int, Page> pages, ScenePath path, int ia, int ib)
		{
			Vector2 a = new Vector2(path.Points[ia].x, path.Points[ia].z), b = new Vector2(path.Points[ib].x, path.Points[ib].z);
			float ha = path.HalfWidth[ia], hb = path.HalfWidth[ib];
			float wa = path.Wear != null && path.Wear.Length > ia ? path.Wear[ia] : 1f, wb = path.Wear != null && path.Wear.Length > ib ? path.Wear[ib] : 1f;
			float sa = path.Surface != null && path.Surface.Length > ia ? ScenePathStyle.SurfaceCode((ScenePathSurface)path.Surface[ia]) : 0f;
			float sb = path.Surface != null && path.Surface.Length > ib ? ScenePathStyle.SurfaceCode((ScenePathSurface)path.Surface[ib]) : 0f;
			float reach = Mathf.Max(ha, hb) + OutsideMetres;
			Vector2 ab = b - a;
			float length2 = Mathf.Max(1e-6f, ab.sqrMagnitude);

			// Pages whose stored texels (their border included) lie within reach of the segment.
			float x0 = Mathf.Min(a.x, b.x) - reach, x1 = Mathf.Max(a.x, b.x) + reach;
			float z0 = Mathf.Min(a.y, b.y) - reach, z1 = Mathf.Max(a.y, b.y) + reach;
			int px0 = Mathf.Max(0, Mathf.FloorToInt((x0 - Origin.x - TexelMetres) / PageMetres));
			int px1 = Mathf.Min(TableWidth - 1, Mathf.FloorToInt((x1 - Origin.x + TexelMetres) / PageMetres));
			int pz0 = Mathf.Max(0, Mathf.FloorToInt((z0 - Origin.y - TexelMetres) / PageMetres));
			int pz1 = Mathf.Min(TableDepth - 1, Mathf.FloorToInt((z1 - Origin.y + TexelMetres) / PageMetres));
			for (int pz = pz0; pz <= pz1; pz++)
			{
				for (int px = px0; px <= px1; px++)
				{
					float pageX = Origin.x + px * PageMetres, pageZ = Origin.y + pz * PageMetres;
					// Stored texel j's centre is at (j - 1 + 0.5) texels from the page's corner (j = 0 is the border).
					int i0 = Mathf.Clamp(Mathf.FloorToInt((x0 - pageX) / TexelMetres + 0.5f), 0, StoredTexels - 1);
					int i1 = Mathf.Clamp(Mathf.CeilToInt((x1 - pageX) / TexelMetres + 0.5f), 0, StoredTexels - 1);
					int j0 = Mathf.Clamp(Mathf.FloorToInt((z0 - pageZ) / TexelMetres + 0.5f), 0, StoredTexels - 1);
					int j1 = Mathf.Clamp(Mathf.CeilToInt((z1 - pageZ) / TexelMetres + 0.5f), 0, StoredTexels - 1);
					int key = pz * TableWidth + px;
					pages.TryGetValue(key, out Page page);
					for (int j = j0; j <= j1; j++)
					{
						float z = pageZ + (j - 0.5f) * TexelMetres;
						for (int i = i0; i <= i1; i++)
						{
							float x = pageX + (i - 0.5f) * TexelMetres;
							float t = Mathf.Clamp01(((x - a.x) * ab.x + (z - a.y) * ab.y) / length2);
							float dx = a.x + ab.x * t - x, dz = a.y + ab.y * t - z;
							float half = Mathf.Lerp(ha, hb, t);
							float edge = Mathf.Sqrt(dx * dx + dz * dz) - half;
							if (edge >= OutsideMetres)
							{
								continue;
							}
							if (page == null)
							{
								page = new Page();
								pages[key] = page;
							}
							int k = j * StoredTexels + i;
							if (edge < page.Edge[k])
							{
								page.Edge[k] = edge;
								page.Half[k] = half;
								page.Wear[k] = Mathf.Lerp(wa, wb, t);
								page.Surface[k] = Mathf.Lerp(sa, sb, t);
							}
						}
					}
				}
			}
		}

		/// <summary>Packs the touched pages into the atlas in page-table order, and writes the table.</summary>
		private void Pack(Dictionary<int, Page> pages)
		{
			var keys = new List<int>(pages.Keys);
			keys.Sort();
			int count = Mathf.Min(keys.Count, MaxAtlasPagesPerSide * MaxAtlasPagesPerSide);
			Dropped = keys.Count - count;
			AtlasPages = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
			PageCount = count;
			Table = new Color32[TableWidth * TableDepth];
			int side = AtlasTexels;
			Atlas = new Color32[side * side];
			var empty = new Color32(255, 0, 0, 0);
			for (int i = 0; i < Atlas.Length; i++)
			{
				Atlas[i] = empty;
			}
			for (int n = 0; n < count; n++)
			{
				int key = keys[n];
				Page page = pages[key];
				int ax = n % AtlasPages, az = n / AtlasPages;
				Table[key] = new Color32((byte)ax, (byte)az, 0, 255);
				for (int j = 0; j < StoredTexels; j++)
				{
					int row = (az * StoredTexels + j) * side + ax * StoredTexels;
					for (int i = 0; i < StoredTexels; i++)
					{
						int k = j * StoredTexels + i;
						Atlas[row + i] = new Color32(
							EncodeEdge(page.Edge[k]),
							(byte)Mathf.RoundToInt(Mathf.Clamp01(page.Half[k] / HalfWidthRange) * 255f),
							(byte)Mathf.RoundToInt(Mathf.Clamp01(page.Wear[k]) * 255f),
							(byte)Mathf.RoundToInt(Mathf.Clamp01(page.Surface[k]) * 255f));
					}
				}
			}
		}

		/// <summary>
		/// The field at a world point as the shader reads it (bilinear inside the page), for tests and tools: false where
		/// no page is stored. Edge in metres (negative inside), half width in metres, wear and surface code 0 … 1.
		/// </summary>
		public bool Sample(float x, float z, out float edge, out float halfWidth, out float wear, out float surface)
		{
			edge = OutsideMetres;
			halfWidth = 0f;
			wear = 0f;
			surface = 0f;
			if (PageCount == 0)
			{
				return false;
			}
			float px = (x - Origin.x) / PageMetres, pz = (z - Origin.y) / PageMetres;
			int ix = Mathf.FloorToInt(px), iz = Mathf.FloorToInt(pz);
			if (ix < 0 || iz < 0 || ix >= TableWidth || iz >= TableDepth)
			{
				return false;
			}
			Color32 entry = Table[iz * TableWidth + ix];
			if (entry.a < 128)
			{
				return false;
			}
			// Continuous atlas coordinate: the page's base, its border, then texels (centre of texel t at t + 0.5).
			float u = entry.r * StoredTexels + 1f + (px - ix) * PageTexels - 0.5f;
			float v = entry.g * StoredTexels + 1f + (pz - iz) * PageTexels - 0.5f;
			int u0 = Mathf.FloorToInt(u), v0 = Mathf.FloorToInt(v);
			float fu = u - u0, fv = v - v0;
			Vector4 Texel(int uu, int vv)
			{
				Color32 c = Atlas[vv * AtlasTexels + uu];
				return new Vector4(c.r, c.g, c.b, c.a) / 255f;
			}
			Vector4 s = Vector4.Lerp(Vector4.Lerp(Texel(u0, v0), Texel(u0 + 1, v0), fu), Vector4.Lerp(Texel(u0, v0 + 1), Texel(u0 + 1, v0 + 1), fu), fv);
			edge = s.x * (InsideMetres + OutsideMetres) - InsideMetres;
			halfWidth = s.y * HalfWidthRange;
			wear = s.z;
			surface = s.w;
			return true;
		}
	}
}

