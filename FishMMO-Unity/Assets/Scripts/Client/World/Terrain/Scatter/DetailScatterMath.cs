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

		/// <summary>Types a terrain can scatter (two RGBA density slices); a type past it stays on the chunk renderer.</summary>
		public const int MaxChannels = 8;

		public const int ChannelsPerSlice = 4;

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

		/// <summary>Where an instance stands in its block, (0..1, 0..1) of the block's extent: anywhere, by its hash.</summary>
		public static Vector2 InBlock(uint hash) => new Vector2(TerrainDetailMath.Unit(hash, 0), TerrainDetailMath.Unit(hash, 1));

		/// <summary>An instance's turn about its up axis, radians.</summary>
		public static float Yaw(uint hash) => TerrainDetailMath.Unit(hash, 2) * 2f * Mathf.PI;

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
}
