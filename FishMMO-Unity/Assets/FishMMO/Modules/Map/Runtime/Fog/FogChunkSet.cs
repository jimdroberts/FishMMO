using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The explored chunks of one scene, on the world-anchored chunk grid both peers share (issue #241).
	/// </summary>
	/// <remarks>
	/// <para><b>World-anchored.</b> Chunk (x, z) covers <c>[x·size, (x+1)·size) × [z·size, (z+1)·size)</c> in world metres,
	/// whatever the scene's map rectangle is, so a re-baked or re-cut map keeps every explored chunk where it was. The
	/// client's display grid (<c>FogOfWarMap</c>) starts on a whole chunk line for the same reason.</para>
	/// <para><b>Storage.</b> One bit per chunk over the bounding box of what has been explored, grown as exploration
	/// spreads; at most <see cref="MaxDimension"/> chunks on a side (164 km at the default 40 m).</para>
	/// <para>The server tracks one per account per scene; the client applies them to its display grid.</para>
	/// </remarks>
	public sealed class FogChunkSet
	{
		/// <summary>The default chunk side, in metres. A scene's map definition may set its own.</summary>
		public const float DefaultChunkSize = 40.0f;

		/// <summary>Most chunks the set spans on either axis.</summary>
		public const int MaxDimension = 4096;

		/// <summary>Chunks added around a growth, so walking along an edge does not reallocate at every step.</summary>
		private const int GrowthMargin = 8;

		/// <summary>The chunk side, in metres.</summary>
		public float ChunkSize { get; }

		/// <summary>How many chunks are explored.</summary>
		public int Count { get; private set; }

		/// <summary>Whether nothing is explored.</summary>
		public bool IsEmpty => Count == 0;

		private int originX, originZ, width, height;
		private ulong[] bits = Array.Empty<ulong>();

		public FogChunkSet(float chunkSize = DefaultChunkSize)
		{
			ChunkSize = chunkSize > 0.0f ? chunkSize : DefaultChunkSize;
		}

		/// <summary>The chunk index containing <paramref name="coordinate"/> on one axis.</summary>
		public static int ChunkOf(float coordinate, float chunkSize)
		{
			return Mathf.FloorToInt(coordinate / chunkSize);
		}

		/// <summary>Whether chunk (x, z) is explored.</summary>
		public bool Contains(int x, int z)
		{
			int lx = x - originX, lz = z - originZ;
			if (lx < 0 || lz < 0 || lx >= width || lz >= height)
			{
				return false;
			}
			int index = lz * width + lx;
			return (bits[index >> 6] & (1UL << (index & 63))) != 0;
		}

		/// <summary>Marks chunk (x, z) explored. False when it already was, or lies past <see cref="MaxDimension"/>.</summary>
		public bool Add(int x, int z)
		{
			if (!EnsureCovers(x, z))
			{
				return false;
			}
			int index = (z - originZ) * width + (x - originX);
			ulong bit = 1UL << (index & 63);
			if ((bits[index >> 6] & bit) != 0)
			{
				return false;
			}
			bits[index >> 6] |= bit;
			++Count;
			return true;
		}

		/// <summary>Marks the chunk under <paramref name="position"/> explored.</summary>
		public bool AddAt(Vector3 position)
		{
			return Add(ChunkOf(position.x, ChunkSize), ChunkOf(position.z, ChunkSize));
		}

		/// <summary>
		/// Marks every chunk the straight line from <paramref name="from"/> to <paramref name="to"/> passes through, so a
		/// fast traveller (a mount, flight) does not skip chunks between two samples.
		/// </summary>
		/// <returns>How many were newly explored.</returns>
		public int AddLine(Vector3 from, Vector3 to)
		{
			float dx = to.x - from.x, dz = to.z - from.z;
			float length = Mathf.Sqrt(dx * dx + dz * dz);
			// Sampled at a quarter chunk: no chunk the line crosses is stepped over (a corner cut is at most that wide).
			int steps = Mathf.Clamp(Mathf.CeilToInt(length / (ChunkSize * 0.25f)), 1, 4096);
			int added = 0;
			for (int i = 0; i <= steps; ++i)
			{
				float t = i / (float)steps;
				if (AddAt(new Vector3(from.x + dx * t, 0.0f, from.z + dz * t)))
				{
					++added;
				}
			}
			return added;
		}

		/// <summary>
		/// Marks every chunk whose nearest point is within <paramref name="radius"/> of <paramref name="center"/> — the rule the
		/// client's <c>FogOfWarMap.RevealAround</c> uses.
		/// </summary>
		public int AddAround(Vector3 center, float radius)
		{
			if (radius <= 0.0f)
			{
				return 0;
			}
			int minX = ChunkOf(center.x - radius, ChunkSize), maxX = ChunkOf(center.x + radius, ChunkSize);
			int minZ = ChunkOf(center.z - radius, ChunkSize), maxZ = ChunkOf(center.z + radius, ChunkSize);
			float radiusSquared = radius * radius;
			int added = 0;
			for (int z = minZ; z <= maxZ; ++z)
			{
				float nearestZ = Mathf.Clamp(center.z, z * ChunkSize, (z + 1) * ChunkSize);
				float ddz = nearestZ - center.z;
				for (int x = minX; x <= maxX; ++x)
				{
					float nearestX = Mathf.Clamp(center.x, x * ChunkSize, (x + 1) * ChunkSize);
					float ddx = nearestX - center.x;
					if (ddx * ddx + ddz * ddz <= radiusSquared && Add(x, z))
					{
						++added;
					}
				}
			}
			return added;
		}

		/// <summary>Marks every chunk overlapping <paramref name="area"/> (x and y are world x and z).</summary>
		public int AddArea(Rect area)
		{
			if (area.width <= 0.0f || area.height <= 0.0f)
			{
				return 0;
			}
			int minX = ChunkOf(area.xMin, ChunkSize), maxX = ChunkOf(area.xMax, ChunkSize);
			int minZ = ChunkOf(area.yMin, ChunkSize), maxZ = ChunkOf(area.yMax, ChunkSize);
			int added = 0;
			for (int z = minZ; z <= maxZ; ++z)
			{
				for (int x = minX; x <= maxX; ++x)
				{
					if (Add(x, z))
					{
						++added;
					}
				}
			}
			return added;
		}

		/// <summary>
		/// Adds everything <paramref name="other"/> has explored. A set with a different chunk size is rasterised by its
		/// chunks' centres.
		/// </summary>
		/// <returns>How many were newly explored here.</returns>
		public int UnionWith(FogChunkSet other)
		{
			if (other == null || other.IsEmpty)
			{
				return 0;
			}
			int added = 0;
			bool sameGrid = Mathf.Approximately(other.ChunkSize, ChunkSize);
			other.ForEach((x, z) =>
			{
				bool isNew = sameGrid
					? Add(x, z)
					: AddAt(new Vector3((x + 0.5f) * other.ChunkSize, 0.0f, (z + 0.5f) * other.ChunkSize));
				if (isNew)
				{
					++added;
				}
			});
			return added;
		}

		/// <summary>Calls <paramref name="visit"/> with every explored chunk, row by row.</summary>
		public void ForEach(Action<int, int> visit)
		{
			if (visit == null || Count == 0)
			{
				return;
			}
			for (int lz = 0; lz < height; ++lz)
			{
				for (int lx = 0; lx < width; ++lx)
				{
					int index = lz * width + lx;
					if ((bits[index >> 6] & (1UL << (index & 63))) != 0)
					{
						visit(originX + lx, originZ + lz);
					}
				}
			}
		}

		/// <summary>The tight bounding box of the explored chunks, in chunk indices; false when empty.</summary>
		public bool TryGetBounds(out int minX, out int minZ, out int maxX, out int maxZ)
		{
			minX = minZ = int.MaxValue;
			maxX = maxZ = int.MinValue;
			if (Count == 0)
			{
				return false;
			}
			int mnx = int.MaxValue, mnz = int.MaxValue, mxx = int.MinValue, mxz = int.MinValue;
			ForEach((x, z) =>
			{
				if (x < mnx) mnx = x;
				if (x > mxx) mxx = x;
				if (z < mnz) mnz = z;
				if (z > mxz) mxz = z;
			});
			minX = mnx; minZ = mnz; maxX = mxx; maxZ = mxz;
			return true;
		}

		/// <summary>Grows the box to cover (x, z), keeping every explored bit. False past <see cref="MaxDimension"/>.</summary>
		private bool EnsureCovers(int x, int z)
		{
			if (width > 0 && x >= originX && z >= originZ && x < originX + width && z < originZ + height)
			{
				return true;
			}

			long newMinX, newMinZ, newMaxX, newMaxZ;
			if (width == 0)
			{
				newMinX = x; newMaxX = x; newMinZ = z; newMaxZ = z;
			}
			else
			{
				newMinX = Math.Min(originX, x); newMaxX = Math.Max(originX + width - 1, x);
				newMinZ = Math.Min(originZ, z); newMaxZ = Math.Max(originZ + height - 1, z);
			}
			if (newMaxX - newMinX + 1 > MaxDimension || newMaxZ - newMinZ + 1 > MaxDimension)
			{
				return false;
			}

			// Slack on the side that grew, within the cap.
			if (x < originX || width == 0) newMinX = Math.Max(newMinX - GrowthMargin, newMaxX - MaxDimension + 1);
			if (x >= originX + width || width == 0) newMaxX = Math.Min(newMaxX + GrowthMargin, newMinX + MaxDimension - 1);
			if (z < originZ || height == 0) newMinZ = Math.Max(newMinZ - GrowthMargin, newMaxZ - MaxDimension + 1);
			if (z >= originZ + height || height == 0) newMaxZ = Math.Min(newMaxZ + GrowthMargin, newMinZ + MaxDimension - 1);

			int newWidth = (int)(newMaxX - newMinX + 1), newHeight = (int)(newMaxZ - newMinZ + 1);
			ulong[] grown = new ulong[(newWidth * newHeight + 63) >> 6];
			for (int lz = 0; lz < height; ++lz)
			{
				for (int lx = 0; lx < width; ++lx)
				{
					int index = lz * width + lx;
					if ((bits[index >> 6] & (1UL << (index & 63))) != 0)
					{
						int target = (int)((originZ + lz - newMinZ) * newWidth + (originX + lx - newMinX));
						grown[target >> 6] |= 1UL << (target & 63);
					}
				}
			}
			bits = grown;
			originX = (int)newMinX;
			originZ = (int)newMinZ;
			width = newWidth;
			height = newHeight;
			return true;
		}
	}
}
