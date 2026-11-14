using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// A static, canonical Huffman code over bytes, for compressing whole network packets with a table both peers already
	/// hold (no table on the wire). Every byte value has a code, so any packet can be encoded; codes are at most
	/// <see cref="MaximumCodeLength"/> bits, so decoding is one table lookup per byte.
	/// </summary>
	/// <remarks>
	/// Bits are written least significant first (as DEFLATE does), with each code stored bit-reversed so the decoder can
	/// index its table with the next <see cref="MaximumCodeLength"/> bits directly. The code is defined entirely by the
	/// 256 code lengths (<see cref="Lengths"/>), which is what a trained table stores.
	/// </remarks>
	public sealed class HuffmanCode
	{
		/// <summary>Longest code, in bits. 15 keeps the decode table at 32,768 entries.</summary>
		public const int MaximumCodeLength = 15;

		private const int SymbolCount = 256;
		private const int DecodeTableSize = 1 << MaximumCodeLength;

		/// <summary>The code length of each byte value, 1..<see cref="MaximumCodeLength"/>.</summary>
		public byte[] Lengths { get; }

		/// <summary>Each byte value's code, bit-reversed for least-significant-first writing.</summary>
		private readonly ushort[] codes = new ushort[SymbolCount];

		/// <summary>Next 15 bits (least significant first) → symbol in the low 8 bits, code length in the high 8.</summary>
		private readonly ushort[] decodeTable = new ushort[DecodeTableSize];

		/// <summary>Builds the code from 256 code lengths, which must describe a complete prefix code.</summary>
		/// <exception cref="ArgumentException">The lengths are not 256 values of 1..15 forming a complete code.</exception>
		public HuffmanCode(byte[] lengths)
		{
			if (lengths == null || lengths.Length != SymbolCount)
			{
				throw new ArgumentException("A Huffman code needs exactly 256 code lengths.", nameof(lengths));
			}
			long kraft = 0;
			for (int i = 0; i < SymbolCount; ++i)
			{
				if (lengths[i] < 1 || lengths[i] > MaximumCodeLength)
				{
					throw new ArgumentException($"Code length {lengths[i]} for byte {i} is outside 1..{MaximumCodeLength}.", nameof(lengths));
				}
				kraft += 1L << (MaximumCodeLength - lengths[i]);
			}
			if (kraft != 1L << MaximumCodeLength)
			{
				throw new ArgumentException("The code lengths do not form a complete prefix code.", nameof(lengths));
			}
			Lengths = (byte[])lengths.Clone();
			BuildCanonicalCodes();
		}

		/// <summary>
		/// Code lengths for byte frequencies: an optimal Huffman code, flattened until no code is longer than
		/// <see cref="MaximumCodeLength"/>. Bytes never seen are counted once, so every byte stays encodable.
		/// </summary>
		public static byte[] BuildLengths(long[] frequencies)
		{
			if (frequencies == null || frequencies.Length != SymbolCount)
			{
				throw new ArgumentException("Frequencies must have 256 entries.", nameof(frequencies));
			}
			long[] weights = new long[SymbolCount];
			for (int i = 0; i < SymbolCount; ++i)
			{
				weights[i] = Math.Max(1L, frequencies[i]);
			}
			while (true)
			{
				byte[] lengths = HuffmanLengths(weights);
				int longest = 0;
				for (int i = 0; i < SymbolCount; ++i)
				{
					longest = Math.Max(longest, lengths[i]);
				}
				if (longest <= MaximumCodeLength)
				{
					return lengths;
				}
				// Too deep: flatten the distribution and rebuild. Rare bytes get slightly shorter codes, common ones keep theirs.
				for (int i = 0; i < SymbolCount; ++i)
				{
					weights[i] = (weights[i] + 1) / 2;
				}
			}
		}

		/// <summary>The bits <paramref name="data"/> would take under this code (no header).</summary>
		public long EncodedBits(ReadOnlySpan<byte> data)
		{
			long bits = 0;
			for (int i = 0; i < data.Length; ++i)
			{
				bits += Lengths[data[i]];
			}
			return bits;
		}

		/// <summary>The bits a byte histogram would take under this code.</summary>
		public long EncodedBits(long[] histogram)
		{
			long bits = 0;
			for (int i = 0; i < SymbolCount; ++i)
			{
				bits += histogram[i] * Lengths[i];
			}
			return bits;
		}

		/// <summary>
		/// Encodes <paramref name="source"/> into <paramref name="destination"/>. Returns the bytes written, or -1 when they
		/// would not fit (the caller then sends the packet uncompressed).
		/// </summary>
		public int Encode(ReadOnlySpan<byte> source, Span<byte> destination)
		{
			ulong bitBuffer = 0;
			int bitCount = 0;
			int written = 0;
			for (int i = 0; i < source.Length; ++i)
			{
				byte symbol = source[i];
				bitBuffer |= (ulong)codes[symbol] << bitCount;
				bitCount += Lengths[symbol];
				while (bitCount >= 8)
				{
					if (written >= destination.Length)
					{
						return -1;
					}
					destination[written++] = (byte)bitBuffer;
					bitBuffer >>= 8;
					bitCount -= 8;
				}
			}
			if (bitCount > 0)
			{
				if (written >= destination.Length)
				{
					return -1;
				}
				destination[written++] = (byte)bitBuffer;
			}
			return written;
		}

		/// <summary>
		/// Decodes exactly <paramref name="destination"/>.Length bytes from <paramref name="source"/>. False when the source
		/// runs out first (a damaged or foreign packet).
		/// </summary>
		public bool Decode(ReadOnlySpan<byte> source, Span<byte> destination)
		{
			ulong bitBuffer = 0;
			int bitCount = 0;
			int read = 0;
			for (int i = 0; i < destination.Length; ++i)
			{
				while (bitCount < MaximumCodeLength && read < source.Length)
				{
					bitBuffer |= (ulong)source[read++] << bitCount;
					bitCount += 8;
				}
				ushort entry = decodeTable[(int)(bitBuffer & (DecodeTableSize - 1))];
				int length = entry >> 8;
				if (length > bitCount)
				{
					return false;
				}
				destination[i] = (byte)entry;
				bitBuffer >>= length;
				bitCount -= length;
			}
			return true;
		}

		private void BuildCanonicalCodes()
		{
			int[] lengthCounts = new int[MaximumCodeLength + 1];
			for (int i = 0; i < SymbolCount; ++i)
			{
				lengthCounts[Lengths[i]]++;
			}
			int[] nextCode = new int[MaximumCodeLength + 2];
			int code = 0;
			for (int length = 1; length <= MaximumCodeLength; ++length)
			{
				code = (code + lengthCounts[length - 1]) << 1;
				nextCode[length] = code;
			}
			for (int symbol = 0; symbol < SymbolCount; ++symbol)
			{
				int length = Lengths[symbol];
				int canonical = nextCode[length]++;
				ushort reversed = (ushort)Reverse(canonical, length);
				codes[symbol] = reversed;
				// Every 15-bit window whose low `length` bits are this code decodes to this symbol.
				for (int fill = reversed; fill < DecodeTableSize; fill += 1 << length)
				{
					decodeTable[fill] = (ushort)((length << 8) | symbol);
				}
			}
		}

		private static int Reverse(int value, int bits)
		{
			int result = 0;
			for (int i = 0; i < bits; ++i)
			{
				result = (result << 1) | ((value >> i) & 1);
			}
			return result;
		}

		/// <summary>Plain Huffman code lengths for positive weights (deterministic: ties break on the lower symbol).</summary>
		private static byte[] HuffmanLengths(long[] weights)
		{
			// Nodes 0..255 are leaves; internal nodes follow. Parent links give each leaf's depth.
			int nodeCount = SymbolCount * 2 - 1;
			long[] nodeWeight = new long[nodeCount];
			int[] parent = new int[nodeCount];
			var queue = new SortedSet<(long Weight, int Node)>();
			for (int i = 0; i < SymbolCount; ++i)
			{
				nodeWeight[i] = weights[i];
				queue.Add((weights[i], i));
			}
			int next = SymbolCount;
			while (queue.Count > 1)
			{
				(long Weight, int Node) a = queue.Min;
				queue.Remove(a);
				(long Weight, int Node) b = queue.Min;
				queue.Remove(b);
				nodeWeight[next] = a.Weight + b.Weight;
				parent[a.Node] = next;
				parent[b.Node] = next;
				queue.Add((nodeWeight[next], next));
				next++;
			}
			int root = next - 1;
			byte[] lengths = new byte[SymbolCount];
			for (int i = 0; i < SymbolCount; ++i)
			{
				int depth = 0;
				for (int n = i; n != root; n = parent[n])
				{
					depth++;
				}
				lengths[i] = (byte)Math.Min(depth, byte.MaxValue);
			}
			return lengths;
		}
	}
}
