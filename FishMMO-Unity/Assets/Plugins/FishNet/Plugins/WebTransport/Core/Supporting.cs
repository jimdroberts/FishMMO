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
		public int Length;
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
			Data = ByteArrayPool.Retrieve(segment.Count);
			Buffer.BlockCopy(segment.Array, segment.Offset, Data, 0, segment.Count);
			ConnectionId = sender;
			Length = segment.Count;
			Channel = channel;
			owned = true;
		}

		public void Dispose()
		{
			if (Data != null && owned)
			{
				owned = false;
				ByteArrayPool.Store(Data);
				Data = null;
			}
		}
	}
}