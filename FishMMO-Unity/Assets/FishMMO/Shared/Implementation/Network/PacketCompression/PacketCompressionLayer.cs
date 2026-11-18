using System;
using FishNet.Managing.Transporting;

namespace FishMMO.Shared
{
	/// <summary>
	/// The FishNet intermediate layer that applies <see cref="PacketCompression"/> to every packet both ways. Added by
	/// <see cref="PacketCompression.TryInstall"/>; never placed in a scene.
	/// </summary>
	public sealed class PacketCompressionLayer : IntermediateLayer
	{
		// One packet at a time on the main thread: the transport copies what it is handed before this is reused.
		private byte[] outgoing = new byte[2048];
		private byte[] incoming = new byte[2048];

		/// <inheritdoc/>
		public override ArraySegment<byte> HandleOutgoing(ArraySegment<byte> src, bool toServer)
		{
			HuffmanCode code = toServer ? PacketCompression.ToServer : PacketCompression.ToClients;
			EnsureCapacity(ref outgoing, src.Count + PacketCompression.MaximumGrowth);
			int written = PacketCompression.Compress(code, src.AsSpan(), outgoing);
			return new ArraySegment<byte>(outgoing, 0, written);
		}

		/// <inheritdoc/>
		public override ArraySegment<byte> HandleIncoming(ArraySegment<byte> src, bool fromServer)
		{
			HuffmanCode code = fromServer ? PacketCompression.ToClients : PacketCompression.ToServer;
			// A packet never expands by more than 8 bits per code bit (1-bit codes), and FishNet's own MTU bounds it.
			EnsureCapacity(ref incoming, Math.Max(src.Count * 8, 64));
			int length = PacketCompression.Decompress(code, src.AsSpan(), incoming);
			return length < 0 ? new ArraySegment<byte>(incoming, 0, 0) : new ArraySegment<byte>(incoming, 0, length);
		}

		private static void EnsureCapacity(ref byte[] buffer, int needed)
		{
			if (buffer.Length < needed)
			{
				buffer = new byte[Math.Max(needed, buffer.Length * 2)];
			}
		}
	}
}
