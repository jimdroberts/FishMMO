using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// The stored and sent form of a <see cref="FogChunkSet"/>: the tight bounding box of the explored chunks as alternating
	/// run lengths (issue #241).
	/// </summary>
	/// <remarks>
	/// <para><b>Layout (version 1).</b> <c>'F' 'G' 0x01</c>, the chunk size (float, little-endian), then varints: min x and
	/// min z (zig-zag), width, height, explored count, and the runs — unexplored first, alternating, row by row over
	/// width × height. An empty set is the header with width and height 0.</para>
	/// <para><b>Why runs.</b> Exploration comes in connected patches, so a row is a handful of runs however wide it is. A 4 km
	/// scene is 10,000 chunks (1.25 KB as raw bits) and typically encodes to a few hundred bytes.</para>
	/// <para>Plain managed code (no zlib), so a browser build decodes it the same as every other platform.</para>
	/// </remarks>
	public static class FogCodec
	{
		private const byte Magic0 = (byte)'F', Magic1 = (byte)'G', Version = 1;

		/// <summary>Most cells a decoded box may span (<see cref="FogChunkSet.MaxDimension"/> squared).</summary>
		private const long MaxCells = (long)FogChunkSet.MaxDimension * FogChunkSet.MaxDimension;

		/// <summary>Encodes <paramref name="set"/>.</summary>
		public static byte[] Encode(FogChunkSet set)
		{
			if (set == null)
			{
				throw new ArgumentNullException(nameof(set));
			}

			var output = new List<byte>(64) { Magic0, Magic1, Version };
			byte[] size = BitConverter.GetBytes(set.ChunkSize);
			if (!BitConverter.IsLittleEndian)
			{
				Array.Reverse(size);
			}
			output.AddRange(size);

			if (!set.TryGetBounds(out int minX, out int minZ, out int maxX, out int maxZ))
			{
				WriteVarint(output, 0); WriteVarint(output, 0); WriteVarint(output, 0); WriteVarint(output, 0); WriteVarint(output, 0);
				return output.ToArray();
			}

			int width = maxX - minX + 1, height = maxZ - minZ + 1;
			WriteVarint(output, ZigZag(minX));
			WriteVarint(output, ZigZag(minZ));
			WriteVarint(output, (ulong)width);
			WriteVarint(output, (ulong)height);
			WriteVarint(output, (ulong)set.Count);

			bool current = false;
			ulong run = 0;
			for (int z = minZ; z <= maxZ; ++z)
			{
				for (int x = minX; x <= maxX; ++x)
				{
					bool explored = set.Contains(x, z);
					if (explored != current)
					{
						WriteVarint(output, run);
						current = explored;
						run = 0;
					}
					++run;
				}
			}
			WriteVarint(output, run);
			return output.ToArray();
		}

		/// <summary>Decodes what <see cref="Encode"/> wrote. False (and null) for anything malformed.</summary>
		public static bool TryDecode(byte[] data, out FogChunkSet set)
		{
			set = null;
			if (data == null || data.Length < 7 || data[0] != Magic0 || data[1] != Magic1 || data[2] != Version)
			{
				return false;
			}

			byte[] sizeBytes = new byte[4];
			Array.Copy(data, 3, sizeBytes, 0, 4);
			if (!BitConverter.IsLittleEndian)
			{
				Array.Reverse(sizeBytes);
			}
			float chunkSize = BitConverter.ToSingle(sizeBytes, 0);
			if (float.IsNaN(chunkSize) || float.IsInfinity(chunkSize) || chunkSize < 1.0f)
			{
				return false;
			}

			int offset = 7;
			if (!TryReadVarint(data, ref offset, out ulong zx) || !TryReadVarint(data, ref offset, out ulong zz) ||
				!TryReadVarint(data, ref offset, out ulong width) || !TryReadVarint(data, ref offset, out ulong height) ||
				!TryReadVarint(data, ref offset, out ulong count))
			{
				return false;
			}

			FogChunkSet decoded = new FogChunkSet(chunkSize);
			if (width == 0 || height == 0)
			{
				set = decoded;
				return count == 0;
			}
			if (width > FogChunkSet.MaxDimension || height > FogChunkSet.MaxDimension || (long)(width * height) > MaxCells)
			{
				return false;
			}

			int minX = UnZigZag(zx), minZ = UnZigZag(zz);
			long cells = (long)(width * height), position = 0;
			bool explored = false;
			while (position < cells)
			{
				if (!TryReadVarint(data, ref offset, out ulong run) || run > (ulong)(cells - position))
				{
					return false;
				}
				if (explored)
				{
					for (long i = position; i < position + (long)run; ++i)
					{
						decoded.Add(minX + (int)(i % (long)width), minZ + (int)(i / (long)width));
					}
				}
				position += (long)run;
				explored = !explored;
			}

			if (offset != data.Length || (ulong)decoded.Count != count)
			{
				return false;
			}
			set = decoded;
			return true;
		}

		private static ulong ZigZag(int value) => (ulong)(uint)((value << 1) ^ (value >> 31));

		private static int UnZigZag(ulong value) => (int)((uint)(value >> 1) ^ (uint)-(long)(value & 1));

		private static void WriteVarint(List<byte> output, ulong value)
		{
			while (value >= 0x80)
			{
				output.Add((byte)(value | 0x80));
				value >>= 7;
			}
			output.Add((byte)value);
		}

		private static bool TryReadVarint(byte[] data, ref int offset, out ulong value)
		{
			value = 0;
			for (int shift = 0; shift < 64; shift += 7)
			{
				if (offset >= data.Length)
				{
					return false;
				}
				byte b = data[offset++];
				value |= (ulong)(b & 0x7F) << shift;
				if ((b & 0x80) == 0)
				{
					return true;
				}
			}
			return false;
		}
	}
}
