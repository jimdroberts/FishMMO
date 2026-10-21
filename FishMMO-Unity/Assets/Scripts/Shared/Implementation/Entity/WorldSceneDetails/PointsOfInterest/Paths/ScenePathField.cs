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
	/// </para>
	/// <para>
	/// <b>Where two paths meet</b> the edge is the nearer of theirs, and the rest is a blend of every way whose edge lies
	/// within <see cref="BlendMetres"/> of it, the nearer weighing more: a trail's earth runs on into the road it joins and
	/// thins into the gravel, rather than stopping at the line where the two edges are equally far (2026-10-19: "hard texture
	/// cuts" — the nearest way alone took the texel, so surface, wear and width all switched across one texel). Except
	/// inside a building's floor (<see cref="ScenePathPointFlags.Floor"/>), whose wear and surface nothing else drawn over
	/// it blends into (a door path's round end or a well's apron reaching through the wall would bring its lower wear, and
	/// so its grass, indoors); its edge is still the deepest of them, so the floor is as bare as the apron round it.
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
		/// <summary>
		/// How much further than the nearest edge another way's edge may be and still blend into a texel, metres: across a
		/// junction's seam its ways mix over about this width, past it the nearer's alone.
		/// </summary>
		public const float BlendMetres = 2f;
		/// <summary>
		/// The field's encoding, baked as the binder's Parameters.w (the shader reads it): 1 the first (nearest way only, earth
		/// coded 0 and track ⅓), 2 blended junctions with the track coded 0 (<see cref="ScenePathStyle.SurfaceCode"/>).
		/// </summary>
		public const float Encoding = 2f;

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
			/// <summary>The deepest floor edge over the texel, which a floor texel's blend is measured from.</summary>
			public readonly float[] FloorEdge = new float[StoredTexels * StoredTexels];
			// Weighted sums while blending (Blend), then the blended values (Resolve).
			public readonly float[] Weight = new float[StoredTexels * StoredTexels];
			public readonly float[] Half = new float[StoredTexels * StoredTexels];
			public readonly float[] Wear = new float[StoredTexels * StoredTexels];
			public readonly float[] Surface = new float[StoredTexels * StoredTexels];
			/// <summary>Inside a building's floor: no other way blends into the texel.</summary>
			public readonly bool[] Floor = new bool[StoredTexels * StoredTexels];

			public Page()
			{
				for (int i = 0; i < Edge.Length; i++)
				{
					Edge[i] = OutsideMetres;
					FloorEdge[i] = OutsideMetres;
				}
			}

			/// <summary>The blended half width, wear and surface: the sums divided by their weight.</summary>
			public void Resolve()
			{
				for (int i = 0; i < Weight.Length; i++)
				{
					if (Weight[i] > 0f)
					{
						float inverse = 1f / Weight[i];
						Half[i] *= inverse;
						Wear[i] *= inverse;
						Surface[i] *= inverse;
					}
				}
			}
		}

		/// <summary>
		/// How much a way whose edge lies <paramref name="beyond"/> metres past a texel's nearest edge weighs in its blend:
		/// 1 for the nearest, falling smoothly to nothing at <see cref="BlendMetres"/>, so two ways equally near mix evenly.
		/// </summary>
		public static float BlendWeight(float beyond)
		{
			float t = 1f - Mathf.Clamp01(beyond / BlendMetres);
			return t * t;
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
			var faded = new List<(ScenePath Path, float[] Half, float[] Wear)>(list.Count);
			foreach (ScenePath path in list)
			{
				EndFade(path, out float[] half, out float[] wear);
				faded.Add((path, half, wear));
			}
			// Two passes: every texel's nearest edge first, then the blend of the ways near it, measured from that edge.
			foreach (bool blend in new[] { false, true })
			{
				foreach ((ScenePath path, float[] half, float[] wear) in faded)
				{
					for (int i = 1; i < path.Count; i++)
					{
						if ((path.FlagsAt(i - 1) & ScenePathPointFlags.Bridge) != 0 && (path.FlagsAt(i) & ScenePathPointFlags.Bridge) != 0)
						{
							continue;
						}
						// Nor under a boardwalk's deck (2026-10-18): the way is walked on its planks, the ground beneath left as it grew.
						if ((path.FlagsAt(i - 1) & path.FlagsAt(i) & (ScenePathPointFlags.Boardwalk | ScenePathPointFlags.Ferry)) != 0)
						{
							continue;
						}
						field.Segment(pages, path, i - 1, i, half, wear, blend);
					}
				}
			}
			foreach (Page page in pages.Values)
			{
				page.Resolve();
			}
			field.Pack(pages);
			return field;
		}

		/// <summary>How far a dead end peters out, metres: a few widths, within bounds.</summary>
		public static float EndFadeMetres(ScenePath path) => path.Class == ScenePathClass.Yard ? 2.5f : Mathf.Clamp(4f * path.MaxHalfWidth + 3f, 4f, 12f);

		/// <summary>
		/// The half widths and wears a path is drawn with: its own, narrowed to nothing and worn away over the last few metres
		/// of each end that stops in a site, so a way peters out into the ground instead of ending in a full-width round
		/// cap (2026-10-11). An end that joins another way (FromId −1) and a settlement's streets are drawn as they
		/// are: a junction must meet, and a street runs to its walls.
		/// </summary>
		public static void EndFade(ScenePath path, out float[] half, out float[] wear)
		{
			int n = path.Count;
			half = new float[n];
			wear = new float[n];
			for (int i = 0; i < n; i++)
			{
				half[i] = path.HalfWidth[i];
				wear[i] = path.Wear != null && path.Wear.Length > i ? path.Wear[i] : 1f;
			}
			if (path.Class == ScenePathClass.Street || n < 2)
			{
				return;
			}
			if (path.Class == ScenePathClass.Yard && path.FromId < 0 && path.ToId < 0)
			{
				// A building's own ground: no end to fade.
				return;
			}
			bool fadeStart = path.FromId >= 0, fadeEnd = path.ToId >= 0;
			if (!fadeStart && !fadeEnd)
			{
				return;
			}
			float reach = EndFadeMetres(path);
			var along = new float[n];
			for (int i = 1; i < n; i++)
			{
				along[i] = along[i - 1] + Vector2.Distance(new Vector2(path.Points[i - 1].x, path.Points[i - 1].z), new Vector2(path.Points[i].x, path.Points[i].z));
			}
			float total = along[n - 1];
			for (int i = 0; i < n; i++)
			{
				float fromEnd = Mathf.Min(fadeStart ? along[i] : float.PositiveInfinity, fadeEnd ? total - along[i] : float.PositiveInfinity);
				float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fromEnd / reach));
				half[i] *= Mathf.Lerp(0.06f, 1f, fade);
				wear[i] *= Mathf.Lerp(0.1f, 1f, fade);
			}
		}

		/// <summary>
		/// One segment over every texel within its reach: its distance, keeping the nearest edge (and the deepest floor), or
		/// with <paramref name="blend"/>, once every edge is in, its half width, wear and surface added to the texel's blend
		/// by how near its edge is to the texel's nearest (<see cref="BlendWeight"/>).
		/// </summary>
		private void Segment(Dictionary<int, Page> pages, ScenePath path, int ia, int ib, float[] halves, float[] wears, bool blend)
		{
			Vector2 a = new Vector2(path.Points[ia].x, path.Points[ia].z), b = new Vector2(path.Points[ib].x, path.Points[ib].z);
			float ha = halves[ia], hb = halves[ib];
			float wa = wears[ia], wb = wears[ib];
			float sa = path.Surface != null && path.Surface.Length > ia ? ScenePathStyle.SurfaceCode((ScenePathSurface)path.Surface[ia]) : ScenePathStyle.SurfaceCode(ScenePathSurface.Earth);
			float sb = path.Surface != null && path.Surface.Length > ib ? ScenePathStyle.SurfaceCode((ScenePathSurface)path.Surface[ib]) : ScenePathStyle.SurfaceCode(ScenePathSurface.Earth);
			float reach = Mathf.Max(ha, hb) + OutsideMetres;
			Vector2 ab = b - a;
			float length2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
			float length = Mathf.Sqrt(length2);
			ScenePathPointFlags both = path.FlagsAt(ia) & path.FlagsAt(ib);
			// A rectangle needs a direction: one point is drawn round.
			bool squared = (both & ScenePathPointFlags.Squared) != 0 && length > 0.01f;
			bool floor = (both & ScenePathPointFlags.Floor) != 0;

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
							float along = ((x - a.x) * ab.x + (z - a.y) * ab.y) / length2;
							float t = Mathf.Clamp01(along);
							float half = Mathf.Lerp(ha, hb, t);
							float edge;
							if (squared)
							{
								// The rectangle's signed distance: past either end, or across by the half width there.
								float overEnd = Mathf.Max(-along, along - 1f) * length;
								float across = Mathf.Abs((x - a.x) * ab.y - (z - a.y) * ab.x) / length - half;
								float ox = Mathf.Max(overEnd, 0f), oz = Mathf.Max(across, 0f);
								edge = Mathf.Sqrt(ox * ox + oz * oz) + Mathf.Min(Mathf.Max(overEnd, across), 0f);
							}
							else
							{
								float dx = a.x + ab.x * t - x, dz = a.y + ab.y * t - z;
								edge = Mathf.Sqrt(dx * dx + dz * dz) - half;
							}
							if (edge >= OutsideMetres)
							{
								continue;
							}
							bool indoors = floor && edge < 0f;
							int k = j * StoredTexels + i;
							if (!blend)
							{
								if (page == null)
								{
									page = new Page();
									pages[key] = page;
								}
								page.Edge[k] = Mathf.Min(edge, page.Edge[k]);
								if (indoors)
								{
									page.Floor[k] = true;
									page.FloorEdge[k] = Mathf.Min(edge, page.FloorEdge[k]);
								}
								continue;
							}
							// A floor blends only the floors over it, from the deepest of them.
							if (page == null || (page.Floor[k] && !indoors))
							{
								continue;
							}
							float weight = BlendWeight(edge - (page.Floor[k] ? page.FloorEdge[k] : page.Edge[k]));
							// And outside its walls a floor fills in only where no other way reaches: its full wear blended out
							// over the apron round it would wear the ground by the wall as bare as the floor.
							if (floor && !indoors)
							{
								weight *= 1e-3f;
							}
							if (weight <= 0f)
							{
								continue;
							}
							page.Weight[k] += weight;
							page.Half[k] += half * weight;
							page.Wear[k] += Mathf.Lerp(wa, wb, t) * weight;
							page.Surface[k] += Mathf.Lerp(sa, sb, t) * weight;
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

