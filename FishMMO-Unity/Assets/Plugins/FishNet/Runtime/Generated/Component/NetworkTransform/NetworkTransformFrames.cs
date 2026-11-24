using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using GameKit.Dependencies.Utilities;

namespace FishNet.Component.Transforming
{
    /// <summary>
    /// FISHMMO EDIT: (upstream PR feat/networktransform-update-frames) the server's unreliable NetworkTransform updates
    /// for one tick, sent to each observer as one PacketId.TransformFrame message instead of one ObserversRpc each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An update sent as an RPC costs a 2 byte RPC link (or a packet id, object id, component index and RPC hash) and a
    /// payload length before its data, which is often only 4 or 5 bytes. In a frame, updates are sorted by object id and
    /// each is preceded only by the id's distance from the previous update's, with one bit saying the update is for the
    /// object's first NetworkTransform (nearly always: the component index is written only otherwise). No length is
    /// written: the client's NetworkTransform reads exactly its own update. The frame starts with its total length, so a client that cannot read an update (its
    /// object is not spawned there yet) skips the rest of that frame only.
    /// </para>
    /// <para>
    /// Who receives an update is decided exactly as for the RPC: the object's observers, minus the owner when it would
    /// discard the update, minus observers the object's ObserverSendFilter declines this tick. Frames are written every
    /// tick, after OnPostTick, so each one carries its own tick even when the transport sends several ticks together.
    /// Reliable updates (settles, teleports) are still sent as RPCs.
    /// </para>
    /// </remarks>
    internal static class NetworkTransformFrames
    {
        private struct Entry
        {
            public int ObjectId;
            public byte ComponentIndex;
            public int Offset;
            public int Length;
        }

        /// <summary>Component index standing for "the object's first NetworkTransform", which is written as a header bit.</summary>
        internal const byte FirstTransform = byte.MaxValue;

        /// <summary>Most a single frame message may hold, so a frame never needs splitting (which would make it reliable).</summary>
        private const int MaximumFrameBytes = 900;

        /// <summary>Largest update put in a frame. Larger ones (a nested transform's full state) are sent as RPCs.</summary>
        internal const int MaximumUpdateBytes = 128;

        /// <summary>This tick's update payloads, each stored once however many observers receive it.</summary>
        private static byte[] _payloads = new byte[4096];
        private static int _payloadLength;

        private static readonly Dictionary<NetworkConnection, List<Entry>> _entriesByConnection = new();
        private static readonly List<NetworkConnection> _connections = new();
        private static readonly Stack<List<Entry>> _entryListCache = new();
        private static readonly Comparison<Entry> _byObjectThenComponent = (a, b) =>
        {
            int c = a.ObjectId.CompareTo(b.ObjectId);
            return c != 0 ? c : a.ComponentIndex.CompareTo(b.ComponentIndex);
        };

        /// <summary>Stores one update's payload for this tick. Returns where it was stored.</summary>
        internal static int AddPayload(ArraySegment<byte> payload)
        {
            int needed = _payloadLength + payload.Count;
            if (needed > _payloads.Length)
                Array.Resize(ref _payloads, Math.Max(needed, _payloads.Length * 2));
            int offset = _payloadLength;
            Buffer.BlockCopy(payload.Array, payload.Offset, _payloads, offset, payload.Count);
            _payloadLength = needed;
            return offset;
        }

        /// <summary>Queues a stored payload for <paramref name="connection"/>'s frame this tick.</summary>
        internal static void Append(NetworkConnection connection, int objectId, byte componentIndex, int offset, int length)
        {
            if (!_entriesByConnection.TryGetValue(connection, out List<Entry> entries))
            {
                entries = _entryListCache.Count > 0 ? _entryListCache.Pop() : new();
                _entriesByConnection[connection] = entries;
                _connections.Add(connection);
            }
            entries.Add(new() { ObjectId = objectId, ComponentIndex = componentIndex, Offset = offset, Length = length });
        }

        /// <summary>Writes and sends every connection's frame for this tick. Called once per tick, after OnPostTick.</summary>
        internal static void Flush(TransportManager transportManager)
        {
            if (_connections.Count == 0)
            {
                _payloadLength = 0;
                return;
            }

            PooledWriter body = WriterPool.Retrieve();
            PooledWriter message = WriterPool.Retrieve();
            for (int c = 0; c < _connections.Count; c++)
            {
                NetworkConnection connection = _connections[c];
                List<Entry> entries = _entriesByConnection[connection];
                if (connection.IsActive)
                    WriteFrames(entries, body, message, transportManager, connection, null);
                entries.Clear();
                _entryListCache.Push(entries);
            }
            body.Store();
            message.Store();
            _entriesByConnection.Clear();
            _connections.Clear();
            _payloadLength = 0;
        }

        /// <summary>
        /// One connection's entries as frame messages: sorted by object, split before a frame would pass
        /// MaximumFrameBytes. Each message is sent to <paramref name="connection"/>, or added to <paramref name="testSink"/>.
        /// </summary>
        private static void WriteFrames(List<Entry> entries, PooledWriter body, PooledWriter message, TransportManager transportManager, NetworkConnection connection, List<byte[]> testSink)
        {
            entries.Sort(_byObjectThenComponent);
            body.Clear();
            int previousId = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                // The header is at most 6 bytes (a 5 byte id distance and a component index).
                if (body.Length > 0 && body.Length + e.Length + 6 > MaximumFrameBytes)
                {
                    Send(body, message, transportManager, connection, testSink);
                    body.Clear();
                    previousId = 0;
                }
                bool first = e.ComponentIndex == FirstTransform;
                body.WriteUnsignedPackedWhole(((ulong)(uint)(e.ObjectId - previousId) << 1) | (first ? 1UL : 0UL));
                if (!first)
                    body.WriteUInt8Unpacked(e.ComponentIndex);
                body.WriteUInt8Array(_payloads, e.Offset, e.Length);
                previousId = e.ObjectId;
            }
            if (body.Length > 0)
                Send(body, message, transportManager, connection, testSink);
        }

        private static void Send(PooledWriter body, PooledWriter message, TransportManager transportManager, NetworkConnection connection, List<byte[]> testSink)
        {
            message.Clear();
            message.WritePacketIdUnpacked(PacketId.TransformFrame);
            message.WriteInt32(body.Length);
            message.WriteArraySegment(body.GetArraySegment());
            if (testSink != null)
                testSink.Add(message.GetArraySegment().ToArray());
            else
                transportManager.SendToClient((byte)Channel.Unreliable, message.GetArraySegment(), connection);
        }

        /// <summary>For tests: the frame messages these updates would be sent as (each starts with its PacketId).</summary>
        internal static List<byte[]> EncodeForTests(IList<(int ObjectId, byte ComponentIndex, byte[] Payload)> updates)
        {
            List<Entry> entries = new();
            int start = _payloadLength;
            foreach ((int objectId, byte componentIndex, byte[] payload) in updates)
                entries.Add(new() { ObjectId = objectId, ComponentIndex = componentIndex, Offset = AddPayload(new(payload)), Length = payload.Length });
            List<byte[]> sink = new();
            PooledWriter body = WriterPool.Retrieve();
            PooledWriter message = WriterPool.Retrieve();
            WriteFrames(entries, body, message, null, null, sink);
            body.Store();
            message.Store();
            _payloadLength = start;
            return sink;
        }

        /// <summary>The first NetworkTransform among the object's NetworkBehaviours, or null.</summary>
        internal static NetworkTransform FirstTransformOf(NetworkObject nob)
        {
            for (int i = 0; i < nob.NetworkBehaviours.Count; i++)
            {
                if (nob.NetworkBehaviours[i] is NetworkTransform networkTransform)
                    return networkTransform;
            }
            return null;
        }

        /// <summary>Drops anything queued; for a server that stops between a post tick and the flush.</summary>
        internal static void Clear()
        {
            foreach (List<Entry> entries in _entriesByConnection.Values)
            {
                entries.Clear();
                _entryListCache.Push(entries);
            }
            _entriesByConnection.Clear();
            _connections.Clear();
            _payloadLength = 0;
        }

        /// <summary>Reads a frame on a client: each update is handed to its NetworkTransform, which reads exactly its own bytes.</summary>
        internal static void Parse(PooledReader reader, ClientObjects objects) => Parse(reader, objects, null);

        /// <summary>
        /// <see cref="Parse(PooledReader, ClientObjects)"/>, or with <paramref name="testRead"/> standing in for the
        /// NetworkTransforms: given an object id, component index and the rest of the frame, it returns how many bytes
        /// that update took, or 0 when the object is unknown.
        /// </summary>
        internal static void Parse(PooledReader reader, ClientObjects objects, Func<int, byte, ArraySegment<byte>, int> testRead)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.Remaining)
            {
                reader.Skip(reader.Remaining);
                return;
            }
            int end = reader.Position + length;
            int objectId = 0;
            while (reader.Position < end)
            {
                ulong header = reader.ReadUnsignedPackedWhole();
                objectId += (int)(uint)(header >> 1);
                byte componentIndex = (header & 1UL) != 0UL ? FirstTransform : reader.ReadUInt8Unpacked();
                if (reader.Position >= end)
                    break;

                ArraySegment<byte> rest = new(reader.GetBuffer(), reader.Position, end - reader.Position);
                int read;
                if (testRead != null)
                {
                    read = testRead(objectId, componentIndex, rest);
                }
                else
                {
                    NetworkTransform networkTransform = null;
                    if (objects.Spawned.TryGetValueIL2CPP(objectId, out NetworkObject nob) && nob != null)
                    {
                        if (componentIndex == FirstTransform)
                            networkTransform = FirstTransformOf(nob);
                        else if (componentIndex < nob.NetworkBehaviours.Count)
                            networkTransform = nob.NetworkBehaviours[componentIndex] as NetworkTransform;
                    }
                    // Not spawned here yet (its spawn is still in flight): the rest of this frame cannot be read.
                    read = networkTransform == null ? 0 : networkTransform.ReadFramedObserversUpdate(rest);
                }
                if (read <= 0 || read > end - reader.Position)
                    break;
                reader.Skip(read);
            }
            if (reader.Position < end)
                reader.Skip(end - reader.Position);
        }
    }
}
