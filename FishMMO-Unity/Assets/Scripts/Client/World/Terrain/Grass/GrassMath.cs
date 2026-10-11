using System;
using System.Numerics;

namespace FishMMO.Client
{
	/// <summary>
	/// The arithmetic of the procedural blade grass, free of Unity and the GPU so the CPU reference, the
	/// tests and the offline harness share it: the hashes, the nested candidate lattice and its distance
	/// thinning (the LOD rings), the clumps, the blade's Bezier shape and the 16-byte blade record.
	/// FishGrassBlades.compute and FishGrassBlades.hlsl mirror every function here line for line; change
	/// both together.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The lattice.</b> Candidate blades sit one per cell of a world-anchored square lattice of
	/// <c>cell</c> metres (cell (i, j) covers [i·cell, (i+1)·cell)), jittered inside the cell by the cell's
	/// own hash. Each cell has a LEVEL, the number of trailing zero bits shared by i and j (capped at
	/// <see cref="MaxLevel"/>): a quarter of the cells are level ≥ 1, a sixteenth level ≥ 2, and so on.
	/// A cell's PRIORITY is a number in [0, 1) placed by its level — level L gets [4^-(L+1), 4^-L), the top
	/// level [0, 4^-MaxLevel) — and spread uniformly inside that range by its hash, so priorities are
	/// uniform over all cells.
	/// </para>
	/// <para>
	/// <b>Thinning.</b> At distance d a blade is kept while its priority is under the density SHARE at d
	/// (<see cref="Share"/>: 1 near, falling through the rings). The kept set at a smaller share is a subset
	/// of the kept set at a larger one, so blades thin out and come back without reshuffling; and a tile
	/// whose nearest point has share s only ever keeps cells of level ≥ <see cref="LevelForShare"/>(s), so
	/// the GPU runs one thread per cell of that coarser lattice (step 2^level), not of the full one. Blades
	/// close to the cut shrink toward the ground (<see cref="ThinFade"/>) instead of popping, and the kept
	/// ones widen (<see cref="Widen"/>) so a thinner far field still reads as a dense one.
	/// </para>
	/// </remarks>
	public static class GrassMath
	{
		/// <summary>The coarsest lattice level (step 2^5 = 32 cells).</summary>
		public const int MaxLevel = 5;

		/// <summary>Cells per tile side: a multiple of 2^<see cref="MaxLevel"/>, so every level's lattice is aligned to tiles.</summary>
		public const int TileCells = 128;

		/// <summary>Candidates per work item side (8 × 8 = one 64-thread group).</summary>
		public const int ItemSide = 8;

		/// <summary>The share of the share at which a blade starts shrinking before it is thinned out.</summary>
		public const float ThinBand = 0.3f;

		/// <summary>
		/// Blade height is packed in 7 bits on a square-root scale up to this many metres. A blade taller than this (a
		/// <see cref="GrassTypeTuning.Tall"/> type: Phragmites, elephant grass) packs half its height and sets
		/// <see cref="TallShift"/> in its colour word, so it reaches <see cref="TallHeightScale"/> times this; every blade
		/// at or under it packs exactly as it always did.
		/// </summary>
		public const float MaxPackedHeight = 2.5f;

		/// <summary>What a tall blade's packed height is multiplied by (<see cref="TallShift"/>).</summary>
		public const float TallHeightScale = 2f;

		/// <summary>The most blade types a record can name: four bits in the packed word, the fifth in the colour word (<see cref="TypeHighShift"/>).</summary>
		public const int MaxTypes = 32;

		public const uint SaltPriority = 0x9E3779B9u;
		public const uint SaltJitterX = 0x85EBCA6Bu;
		public const uint SaltJitterZ = 0xC2B2AE35u;
		public const uint SaltSpawn = 0x27D4EB2Fu;
		public const uint SaltClumpX = 0x165667B1u;
		public const uint SaltClumpZ = 0xD3A2646Cu;
		public const uint SaltClump = 0xFD7046C5u;
		public const uint SaltBlade = 0xB55A4F09u;
		public const uint SaltPull = 0x94D049BBu;
		public const uint SaltSprinkle = 0x5BD1E995u;

		// ── Hashes ───────────────────────────────────────────────────

		/// <summary>The finaliser of FishTerrainInstancing.compute's FishHash (lowbias32).</summary>
		public static uint Mix(uint x)
		{
			unchecked
			{
				x ^= x >> 16;
				x *= 0x7feb352du;
				x ^= x >> 15;
				x *= 0x846ca68bu;
				x ^= x >> 16;
				return x;
			}
		}

		/// <summary>A hash of an integer lattice point and a salt.</summary>
		public static uint Hash(int x, int z, uint salt)
		{
			unchecked
			{
				return Mix((uint)x * 0x8da6b343u ^ Mix((uint)z * 0xd8163841u ^ salt));
			}
		}

		/// <summary>A hash's low 24 bits as [0, 1).</summary>
		public static float Unit(uint h) => (h & 0xFFFFFFu) / 16777216f;

		/// <summary>A hash of a world position's exact bits (the vertex shader's per-blade randoms; roots are deterministic, so is this).</summary>
		public static uint HashPosition(float x, float z, uint salt)
		{
			unchecked
			{
				return Mix((uint)BitConverter.SingleToInt32Bits(x) ^ Mix((uint)BitConverter.SingleToInt32Bits(z) ^ salt));
			}
		}

		// ── The nested lattice ───────────────────────────────────────

		/// <summary>Trailing zero bits of a cell index (32 for zero), as HLSL firstbitlow gives them.</summary>
		public static int TrailingZeros(int v)
		{
			if (v == 0)
			{
				return 32;
			}
			uint u = unchecked((uint)v);
			int n = 0;
			while ((u & 1u) == 0u)
			{
				u >>= 1;
				n++;
			}
			return n;
		}

		/// <summary>A cell's level: the trailing zeros its two indices share, at most <see cref="MaxLevel"/>.</summary>
		public static int CellLevel(int i, int j) => Math.Min(Math.Min(TrailingZeros(i), TrailingZeros(j)), MaxLevel);

		/// <summary>4^-n.</summary>
		public static float Quarter(int n) => (float)Math.Pow(0.25, n);

		/// <summary>A cell's priority from its level and a uniform number (see the class remarks).</summary>
		public static float Priority(int level, float u)
		{
			if (level >= MaxLevel)
			{
				return u * Quarter(MaxLevel);
			}
			float hi = Quarter(level), lo = Quarter(level + 1);
			return lo + u * (hi - lo);
		}

		/// <summary>The cell's priority, from its indices.</summary>
		public static float CellPriority(int i, int j) => Priority(CellLevel(i, j), Unit(Hash(i, j, SaltPriority)));

		/// <summary>The coarsest lattice level whose cells can still be kept at <paramref name="share"/>: every cell of a lower level has priority ≥ share.</summary>
		public static int LevelForShare(float share)
		{
			if (share >= 1f)
			{
				return 0;
			}
			if (share <= 0f)
			{
				return MaxLevel;
			}
			int level = (int)Math.Floor(Math.Log(1.0 / share) / Math.Log(4.0) + 1e-6);
			return Math.Max(0, Math.Min(MaxLevel, level));
		}

		/// <summary>
		/// The density share at distance <paramref name="d"/>: 1 to the first ring, then log-log between the
		/// ring points (distance, density), flat after the last. Densities are relative to the first.
		/// </summary>
		public static float Share(float d, float[] ringDistance, float[] ringDensity, int count)
		{
			if (count <= 0 || ringDensity[0] <= 0f || d <= ringDistance[0])
			{
				return 1f;
			}
			for (int r = 1; r < count; r++)
			{
				if (d <= ringDistance[r])
				{
					float a = (float)Math.Log(Math.Max(1e-3f, ringDistance[r - 1])), b = (float)Math.Log(Math.Max(1e-3f, ringDistance[r]));
					float t = b > a ? ((float)Math.Log(d) - a) / (b - a) : 1f;
					float da = (float)Math.Log(Math.Max(1e-6f, ringDensity[r - 1])), db = (float)Math.Log(Math.Max(1e-6f, ringDensity[r]));
					return Math.Min(1f, (float)Math.Exp(da + (db - da) * t) / ringDensity[0]);
				}
			}
			return Math.Min(1f, ringDensity[count - 1] / ringDensity[0]);
		}

		/// <summary>How much of a kept blade stands (1 whole .. 0 sunk), by how close its priority is to the cut.</summary>
		public static float ThinFade(float priority, float share)
		{
			if (priority >= share)
			{
				return 0f;
			}
			return Math.Min(1f, (share - priority) / Math.Max(1e-9f, share * ThinBand));
		}

		/// <summary>The width multiplier of a field thinned to <paramref name="share"/>: share^-exponent, at most <paramref name="max"/>.</summary>
		public static float Widen(float share, float exponent, float max)
		{
			return Math.Min(max, (float)Math.Pow(Math.Max(1e-6f, share), -exponent));
		}

		/// <summary>Candidates a tile processes at a level (one per lattice cell of step 2^level).</summary>
		public static int CandidatesPerTile(int level)
		{
			int side = TileCells >> level;
			return side * side;
		}

		/// <summary>Work items (8 × 8 candidates) a tile needs at a level, per side.</summary>
		public static int ItemsPerSide(int level) => Math.Max(1, ((TileCells >> level) + ItemSide - 1) / ItemSide);

		// ── Candidates and clumps ────────────────────────────────────

		/// <summary>A candidate's root on the ground plane before clumping: its cell, jittered by the cell's hash.</summary>
		public static Vector2 CellRoot(int i, int j, float cell)
		{
			return new Vector2((i + Unit(Hash(i, j, SaltJitterX))) * cell, (j + Unit(Hash(i, j, SaltJitterZ))) * cell);
		}

		/// <summary>The clump a point belongs to: the nearest jittered centre of the 3 × 3 clump cells around it.</summary>
		public static void NearestClump(Vector2 p, float clumpMetres, out int ci, out int cj, out Vector2 centre)
		{
			int bx = (int)Math.Floor(p.X / clumpMetres), bz = (int)Math.Floor(p.Y / clumpMetres);
			float best = float.MaxValue;
			ci = bx;
			cj = bz;
			centre = p;
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int x = bx + dx, z = bz + dz;
					var c = new Vector2((x + 0.15f + 0.7f * Unit(Hash(x, z, SaltClumpX))) * clumpMetres, (z + 0.15f + 0.7f * Unit(Hash(x, z, SaltClumpZ))) * clumpMetres);
					float d = Vector2.DistanceSquared(p, c);
					if (d < best)
					{
						best = d;
						ci = x;
						cj = z;
						centre = c;
					}
				}
			}
		}

		/// <summary>What a blade takes from its clump and itself (everything the compute writes into a record).</summary>
		public struct BladeShape
		{
			public Vector2 Root;
			/// <summary>Facing on the ground plane, radians (the blade bends toward its face).</summary>
			public float Facing;
			/// <summary>Tip lean, 0 upright .. 1 lying over.</summary>
			public float Lean;
			/// <summary>Height as a multiple of the type's height (clump × own).</summary>
			public float HeightScale;
			/// <summary>The clump's colour bias, 0..1.</summary>
			public float ClumpColour;
			/// <summary>The uniform number that picks the type (the clump's own, so a tussock is one species).</summary>
			public float TypePick;
		}

		/// <summary>
		/// The blade of cell (i, j) shaped by its clump: pulled toward the clump's centre by
		/// <paramref name="pull"/>, facing outward from it (a tussock opens like a fountain) blended with
		/// the clump's own facing by <paramref name="facingStrength"/>, sized by the clump and itself.
		/// </summary>
		public static BladeShape Shape(int i, int j, float cell, float clumpMetres, float pull, float facingStrength, float heightVariation, float maxLean)
		{
			Vector2 root = CellRoot(i, j, cell);
			NearestClump(root, clumpMetres, out int ki, out int kj, out Vector2 centre);
			uint clump = Hash(ki, kj, SaltClump);
			uint own = Hash(i, j, SaltBlade);
			uint pullHash = Hash(i, j, SaltPull);

			Vector2 toRoot = root - centre;
			float r = toRoot.Length() / Math.Max(1e-4f, clumpMetres);
			root = Vector2.Lerp(root, centre, pull * (0.4f + 0.6f * Unit(pullHash)));

			float outward = (float)Math.Atan2(toRoot.Y, toRoot.X);
			float clumpFacing = Unit(Mix(clump ^ 0x51ED270Bu)) * 6.2831853f;
			float jitter = (Unit(own) - 0.5f) * 2.4f;
			float facing = AngleLerp(outward, clumpFacing, facingStrength) + jitter * (1f - 0.5f * facingStrength);

			float clumpHeight = 1f + (Unit(Mix(clump ^ 0x2545F491u)) - 0.5f) * 2f * heightVariation;
			// Taller in the middle of a clump, shorter toward its edge.
			float middle = 1f - 0.35f * Math.Min(1f, r);
			float ownHeight = 0.72f + 0.38f * Unit(Mix(own ^ 0x68E31DA4u));
			float lean = maxLean * (0.25f + 0.75f * Math.Min(1f, r * 1.2f)) * (0.5f + 0.5f * Unit(Mix(own ^ 0xB5297A4Du)));

			return new BladeShape
			{
				Root = root,
				Facing = Wrap(facing),
				Lean = Math.Min(1f, Math.Max(0f, lean)),
				HeightScale = Math.Max(0.1f, clumpHeight * middle * ownHeight),
				ClumpColour = Unit(Mix(clump ^ 0x1B873593u)),
				TypePick = Unit(Mix(clump ^ 0xE6546B64u)),
			};
		}

		private static float Wrap(float a)
		{
			const float tau = 6.2831853f;
			a %= tau;
			return a < 0f ? a + tau : a;
		}

		private static float AngleLerp(float a, float b, float t)
		{
			float d = (b - a) % 6.2831853f;
			if (d > 3.14159265f) d -= 6.2831853f;
			if (d < -3.14159265f) d += 6.2831853f;
			return a + d * t;
		}

		/// <summary>
		/// The sprinkled channel a candidate holds, or −1 (it is then a tussock species' candidate, or bare). A
		/// sprinkled type (<see cref="GrassTypeTuning.Sprinkle"/>) has its own chance per candidate,
		/// <c>min(1, weight / fullness) × chance</c> with chance = stems per square metre × density factor / the
		/// near density, whatever else grows there; the chances stack in channel order and one uniform number
		/// <paramref name="u"/> (the cell's <see cref="SaltSprinkle"/> hash) picks among them.
		/// FishGrassBlades.compute FishGrassGenerate mirrors it.
		/// </summary>
		public static int PickSprinkle(float u, float[] weights, float[] chances, int count, float fullness)
		{
			float at = 0f;
			fullness = Math.Max(0.05f, fullness);
			for (int c = 0; c < count; c++)
			{
				float w = Math.Max(0f, weights[c]);
				if (w <= 0f || chances[c] <= 0f)
				{
					continue;
				}
				at += Math.Min(1f, w / fullness) * chances[c];
				if (u < at)
				{
					return c;
				}
			}
			return -1;
		}

		/// <summary>The type a pick lands on among channel densities (cumulative), or −1 when they are all 0.</summary>
		public static int PickChannel(float pick, float[] weights, int count)
		{
			float total = 0f;
			for (int c = 0; c < count; c++)
			{
				total += Math.Max(0f, weights[c]);
			}
			if (total <= 0f)
			{
				return -1;
			}
			float at = pick * total;
			int last = -1;
			for (int c = 0; c < count; c++)
			{
				float w = Math.Max(0f, weights[c]);
				at -= w;
				if (w > 0f)
				{
					last = c;
					if (at < 0f)
					{
						return c;
					}
				}
			}
			// Rounding past the end: the last channel with any weight.
			return last;
		}

		// ── The blade record (20 bytes: float3 root, uint packed, uint colour) ─────

		/// <summary>Bytes per blade in the slot buffers (FishGrassBlades.compute GrassBlade).</summary>
		public const int BladeStride = 20;

		/// <summary>
		/// Packs a blade: type 4 bits (its low four; the fifth rides the colour word, <see cref="RecordBits"/>), facing 7
		/// (1/128 turn), height 7 (square-root scale to <see cref="MaxPackedHeight"/>; a taller blade packs half its height
		/// and is marked tall in the colour word), lean 4, thin fade 5, clump colour 5.
		/// </summary>
		/// <remarks>
		/// The word is full, and every one of its fields is already as coarse as it can be without showing (facing in
		/// 1/128 turns, lean in 16ths), so the 32 types and the tall blades took two of the colour word's unused bits
		/// (24..28) instead of a field's precision: a blade of type 0..15 at or under 2.5 m packs bit for bit as it did
		/// when there were 16 types.
		/// </remarks>
		public static uint Pack(int type, float facing, float height, float lean, float fade, float clumpColour)
		{
			uint t = (uint)Math.Max(0, Math.Min(MaxTypes - 1, type)) & 15u;
			float packedHeight = height > MaxPackedHeight ? height / TallHeightScale : height;
			uint f = (uint)((int)Math.Round(facing / 6.2831853f * 128f) & 127);
			uint h = (uint)Math.Max(0, Math.Min(127, (int)Math.Round(Math.Sqrt(Math.Max(0f, packedHeight) / MaxPackedHeight) * 127f)));
			uint l = (uint)Math.Max(0, Math.Min(15, (int)Math.Round(lean * 15f)));
			uint d = (uint)Math.Max(0, Math.Min(31, (int)Math.Round(fade * 31f)));
			uint c = (uint)Math.Max(0, Math.Min(31, (int)Math.Round(clumpColour * 31f)));
			return t | f << 4 | h << 11 | l << 18 | d << 22 | c << 27;
		}

		/// <summary>The packed word alone, as a record whose colour word carries no <see cref="RecordBits"/> (types 0..15, blades to 2.5 m).</summary>
		public static void Unpack(uint packed, out int type, out float facing, out float height, out float lean, out float fade, out float clumpColour)
		{
			Unpack(packed, 0u, out type, out facing, out height, out lean, out fade, out clumpColour);
		}

		/// <summary>A whole record: the packed word and its colour word's <see cref="RecordBits"/> (FishGrassBlades.hlsl GrassUnpack).</summary>
		public static void Unpack(uint packed, uint colour, out int type, out float facing, out float height, out float lean, out float fade, out float clumpColour)
		{
			type = (int)(packed & 15u) | (int)(colour >> TypeHighShift & 1u) << 4;
			facing = ((packed >> 4) & 127u) / 128f * 6.2831853f;
			float q = ((packed >> 11) & 127u) / 127f;
			height = q * q * MaxPackedHeight * ((colour >> TallShift & 1u) != 0u ? TallHeightScale : 1f);
			lean = ((packed >> 18) & 15u) / 15f;
			fade = ((packed >> 22) & 31u) / 31f;
			clumpColour = ((packed >> 27) & 31u) / 31f;
		}

		/// <summary>Where a record's type's fifth bit sits in its colour word (types 16..31).</summary>
		public const int TypeHighShift = 28;

		/// <summary>Where a record's tall mark sits in its colour word: its packed height is half its own (<see cref="TallHeightScale"/>).</summary>
		public const int TallShift = 27;

		/// <summary>
		/// The bits of a blade's colour word that belong to its shape, not its colour: the type's fifth bit and the tall
		/// mark. Every record of the blade carries them, its shadow casters' colourless ones too (their colour word is
		/// these bits and the head part, never the valid flag). Zero for a type under 16 no taller than
		/// <see cref="MaxPackedHeight"/>, so such a record is what it always was. FishGrassBlades.compute GrassRecordBits.
		/// </summary>
		public static uint RecordBits(int type, float height)
		{
			uint high = (uint)Math.Max(0, Math.Min(MaxTypes - 1, type)) >> 4 & 1u;
			uint tall = height > MaxPackedHeight ? 1u : 0u;
			return high << TypeHighShift | tall << TallShift;
		}

		/// <summary>
		/// Packs the terrain's linear albedo under a blade: square root (so dark soils keep their steps) at
		/// 8 bits a channel, bit 31 set when <paramref name="valid"/> (FishGrassBlades.compute GrassPackColour).
		/// Bits 29..30 are the record's head part (<see cref="WithHeadPart"/>), 27..28 its <see cref="RecordBits"/>, 24..26 unused.
		/// </summary>
		public static uint PackColour(float r, float g, float b, bool valid)
		{
			static uint Q(float v) => (uint)Math.Max(0, Math.Min(255, (int)Math.Round(Math.Sqrt(Math.Max(0f, Math.Min(1f, v))) * 255f)));
			return Q(r) | Q(g) << 8 | Q(b) << 16 | (valid ? 1u : 0u) << 31;
		}

		/// <summary>The inverse of <see cref="PackColour"/> (FishGrassBlades.hlsl GrassUnpackColour).</summary>
		public static void UnpackColour(uint packed, out float r, out float g, out float b, out bool valid)
		{
			static float D(uint q) { float v = q / 255f; return v * v; }
			r = D(packed & 255u);
			g = D(packed >> 8 & 255u);
			b = D(packed >> 16 & 255u);
			valid = (packed >> 31) != 0u;
		}

		/// <summary>Where a record's head part sits in its colour word.</summary>
		public const int HeadPartShift = 29;

		/// <summary>
		/// The colour word of one of a stem's head records: part 0 is the stem itself, 1 and 2 the two crossed cards of
		/// its head. A head record repeats the stem's root and packed shape, so the vertex shader rebuilds the same stem
		/// (the same wind) and draws the card at its tip; the blade strip mesh is the card's geometry. Mirrored by
		/// FishGrassBlades.compute GrassWithHeadPart and FishGrassBlades.hlsl GrassHeadPart.
		/// </summary>
		public static uint WithHeadPart(uint colour, int part) => (colour & ~(3u << HeadPartShift)) | ((uint)part & 3u) << HeadPartShift;

		public static int HeadPartOf(uint colour) => (int)(colour >> HeadPartShift & 3u);

		/// <summary>
		/// One texel of a terrain's packed surface layers (GrassTerrain.SurfaceLayers, read by FishGrassBlades.compute
		/// GrassTerrainColourAt): the two strongest splat layers less the third's weight (the terrain shader's
		/// seam-free rule), as r = first layer id, g = second id, b = the first's share of the two (0..255), a = 255
		/// (0 where no layer has weight). One grass-owned texture instead of binding the terrain's own control maps
		/// to the compute: on OpenGL those extra per-dispatch bindings made whole terrains' blades flicker.
		/// </summary>
		public static uint PackSurfaceLayers(float[] weights, int count)
		{
			float w0 = 0f, w1 = 0f, w2 = 0f;
			int id0 = 0, id1 = 0;
			for (int l = 0; l < count && l < 256; l++)
			{
				float v = weights[l];
				if (v > w0)
				{
					w2 = w1;
					w1 = w0; id1 = id0;
					w0 = v; id0 = l;
				}
				else if (v > w1)
				{
					w2 = w1;
					w1 = v; id1 = l;
				}
				else if (v > w2)
				{
					w2 = v;
				}
			}
			float a = Math.Max(0f, w0 - w2), b = Math.Max(0f, w1 - w2);
			if (w0 <= 0f)
			{
				return 0u;
			}
			float share = a + b > 1e-5f ? a / (a + b) : 1f;
			uint q = (uint)Math.Max(0, Math.Min(255, (int)Math.Round(share * 255f)));
			return (uint)id0 | (uint)id1 << 8 | q << 16 | 255u << 24;
		}

		// ── The blade's curve ────────────────────────────────────────

		/// <summary>
		/// The blade's quadratic Bezier, root at the origin: the tip leans <paramref name="lean"/> of its
		/// height toward <paramref name="facing"/> and is pushed by <paramref name="push"/> (metres on the
		/// ground plane, the wind), the control point sits between the straight chord's middle (bend 0) and
		/// straight above the root (bend 1); then the whole curve is scaled about the root so its length is
		/// <paramref name="height"/>: wind and lean bend a blade over, never stretch it.
		/// </summary>
		public static void Curve(float height, float lean, float bend, Vector2 facing, Vector2 push, out Vector3 control, out Vector3 tip)
		{
			float upright = (float)Math.Sqrt(Math.Max(0.0, 1.0 - lean * lean));
			tip = new Vector3(facing.X * lean * height + push.X, upright * height, facing.Y * lean * height + push.Y);
			Vector3 chordMiddle = tip * 0.5f;
			var above = new Vector3(0f, tip.Y, 0f);
			control = Vector3.Lerp(chordMiddle, above, bend);
			float length = ApproximateLength(control, tip);
			float scale = height / Math.Max(1e-5f, length);
			control *= scale;
			tip *= scale;
		}

		/// <summary>A quadratic Bezier's length from the origin (Gravesen: two thirds chord, one third control polygon).</summary>
		public static float ApproximateLength(Vector3 control, Vector3 tip)
		{
			return (2f * tip.Length() + control.Length() + (tip - control).Length()) / 3f;
		}

		/// <summary>The curve at t (root at the origin).</summary>
		public static Vector3 Point(Vector3 control, Vector3 tip, float t) => 2f * t * (1f - t) * control + t * t * tip;

		/// <summary>The curve's tangent at t (not normalised).</summary>
		public static Vector3 Tangent(Vector3 control, Vector3 tip, float t) => 2f * (1f - t) * control + 2f * t * (tip - control);

		/// <summary>The curve's length by summing <paramref name="steps"/> chords (tests and the harness).</summary>
		public static float MeasuredLength(Vector3 control, Vector3 tip, int steps = 256)
		{
			float length = 0f;
			Vector3 last = Vector3.Zero;
			for (int s = 1; s <= steps; s++)
			{
				Vector3 p = Point(control, tip, s / (float)steps);
				length += (p - last).Length();
				last = p;
			}
			return length;
		}

		/// <summary>Blade width at t along it, as a share of its base width: full to a third, then tapering to the tip.</summary>
		public static float WidthAt(float t) => 1f - (float)Math.Pow(Math.Min(1f, Math.Max(0f, t)), 1.6);

		/// <summary>The rows of a blade strip with <paramref name="segments"/> segments: t of each row (the last row is the tip point).</summary>
		public static float RowT(int row, int segments) => (float)Math.Pow(row / (float)segments, 0.85);
	}
}
