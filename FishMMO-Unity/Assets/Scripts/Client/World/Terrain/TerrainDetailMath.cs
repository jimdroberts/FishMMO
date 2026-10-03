using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The arithmetic of the instanced terrain detail renderer, free of scene objects so it can be tested:
	/// how many plants a detail cell holds, where each one stands inside it, how big it is, and which
	/// chunks a camera's detail distance reaches.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Deterministic by world cell.</b> Every value an instance takes comes from a hash of its detail
	/// cell's WORLD coordinates (the cell index counted from the world origin, not the tile's corner),
	/// the prototype's index and seed, and the instance's number in the cell. A chunk rebuilt after it
	/// was released, or built in another session, holds the same plants in the same places.
	/// </para>
	/// <para>
	/// <b>Counts follow Unity's.</b> In coverage mode a cell's map value (0–255) is the share of it covered,
	/// and a fully covered cell holds the prototype's coverage (<see cref="TerrainData.ComputeDetailCoverage"/>,
	/// Unity's own "instances that fit in a square unit", which folds in the prototype's density and size)
	/// times the cell's area, times the terrain's density scale. In instance-count mode the value is the
	/// count. The fraction left over is a hashed coin toss, so the expected count is exact.
	/// </para>
	/// </remarks>
	public static class TerrainDetailMath
	{
		/// <summary>The side of a detail chunk, metres (rounded to whole detail cells).</summary>
		public const float DefaultChunkMetres = 32f;

		/// <summary>Scales the coverage-mode count; 1 is Unity's own coverage. The calibration test reports the measured ratio.</summary>
		public const float CoverageCalibration = 1f;

		/// <summary>The most instances one cell may hold (a guard against a bad prototype).</summary>
		public const int MaxPerCell = 64;

		// ── Hashing ───────────────────────────────────────────────────

		/// <summary>A 32-bit integer mix (lowbias32): every input bit reaches every output bit.</summary>
		public static uint Mix(uint x)
		{
			x ^= x >> 16;
			x *= 0x7feb352dU;
			x ^= x >> 15;
			x *= 0x846ca68bU;
			x ^= x >> 16;
			return x;
		}

		/// <summary>The hash of a world detail cell for one prototype (its seed) and one instance in it.</summary>
		public static uint Hash(int cellX, int cellZ, int prototype, int seed, int instance)
		{
			uint h = Mix((uint)cellX * 0x9E3779B1U ^ Mix((uint)cellZ + 0x632BE5ABU));
			h = Mix(h ^ (uint)prototype * 0x85EBCA77U);
			h = Mix(h ^ (uint)seed * 0xC2B2AE3DU);
			return Mix(h ^ (uint)instance * 0x27D4EB2FU);
		}

		/// <summary>A value in [0, 1) from a hash and a stream number (each draw of an instance its own stream).</summary>
		public static float Unit(uint hash, int stream)
		{
			return (Mix(hash ^ (uint)(stream + 1) * 0x165667B1U) >> 8) * (1f / 16777216f);
		}

		/// <summary>The world index of the detail cell that holds a world coordinate, for cells of <paramref name="cellSize"/>.</summary>
		public static int WorldCell(float world, float cellSize)
		{
			return Mathf.FloorToInt(world / cellSize);
		}

		// ── Counts ────────────────────────────────────────────────────

		/// <summary>
		/// The expected instances in one cell: in coverage mode <c>value/255 × coverage-per-m² × cell area ×
		/// density scale × calibration</c>; in instance-count mode <c>value × density scale</c>. Never negative,
		/// at most <see cref="MaxPerCell"/>.
		/// </summary>
		public static float ExpectedInCell(int value, bool coverageMode, float coveragePerSquareMetre, float cellArea, float densityScale)
		{
			if (value <= 0 || densityScale <= 0f)
			{
				return 0f;
			}
			float expected = coverageMode
				? value / 255f * Mathf.Max(0f, coveragePerSquareMetre) * cellArea * densityScale * CoverageCalibration
				: value * densityScale;
			return Mathf.Min(expected, MaxPerCell);
		}

		/// <summary>The whole count of a cell: the integer part, plus one when the cell's own coin lands under the fraction.</summary>
		public static int CountInCell(float expected, uint cellHash)
		{
			if (expected <= 0f)
			{
				return 0;
			}
			int whole = Mathf.FloorToInt(expected);
			return whole + (Unit(cellHash, 7) < expected - whole ? 1 : 0);
		}

		/// <summary>
		/// Distance thinning on the GPU path: every instance is drawn to this distance, metres; beyond it
		/// a share of them (by a hash of their position, dither-faded at the edge of the share) falling
		/// linearly to <see cref="ThinKeepAtDistance"/> at the detail distance. A blade of grass 200 m out
		/// covers a pixel or two, so a quarter of them reads as the same meadow for a quarter of the vertices.
		/// </summary>
		public const float ThinStartMetres = 60f;

		/// <summary>The share of instances kept at the detail distance itself (see <see cref="ThinStartMetres"/>).</summary>
		public const float ThinKeepAtDistance = 0.25f;

		/// <summary>
		/// An upper bound on one prototype's instances within <paramref name="distance"/> of a point: the cells a
		/// disc of that radius (widened by a cell diagonal) can touch, each fully covered and holding its count
		/// rounded up.
		/// </summary>
		public static long DiscBound(float distance, float cellX, float cellZ, float coveragePerSquareMetre, float densityScale)
		{
			float area = cellX * cellZ;
			if (area <= 0f || distance <= 0f)
			{
				return 0;
			}
			float radius = distance + 2f * Mathf.Sqrt(cellX * cellX + cellZ * cellZ);
			double cells = Math.Ceiling(Math.PI * radius * radius / area);
			int perCell = Mathf.CeilToInt(ExpectedInCell(255, true, coveragePerSquareMetre, area, densityScale));
			return (long)(cells * perCell);
		}

		// ── Placement ─────────────────────────────────────────────────

		/// <summary>
		/// Where an instance stands inside its cell, (0..1, 0..1): jittered by the prototype's position jitter
		/// (1 anywhere in the cell, 0 at its centre).
		/// </summary>
		public static Vector2 InCell(uint hash, float jitter)
		{
			jitter = Mathf.Clamp01(jitter);
			return new Vector2(0.5f + (Unit(hash, 0) - 0.5f) * jitter, 0.5f + (Unit(hash, 1) - 0.5f) * jitter);
		}

		/// <summary>The instance's turn about its up axis, degrees.</summary>
		public static float Yaw(uint hash) => Unit(hash, 2) * 360f;

		/// <summary>
		/// Smooth value noise in [0, 1] over the world (bilinear between hashed lattice values with a
		/// smoothstep), so neighbouring plants share a size, as Unity's noise spread does.
		/// </summary>
		public static float Noise(float x, float z, int seed)
		{
			int x0 = Mathf.FloorToInt(x), z0 = Mathf.FloorToInt(z);
			float fx = x - x0, fz = z - z0;
			fx = fx * fx * (3f - 2f * fx);
			fz = fz * fz * (3f - 2f * fz);
			float a = Unit(Hash(x0, z0, -1, seed, 0), 3), b = Unit(Hash(x0 + 1, z0, -1, seed, 0), 3);
			float c = Unit(Hash(x0, z0 + 1, -1, seed, 0), 3), d = Unit(Hash(x0 + 1, z0 + 1, -1, seed, 0), 3);
			return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
		}

		/// <summary>An instance's (width, height) between the prototype's ranges by the noise at its position.</summary>
		public static Vector2 Size(float noise, float minWidth, float maxWidth, float minHeight, float maxHeight)
		{
			return new Vector2(Mathf.Lerp(minWidth, maxWidth, noise), Mathf.Lerp(minHeight, maxHeight, noise));
		}

		// ── Chunks ────────────────────────────────────────────────────

		/// <summary>Detail cells per chunk side: the chunk length in whole cells, at least one.</summary>
		public static int ChunkCells(float cellSize, float chunkMetres = DefaultChunkMetres)
		{
			return cellSize <= 0f ? 1 : Mathf.Max(1, Mathf.RoundToInt(chunkMetres / cellSize));
		}

		/// <summary>
		/// The chunk index range (inclusive, clamped to <paramref name="chunks"/>) a circle of
		/// <paramref name="radius"/> around <paramref name="local"/> (metres from the terrain's corner, one axis)
		/// overlaps; empty (min &gt; max) when it misses the terrain.
		/// </summary>
		public static void ChunkRange(float local, float radius, float chunkMetres, int chunks, out int min, out int max)
		{
			min = Mathf.Max(0, Mathf.FloorToInt((local - radius) / chunkMetres));
			max = Mathf.Min(chunks - 1, Mathf.FloorToInt((local + radius) / chunkMetres));
		}

		/// <summary>The distance from a point to a square in the ground plane (0 inside).</summary>
		public static float DistanceToSquare(float x, float z, float minX, float minZ, float side)
		{
			float dx = Mathf.Max(0f, Mathf.Max(minX - x, x - (minX + side)));
			float dz = Mathf.Max(0f, Mathf.Max(minZ - z, z - (minZ + side)));
			return Mathf.Sqrt(dx * dx + dz * dz);
		}
	}
}
