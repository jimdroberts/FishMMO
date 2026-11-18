#if UNITY_EDITOR
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The cells of the vine atlas (<see cref="VineAtlas"/>), by what is drawn in them.</summary>
	/// <remarks>Append-only: a cell's index is its place in the texture, and every generated vine mesh's UVs point at it.</remarks>
	public enum VineCell
	{
		/// <summary>
		/// A trailing ivy shoot: a stem up the middle of the cell from its foot (v = 0) to its top, with the lobed leaves of
		/// common ivy on short stalks to either side, smaller toward the tip. The card a hanging strand or a reaching tip is
		/// cut from.
		/// </summary>
		IvySpray = 0,
		/// <summary>A dense, ragged-edged mass of overlapping ivy leaves: what a wall or a trunk is clad in.</summary>
		IvyMass = 1,
		/// <summary>A shoot of a jungle liana's (or a philodendron's) broad heart-shaped leaves, alternating on long stalks.</summary>
		HeartSpray = 2,
		/// <summary>A dense mass of heart-shaped leaves.</summary>
		HeartMass = 3,
		/// <summary>A shoot of Virginia creeper: each leaf five toothed leaflets from one stalk.</summary>
		PalmateSpray = 4,
		/// <summary>A dense mass of creeper leaves.</summary>
		PalmateMass = 5,
		/// <summary>
		/// Bare strands hanging from the top of the cell (v = 1) to ragged ends: a liana's aerial roots, the dead stems
		/// of a creeper. No leaves.
		/// </summary>
		Roots = 6,
		/// <summary>Opaque woody stem, streaked along its length (v): every stem tube's texture.</summary>
		Stem = 7,
	}

	/// <summary>
	/// The texture every generated vine is cut from (2026-10-12: "vines and ivy in the world"): a 4×2 grid of
	/// 256-pixel cells (<see cref="VineCell"/>), alpha for the outline, near-white for the colour.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Its own texture.</b> The foliage atlas the trees and shrubs share is full (FoliageAtlas: "after it and Strand the
	/// atlas is full"), and growing its grid would move every existing plant's UVs. A vine's leaves are their own shapes
	/// anyway — the lobed leaf of ivy, a liana's heart, the five leaflets of a creeper — so they get a texture of their
	/// own, built the same way: shapes here, colours on the vertices (one texture serves the evergreen ivy, the jungle's
	/// lianas and a creeper that turns red in autumn), drawn analytically with a one-pixel soft edge so the 0.5 contour
	/// is clean for the cutoff, and imported like the foliage atlas (coverage-preserving mipmaps, alpha dilation;
	/// ProceduralArtTextureImport).
	/// </para>
	/// <para>
	/// <b>Deterministic.</b> Every cell draws from its own stream, seeded by its index and the generation seed, so the
	/// texture is the same bytes on every machine and a cell added later changes no other cell.
	/// </para>
	/// </remarks>
	public static class VineAtlas
	{
		public const int Columns = 4, Rows = 2;
		public const int CellSize = 256;
		public const int Width = Columns * CellSize, Height = Rows * CellSize;

		/// <summary>The UV rectangle of a cell, inset by a few texels so mip filtering does not borrow from its neighbours.</summary>
		public static Rect CellRect(VineCell cell)
		{
			int index = (int)cell;
			int col = index % Columns, row = index / Columns;
			float w = 1f / Columns, h = 1f / Rows;
			float insetU = 4f / Width, insetV = 4f / Height;
			return new Rect(col * w + insetU, row * h + insetV, w - insetU * 2f, h - insetV * 2f);
		}

		/// <summary>A UV inside a cell, from coordinates 0..1 across it.</summary>
		public static Vector2 CellUV(VineCell cell, float u, float v)
		{
			Rect r = CellRect(cell);
			return new Vector2(r.xMin + Mathf.Clamp01(u) * r.width, r.yMin + Mathf.Clamp01(v) * r.height);
		}

		/// <summary>Draws the atlas: RGBA, rows bottom-up, <see cref="Width"/> × <see cref="Height"/>.</summary>
		public static Color32[] Generate(int seed)
		{
			var pixels = new Color32[Width * Height];
			var alpha = new float[CellSize * CellSize];
			var lum = new float[CellSize * CellSize];
			for (int index = 0; index < Columns * Rows; index++)
			{
				System.Array.Clear(alpha, 0, alpha.Length);
				for (int i = 0; i < lum.Length; i++)
				{
					lum[i] = 0.85f;
				}
				var canvas = new Canvas(CellSize, alpha, lum, new DeterministicRNG(ProceduralNoise.SeedFor("vineatlas/" + index, seed)));
				switch ((VineCell)index)
				{
					case VineCell.IvySpray: Spray(canvas, LeafShape.Ivy, 7, 0.27f, 0.17f); break;
					case VineCell.IvyMass: Mass(canvas, LeafShape.Ivy, 46, 0.15f, 0.22f); break;
					case VineCell.HeartSpray: Spray(canvas, LeafShape.Heart, 5, 0.3f, 0.2f); break;
					case VineCell.HeartMass: Mass(canvas, LeafShape.Heart, 30, 0.18f, 0.26f); break;
					case VineCell.PalmateSpray: Spray(canvas, LeafShape.Palmate, 5, 0.3f, 0.2f); break;
					case VineCell.PalmateMass: Mass(canvas, LeafShape.Palmate, 26, 0.2f, 0.27f); break;
					case VineCell.Roots: DrawRoots(canvas); break;
					case VineCell.Stem: DrawStem(canvas, seed); break;
				}
				int col = index % Columns, row = index / Columns;
				for (int y = 0; y < CellSize; y++)
				{
					for (int x = 0; x < CellSize; x++)
					{
						int c = y * CellSize + x;
						byte l = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(lum[c]) * 255f), 0, 255);
						pixels[(row * CellSize + y) * Width + col * CellSize + x] = new Color32(l, l, l,
							(byte)Mathf.Clamp(Mathf.RoundToInt(alpha[c] * 255f), 0, 255));
					}
				}
			}
			return pixels;
		}

		/// <summary>The share of a cell's texels that are leaf or stem (alpha at least half): how much a card of it covers.</summary>
		public static float Coverage(Color32[] pixels, VineCell cell)
		{
			int index = (int)cell;
			int col = index % Columns, row = index / Columns;
			int solid = 0;
			for (int y = 0; y < CellSize; y++)
			{
				for (int x = 0; x < CellSize; x++)
				{
					if (pixels[(row * CellSize + y) * Width + col * CellSize + x].a >= 128)
					{
						solid++;
					}
				}
			}
			return solid / (float)(CellSize * CellSize);
		}

		// ── Leaves ──────────────────────────────────────────────

		private enum LeafShape
		{
			/// <summary>Common ivy's juvenile leaf: three to five triangular lobes, palmately veined, pale veins on dark.</summary>
			Ivy,
			/// <summary>A broad heart: two rounded lobes either side of the stalk, drawn out to a point.</summary>
			Heart,
			/// <summary>Five toothed leaflets fanned from the top of one stalk.</summary>
			Palmate,
		}

		/// <summary>A leaf of a shape, its base at (x, y), its axis along <paramref name="angle"/> (radians), <paramref name="size"/> long.</summary>
		private static void Leaf(Canvas c, LeafShape shape, float x, float y, float angle, float size, float lum)
		{
			float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
			Vector2 axis = new Vector2(ca, sa);
			Vector2 Along(float deg, float length)
			{
				float a = angle + deg * Mathf.Deg2Rad;
				return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * length;
			}
			var at = new Vector2(x, y);
			float vein = lum * 1.18f;
			switch (shape)
			{
				case LeafShape.Ivy:
				{
					// The blade's body, then the lobes from the point the veins spread from.
					Vector2 hub = at + axis * (0.16f * size);
					c.Disc(hub.x + axis.x * 0.1f * size, hub.y + axis.y * 0.1f * size, 0.27f * size, lum);
					int lobes = c.Rng.NextFloat() < 0.35f ? 3 : 5;
					float[] degs = lobes == 3 ? new[] { 0f, -48f, 48f } : new[] { 0f, -42f, 42f, -88f, 88f };
					float[] lens = lobes == 3 ? new[] { 0.84f, 0.62f, 0.62f } : new[] { 0.84f, 0.62f, 0.62f, 0.4f, 0.4f };
					for (int k = 0; k < degs.Length; k++)
					{
						float deg = degs[k] + c.Rng.Range(-6f, 6f);
						float length = lens[k] * size * c.Jitter(0.08f);
						c.Lobe(hub, Along(deg, 1f), length, length * 0.62f, lum);
					}
					for (int k = 0; k < degs.Length; k++)
					{
						Vector2 tip = hub + Along(degs[k], lens[k] * size * 0.8f);
						c.Segment(hub.x, hub.y, tip.x, tip.y, 0.012f * size, 0.004f * size, vein);
					}
					break;
				}
				case LeafShape.Heart:
				{
					// The two lobes either side of the stalk's end, and the point drawn out from between them.
					Vector2 side = new Vector2(-sa, ca);
					Vector2 hub = at + axis * (0.08f * size);
					c.Disc(hub.x + (axis.x * 0.22f + side.x * 0.2f) * size, hub.y + (axis.y * 0.22f + side.y * 0.2f) * size, 0.24f * size, lum);
					c.Disc(hub.x + (axis.x * 0.22f - side.x * 0.2f) * size, hub.y + (axis.y * 0.22f - side.y * 0.2f) * size, 0.24f * size, lum);
					c.Lobe(hub + axis * (0.05f * size), axis, 0.92f * size, 0.68f * size, lum, 0.6f);
					Vector2 tip = hub + axis * (0.9f * size);
					c.Segment(hub.x, hub.y, tip.x, tip.y, 0.016f * size, 0.004f * size, vein);
					for (int k = 1; k <= 3; k++)
					{
						Vector2 from = hub + axis * (0.16f * k * size);
						for (int s = -1; s <= 1; s += 2)
						{
							Vector2 to = from + Along(s * 50f, 0.3f * size * (1.1f - 0.2f * k));
							c.Segment(from.x, from.y, to.x, to.y, 0.008f * size, 0.003f * size, vein * 0.97f);
						}
					}
					break;
				}
				case LeafShape.Palmate:
				{
					// The leaflets fan from the top of the stalk, the middle one longest.
					float[] degs = { 0f, -36f, 36f, -72f, 72f };
					float[] lens = { 0.8f, 0.72f, 0.72f, 0.52f, 0.52f };
					for (int k = 0; k < degs.Length; k++)
					{
						float deg = degs[k] + c.Rng.Range(-5f, 5f);
						float length = lens[k] * size * c.Jitter(0.07f);
						Vector2 dir = Along(deg, 1f);
						c.Lobe(at, dir, length, length * 0.42f, lum, 0.45f, 9f);
						Vector2 tip = at + dir * (length * 0.85f);
						c.Segment(at.x, at.y, tip.x, tip.y, 0.01f * size, 0.003f * size, vein);
					}
					break;
				}
			}
		}

		/// <summary>A shoot: a stem up the cell with leaves alternating along it on short stalks, smaller toward the tip.</summary>
		private static void Spray(Canvas c, LeafShape shape, int leaves, float baseSize, float tipSize)
		{
			const int joints = 12;
			var stem = new Vector2[joints + 1];
			float bend = c.Rng.Range(-0.05f, 0.05f), wave = c.Rng.Range(0f, 6.28f);
			for (int j = 0; j <= joints; j++)
			{
				float t = j / (float)joints;
				stem[j] = new Vector2(0.5f + bend * t * t + 0.015f * Mathf.Sin(wave + t * 7f), 0.02f + 0.9f * t);
			}
			for (int j = 0; j < joints; j++)
			{
				float t = j / (float)joints;
				c.Segment(stem[j].x, stem[j].y, stem[j + 1].x, stem[j + 1].y, Mathf.Lerp(0.022f, 0.01f, t), Mathf.Lerp(0.022f, 0.01f, t + 1f / joints), 0.5f);
			}
			for (int i = 0; i < leaves; i++)
			{
				float t = 0.1f + 0.78f * i / Mathf.Max(1, leaves - 1);
				Vector2 node = Sample(stem, t);
				float side = i % 2 == 0 ? 1f : -1f;
				float size = Mathf.Lerp(baseSize, tipSize, t) * c.Jitter(0.08f);
				// The stalk out to the side and up; the leaf on along it, turned a little further out.
				float stalkAngle = Mathf.PI * 0.5f - side * Mathf.Deg2Rad * c.Rng.Range(55f, 72f);
				float stalk = size * 0.28f;
				var baseAt = node + new Vector2(Mathf.Cos(stalkAngle), Mathf.Sin(stalkAngle)) * stalk;
				c.Segment(node.x, node.y, baseAt.x, baseAt.y, 0.009f, 0.006f, 0.55f);
				float leafAngle = stalkAngle - side * Mathf.Deg2Rad * c.Rng.Range(0f, 18f);
				// Keep the leaf inside the cell: a leaf that would run off its side is turned up the stem.
				Vector2 reach = baseAt + new Vector2(Mathf.Cos(leafAngle), Mathf.Sin(leafAngle)) * size;
				if (reach.x < 0.06f || reach.x > 0.94f)
				{
					leafAngle = Mathf.Lerp(leafAngle, Mathf.PI * 0.5f, 0.45f);
				}
				Leaf(c, shape, baseAt.x, baseAt.y, leafAngle, size, Mathf.Lerp(0.74f, 0.9f, c.Rng.NextFloat()));
			}
			// The growing tip: one small leaf straight on up the stem.
			Vector2 tipAt = stem[joints];
			Leaf(c, shape, tipAt.x, tipAt.y - 0.02f, Mathf.PI * 0.5f + c.Rng.Range(-0.2f, 0.2f), tipSize * 0.7f, 0.86f);
		}

		/// <summary>A dense mass: leaves over a round patch, any way up, a few stems showing between them, ragged at its edge.</summary>
		private static void Mass(Canvas c, LeafShape shape, int count, float minSize, float maxSize)
		{
			const float reach = 0.42f;
			for (int k = 0; k < 6; k++)
			{
				float a = c.Rng.Range(0f, Mathf.PI * 2f);
				c.Segment(0.5f, 0.5f, 0.5f + Mathf.Cos(a) * reach * 0.9f, 0.5f + Mathf.Sin(a) * reach * 0.9f, 0.016f, 0.006f, 0.45f);
			}
			for (int k = 0; k < count; k++)
			{
				float r = (reach - maxSize * 0.55f) * Mathf.Sqrt(c.Rng.NextFloat());
				float a = c.Rng.Range(0f, Mathf.PI * 2f);
				float x = 0.5f + Mathf.Cos(a) * r, y = 0.5f + Mathf.Sin(a) * r;
				float angle = a + Mathf.Deg2Rad * c.Rng.Range(-70f, 70f);
				float size = c.Rng.Range(minSize, maxSize);
				// Later leaves lie over earlier ones and are lit a little more: the mass reads as layered, not flat.
				float lum = (0.66f + 0.24f * (k + 1f) / count) * c.Jitter(0.06f);
				Leaf(c, shape, x - Mathf.Cos(angle) * size * 0.4f, y - Mathf.Sin(angle) * size * 0.4f, angle, size, lum);
			}
		}

		/// <summary>Bare strands from the top of the cell, kinked, thinning to their ends at different lengths.</summary>
		private static void DrawRoots(Canvas c)
		{
			const int strands = 13;
			for (int s = 0; s < strands; s++)
			{
				float x = 0.08f + 0.84f * (s + c.Rng.Range(0.2f, 0.8f)) / strands;
				float end = c.Rng.Range(0.05f, 0.7f);
				float y = 0.99f;
				float w = c.Rng.Range(0.012f, 0.022f);
				const int joints = 7;
				for (int j = 0; j < joints; j++)
				{
					float ny = Mathf.Lerp(0.99f, end, (j + 1f) / joints);
					float nx = Mathf.Clamp(x + c.Rng.Range(-0.025f, 0.025f), 0.03f, 0.97f);
					float w1 = w * (1f - 0.75f * (j + 1f) / joints);
					c.Segment(x, y, nx, ny, w * (1f - 0.75f * j / joints), w1, c.Rng.Range(0.5f, 0.7f));
					x = nx;
					y = ny;
				}
			}
		}

		/// <summary>Opaque bark: fine streaks along the stem (v), a little lighter in its ridges.</summary>
		private static void DrawStem(Canvas c, int seed)
		{
			for (int y = 0; y < c.Size; y++)
			{
				for (int x = 0; x < c.Size; x++)
				{
					float u = (x + 0.5f) / c.Size, v = (y + 0.5f) / c.Size;
					float streak = ProceduralNoise.PeriodicFbm(u, v, 16, 2, 3, 0.5f, ProceduralNoise.SeedFor("vinestem", seed));
					int i = y * c.Size + x;
					c.Alpha[i] = 1f;
					c.Lum[i] = 0.72f + 0.22f * streak;
				}
			}
		}

		private static Vector2 Sample(Vector2[] path, float t)
		{
			float f = Mathf.Clamp01(t) * (path.Length - 1);
			int i = Mathf.Min(path.Length - 2, Mathf.FloorToInt(f));
			return Vector2.Lerp(path[i], path[i + 1], f - i);
		}

		/// <summary>A cell's alpha and brightness while it is drawn, in cell units (0..1 across).</summary>
		private sealed class Canvas
		{
			public readonly int Size;
			public readonly float[] Alpha;
			public readonly float[] Lum;
			public readonly DeterministicRNG Rng;

			public Canvas(int size, float[] alpha, float[] lum, DeterministicRNG rng)
			{
				Size = size;
				Alpha = alpha;
				Lum = lum;
				Rng = rng;
			}

			public float Jitter(float amount) => 1f + Rng.Range(-amount, amount);

			private void Write(int i, float coverage, float lum)
			{
				if (coverage <= 0f)
				{
					return;
				}
				// Over-compositing: later shapes lie on earlier ones.
				Lum[i] = Mathf.Lerp(Lum[i], lum, coverage);
				Alpha[i] = Mathf.Max(Alpha[i], coverage);
			}

			/// <summary>A tapered line from (x0,y0) to (x1,y1), widths in cell units.</summary>
			public void Segment(float x0, float y0, float x1, float y1, float w0, float w1, float lum)
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
						Write(y * Size + x, Mathf.Clamp01(0.5f - d), lum);
					}
				}
			}

			public void Disc(float x, float y, float radius, float lum) => Segment(x, y, x + 1e-4f, y, radius * 2f, radius * 2f, lum);

			/// <summary>
			/// A pointed lobe from <paramref name="from"/> along <paramref name="dir"/>: widest <paramref name="widest"/> of the
			/// way out, pointed at its tip, its edge toothed when <paramref name="teeth"/> is set (teeth along its length).
			/// Shaded a little darker toward its edge.
			/// </summary>
			public void Lobe(Vector2 from, Vector2 dir, float length, float width, float lum, float widest = 0.42f, float teeth = 0f)
			{
				dir = dir.normalized;
				var side = new Vector2(-dir.y, dir.x);
				float px = 1f / Size;
				float reach = length + width;
				int minX = Mathf.Max(0, Mathf.FloorToInt((from.x - reach) * Size)), maxX = Mathf.Min(Size - 1, Mathf.CeilToInt((from.x + reach) * Size));
				int minY = Mathf.Max(0, Mathf.FloorToInt((from.y - reach) * Size)), maxY = Mathf.Min(Size - 1, Mathf.CeilToInt((from.y + reach) * Size));
				for (int py = minY; py <= maxY; py++)
				{
					for (int pxi = minX; pxi <= maxX; pxi++)
					{
						var p = new Vector2((pxi + 0.5f) * px - from.x, (py + 0.5f) * px - from.y);
						float a = Vector2.Dot(p, dir) / Mathf.Max(1e-5f, length);
						float b = Vector2.Dot(p, side);
						if (a < -0.05f || a > 1.05f)
						{
							continue;
						}
						float ac = Mathf.Clamp01(a);
						// Widest at `widest` of the way out: a sine stretched so its crest lands there, pointed at the tip.
						float k = Mathf.Log(0.5f) / Mathf.Log(Mathf.Clamp(widest, 0.1f, 0.9f));
						float half = width * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(ac, k)), 0.7f);
						if (teeth > 0f)
						{
							half *= 1f - 0.14f * Mathf.Abs(Mathf.Sin(ac * teeth * Mathf.PI)) * Mathf.Clamp01(ac * 3f);
						}
						float d = (Mathf.Abs(b) - half) * Size;
						float coverage = Mathf.Clamp01(0.5f - d);
						float shade = lum * (1f - 0.12f * Mathf.Abs(b) / Mathf.Max(1e-5f, half));
						Write(py * Size + pxi, coverage, shade);
					}
				}
			}
		}
	}
}
#endif
