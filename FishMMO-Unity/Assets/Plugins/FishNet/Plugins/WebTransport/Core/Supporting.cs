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
        public readonly int ConnectionId;
        public byte[] Data;
        public int Length;
        public readonly byte Channel;
        /// <summary>
        /// Guard against double-dispose. Dispose sets this to false
        /// after returning the buffer to the pool, preventing a
        /// second dispose from corrupting the ByteArrayPool.
        /// </summary>
        private bool _owned;

        public Packet(int sender, ArraySegment<byte> segment, byte channel)
        {
            Data = ByteArrayPool.Retrieve(segment.Count);
            Buffer.BlockCopy(segment.Array, segment.Offset, Data, 0, segment.Count);
            ConnectionId = sender;
            Length = segment.Count;
            Channel = channel;
            _owned = true;
        }

        public void Dispose()
        {
            if (Data != null && _owned)
            {
                _owned = false;
                ByteArrayPool.Store(Data);
                Data = null;
            }
        }
    }
}