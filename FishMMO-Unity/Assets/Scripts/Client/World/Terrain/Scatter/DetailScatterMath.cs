using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The arithmetic of the GPU detail scatter, free of scene objects so it can be tested: the 2 m blocks the detail
	/// layers are summed into, a block's instance count and its upper bound, where an instance stands in its block, and
	/// the distance thinning. FishDetailScatter.compute mirrors every function here; change both together.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Counts, not a smoothed field.</b> The blade grass smooths its detail layers over 2 m, because the scatter
	/// writes grass as points and blades want a field. Mesh details are the opposite: a cactus or a ring of mushrooms
	/// is a point, and smoothing would turn it into a haze of probability. So a block keeps the EXACT expected count
	/// of its cells (<see cref="TerrainDetailMath.ExpectedInCell"/>, as the chunk renderer counts them, summed over
	/// every prototype of the type), and its instances stand anywhere in the block: a point-placed plant moves by at
	/// most the block's size, and the count over any area is what the scatter painted.
	/// </para>
	/// <para>
	/// <b>Deterministic by world block.</b> A block's count coin and each instance's place, turn and size come from a
	/// hash of the block's WORLD index, the type's salt (a hash of its prefab's name, the same in every session) and
	/// the channel's seed, so a block regenerated every frame holds the same plants every frame, for every player.
	/// </para>
	/// <para>
	/// <b>Bounded.</b> A block of a channel holds at most <see cref="Bound"/> instances, whatever its coin, so the CPU
	/// sizes each prototype's slot from the blocks a camera lists before the GPU runs, and nothing is dropped.
	/// </para>
	/// </remarks>
	public static class DetailScatterMath
	{
		/// <summary>The block side the detail layers are summed into, metres (rounded to whole detail cells).</summary>
		public const float BlockMetres = 2f;

		/// <summary>Blocks per work item side: 8 × 8 blocks is one 64-thread group.</summary>
		public const int ItemSide = 8;

		/// <summary>The most instances one block of one channel holds (32 a square metre at 2 m blocks): a guard against a bad prototype; the system warns when a terrain reaches it.</summary>
		public const int MaxPerBlock = 128;

		/// <summary>
		/// Types one terrain can scatter, a channel each (2026-10-27: was 8, two RGBA density slices). The density is sparse now
		/// (<see cref="PackEntry"/>: only the painted pairs of block and channel are kept), so a channel costs nothing where it is
		/// not painted; the cap is the table's. Moron 2's busiest terrain paints 63 mesh kinds. Past it the cut reports an error
		/// (TerrainDetailBaker) and play logs one: nothing is drawn on the CPU instead.
		/// </summary>
		public const int MaxChannels = 256;

		// ── The sparse density (2026-10-27) ───────────────────────────────────────────────────────────────────────
		/* A terrain's density is every painted (block, channel) pair, one uint each, grouped by work item and within it by
		 * block (0..63, row z first), channel ascending: bits 0..15 the stored count as a half float (Stored: what the dense
		 * slices held), 16..27 the channel, 28..31 the block's painted cells (CellMask). Each item has a header of
		 * ItemHeaderUints in the GPU table: its first entry, then 65 sixteen-bit starts (blocks 0..63 and the end) relative
		 * to it. FishDetailScatter.compute reads it (ScatterBlockEntries); change both together. */

		/// <summary>Where an entry's channel starts, and how many bits it has (the format's limit; <see cref="MaxChannels"/> is the table's).</summary>
		public const int EntryChannelShift = 16, EntryChannelBits = 12;

		/// <summary>Where an entry's painted-cell mask starts.</summary>
		public const int EntryMaskShift = 28;

		/// <summary>Blocks in a work item.</summary>
		public const int BlocksPerItem = ItemSide * ItemSide;

		/// <summary>Uints of an item's header: its first entry, then 65 sixteen-bit block starts packed two a uint.</summary>
		public const int ItemHeaderUints = 1 + (BlocksPerItem + 2) / 2;

		/// <summary>The most entries one block keeps (a channel each): the starts are sixteen-bit, 64 × this must stay under 65536.</summary>
		public const int MaxEntriesPerBlock = 1023;

		/// <summary>The hash stream a painted cell is picked from (FishDetailScatter.compute SCATTER_CELL_STREAM).</summary>
		public const int CellStream = 10;

		/// <summary>
		/// The cells a side of a block the painted-cell mask covers: one for a 2 m cell, two for 1 m cells (the generator's), 0 for
		/// finer cells (no mask: the block's instances stand anywhere in it, as every one did before 2026-10-27).
		/// </summary>
		public static int MaskCells(int factor) => factor == 1 ? 1 : factor == 2 ? 2 : 0;

		/// <summary>The mask of a block whose every cell is painted (or that keeps no mask): its instances stand anywhere in it.</summary>
		public static uint FullMask(int factor) => factor == 1 ? 1u : 0xFu;

		/// <summary>The mask bit of the cell (<paramref name="cx"/>, <paramref name="cz"/>) of its block (row z first).</summary>
		public static uint CellBit(int cx, int cz, int factor)
		{
			int side = MaskCells(factor);
			return side == 0 ? 0xFu : 1u << (cz * side + cx);
		}

		/// <summary>One sparse entry: its stored count (a <see cref="Stored"/> value), its channel and its painted cells.</summary>
		public static uint PackEntry(float stored, int channel, uint mask)
		{
			uint half = Mathf.FloatToHalf(Mathf.Max(0f, stored));
			return half | ((uint)channel & ((1u << EntryChannelBits) - 1u)) << EntryChannelShift | (mask & 0xFu) << EntryMaskShift;
		}

		/// <summary>The inverse of <see cref="PackEntry"/> (FishDetailScatter.compute's loop over a block's entries).</summary>
		public static void UnpackEntry(uint entry, out float stored, out int channel, out uint mask)
		{
			stored = Mathf.HalfToFloat((ushort)(entry & 0xFFFFu));
			channel = (int)(entry >> EntryChannelShift & ((1u << EntryChannelBits) - 1u));
			mask = entry >> EntryMaskShift & 0xFu;
		}

		/// <summary>
		/// Which painted cell of its block an instance stands in (row z first, 0..3), by its own draw <paramref name="unit"/>
		/// (0..1): each set bit of <paramref name="mask"/> alike likely. FishDetailScatter.compute ScatterPickCell.
		/// </summary>
		public static int PickCell(uint mask, float unit)
		{
			mask &= 0xFu;
			int n = 0;
			for (int i = 0; i < 4; i++)
			{
				n += (int)(mask >> i & 1u);
			}
			if (n == 0)
			{
				return 0;
			}
			int k = Mathf.Min(n - 1, (int)(unit * n));
			for (int i = 0; i < 4; i++)
			{
				if ((mask >> i & 1u) == 0u)
				{
					continue;
				}
				if (k == 0)
				{
					return i;
				}
				k--;
			}
			return 0;
		}

		/// <summary>
		/// Where an instance stands in its block, metres from the block's corner: anywhere in it (<see cref="InBlock"/> across its
		/// <paramref name="extent"/>, a partial last block's share) when the block is wholly painted or keeps no mask, exactly as
		/// before the masks; else anywhere in one of its painted cells (<see cref="PickCell"/>, cells of half the block's
		/// <paramref name="blockSize"/>). So a fern at a painted edge (a shore, a floor's clearance, a biome's border) stays on the
		/// cells the scatter painted, as the chunk renderer kept it in its cell.
		/// </summary>
		public static Vector2 InBlockCells(uint hash, uint mask, int factor, Vector2 blockSize, Vector2 extent)
		{
			Vector2 u = InBlock(hash);
			if (MaskCells(factor) != 2 || (mask & 0xFu) == 0xFu)
			{
				return Vector2.Scale(u, extent);
			}
			int cell = PickCell(mask, TerrainDetailMath.Unit(hash, CellStream));
			return new Vector2(((cell & 1) + u.x) * 0.5f * blockSize.x, ((cell >> 1) + u.y) * 0.5f * blockSize.y);
		}

		/// <summary>The detail-map rows a build strip reads: whole item rows (an item is <see cref="ItemSide"/> blocks of <paramref name="factor"/> cells), about 64.</summary>
		public static int StripRows(int factor)
		{
			int itemRow = ItemSide * Mathf.Max(1, factor);
			return Mathf.Max(itemRow, 64 / itemRow * itemRow);
		}

		/// <summary>The share of the thinning band an instance fades across (FishTerrainInstancing.compute's FISH_THIN_BAND).</summary>
		public const float ThinBand = 0.08f;

		/// <summary>Detail cells per block side.</summary>
		public static int BlockFactor(float cellMetres) => Mathf.Max(1, Mathf.RoundToInt(BlockMetres / Mathf.Max(1e-3f, cellMetres)));

		/// <summary>Blocks per side for <paramref name="resolution"/> detail cells (the last block may be partial).</summary>
		public static int Blocks(int resolution, int factor) => (resolution + factor - 1) / Mathf.Max(1, factor);

		/// <summary>Work items per side for <paramref name="blocks"/> blocks.</summary>
		public static int Items(int blocks) => (blocks + ItemSide - 1) / ItemSide;

		/// <summary>The world index of a terrain's first block: a block's world index is this plus its own.</summary>
		public static int WorldBlockBase(float origin, float blockMetres) => Mathf.FloorToInt(origin / Mathf.Max(1e-4f, blockMetres) + 0.5f);

		/// <summary>An expected count as the GPU reads it back (the density slices are half floats), capped.</summary>
		public static float Stored(float expected)
		{
			if (!(expected > 0f))
			{
				return 0f;
			}
			return Mathf.HalfToFloat(Mathf.FloatToHalf(Mathf.Min(expected, MaxPerBlock)));
		}

		/// <summary>A block's instances: the whole part plus its own coin under the fraction (<see cref="TerrainDetailMath.CountInCell"/>), capped.</summary>
		public static int Count(float stored, uint countHash) => Mathf.Min(MaxPerBlock, TerrainDetailMath.CountInCell(stored, countHash));

		/// <summary>The most instances a block can hold whatever its coin: an upper bound on <see cref="Count"/>.</summary>
		public static int Bound(float stored) => stored > 0f ? Mathf.Min(MaxPerBlock, Mathf.CeilToInt(stored)) : 0;

		/// <summary>
		/// A type's salt: FNV-1a of its prefab's name, so the same plants stand in the same places in every session and
		/// for every player, whatever order the types were met in.
		/// </summary>
		public static uint Salt(string name)
		{
			uint h = 2166136261u;
			if (name != null)
			{
				foreach (char c in name)
				{
					h = (h ^ c) * 16777619u;
				}
			}
			return h;
		}

		/// <summary>The hash of a world block for a type and a channel's seed: instance 0 is the block's count coin, k + 1 the k-th instance.</summary>
		public static uint Hash(int worldBlockX, int worldBlockZ, uint salt, int seed, int instance)
		{
			return TerrainDetailMath.Hash(worldBlockX, worldBlockZ, unchecked((int)salt), seed, instance);
		}

		/// <summary>Where an instance stands in its block, (0..1, 0..1) of the block's extent: anywhere, by its hash (a wholly painted block: <see cref="InBlockCells"/>).</summary>
		public static Vector2 InBlock(uint hash) => new Vector2(TerrainDetailMath.Unit(hash, 0), TerrainDetailMath.Unit(hash, 1));

		/// <summary>An instance's turn about its up axis, radians.</summary>
		public static float Yaw(uint hash) => TerrainDetailMath.Unit(hash, 2) * 2f * Mathf.PI;

		/// <summary>The hash stream an instance's variant mesh is drawn from (FishDetailScatter.compute SCATTER_VARIANT_STREAM).</summary>
		public const int VariantStream = 5;

		/// <summary>
		/// Which of a type's <paramref name="variants"/> meshes an instance draws (2026-10-26): 0 for a type of one mesh, else
		/// one of them by its own hash, every one alike likely; its own stream, so its place, turn and size are as they were.
		/// </summary>
		public static int Variant(uint hash, int variants)
		{
			if (variants <= 1)
			{
				return 0;
			}
			return Mathf.Min(variants - 1, (int)(TerrainDetailMath.Unit(hash, VariantStream) * variants));
		}

		/// <summary>
		/// The instances one of a type's <paramref name="variants"/> slots is sized for when the type's whole bound is
		/// <paramref name="bound"/>: its even share and five standard deviations of the hash's spread over it (a binomial
		/// draw), never more than the whole. The CPU cannot know each variant's count without the GPU's hashes, and the whole
		/// bound for every variant would hold the type's instances <paramref name="variants"/> times over.
		/// </summary>
		public static long VariantBound(long bound, int variants)
		{
			if (variants <= 1 || bound <= 0)
			{
				return bound;
			}
			double p = 1.0 / variants;
			double share = bound * p + 5.0 * System.Math.Sqrt(bound * p * (1.0 - p)) + 16.0;
			return System.Math.Min(bound, (long)System.Math.Ceiling(share));
		}

		/// <summary>
		/// The share of instances kept at distance <paramref name="d"/>: 1 to <paramref name="thinStart"/>, then falling
		/// linearly to <paramref name="keepAtDistance"/> at <paramref name="drawDistance"/>.
		/// </summary>
		public static float Keep(float d, float thinStart, float keepAtDistance, float drawDistance)
		{
			if (keepAtDistance >= 1f || d <= thinStart)
			{
				return 1f;
			}
			float t = Mathf.Clamp01((d - thinStart) / Mathf.Max(1e-3f, drawDistance - thinStart));
			return Mathf.Lerp(1f, keepAtDistance, t);
		}

		/// <summary>
		/// How much of an instance the thinning fades out: 0 kept whole, (0, 1) dithered, 1 gone; kept while its own
		/// number <paramref name="positionHash01"/> (a hash of its position) is under the share kept.
		/// </summary>
		public static float ThinFade(float keep, float positionHash01)
		{
			return keep >= 1f ? 0f : Mathf.Clamp01(1f - (keep - positionHash01) / ThinBand);
		}

		/// <summary>How much of an instance dissolves at the edge of the draw distance: 0 inside the last band, 1 at the distance.</summary>
		public static float EdgeFade(float d, float drawDistance, float bandShare)
		{
			float band = Mathf.Max(1e-3f, bandShare * drawDistance);
			return Mathf.Clamp01((d - (drawDistance - band)) / band);
		}

		// ── Bushes: levels of detail and a fixed reach (FishDetailScatter.compute ScatterSelectLevel) ──

		/// <summary>How far a bush <paramref name="height"/> metres tall is drawn: its height times the metres per metre, clamped.</summary>
		public static float BushDrawDistance(float height, float metresPerMetre, float min, float max)
		{
			return Mathf.Clamp(Mathf.Max(0f, height) * metresPerMetre, min, Mathf.Max(min, max));
		}

		/// <summary>
		/// The level an instance <paramref name="d"/> metres away draws, for a type of <paramref name="levels"/> levels that
		/// step at <paramref name="metresPerMetre"/> (x: 0 → 1, y: 1 → 2) times the instance's height. In the last
		/// <paramref name="band"/> share of a level's reach it cross-fades into the next: <paramref name="fade"/> −f on the
		/// level returned and +f on <paramref name="partner"/> (FishLodFade.hlsl's convention: equal magnitudes, so each pixel
		/// is drawn by exactly one of the two); outside a band, fade 0 and no partner.
		/// </summary>
		public static int SelectLevel(float d, float height, int levels, Vector2 metresPerMetre, float band, out float fade, out int partner, out float partnerFade)
		{
			fade = 0f;
			partner = -1;
			partnerFade = 0f;
			levels = Mathf.Clamp(levels, 1, DetailScatterSettings.MaxLevels);
			if (levels <= 1)
			{
				return 0;
			}
			float end0 = metresPerMetre.x * height, end1 = metresPerMetre.y * height;
			int level = d < end0 ? 0 : levels > 2 && d < end1 ? 1 : levels - 1;
			if (level < levels - 1)
			{
				float end = level == 0 ? end0 : end1;
				float width = Mathf.Clamp(band, 0f, 1f) * end;
				float f = width > 0f ? (d - (end - width)) / width : 0f;
				if (f > 0f)
				{
					fade = -f;
					partner = level + 1;
					partnerFade = f;
				}
			}
			return level;
		}

		/// <summary>
		/// The levels (a bit each) any instance of heights <paramref name="minHeight"/>..<paramref name="maxHeight"/> between
		/// <paramref name="near"/> and <paramref name="far"/> metres away can draw, cross-fades included: which of a work
		/// item's slots its upper bound goes to (<see cref="SelectLevel"/>'s superset).
		/// </summary>
		public static int LevelsReached(float near, float far, float minHeight, float maxHeight, int levels, Vector2 metresPerMetre, float band)
		{
			levels = Mathf.Clamp(levels, 1, DetailScatterSettings.MaxLevels);
			if (levels <= 1)
			{
				return 1;
			}
			float keep = 1f - Mathf.Clamp01(band);
			int mask = 0;
			if (near < metresPerMetre.x * maxHeight)
			{
				mask |= 1;
			}
			if (levels > 2)
			{
				if (far >= metresPerMetre.x * minHeight * keep && near < metresPerMetre.y * maxHeight)
				{
					mask |= 2;
				}
				if (far >= metresPerMetre.y * minHeight * keep)
				{
					mask |= 4;
				}
			}
			else if (far >= metresPerMetre.x * minHeight * keep)
			{
				mask |= 2;
			}
			return mask;
		}

		/// <summary>The hash of a position the thinning keeps by (FishTerrainInstancing.compute FishThinFade's), 0..1.</summary>
		public static float PositionHash01(float x, float z)
		{
			uint h = TerrainDetailMath.Mix(unchecked((uint)System.BitConverter.SingleToInt32Bits(x)) ^ TerrainDetailMath.Mix(unchecked((uint)System.BitConverter.SingleToInt32Bits(z))));
			return (h & 0xFFFFFFu) / 16777216f;
		}
	}

	/// <summary>
	/// A terrain's sparse density as a build makes it (<see cref="DetailScatterMath.PackEntry"/>): item rows emitted in order
	/// from a strip's per-channel expected counts, every painted (block, channel) pair an entry, items and blocks in order, and
	/// each item's exact upper bound per channel for the CPU's slot sizing. Pure arithmetic, so a build, a bake and a test
	/// make the same entries from the same counts.
	/// </summary>
	public sealed class DetailScatterDensity
	{
		public readonly int Blocks, ItemsSide, Channels, Factor;
		/// <summary>Every entry, by item, then block (0..63, row z first), then channel ascending.</summary>
		public readonly System.Collections.Generic.List<uint> Entries = new System.Collections.Generic.List<uint>();
		/// <summary>Per item its first entry; one more at the end (the total).</summary>
		public readonly int[] ItemEntryStart;
		/// <summary>Per item 65 starts relative to its first entry: blocks 0..63 and the end.</summary>
		public readonly ushort[] BlockStarts;
		/// <summary>Blocks painted denser than <see cref="DetailScatterMath.MaxPerBlock"/> (drawn at the cap), and the first such channel.</summary>
		public int ClampedBlocks, ClampedChannel = -1;
		/// <summary>Blocks with more painted channels than <see cref="DetailScatterMath.MaxEntriesPerBlock"/> (the rest dropped).</summary>
		public int CrowdedBlocks;
		private int nextItem;

		public DetailScatterDensity(int blocks, int channels, int factor)
		{
			Blocks = blocks;
			ItemsSide = DetailScatterMath.Items(blocks);
			Channels = channels;
			Factor = factor;
			ItemEntryStart = new int[ItemsSide * ItemsSide + 1];
			BlockStarts = new ushort[ItemsSide * ItemsSide * (DetailScatterMath.BlocksPerItem + 1)];
		}

		/// <summary>True once every item has been emitted.</summary>
		public bool Complete => nextItem >= ItemsSide * ItemsSide;

		/// <summary>
		/// Emits the item rows wholly inside block rows <paramref name="blockRow0"/>.. of a strip (<paramref name="blockRows"/>
		/// rows; the last strip may end mid item row: the terrain's last blocks). <paramref name="expected"/>[c] holds channel
		/// c's expected instances per block of the strip (row z first, <see cref="Blocks"/> a row; null for a channel the strip
		/// never painted) and <paramref name="cells"/>[c] each block's painted cells (<see cref="DetailScatterMath.CellBit"/>).
		/// </summary>
		public void EmitRows(float[][] expected, byte[][] cells, int blockRow0, int blockRows)
		{
			int itemRow0 = blockRow0 / DetailScatterMath.ItemSide;
			int blockEnd = System.Math.Min(Blocks, blockRow0 + blockRows);
			int itemRowEnd = blockEnd >= Blocks ? ItemsSide : blockEnd / DetailScatterMath.ItemSide;
			uint full = DetailScatterMath.FullMask(Factor);
			for (int iz = itemRow0; iz < itemRowEnd; iz++)
			{
				for (int ix = 0; ix < ItemsSide; ix++)
				{
					int item = iz * ItemsSide + ix;
					int first = Entries.Count;
					ItemEntryStart[item] = first;
					int startsAt = item * (DetailScatterMath.BlocksPerItem + 1);
					for (int b = 0; b < DetailScatterMath.BlocksPerItem; b++)
					{
						BlockStarts[startsAt + b] = (ushort)(Entries.Count - first);
						int bx = ix * DetailScatterMath.ItemSide + b % DetailScatterMath.ItemSide;
						int bz = iz * DetailScatterMath.ItemSide + b / DetailScatterMath.ItemSide;
						if (bx >= Blocks || bz >= Blocks)
						{
							continue;
						}
						int at = (bz - blockRow0) * Blocks + bx;
						int kept = 0;
						for (int c = 0; c < Channels; c++)
						{
							float[] values = expected[c];
							if (values == null || !(values[at] > 0f))
							{
								continue;
							}
							if (kept >= DetailScatterMath.MaxEntriesPerBlock)
							{
								CrowdedBlocks++;
								break;
							}
							float stored = DetailScatterMath.Stored(values[at]);
							if (values[at] > DetailScatterMath.MaxPerBlock)
							{
								ClampedBlocks++;
								ClampedChannel = ClampedChannel < 0 ? c : ClampedChannel;
							}
							uint mask = cells[c] != null ? cells[c][at] : full;
							Entries.Add(DetailScatterMath.PackEntry(stored, c, mask == 0u ? full : mask));
							kept++;
						}
					}
					BlockStarts[startsAt + DetailScatterMath.BlocksPerItem] = (ushort)(Entries.Count - first);
					nextItem = item + 1;
				}
			}
			ItemEntryStart[ItemEntryStart.Length - 1] = Entries.Count;
		}

		/// <summary>
		/// Each item's exact upper bound per channel (<see cref="DetailScatterMath.Bound"/> summed over its blocks), sparse: item i's
		/// pairs are <paramref name="channel"/>/<paramref name="bound"/>[<paramref name="start"/>[i]..<paramref name="start"/>[i + 1]),
		/// channel ascending.
		/// </summary>
		public static void ItemBounds(int[] itemEntryStart, uint[] entries, int channels, out int[] start, out ushort[] channel, out int[] bound)
		{
			int items = itemEntryStart.Length - 1;
			start = new int[items + 1];
			var sums = new int[System.Math.Max(1, channels)];
			var touched = new System.Collections.Generic.List<int>();
			var outChannel = new System.Collections.Generic.List<ushort>();
			var outBound = new System.Collections.Generic.List<int>();
			for (int i = 0; i < items; i++)
			{
				start[i] = outChannel.Count;
				for (int e = itemEntryStart[i]; e < itemEntryStart[i + 1]; e++)
				{
					DetailScatterMath.UnpackEntry(entries[e], out float stored, out int c, out _);
					if (c >= sums.Length)
					{
						continue;
					}
					if (sums[c] == 0)
					{
						touched.Add(c);
					}
					sums[c] += DetailScatterMath.Bound(stored);
				}
				touched.Sort();
				foreach (int c in touched)
				{
					if (sums[c] > 0)
					{
						outChannel.Add((ushort)c);
						outBound.Add(sums[c]);
					}
					sums[c] = 0;
				}
				touched.Clear();
			}
			start[items] = outChannel.Count;
			channel = outChannel.ToArray();
			bound = outBound.ToArray();
		}

		/// <summary>
		/// Item headers for the GPU table (<see cref="DetailScatterMath.ItemHeaderUints"/> uints an item: its first entry at
		/// <paramref name="entryBase"/> + its start, then its 65 sixteen-bit block starts two a uint) written at
		/// <paramref name="at"/> of <paramref name="table"/>.
		/// </summary>
		public static void WriteHeaders(int[] itemEntryStart, ushort[] blockStarts, uint entryBase, uint[] table, int at)
		{
			int items = itemEntryStart.Length - 1;
			const int starts = DetailScatterMath.BlocksPerItem + 1;
			for (int i = 0; i < items; i++)
			{
				int h = at + i * DetailScatterMath.ItemHeaderUints;
				table[h] = entryBase + (uint)itemEntryStart[i];
				for (int w = 0; w < DetailScatterMath.ItemHeaderUints - 1; w++)
				{
					int lo = 2 * w, hi = 2 * w + 1;
					uint a = lo < starts ? blockStarts[i * starts + lo] : 0u;
					uint b = hi < starts ? blockStarts[i * starts + hi] : 0u;
					table[h + 1 + w] = a | b << 16;
				}
			}
		}
	}
}
