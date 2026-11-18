#if UNITY_EDITOR
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The cells of the foliage atlas, by what is drawn in them.</summary>
	public enum FoliageCell
	{
		Blade = 0,
		Frond = 1,
		BroadLeaves = 2,
		NeedleSpray = 3,
		PalmFrond = 4,
		Flower = 5,
		SmallLeaves = 6,
		Strap = 7,
		Twigs = 8,
		BambooLeaves = 9,
		/// <summary>A dense round mass of broad leaves, ragged at its edge: a shrub's crown (BushMeshes).</summary>
		BushBroad = 10,
		/// <summary>A dense mass of small leaves on fine twigs (box, sagebrush, willow).</summary>
		BushSmall = 11,
		/// <summary>A dense mass of needle shoots (juniper, mountain pine, gorse).</summary>
		BushNeedle = 12,
		/// <summary>
		/// One broad paddle leaf filling the cell from its stalk (v = 0) to its rounded tip (v = 1), pale midrib up the
		/// middle, torn between its veins (banana, heliconia, monstera). The last two free cells were claimed by the
		/// vegetation expansion (2026-10-10): after it and <see cref="Strand"/> the atlas is full, and another shape
		/// means growing the grid.
		/// </summary>
		Paddle = 13,
		/// <summary>
		/// Fine strands hanging from the cell's foot (v = 0) toward its top, ragged where they end, each set with short
		/// narrow leaflets angled along it: Spanish moss and old man's beard as a short card, a weeping willow's
		/// pendulous shoots as a long one.
		/// </summary>
		Strand = 14,
		Solid = 15,
	}

	/// <summary>
	/// One texture holding every leaf, frond, blade and petal shape the generated plants are cut
	/// from: a 4×4 grid of cells, alpha for the outline, near-white for the colour.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shapes here, colours on the vertices.</b> The cells are drawn almost white, with only
	/// the light and dark of midribs and leaf-to-leaf variation, and every plant's hue comes from
	/// its vertex colours. One atlas then serves a spruce, a birch and a dry savanna bush, every
	/// plant shares one texture (one material per kind, instancing-friendly), and recolouring a
	/// species is a number in a table rather than a new texture.
	/// </para>
	/// <para>
	/// <b>Drawn analytically, antialiased.</b> Each shape is a distance test with a one-pixel soft
	/// edge, so the alpha has a clean 0.5 contour for the cutoff at every size. Imported with
	/// alpha-coverage-preserving mipmaps and alpha-is-transparency dilation (see the generator),
	/// so leaves do not thin out with distance or grow dark fringes.
	/// </para>
	/// </remarks>
	public static class FoliageAtlas
	{
		public const int Grid = 4;

		/// <summary>The UV rectangle of a cell, inset by a few texels so mip filtering does not borrow from its neighbours.</summary>
		public static Rect CellRect(FoliageCell cell, int atlasSize = 1024)
		{
			int index = (int)cell;
			int col = index % Grid, row = index / Grid;
			float cellUV = 1f / Grid;
			float inset = 4f / atlasSize;
			return new Rect(col * cellUV + inset, row * cellUV + inset, cellUV - inset * 2f, cellUV - inset * 2f);
		}

		/// <summary>A UV inside a cell, from coordinates 0..1 across it.</summary>
		public static Vector2 CellUV(FoliageCell cell, float u, float v, int atlasSize = 1024)
		{
			Rect r = CellRect(cell, atlasSize);
			return new Vector2(r.xMin + Mathf.Clamp01(u) * r.width, r.yMin + Mathf.Clamp01(v) * r.height);
		}

		/// <summary>Draws the atlas: RGBA, rows bottom-up.</summary>
		public static Color32[] Generate(int size, int seed)
		{
			var pixels = new Color32[size * size];
			int cellSize = size / Grid;
			var alpha = new float[cellSize * cellSize];
			var lum = new float[cellSize * cellSize];
			var hue = new Color[cellSize * cellSize];
			for (int index = 0; index < Grid * Grid; index++)
			{
				System.Array.Clear(alpha, 0, alpha.Length);
				for (int i = 0; i < lum.Length; i++)
				{
					lum[i] = 0.9f;
					hue[i] = Color.white;
				}
				var canvas = new Canvas(cellSize, alpha, lum, hue, new DeterministicRNG(ProceduralNoise.SeedFor("atlas/" + index, seed)));
				switch ((FoliageCell)index)
				{
					case FoliageCell.Blade: DrawBlade(canvas); break;
					case FoliageCell.Frond: DrawFrond(canvas); break;
					case FoliageCell.BroadLeaves: DrawBroadLeaves(canvas); break;
					case FoliageCell.NeedleSpray: DrawNeedleSpray(canvas); break;
					case FoliageCell.PalmFrond: DrawPalmFrond(canvas); break;
					case FoliageCell.Flower: DrawFlower(canvas); break;
					case FoliageCell.SmallLeaves: DrawSmallLeaves(canvas); break;
					case FoliageCell.Strap: DrawStrap(canvas); break;
					case FoliageCell.Twigs: DrawTwigs(canvas); break;
					case FoliageCell.BambooLeaves: DrawBambooLeaves(canvas); break;
					case FoliageCell.BushBroad: DrawLeafMass(canvas, 110, 0.17f, 0.26f, 0.55f); break;
					case FoliageCell.BushSmall: DrawLeafMass(canvas, 620, 0.06f, 0.1f, 0.5f); break;
					case FoliageCell.BushNeedle: DrawNeedleMass(canvas); break;
					// Drawn last of the shapes (2026-10-10): each cell's stream is seeded by its index, so these change no
					// other cell's pixels.
					case FoliageCell.Paddle: DrawPaddle(canvas); break;
					case FoliageCell.Strand: DrawStrands(canvas); break;
					case FoliageCell.Solid: canvas.Fill(1f, 1f); break;
					default: break;
				}
				int col = index % Grid, row = index / Grid;
				for (int y = 0; y < cellSize; y++)
				{
					for (int x = 0; x < cellSize; x++)
					{
						int c = y * cellSize + x;
						Color colour = hue[c] * Mathf.Clamp01(lum[c]);
						pixels[(row * cellSize + y) * size + col * cellSize + x] = new Color32(
							(byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * 255f), 0, 255),
							(byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * 255f), 0, 255),
							(byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * 255f), 0, 255),
							(byte)Mathf.Clamp(Mathf.RoundToInt(alpha[c] * 255f), 0, 255));
					}
				}
			}
			return pixels;
		}

		/// <summary>Bilinear sample with clamping, for the billboard rasteriser.</summary>
		public static Color Sample(Color32[] pixels, int size, Vector2 uv, bool wrap)
		{
			float x = uv.x * size - 0.5f, y = uv.y * size - 0.5f;
			int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
			float fx = x - x0, fy = y - y0;
			Color c00 = Fetch(pixels, size, x0, y0, wrap), c10 = Fetch(pixels, size, x0 + 1, y0, wrap);
			Color c01 = Fetch(pixels, size, x0, y0 + 1, wrap), c11 = Fetch(pixels, size, x0 + 1, y0 + 1, wrap);
			return Color.Lerp(Color.Lerp(c00, c10, fx), Color.Lerp(c01, c11, fx), fy);
		}

		private static Color Fetch(Color32[] pixels, int size, int x, int y, bool wrap)
		{
			if (wrap)
			{
				x = ((x % size) + size) % size;
				y = ((y % size) + size) % size;
			}
			else
			{
				x = Mathf.Clamp(x, 0, size - 1);
				y = Mathf.Clamp(y, 0, size - 1);
			}
			return pixels[y * size + x];
		}

		// ── Drawing ───────────────────────────────────────────────────

		/// <summary>One cell being drawn: coordinates 0..1 across it.</summary>
		private sealed class Canvas
		{
			public readonly int Size;
			public readonly float[] Alpha;
			public readonly float[] Lum;
			public readonly Color[] Hue;
			public readonly DeterministicRNG Rng;

			public Canvas(int size, float[] alpha, float[] lum, Color[] hue, DeterministicRNG rng)
			{
				Size = size;
				Alpha = alpha;
				Lum = lum;
				Hue = hue;
				Rng = rng;
			}

			public void Fill(float alpha, float lum)
			{
				for (int i = 0; i < Alpha.Length; i++)
				{
					Alpha[i] = alpha;
					Lum[i] = lum;
				}
			}

			private void Write(int i, float coverage, float lum, Color hue)
			{
				if (coverage <= 0f)
				{
					return;
				}
				// Over-compositing: later shapes lie on earlier ones.
				Lum[i] = Mathf.Lerp(Lum[i], lum, coverage);
				Hue[i] = Color.Lerp(Hue[i], hue, coverage);
				Alpha[i] = Mathf.Max(Alpha[i], coverage);
			}

			/// <summary>A tapered line from (x0,y0) to (x1,y1), widths in cell units.</summary>
			public void Segment(float x0, float y0, float x1, float y1, float w0, float w1, float lum, Color hue)
			{
				float px = 1f / Size;
				float pad = Mathf.Max(w0, w1) + px * 2f;
				int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(x0, x1) - pad) * Size));
				int maxX = Mathf.Min(Size - 1, Mathf.CeilToInt((Mathf.Max(x0, x1) + pad) * Size));
				int minY = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(y0, y1) - pad) * Size));
				int maxY = Mathf.Min(Size - 1, Mathf.CeilToInt((Mathf.Max(y0, y1) + pad) * Size));
				float dx = x1 - x0, dy = y1 - y0;
				float lengthSq = Mathf.Max(1e-8f, dx * dx + dy * dy);
				for (int y = minY; y <= maxY; y++)
				{
					for (int x = minX; x <= maxX; x++)
					{
						float cx = (x + 0.5f) * px - x0, cy = (y + 0.5f) * px - y0;
						float t = Mathf.Clamp01((cx * dx + cy * dy) / lengthSq);
						float ex = cx - dx * t, ey = cy - dy * t;
						float half = Mathf.Lerp(w0, w1, t) * 0.5f;
						float d = (Mathf.Sqrt(ex * ex + ey * ey) - half) * Size;
						Write(y * Size + x, Mathf.Clamp01(0.5f - d), lum, hue);
					}
				}
			}

			/// <summary>A leaf from its base at (x,y), pointing along <paramref name="angle"/> (radians).</summary>
			public void Leaf(float x, float y, float angle, float length, float width, float lum, Color hue, float midrib = 0.12f)
			{
				float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
				float px = 1f / Size;
				float reach = length + width;
				int minX = Mathf.Max(0, Mathf.FloorToInt((x - reach) * Size)), maxX = Mathf.Min(Size - 1, Mathf.CeilToInt((x + reach) * Size));
				int minY = Mathf.Max(0, Mathf.FloorToInt((y - reach) * Size)), maxY = Mathf.Min(Size - 1, Mathf.CeilToInt((y + reach) * Size));
				for (int py = minY; py <= maxY; py++)
				{
					for (int pxi = minX; pxi <= maxX; pxi++)
					{
						float lx = (pxi + 0.5f) * px - x, ly = (py + 0.5f) * px - y;
						float a = (lx * ca + ly * sa) / Mathf.Max(1e-5f, length);
						float b = -lx * sa + ly * ca;
						if (a < -0.02f || a > 1.02f)
						{
							continue;
						}
						float ac = Mathf.Clamp01(a);
						// Widest a third of the way out, rounded at the base, pointed at the tip.
						float half = width * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(ac, 0.8f)), 0.75f);
						float d = (Mathf.Abs(b) - half) * Size;
						float coverage = Mathf.Clamp01(0.5f - d);
						float shade = lum * (0.92f + 0.08f * Mathf.Abs(b) / Mathf.Max(1e-5f, half));
						if (Mathf.Abs(b) < width * midrib * 0.15f)
						{
							shade *= 0.78f;
						}
						Write(py * Size + pxi, coverage, shade, hue);
					}
				}
			}

			public void Disc(float x, float y, float radius, float lum, Color hue)
			{
				Segment(x, y, x + 1e-4f, y, radius * 2f, radius * 2f, lum, hue);
			}

			public float Jitter(float amount) => 1f + Rng.Range(-amount, amount);
		}

		private static void DrawBlade(Canvas c)
		{
			// Opaque: the blade mesh is its own outline. Lengthwise streaks and a lighter tip.
			for (int y = 0; y < c.Size; y++)
			{
				for (int x = 0; x < c.Size; x++)
				{
					float u = (x + 0.5f) / c.Size, v = (y + 0.5f) / c.Size;
					float streak = ProceduralNoise.PeriodicFbm(u, v, 12, 1, 3, 0.5f, 501) * 0.08f;
					float rib = Mathf.Abs(u - 0.5f) < 0.04f ? -0.06f : 0f;
					int i = y * c.Size + x;
					c.Alpha[i] = 1f;
					c.Lum[i] = 0.78f + 0.2f * v + streak + rib;
				}
			}
		}

		private static void DrawStrap(Canvas c)
		{
			for (int y = 0; y < c.Size; y++)
			{
				for (int x = 0; x < c.Size; x++)
				{
					float u = (x + 0.5f) / c.Size, v = (y + 0.5f) / c.Size;
					float edge = 0.5f - Mathf.Abs(u - 0.5f);
					float streak = ProceduralNoise.PeriodicFbm(u, v, 10, 1, 3, 0.5f, 507) * 0.1f;
					int i = y * c.Size + x;
					c.Alpha[i] = Mathf.Clamp01(edge * 10f);
					c.Lum[i] = 0.8f + streak + 0.1f * v;
				}
			}
		}

		private static void DrawFrond(Canvas c)
		{
			c.Segment(0.5f, 0.02f, 0.5f, 0.97f, 0.018f, 0.006f, 0.7f, Color.white);
			for (int k = 0; k < 14; k++)
			{
				float t = 0.07f + k * 0.064f;
				float length = 0.44f * Mathf.Pow(1f - t, 0.55f) * Mathf.Clamp01(t * 6f + 0.3f);
				float width = length * 0.3f;
				float lum = 0.86f * c.Jitter(0.06f);
				c.Leaf(0.5f, t, Mathf.Deg2Rad * 38f, length, width, lum, Color.white);
				c.Leaf(0.5f, t + 0.02f, Mathf.Deg2Rad * 142f, length, width, lum, Color.white);
			}
		}

		private static void DrawBroadLeaves(Canvas c)
		{
			c.Segment(0.5f, 0.0f, 0.5f, 0.72f, 0.02f, 0.008f, 0.55f, Color.white);
			for (int k = 0; k < 9; k++)
			{
				float t = 0.12f + k * 0.075f;
				bool left = (k & 1) == 0;
				float angle = Mathf.Deg2Rad * (left ? 90f + c.Rng.Range(25f, 65f) : 90f - c.Rng.Range(25f, 65f));
				float length = c.Rng.Range(0.26f, 0.34f) * (1f - t * 0.35f);
				c.Leaf(0.5f, t, angle, length, length * 0.55f, 0.88f * c.Jitter(0.08f), Color.white, 0.2f);
			}
			c.Leaf(0.5f, 0.7f, Mathf.Deg2Rad * 90f, 0.28f, 0.15f, 0.9f, Color.white, 0.2f);
		}

		private static void DrawNeedleSpray(Canvas c)
		{
			void Twig(float x0, float y0, float angle, float length, float needleLength)
			{
				float x1 = x0 + Mathf.Cos(angle) * length, y1 = y0 + Mathf.Sin(angle) * length;
				c.Segment(x0, y0, x1, y1, 0.012f, 0.004f, 0.5f, Color.white);
				int needles = Mathf.RoundToInt(length * 90f);
				for (int n = 0; n < needles; n++)
				{
					float t = (n + 0.5f) / needles;
					float bx = Mathf.Lerp(x0, x1, t), by = Mathf.Lerp(y0, y1, t);
					float nl = needleLength * (1f - t * 0.5f) * c.Jitter(0.2f);
					float lum = 0.85f * c.Jitter(0.08f);
					foreach (float side in new[] { -1f, 1f })
					{
						float a = angle + side * Mathf.Deg2Rad * c.Rng.Range(40f, 60f);
						c.Segment(bx, by, bx + Mathf.Cos(a) * nl, by + Mathf.Sin(a) * nl, 0.0075f, 0.003f, lum, Color.white);
					}
				}
			}
			float up = Mathf.Deg2Rad * 90f;
			Twig(0.5f, 0.02f, up, 0.92f, 0.1f);
			for (int k = 0; k < 4; k++)
			{
				float t = 0.18f + k * 0.17f;
				float side = (k & 1) == 0 ? 1f : -1f;
				Twig(0.5f, t, up - side * Mathf.Deg2Rad * 42f, 0.32f * (1f - t * 0.6f), 0.07f);
			}
		}

		private static void DrawPalmFrond(Canvas c)
		{
			c.Segment(0.5f, 0.0f, 0.5f, 0.99f, 0.024f, 0.006f, 0.6f, Color.white);
			for (int k = 0; k < 30; k++)
			{
				float t = 0.05f + k * 0.031f;
				float length = 0.47f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.05f)) + 0.05f;
				float lum = 0.86f * c.Jitter(0.06f);
				c.Leaf(0.5f, t, Mathf.Deg2Rad * 22f, length, 0.045f, lum, Color.white, 0.3f);
				c.Leaf(0.5f, t + 0.01f, Mathf.Deg2Rad * 158f, length, 0.045f, lum, Color.white, 0.3f);
			}
		}

		private static void DrawFlower(Canvas c)
		{
			int petals = 7;
			for (int k = 0; k < petals; k++)
			{
				float a = (k + 0.25f) * Mathf.PI * 2f / petals;
				c.Leaf(0.5f, 0.5f, a, 0.44f, 0.24f, 0.96f * c.Jitter(0.03f), Color.white, 0.05f);
			}
			c.Disc(0.5f, 0.5f, 0.1f, 0.95f, new Color(1f, 0.78f, 0.2f));
		}

		private static void DrawSmallLeaves(Canvas c)
		{
			for (int k = 0; k < 16; k++)
			{
				float x = 0.5f + c.Rng.Range(-0.12f, 0.12f), y = c.Rng.Range(0.08f, 0.5f);
				float angle = Mathf.Deg2Rad * c.Rng.Range(10f, 170f);
				float length = c.Rng.Range(0.16f, 0.24f);
				c.Leaf(x, y, angle, length, length * 0.6f, 0.86f * c.Jitter(0.1f), Color.white, 0.2f);
			}
		}

		private static void DrawTwigs(Canvas c)
		{
			void Branch(float x, float y, float angle, float length, float width, int depth)
			{
				float x1 = x + Mathf.Cos(angle) * length, y1 = y + Mathf.Sin(angle) * length;
				c.Segment(x, y, x1, y1, width, width * 0.6f, 0.62f * c.Jitter(0.1f), Color.white);
				if (depth <= 0)
				{
					return;
				}
				int children = 2 + (c.Rng.Next(2));
				for (int k = 0; k < children; k++)
				{
					float t = c.Rng.Range(0.45f, 1f);
					float bx = Mathf.Lerp(x, x1, t), by = Mathf.Lerp(y, y1, t);
					float spread = Mathf.Deg2Rad * c.Rng.Range(20f, 50f) * (k % 2 == 0 ? 1f : -1f);
					Branch(bx, by, angle + spread, length * c.Rng.Range(0.45f, 0.65f), width * 0.65f, depth - 1);
				}
			}
			Branch(0.5f, 0.0f, Mathf.Deg2Rad * 90f, 0.38f, 0.035f, 3);
		}

		private static void DrawBambooLeaves(Canvas c)
		{
			c.Segment(0.5f, 0.0f, 0.5f, 0.3f, 0.015f, 0.008f, 0.6f, Color.white);
			for (int k = 0; k < 7; k++)
			{
				float angle = Mathf.Deg2Rad * (20f + k * 23f + c.Rng.Range(-6f, 6f));
				c.Leaf(0.5f, 0.28f, angle, c.Rng.Range(0.38f, 0.47f), 0.08f, 0.86f * c.Jitter(0.08f), Color.white, 0.25f);
			}
		}

		/// <summary>
		/// A banana leaf: a short stalk, then an oblong blade rounded at both ends, with a pale raised midrib and fine
		/// lateral veins running out and a little up to the margin. The wind tears such a leaf along those veins, so a
		/// few ragged slits run in from the edge on either side, each its own depth, with whole stretches between —
		/// a whole paddle reads as a cut-out, a torn one as a banana. Drawn for a card about three to four times as
		/// long as wide (a banana's leaf on its frond strip): the veins' slope here is a third of what it looks once
		/// stretched. Heliconias and monstera (the jungle's broad herbs) wear the same cell at their own sizes.
		/// </summary>
		private static void DrawPaddle(Canvas c)
		{
			const float baseV = 0.07f, tipV = 0.985f, halfMax = 0.45f, slope = 0.1f;
			// The tears, per side: where each meets the margin (its vein's v at the edge), how far in it runs, how wide it gapes.
			var tears = new (float v, float depth, float gape)[2][];
			for (int side = 0; side < 2; side++)
			{
				int count = 3 + c.Rng.Next(4);
				tears[side] = new (float, float, float)[count];
				for (int k = 0; k < count; k++)
				{
					float v = Mathf.Lerp(baseV + 0.18f, tipV - 0.1f, (k + c.Rng.Range(0.1f, 0.9f)) / count);
					tears[side][k] = (v, c.Rng.Range(0.3f, 1f), c.Rng.Range(0.002f, 0.007f));
				}
			}
			int size = c.Size;
			float px = 1f / size;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) * px, v = (y + 0.5f) * px;
					float across = u - 0.5f;
					float d = Mathf.Abs(across);
					int i = y * size + x;
					if (v < baseV)
					{
						// The stalk.
						float stalk = Mathf.Clamp01(0.5f - (d - 0.02f) * size);
						c.Alpha[i] = stalk;
						c.Lum[i] = 0.78f;
						continue;
					}
					float s = (v - baseV) / (tipV - baseV);
					// Rounded into the stalk over the first quarter and over the tip in the last, near parallel between.
					float shoulder = Mathf.Sqrt(Mathf.Clamp01(1f - (1f - Mathf.Clamp01(s / 0.25f)) * (1f - Mathf.Clamp01(s / 0.25f))));
					float tip = s > 0.7f ? Mathf.Sqrt(Mathf.Clamp01(1f - (s - 0.7f) * (s - 0.7f) / 0.09f)) : 1f;
					float half = Mathf.Max(0.02f, halfMax * Mathf.Max(0.05f, shoulder) * tip * (0.94f + 0.06f * Mathf.Sin(Mathf.PI * s)));
					float coverage = Mathf.Clamp01(0.5f - (d - half) * size);
					if (s > 1f)
					{
						coverage = 0f;
					}
					// A lateral vein through this point meets the midrib at vRib and runs out and up at the slope.
					float vRib = v - d * slope;
					foreach ((float tv, float depth, float gape) in tears[across < 0f ? 0 : 1])
					{
						// Distance across the tear's line, which runs along the vein that meets the margin at tv.
						float lineV = tv - (half - d) * slope;
						if (d < half * (1f - depth))
						{
							continue;
						}
						float gap = Mathf.Abs(v - lineV) - gape * Mathf.Clamp01((d - half * (1f - depth)) / Mathf.Max(1e-3f, half * depth) + 0.2f);
						coverage = Mathf.Min(coverage, Mathf.Clamp01(0.5f + gap * size));
					}
					c.Alpha[i] = coverage;
					float veins = 0.035f * Mathf.Sin(vRib * 180f);
					float rib = d < 0.016f ? 0.12f : d < 0.026f ? 0.05f : 0f;
					float edge = -0.06f * Mathf.Clamp01((d / Mathf.Max(0.02f, half) - 0.8f) * 5f);
					c.Lum[i] = 0.8f + veins + rib + edge + 0.05f * s;
				}
			}
		}

		/// <summary>
		/// Strands hanging from the cell's foot: a dozen fine wavy threads of their own lengths (a ragged lower edge
		/// when hung), each set along its length with short narrow leaflets angled down it in pairs. On a short card
		/// they read as Spanish moss's grey tangle; stretched down a weeping willow, as its shoots of narrow leaves.
		/// </summary>
		private static void DrawStrands(Canvas c)
		{
			const int strands = 14;
			for (int k = 0; k < strands; k++)
			{
				float x0 = 0.07f + 0.86f * (k + c.Rng.Range(-0.35f, 0.35f)) / (strands - 1);
				float length = c.Rng.Range(0.62f, 0.97f);
				float waves = c.Rng.Range(1.5f, 3.5f), phase = c.Rng.Range(0f, Mathf.PI * 2f), sway = c.Rng.Range(0.015f, 0.04f);
				float lum = 0.82f * c.Jitter(0.08f);
				const int pieces = 20;
				float px0 = x0, py0 = 0f;
				for (int n = 1; n <= pieces; n++)
				{
					float t = (float)n / pieces;
					float py = t * length;
					float pxn = x0 + sway * Mathf.Sin(waves * Mathf.PI * t + phase) * t;
					c.Segment(px0, py0, pxn, py, Mathf.Lerp(0.012f, 0.006f, t - 1f / pieces), Mathf.Lerp(0.012f, 0.006f, t), lum * 0.85f, Color.white);
					// Leaflets in pairs along the piece, angled down the thread (toward its free end).
					float heading = Mathf.Atan2(py - py0, pxn - px0);
					for (int side = -1; side <= 1; side += 2)
					{
						float a = heading + side * Mathf.Deg2Rad * c.Rng.Range(18f, 38f);
						float leafLength = c.Rng.Range(0.06f, 0.1f) * (1f - 0.35f * t);
						c.Leaf(px0, py0, a, leafLength, leafLength * 0.3f, lum * c.Jitter(0.1f), Color.white, 0.1f);
					}
					px0 = pxn;
					py0 = py;
				}
			}
		}

		/// <summary>
		/// A shrub's leaf mass: leaves heaped over a round patch, every one pointing out from the middle at its own angle,
		/// so the edge is a ragged fringe of leaf tips and the middle is solid. The sparse sprays the trees hang left a
		/// shrub's card mostly empty (a broad-leaf spray covers a quarter of its card, a needle spray a ninth) and a bush
		/// built from them needed four to nine times the cards to hide anything behind it. Leaves laid first are the
		/// ones behind and are drawn darker, so the mass has depth.
		/// </summary>
		private static void DrawLeafMass(Canvas c, int count, float minLength, float maxLength, float width)
		{
			float reach = 0.47f - maxLength;
			// A few twigs showing between the leaves.
			for (int k = 0; k < 5; k++)
			{
				float a = c.Rng.Range(0f, Mathf.PI * 2f);
				c.Segment(0.5f, 0.5f, 0.5f + Mathf.Cos(a) * reach, 0.5f + Mathf.Sin(a) * reach, 0.012f, 0.004f, 0.45f, Color.white);
			}
			for (int k = 0; k < count; k++)
			{
				float r = reach * Mathf.Sqrt(c.Rng.NextFloat());
				float a = c.Rng.Range(0f, Mathf.PI * 2f);
				float x = 0.5f + Mathf.Cos(a) * r, y = 0.5f + Mathf.Sin(a) * r;
				// Outward near the edge, any way in the middle.
				float angle = a + Mathf.Deg2Rad * c.Rng.Range(-55f, 55f) * (0.4f + 0.6f * r / reach) + (r < reach * 0.3f ? c.Rng.Range(-3f, 3f) : 0f);
				float length = c.Rng.Range(minLength, maxLength);
				float lum = (0.62f + 0.3f * (k + 1f) / count) * c.Jitter(0.08f);
				c.Leaf(x, y, angle, length, length * width, lum, Color.white, 0.2f);
			}
		}

		/// <summary>A shrub's mass of needle shoots: short twigs radiating over a round patch, each bristling with needles.</summary>
		private static void DrawNeedleMass(Canvas c)
		{
			const int shoots = 80;
			for (int k = 0; k < shoots; k++)
			{
				float r = 0.31f * Mathf.Sqrt(c.Rng.NextFloat());
				float a = c.Rng.Range(0f, Mathf.PI * 2f);
				float x0 = 0.5f + Mathf.Cos(a) * r, y0 = 0.5f + Mathf.Sin(a) * r;
				float angle = a + Mathf.Deg2Rad * c.Rng.Range(-40f, 40f);
				float length = c.Rng.Range(0.12f, 0.17f);
				float x1 = x0 + Mathf.Cos(angle) * length, y1 = y0 + Mathf.Sin(angle) * length;
				float shade = (0.6f + 0.3f * (k + 1f) / shoots) * c.Jitter(0.06f);
				c.Segment(x0, y0, x1, y1, 0.01f, 0.004f, shade * 0.7f, Color.white);
				const int needles = 16;
				for (int n = 0; n < needles; n++)
				{
					float t = (n + 0.5f) / needles;
					float bx = Mathf.Lerp(x0, x1, t), by = Mathf.Lerp(y0, y1, t);
					float nl = 0.055f * (1f - t * 0.4f) * c.Jitter(0.2f);
					foreach (float side in new[] { -1f, 1f })
					{
						float na = angle + side * Mathf.Deg2Rad * c.Rng.Range(35f, 65f);
						c.Segment(bx, by, bx + Mathf.Cos(na) * nl, by + Mathf.Sin(na) * nl, 0.008f, 0.003f, shade * c.Jitter(0.06f), Color.white);
					}
				}
			}
		}
	}
}
#endif
