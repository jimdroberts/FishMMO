#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The structural pattern a surface recipe is built around.</summary>
	public enum SurfaceMotif
	{
		/// <summary>Fine grain only: sand, soil, dust, ash, regolith, snow, silt.</summary>
		Granular,
		/// <summary>Rounded stones in a finer matrix: gravel, pebbles, flagstones.</summary>
		Cobbles,
		/// <summary>Plates parted by sunken cracks: dried mud, cracked earth, lava crust.</summary>
		Cracks,
		/// <summary>Plates parted by raised ridges: salt pans, frost polygons.</summary>
		Ridges,
		/// <summary>Stamped blades lying every way: lawn and meadow grass from above.</summary>
		Blades,
		/// <summary>Stamped overlapping leaves: broadleaf litter.</summary>
		Leaves,
		/// <summary>Stamped thin needles: conifer litter.</summary>
		Needles,
		/// <summary>Warped horizontal bands: sedimentary cliffs, sandstone, palm bark rings.</summary>
		Strata,
		/// <summary>Ridged fractal relief broken into blocks: rock, basalt, ice.</summary>
		Fractured,
		/// <summary>Wind or wave ripples: dune sand, beach sand.</summary>
		Ripples,
		/// <summary>Soft cushions: moss, lichen.</summary>
		Clumps,
		/// <summary>Noise drawn out along one axis: bark furrows, peat fibres, cactus ribs.</summary>
		Fibres,
	}

	/// <summary>
	/// Everything that makes one procedural surface what it is. A plain value, so the catalogue of
	/// surfaces is a table a person can read and a test can enumerate.
	/// </summary>
	/// <remarks>
	/// Colours are authored in sRGB, as a person picks them and as the albedo texture stores them.
	/// <see cref="ReliefMetres"/> is the real height range of the surface's bumps across a tile of
	/// <see cref="TileMetres"/>: the normal map is derived from that physical slope, so a grass tile
	/// four metres across with three centimetres of relief comes out as gentle as it should, and a
	/// cobble tile with ten centimetres of relief as bold, without a per-surface fudge factor.
	/// </remarks>
	[Serializable]
	public struct SurfaceRecipe
	{
		/// <summary>The stable name every file derived from this recipe is named for.</summary>
		public string Name;
		/// <summary>Metres of ground one tile covers.</summary>
		public float TileMetres;
		/// <summary>Height range of the surface's relief across the tile, in metres.</summary>
		public float ReliefMetres;

		/// <summary>The base colour ramp, dark to light, driven by the base noise.</summary>
		public Color Dark, Mid, Light;
		/// <summary>Lattice cells across the tile for the base noise's first octave.</summary>
		public int BaseCells;
		public int Octaves;
		public float Persistence;
		/// <summary>Domain warp, in tiles. Bends straight features into natural ones.</summary>
		public float Warp;

		public SurfaceMotif Motif;
		/// <summary>Cells across the tile (cellular motifs), bands (strata), or wave number (ripples).</summary>
		public int MotifCells;
		/// <summary>Stamped features per tile (blades, leaves, needles).</summary>
		public int MotifCount;
		/// <summary>Stamp length or cell feature size, as a fraction of the tile.</summary>
		public float MotifSize;
		/// <summary>How much of the height the motif provides, 0..1; the rest is the base noise.</summary>
		public float MotifWeight;
		/// <summary>The motif's own colours: stones, cracks, blades, leaves.</summary>
		public Color MotifDark, MotifLight;
		/// <summary>Streak axis for <see cref="SurfaceMotif.Fibres"/>: true runs the fibres down the tile (v).</summary>
		public bool Vertical;

		/// <summary>Small scattered specks (flowers, shells, lichen spots); none when empty.</summary>
		public Color[] Speckles;
		/// <summary>Specks per tile.</summary>
		public int SpeckleCount;
		/// <summary>Speck radius as a fraction of the tile.</summary>
		public float SpeckleSize;

		public float Smoothness;
		/// <summary>How much smoothness rises in hollows (wet and polished) and falls on crests.</summary>
		public float SmoothnessVariation;
		public float Metallic;
		/// <summary>How strongly hollows read as occluded, 0..1.</summary>
		public float Occlusion;
		/// <summary>TerrainLayer normal scale.</summary>
		public float NormalScale;
	}

	/// <summary>A synthesised surface: tileable maps at one resolution, row-major from the bottom row up (Unity's order).</summary>
	public sealed class SurfaceMaps
	{
		public int Size;
		/// <summary>0..1 height. Kept for tests and for the mask's blue channel.</summary>
		public float[] Height;
		/// <summary>RGB albedo (sRGB), A smoothness.</summary>
		public Color32[] Albedo;
		/// <summary>Tangent-space normal, OpenGL convention (+Y = +V), as Unity's normal-map importer expects.</summary>
		public Color32[] Normal;
		/// <summary>R metallic, G occlusion, B height, A smoothness: the URP terrain mask layout.</summary>
		public Color32[] Mask;
	}

	/// <summary>
	/// Turns a <see cref="SurfaceRecipe"/> into seamless albedo, normal and mask maps.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Seamless by construction, not by blending.</b> Every pattern is periodic over the tile:
	/// the noise lattices wrap (<see cref="ProceduralNoise.Periodic"/>), cellular feature points are
	/// hashed by wrapped cell, stamps that run off one edge are drawn on again from the other, and
	/// the normal and occlusion passes read their neighbours with wrap-around. The usual trick —
	/// cross-fading a texture with an offset copy of itself — halves the contrast across the seam
	/// region and shows as a soft cross on every tile; none of that here.
	/// </para>
	/// <para>
	/// <b>Albedo carries no lighting.</b> Occlusion goes to the mask, where the terrain shader
	/// applies it under the actual light. Only material colour changes live in the albedo — dirt
	/// gathered in cracks, the different colour of a stone and its matrix — so the same layer reads
	/// correctly at noon and at dusk.
	/// </para>
	/// <para>
	/// <b>Parallel and deterministic.</b> The per-pixel passes run row by row on the thread pool and
	/// are pure functions of the pixel; the stamping passes run on one thread from one seeded
	/// generator. The same recipe, size and seed give the same bytes every time.
	/// </para>
	/// </remarks>
	public static class SurfaceSynth
	{
		/// <summary>Builds every map of a recipe.</summary>
		public static SurfaceMaps Generate(in SurfaceRecipe recipe, int size, int seed)
		{
			if (size < 8 || (size & (size - 1)) != 0)
			{
				throw new ArgumentException("Surface size must be a power of two of at least 8.", nameof(size));
			}
			int s = ProceduralNoise.SeedFor(recipe.Name, seed);
			int n = size * size;
			var height = new float[n];
			var tone = new float[n];      // 0..1 position on the base colour ramp
			var motif = new float[n];     // 0..1 motif colour blend
			var motifTint = new float[n]; // -1..1 per-feature colour jitter
			var speck = new int[n];       // 1 + speckle colour index, 0 none

			SurfaceRecipe r = recipe;
			Parallel.For(0, size, y =>
			{
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float v = (y + 0.5f) / size;
					BasePixel(in r, u, v, s, out float h, out float t);
					float mh = 0f, mc = 0f, mt = 0f;
					PatternPixel(in r, u, v, s, ref mh, ref mc, ref mt);
					int i = y * size + x;
					height[i] = Mathf.Lerp(h, mh, Mathf.Clamp01(r.MotifWeight));
					tone[i] = t;
					motif[i] = mc;
					motifTint[i] = mt;
				}
			});

			// Stamped motifs draw over the per-pixel result.
			var rng = new DeterministicRNG(s);
			switch (r.Motif)
			{
				case SurfaceMotif.Blades: StampBlades(in r, size, rng, height, motif, motifTint); break;
				case SurfaceMotif.Leaves: StampLeaves(in r, size, rng, height, motif, motifTint); break;
				case SurfaceMotif.Needles: StampNeedles(in r, size, rng, height, motif, motifTint); break;
			}
			if (r.Speckles != null && r.Speckles.Length > 0 && r.SpeckleCount > 0)
			{
				StampSpeckles(in r, size, rng, height, speck);
			}

			Normalise(height);
			float texelMetres = Mathf.Max(1e-4f, r.TileMetres) / size;
			Color32[] normal = NormalsFromHeight(height, size, Mathf.Max(0f, r.ReliefMetres) / texelMetres);
			float[] occlusion = OcclusionFromHeight(height, size, Mathf.Max(1, size / 64), r.Occlusion);

			var albedo = new Color32[n];
			var mask = new Color32[n];
			Color dark = ToLinear(r.Dark), mid = ToLinear(r.Mid), light = ToLinear(r.Light);
			Color mDark = ToLinear(r.MotifDark), mLight = ToLinear(r.MotifLight);
			Color[] speckles = r.Speckles != null ? Array.ConvertAll(r.Speckles, ToLinear) : null;
			byte metallic = ToByte(r.Metallic);
			Parallel.For(0, size, y =>
			{
				for (int x = 0; x < size; x++)
				{
					int i = y * size + x;
					float t = tone[i];
					// Blend in linear space, store in sRGB: a ramp blended in sRGB goes muddy in the middle.
					Color c = t < 0.5f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, light, (t - 0.5f) * 2f);
					float mc = motif[i];
					if (mc > 0f)
					{
						float jitter = motifTint[i];
						Color m = Color.Lerp(mDark, mLight, Mathf.Clamp01(0.5f + 0.5f * jitter));
						c = Color.Lerp(c, m, Mathf.Clamp01(mc));
					}
					if (speck[i] > 0)
					{
						c = speckles[speck[i] - 1];
					}
					// Material, not light: fine dirt settles into the deepest hollows.
					float hollow = Mathf.Clamp01(1f - occlusion[i]);
					c *= 1f - hollow * 0.25f;
					float smooth = Mathf.Clamp01(r.Smoothness + (0.5f - height[i]) * r.SmoothnessVariation);
					Color srgb = ToGamma(c);
					albedo[i] = new Color32(ToByte(srgb.r), ToByte(srgb.g), ToByte(srgb.b), ToByte(smooth));
					mask[i] = new Color32(metallic, ToByte(occlusion[i]), ToByte(height[i]), ToByte(smooth));
				}
			});

			return new SurfaceMaps { Size = size, Height = height, Albedo = albedo, Normal = normal, Mask = mask };
		}

		private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

		/* The sRGB transfer curve, written out: Color.linear and Color.gamma call into the engine,
		 * and these run on worker threads, where engine calls are not allowed. */
		public static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055) / 1.055, 2.4);
		public static float ToGamma(float c) => c <= 0.0031308f ? c * 12.92f : (float)(1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055);
		public static Color ToLinear(Color c) => new Color(ToLinear(c.r), ToLinear(c.g), ToLinear(c.b), c.a);
		public static Color ToGamma(Color c) => new Color(ToGamma(Mathf.Max(0f, c.r)), ToGamma(Mathf.Max(0f, c.g)), ToGamma(Mathf.Max(0f, c.b)), c.a);

		// ── Per-pixel passes ──────────────────────────────────────────

		private static void BasePixel(in SurfaceRecipe r, float u, float v, int seed, out float height, out float tone)
		{
			int cells = Mathf.Max(1, r.BaseCells);
			int octaves = Mathf.Clamp(r.Octaves, 1, 8);
			if (r.Warp > 0f)
			{
				// Two periodic fields displace the lookup; periodic plus periodic stays periodic.
				float wu = ProceduralNoise.PeriodicFbm(u, v, Mathf.Max(1, cells / 2), 3, 0.5f, seed + 71);
				float wv = ProceduralNoise.PeriodicFbm(u, v, Mathf.Max(1, cells / 2), 3, 0.5f, seed + 113);
				u += wu * r.Warp;
				v += wv * r.Warp;
			}
			float n = ProceduralNoise.PeriodicFbm(u, v, cells, octaves, r.Persistence <= 0f ? 0.5f : r.Persistence, seed);
			height = n * 0.5f + 0.5f;
			// The colour ramp follows a broader version of the same field plus its own large blotches,
			// so colour and relief agree without being the same picture.
			float blotch = ProceduralNoise.PeriodicFbm(u, v, Mathf.Max(1, cells / 4), 3, 0.5f, seed + 977);
			tone = Mathf.Clamp01(0.5f + n * 0.55f + blotch * 0.35f);
		}

		private static void PatternPixel(in SurfaceRecipe r, float u, float v, int seed, ref float height, ref float colour, ref float tint)
		{
			int cells = Mathf.Max(1, r.MotifCells);
			switch (r.Motif)
			{
				case SurfaceMotif.Granular:
				{
					float g = ProceduralNoise.PeriodicFbm(u, v, cells, 3, 0.6f, seed + 300);
					height = g * 0.5f + 0.5f;
					colour = Mathf.Clamp01(Mathf.Abs(g) * 1.2f - 0.3f);
					tint = g;
					break;
				}
				case SurfaceMotif.Cobbles:
				{
					// Warp the lookup slightly so stones are not Voronoi-straight.
					float wu = u + ProceduralNoise.PeriodicFbm(u, v, cells, 2, 0.5f, seed + 301) * 0.35f / cells;
					float wv = v + ProceduralNoise.PeriodicFbm(u, v, cells, 2, 0.5f, seed + 302) * 0.35f / cells;
					ProceduralNoise.Cell c = ProceduralNoise.PeriodicCellular(wu, wv, cells, 0.85f, seed + 303);
					float edge = c.F2 - c.F1;
					float inside = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / Mathf.Max(0.02f, r.MotifSize)));
					float dome = Mathf.Sqrt(Mathf.Clamp01(inside));
					float grain = ProceduralNoise.PeriodicFbm(u, v, cells * 8, 2, 0.5f, seed + 304) * 0.08f;
					height = dome * (0.75f + 0.25f * ProceduralNoise.ToUnit(c.Id)) + grain;
					colour = inside;
					tint = ProceduralNoise.ToUnit(ProceduralNoise.Mix(c.Id)) * 2f - 1f;
					break;
				}
				case SurfaceMotif.Cracks:
				case SurfaceMotif.Ridges:
				{
					float wu = u + ProceduralNoise.PeriodicFbm(u, v, cells * 2, 3, 0.5f, seed + 311) * 0.25f / cells;
					float wv = v + ProceduralNoise.PeriodicFbm(u, v, cells * 2, 3, 0.5f, seed + 312) * 0.25f / cells;
					ProceduralNoise.Cell c = ProceduralNoise.PeriodicCellular(wu, wv, cells, 0.9f, seed + 313);
					float edge = c.F2 - c.F1;
					float width = Mathf.Max(0.01f, r.MotifSize);
					float line = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / width));
					float plate = Mathf.Clamp01(1f - c.F1 * 0.6f);
					if (r.Motif == SurfaceMotif.Cracks)
					{
						height = Mathf.Clamp01(plate * 0.35f + 0.65f - line);
					}
					else
					{
						height = Mathf.Clamp01(0.35f + line * 0.65f - plate * 0.1f);
					}
					colour = line;
					tint = ProceduralNoise.ToUnit(c.Id) * 2f - 1f;
					break;
				}
				case SurfaceMotif.Strata:
				{
					float warp = ProceduralNoise.PeriodicFbm(u, v, 3, 4, 0.55f, seed + 321) * 0.6f;
					float band = v * cells + warp;
					int index = Mathf.FloorToInt(band);
					float f = band - index;
					// Each band its own hardness: hard beds stand proud, soft ones weather back.
					uint bandHash = ProceduralNoise.Hash(((index % cells) + cells) % cells, 0, seed + 322);
					float hard = ProceduralNoise.ToUnit(bandHash);
					float profile = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(f * 6f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - f) * 6f));
					float rough = ProceduralNoise.PeriodicRidged(u, v, cells, 4, 0.5f, seed + 323);
					height = Mathf.Clamp01(hard * 0.6f * profile + rough * 0.4f);
					colour = hard;
					tint = hard * 2f - 1f;
					break;
				}
				case SurfaceMotif.Fractured:
				{
					float ridge = ProceduralNoise.PeriodicRidged(u, v, cells, 5, 0.5f, seed + 331);
					ProceduralNoise.Cell c = ProceduralNoise.PeriodicCellular(u, v, Mathf.Max(1, cells / 2), 1f, seed + 332);
					float joint = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((c.F2 - c.F1) / Mathf.Max(0.02f, r.MotifSize)));
					height = Mathf.Clamp01(ridge * 0.6f + joint * 0.4f * (0.7f + 0.3f * ProceduralNoise.ToUnit(c.Id)));
					colour = 1f - joint;
					tint = ProceduralNoise.ToUnit(c.Id) * 2f - 1f;
					break;
				}
				case SurfaceMotif.Ripples:
				{
					// An integer wave vector keeps the ripples periodic; the warp keeps them from ruling lines.
					float warp = ProceduralNoise.PeriodicFbm(u, v, 4, 3, 0.5f, seed + 341) * 1.5f;
					float phase = (u * cells + v * Mathf.Max(1, cells / 6) + warp) * Mathf.PI * 2f;
					// Ripples are asymmetric: a long gentle stoss side and a short steep lee.
					float w = Mathf.Sin(phase);
					float ripple = Mathf.Pow(w * 0.5f + 0.5f, 1.6f);
					height = ripple;
					colour = Mathf.Clamp01(1f - ripple) * 0.6f;
					tint = w;
					break;
				}
				case SurfaceMotif.Clumps:
				{
					ProceduralNoise.Cell c = ProceduralNoise.PeriodicCellular(u, v, cells, 1f, seed + 351);
					float cushion = Mathf.Clamp01(1f - c.F1 / Mathf.Max(0.2f, r.MotifSize * cells));
					float fine = ProceduralNoise.PeriodicFbm(u, v, cells * 6, 3, 0.55f, seed + 352);
					height = Mathf.Clamp01(Mathf.Sqrt(cushion) * 0.75f + fine * 0.2f + 0.1f);
					colour = cushion;
					tint = ProceduralNoise.ToUnit(c.Id) * 2f - 1f + fine * 0.5f;
					break;
				}
				case SurfaceMotif.Fibres:
				{
					int along = Mathf.Max(1, cells / 8);
					float n = r.Vertical
						? ProceduralNoise.PeriodicFbm(u, v, cells, along, 4, 0.55f, seed + 361)
						: ProceduralNoise.PeriodicFbm(u, v, along, cells, 4, 0.55f, seed + 361);
					float furrow = 1f - Mathf.Abs(n);
					height = Mathf.Clamp01(furrow * furrow);
					colour = Mathf.Clamp01(1f - furrow * 1.3f);
					tint = n;
					break;
				}
				default:
				{
					// Stamped motifs start from a soft ground the stamps are laid on.
					float g = ProceduralNoise.PeriodicFbm(u, v, Mathf.Max(4, cells), 3, 0.5f, seed + 390);
					height = 0.25f + g * 0.1f;
					colour = 0f;
					tint = g;
					break;
				}
			}
		}

		// ── Stamps ────────────────────────────────────────────────────

		private static int WrapIndex(int i, int size) => ((i % size) + size) % size;

		/// <summary>Draws a tapered segment into the maps with wrap-around; returns nothing, writes where it is on top.</summary>
		private static void StampSegment(int size, float x0, float y0, float x1, float y1, float w0, float w1,
			float baseHeight, float topHeight, float tint, float[] height, float[] motif, float[] motifTint)
		{
			float minX = Mathf.Min(x0, x1) - Mathf.Max(w0, w1) - 1f, maxX = Mathf.Max(x0, x1) + Mathf.Max(w0, w1) + 1f;
			float minY = Mathf.Min(y0, y1) - Mathf.Max(w0, w1) - 1f, maxY = Mathf.Max(y0, y1) + Mathf.Max(w0, w1) + 1f;
			float dx = x1 - x0, dy = y1 - y0;
			float lengthSq = Mathf.Max(1e-6f, dx * dx + dy * dy);
			for (int py = Mathf.FloorToInt(minY); py <= Mathf.CeilToInt(maxY); py++)
			{
				for (int px = Mathf.FloorToInt(minX); px <= Mathf.CeilToInt(maxX); px++)
				{
					float cx = px + 0.5f - x0, cy = py + 0.5f - y0;
					float t = Mathf.Clamp01((cx * dx + cy * dy) / lengthSq);
					float ex = cx - dx * t, ey = cy - dy * t;
					float halfWidth = Mathf.Lerp(w0, w1, t);
					float d = Mathf.Sqrt(ex * ex + ey * ey);
					if (d > halfWidth)
					{
						continue;
					}
					float across = 1f - d / Mathf.Max(1e-3f, halfWidth);
					float h = Mathf.Lerp(baseHeight, topHeight, t) * (0.6f + 0.4f * Mathf.Sqrt(across));
					int i = WrapIndex(py, size) * size + WrapIndex(px, size);
					if (h >= height[i])
					{
						height[i] = h;
						motif[i] = 1f;
						motifTint[i] = tint + (across - 0.5f) * 0.3f;
					}
				}
			}
		}

		private static void StampBlades(in SurfaceRecipe r, int size, DeterministicRNG rng, float[] height, float[] motif, float[] motifTint)
		{
			float length = Mathf.Max(2f, r.MotifSize * size);
			for (int b = 0; b < r.MotifCount; b++)
			{
				float x = rng.NextFloat() * size, y = rng.NextFloat() * size;
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float len = length * rng.Range(0.6f, 1.2f);
				// A blade seen from above curves a little as it falls over.
				float bend = rng.Range(-0.4f, 0.4f);
				float mx = x + Mathf.Cos(angle) * len * 0.5f, my = y + Mathf.Sin(angle) * len * 0.5f;
				float ex = mx + Mathf.Cos(angle + bend) * len * 0.5f, ey = my + Mathf.Sin(angle + bend) * len * 0.5f;
				float width = Mathf.Max(0.9f, size / 512f * rng.Range(1.2f, 2.2f));
				float baseH = rng.Range(0.35f, 0.8f);
				float tint = rng.Range(-1f, 1f);
				StampSegment(size, x, y, mx, my, width, width * 0.8f, baseH, baseH + 0.15f, tint, height, motif, motifTint);
				StampSegment(size, mx, my, ex, ey, width * 0.8f, 0.3f, baseH + 0.15f, baseH + 0.2f, tint, height, motif, motifTint);
			}
		}

		private static void StampNeedles(in SurfaceRecipe r, int size, DeterministicRNG rng, float[] height, float[] motif, float[] motifTint)
		{
			float length = Mathf.Max(3f, r.MotifSize * size);
			for (int b = 0; b < r.MotifCount; b++)
			{
				float x = rng.NextFloat() * size, y = rng.NextFloat() * size;
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float len = length * rng.Range(0.7f, 1.1f);
				float ex = x + Mathf.Cos(angle) * len, ey = y + Mathf.Sin(angle) * len;
				float width = Mathf.Max(0.6f, size / 1024f * rng.Range(0.8f, 1.3f));
				// Later needles lie on earlier ones: the litter builds up in layers.
				float layer = 0.3f + 0.6f * b / Mathf.Max(1, r.MotifCount);
				StampSegment(size, x, y, ex, ey, width, width, layer, layer, rng.Range(-1f, 1f), height, motif, motifTint);
			}
		}

		private static void StampLeaves(in SurfaceRecipe r, int size, DeterministicRNG rng, float[] height, float[] motif, float[] motifTint)
		{
			float radius = Mathf.Max(3f, r.MotifSize * size);
			for (int l = 0; l < r.MotifCount; l++)
			{
				float cx = rng.NextFloat() * size, cy = rng.NextFloat() * size;
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float len = radius * rng.Range(0.7f, 1.3f);
				float wid = len * rng.Range(0.35f, 0.55f);
				float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
				float layer = 0.25f + 0.65f * l / Mathf.Max(1, r.MotifCount);
				float tint = rng.Range(-1f, 1f);
				int extent = Mathf.CeilToInt(len) + 1;
				for (int py = -extent; py <= extent; py++)
				{
					for (int px = -extent; px <= extent; px++)
					{
						// Into the leaf's own frame: a along the midrib, b across it.
						float a = (px * ca + py * sa) / len;
						float b = (-px * sa + py * ca) / wid;
						// A leaf outline: widest a third of the way up, pointed at the tip.
						float halfAt = Mathf.Sqrt(Mathf.Clamp01(1f - a * a)) * (1f - 0.35f * a);
						if (Mathf.Abs(a) > 1f || Mathf.Abs(b) > halfAt)
						{
							continue;
						}
						float edge = 1f - Mathf.Abs(b) / Mathf.Max(1e-3f, halfAt);
						float midrib = Mathf.Abs(b) < 0.08f ? 0.04f : 0f;
						// Leaves curl: the edges lift off the ground a little.
						float h = layer + (1f - edge) * 0.04f + midrib;
						int i = WrapIndex(Mathf.RoundToInt(cy) + py, size) * size + WrapIndex(Mathf.RoundToInt(cx) + px, size);
						height[i] = h;
						motif[i] = 1f;
						motifTint[i] = tint + (midrib > 0f ? -0.3f : 0f);
					}
				}
			}
		}

		private static void StampSpeckles(in SurfaceRecipe r, int size, DeterministicRNG rng, float[] height, int[] speck)
		{
			float radius = Mathf.Max(0.8f, r.SpeckleSize * size);
			int extent = Mathf.CeilToInt(radius) + 1;
			for (int k = 0; k < r.SpeckleCount; k++)
			{
				float cx = rng.NextFloat() * size, cy = rng.NextFloat() * size;
				int colour = 1 + rng.Next(r.Speckles.Length);
				float rad = radius * rng.Range(0.6f, 1.2f);
				for (int py = -extent; py <= extent; py++)
				{
					for (int px = -extent; px <= extent; px++)
					{
						float d = Mathf.Sqrt(px * px + py * py);
						if (d > rad)
						{
							continue;
						}
						int i = WrapIndex(Mathf.RoundToInt(cy) + py, size) * size + WrapIndex(Mathf.RoundToInt(cx) + px, size);
						speck[i] = colour;
						height[i] = Mathf.Max(height[i], 0.85f + 0.15f * (1f - d / rad));
					}
				}
			}
		}

		// ── Derived maps ──────────────────────────────────────────────

		/// <summary>Rescales a height field to span 0..1 exactly; a flat field stays flat at 0.5.</summary>
		public static void Normalise(float[] height)
		{
			float min = float.MaxValue, max = float.MinValue;
			for (int i = 0; i < height.Length; i++)
			{
				if (height[i] < min) min = height[i];
				if (height[i] > max) max = height[i];
			}
			float range = max - min;
			for (int i = 0; i < height.Length; i++)
			{
				height[i] = range > 1e-6f ? (height[i] - min) / range : 0.5f;
			}
		}

		/// <summary>
		/// A tangent-space normal map from a tileable height field, by central differences that wrap
		/// at the edges.
		/// </summary>
		/// <param name="slopeScale">
		/// Converts a height difference of 1 between neighbouring texels into rise over run: the
		/// relief in metres divided by the texel size in metres.
		/// </param>
		/// <remarks>
		/// Unity's normal-map importer expects the OpenGL convention: green up the texture (+V). Rows
		/// here run bottom to top, as <see cref="Texture2D.SetPixels32(Color32[])"/> stores them, so a
		/// height rising with the row index tilts the normal toward -V.
		/// </remarks>
		public static Color32[] NormalsFromHeight(float[] height, int size, float slopeScale)
		{
			var result = new Color32[size * size];
			Parallel.For(0, size, y =>
			{
				int up = (y + 1) % size, down = (y - 1 + size) % size;
				for (int x = 0; x < size; x++)
				{
					int right = (x + 1) % size, left = (x - 1 + size) % size;
					float dx = (height[y * size + right] - height[y * size + left]) * 0.5f * slopeScale;
					float dy = (height[up * size + x] - height[down * size + x]) * 0.5f * slopeScale;
					var n = new Vector3(-dx, -dy, 1f).normalized;
					result[y * size + x] = new Color32(
						(byte)Mathf.Clamp(Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 0, 255),
						255);
				}
			});
			return result;
		}

		/// <summary>
		/// Cavity occlusion, 1 open .. 0 deep in a hollow: how far each texel sits below the average
		/// of its neighbourhood, from a wrapping box blur.
		/// </summary>
		public static float[] OcclusionFromHeight(float[] height, int size, int radius, float strength)
		{
			float[] blurred = BoxBlurWrap(height, size, radius);
			var result = new float[height.Length];
			for (int i = 0; i < height.Length; i++)
			{
				float below = Mathf.Max(0f, blurred[i] - height[i]);
				result[i] = Mathf.Clamp01(1f - below * 4f * Mathf.Clamp01(strength));
			}
			return result;
		}

		/// <summary>A separable box blur with wrap-around, O(1) per texel whatever the radius.</summary>
		public static float[] BoxBlurWrap(float[] source, int size, int radius)
		{
			var temp = new float[source.Length];
			var result = new float[source.Length];
			float inv = 1f / (radius * 2 + 1);
			Parallel.For(0, size, y =>
			{
				int row = y * size;
				float sum = 0f;
				for (int k = -radius; k <= radius; k++)
				{
					sum += source[row + WrapIndex(k, size)];
				}
				for (int x = 0; x < size; x++)
				{
					temp[row + x] = sum * inv;
					sum += source[row + WrapIndex(x + radius + 1, size)] - source[row + WrapIndex(x - radius, size)];
				}
			});
			Parallel.For(0, size, x =>
			{
				float sum = 0f;
				for (int k = -radius; k <= radius; k++)
				{
					sum += temp[WrapIndex(k, size) * size + x];
				}
				for (int y = 0; y < size; y++)
				{
					result[y * size + x] = sum * inv;
					sum += temp[WrapIndex(y + radius + 1, size) * size + x] - temp[WrapIndex(y - radius, size) * size + x];
				}
			});
			return result;
		}
	}
}
#endif
