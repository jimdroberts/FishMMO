using FishNet.Utility.Performance;
using System;

namespace FishNet.Transporting.WebTransport
{
	/// <summary>
	/// Internal packet structure for the outgoing queue.
	/// Backed by ByteArrayPool to minimise GC allocations.
	/// </summary>
	internal struct Packet
	{
		/// <summary>
		/// The connection ID of the sender (server) or target (client).
		/// -1 on the server means broadcast to all clients.
		/// </summary>
		public readonly int ConnectionId;
		/// <summary>
		/// The packet data buffer, leased from <see cref="ByteArrayPool"/>.
		/// Public getter allows reading the buffer; private setter prevents
		/// external code from replacing the array reference (which could
		/// orphan the leased buffer in the pool).
		/// </summary>
		public byte[] Data { get; private set; }
		/// <summary>
		/// The number of valid bytes in <see cref="Data"/>.
		/// </summary>
		public int Length { get; private set; }
		/// <summary>
		/// The channel: 0 = reliable (stream), 1 = unreliable (datagram).
		/// </summary>
		public readonly byte Channel;
		/// <summary>
		/// Guard against double-dispose. Dispose sets this to false
		/// after returning the buffer to the pool, preventing a
		/// second dispose from corrupting the ByteArrayPool.
		/// </summary>
		private bool owned;

		public Packet(int sender, ArraySegment<byte> segment, byte channel)
		{
			this.Data = ByteArrayPool.Retrieve(segment.Count);
			Buffer.BlockCopy(segment.Array, segment.Offset, this.Data, 0, segment.Count);
			this.ConnectionId = sender;
			this.Length = segment.Count;
			this.Channel = channel;
			this.owned = true;
		}

		public void Dispose()
		{
			if (this.Data != null && this.owned)
			{
				this.owned = false;
				ByteArrayPool.Store(this.Data);
				this.Data = null;
			}
		}
	}
}