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

        public Packet(int connectionId, byte[] data, int length, byte channel)
        {
            ConnectionId = connectionId;
            Data = data;
            Length = length;
            Channel = channel;
        }

        public Packet(int sender, ArraySegment<byte> segment, byte channel)
        {
            Data = ByteArrayPool.Retrieve(segment.Count);
            Buffer.BlockCopy(segment.Array, segment.Offset, Data, 0, segment.Count);
            ConnectionId = sender;
            Length = segment.Count;
            Channel = channel;
        }

        public ArraySegment<byte> GetArraySegment()
        {
            return new ArraySegment<byte>(Data, 0, Length);
        }

        /// <summary>
        /// Adds additional length to the packet, resizing Data if needed.
        /// </summary>
        public void AddLength(int length)
        {
            int totalNeeded = Length + length;
            if (Data.Length < totalNeeded)
                Array.Resize(ref Data, totalNeeded);
            Length += length;
        }

        public void Dispose()
        {
            if (Data != null)
            {
                ByteArrayPool.Store(Data);
                Data = null;
            }
        }
    }
}
