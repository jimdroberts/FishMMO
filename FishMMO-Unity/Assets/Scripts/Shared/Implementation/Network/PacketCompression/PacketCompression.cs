using System;
using FishNet.Managing;
using FishNet.Managing.Transporting;

namespace FishMMO.Shared
{
	/// <summary>
	/// Whole-packet compression between the game client and every server, with static Huffman codes trained on real game
	/// traffic (<see cref="PacketCompressionTables"/>). Lossless: the receiver gets exactly the bytes FishNet wrote.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a static code.</b> Game packets are small (tens to a few hundred bytes): a general compressor has no history to
	/// work with inside one, and a shared adaptive model cannot survive unreliable, unordered delivery. A fixed code both
	/// peers already hold needs neither: each packet is compressed on its own, with nothing on the wire but its bits.
	/// </para>
	/// <para>
	/// <b>Wire format.</b> One header byte: <see cref="RawPacket"/> followed by the packet unchanged, or
	/// <see cref="HuffmanPacket"/> followed by the packet's length (a 7-bit varint) and its code bits. The sender takes
	/// whichever is shorter, so a packet never grows by more than <see cref="MaximumGrowth"/>.
	/// </para>
	/// <para>
	/// <b>Switched on by the tables.</b> Installed only when both trained tables exist, and then on every peer of the same
	/// build (client, login, world and scene servers), so the two ends always agree. Until a table has been trained from a
	/// sample (the simulate report's compression lines), nothing is installed and the wire is unchanged.
	/// </para>
	/// </remarks>
	public static class PacketCompression
	{
		/// <summary>Header: the packet follows unchanged.</summary>
		public const byte RawPacket = 0;
		/// <summary>Header: the packet's length and its Huffman bits follow.</summary>
		public const byte HuffmanPacket = 1;
		/// <summary>The most a packet can grow by: the header byte of a raw packet.</summary>
		public const int MaximumGrowth = 1;

		private static HuffmanCode toClients;
		private static HuffmanCode toServer;
		private static bool resolved;

		/// <summary>True when this build has trained tables, so compression is installed on every connection.</summary>
		public static bool Available
		{
			get
			{
				Resolve();
				return toClients != null && toServer != null;
			}
		}

		/// <summary>The code for packets from a server to its clients, or null when untrained.</summary>
		public static HuffmanCode ToClients
		{
			get
			{
				Resolve();
				return toClients;
			}
		}

		/// <summary>The code for packets from a client to a server, or null when untrained.</summary>
		public static HuffmanCode ToServer
		{
			get
			{
				Resolve();
				return toServer;
			}
		}

		/// <summary>
		/// Installs the compression layer on a network manager that has not started yet. Does nothing (and returns false)
		/// when the build has no trained tables; true when installed now or already.
		/// </summary>
		public static bool TryInstall(NetworkManager networkManager)
		{
			if (networkManager == null || networkManager.TransportManager == null || !Available)
			{
				return false;
			}
			if (networkManager.TransportManager.HasIntermediateLayer)
			{
				return networkManager.GetComponent<PacketCompressionLayer>() != null;
			}
			PacketCompressionLayer layer = networkManager.gameObject.AddComponent<PacketCompressionLayer>();
			if (!networkManager.TransportManager.SetIntermediateLayer(layer, MaximumGrowth))
			{
				UnityEngine.Object.Destroy(layer);
				return false;
			}
			return true;
		}

		/// <summary>
		/// Compresses <paramref name="source"/> into <paramref name="destination"/> (header included), choosing raw when the
		/// code would not be shorter. Returns the bytes written; <paramref name="destination"/> must hold source + growth.
		/// </summary>
		public static int Compress(HuffmanCode code, ReadOnlySpan<byte> source, Span<byte> destination)
		{
			int lengthBytes = VarintLength(source.Length);
			// Worth trying only when the bits could beat the raw form, header and length included.
			if (code != null)
			{
				long bits = code.EncodedBits(source);
				int huffmanTotal = 1 + lengthBytes + (int)((bits + 7) / 8);
				if (huffmanTotal < 1 + source.Length && huffmanTotal <= destination.Length)
				{
					destination[0] = HuffmanPacket;
					int offset = 1 + WriteVarint(destination.Slice(1), source.Length);
					int written = code.Encode(source, destination.Slice(offset));
					if (written >= 0)
					{
						return offset + written;
					}
				}
			}
			destination[0] = RawPacket;
			source.CopyTo(destination.Slice(1));
			return 1 + source.Length;
		}

		/// <summary>
		/// Restores a packet written by <see cref="Compress"/>. Returns its length in <paramref name="destination"/>, or -1
		/// for a damaged or unknown packet (dropped by the caller, as a corrupt datagram would be).
		/// </summary>
		public static int Decompress(HuffmanCode code, ReadOnlySpan<byte> source, Span<byte> destination)
		{
			if (source.Length < 1)
			{
				return -1;
			}
			byte header = source[0];
			if (header == RawPacket)
			{
				int rawLength = source.Length - 1;
				if (rawLength > destination.Length)
				{
					return -1;
				}
				source.Slice(1).CopyTo(destination);
				return rawLength;
			}
			if (header != HuffmanPacket || code == null)
			{
				return -1;
			}
			int read = ReadVarint(source.Slice(1), out int length);
			if (read <= 0 || length < 0 || length > destination.Length)
			{
				return -1;
			}
			return code.Decode(source.Slice(1 + read), destination.Slice(0, length)) ? length : -1;
		}

		/// <summary>Bytes a 7-bit varint of <paramref name="value"/> takes.</summary>
		public static int VarintLength(int value)
		{
			int length = 1;
			uint v = (uint)value;
			while (v >= 0x80)
			{
				v >>= 7;
				length++;
			}
			return length;
		}

		private static int WriteVarint(Span<byte> destination, int value)
		{
			uint v = (uint)value;
			int i = 0;
			while (v >= 0x80)
			{
				destination[i++] = (byte)(v | 0x80);
				v >>= 7;
			}
			destination[i++] = (byte)v;
			return i;
		}

		private static int ReadVarint(ReadOnlySpan<byte> source, out int value)
		{
			uint result = 0;
			for (int i = 0; i < 4 && i < source.Length; ++i)
			{
				byte b = source[i];
				result |= (uint)(b & 0x7F) << (7 * i);
				if ((b & 0x80) == 0)
				{
					value = (int)result;
					return i + 1;
				}
			}
			value = 0;
			return -1;
		}

		private static void Resolve()
		{
			if (resolved)
			{
				return;
			}
			resolved = true;
			toClients = PacketCompressionTables.ToClientsLengths != null ? new HuffmanCode(PacketCompressionTables.ToClientsLengths) : null;
			toServer = PacketCompressionTables.ToServerLengths != null ? new HuffmanCode(PacketCompressionTables.ToServerLengths) : null;
		}
	}
}
