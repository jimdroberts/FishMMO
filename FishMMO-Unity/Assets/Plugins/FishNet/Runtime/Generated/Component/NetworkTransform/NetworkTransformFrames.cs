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
    /// FISHMMO EDIT: (frame slots) object ids are shuffled across the whole id range, so the distance between two sorted
    /// ids is usually thousands and its header took about 2 bytes. Each observer instead gets a small number per object it
    /// sees (a slot, the lowest free one), sent once in the first NetworkTransform's spawn payload. A frame lists its
    /// slotted updates first, sorted by slot, each preceded only by the slot's distance from the previous one: about one
    /// byte. A slot freed when the object leaves the observer's view is not reused for <see cref="SlotQuarantineSeconds"/>,
    /// so an update still in flight for the old object finds no transform and is skipped, never applied to the new one.
    /// Updates without a slot (other NetworkTransforms on the object, or slots off) follow, encoded by object id as before.
    /// </para>
    /// <para>
    /// Who receives an update is decided exactly as for the RPC: the object's observers, minus the owner when it would
    /// discard the update, minus observers the object's ObserverSendFilter declines this tick. Frames are written every
    /// tick, after OnPostTick, so each one carries its own tick even when the transport sends several ticks together.
    /// Reliable updates (settles, teleports) are still sent as RPCs.
    /// </para>
    /// </remarks>
    public static class NetworkTransformFrames
    {
        private struct Entry
        {
            public int ObjectId;
            public byte ComponentIndex;
            public int Offset;
            public int Length;
            /// <summary>The observer's slot for the object, or -1 when the update is encoded by object id.</summary>
            public int Slot;
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
            // Slotted updates first, by slot; then the rest by object and component.
            bool aSlotted = a.Slot >= 0, bSlotted = b.Slot >= 0;
            if (aSlotted != bSlotted)
                return aSlotted ? -1 : 1;
            if (aSlotted)
                return a.Slot.CompareTo(b.Slot);
            int c = a.ObjectId.CompareTo(b.ObjectId);
            return c != 0 ? c : a.ComponentIndex.CompareTo(b.ComponentIndex);
        };

        #region FISHMMO EDIT: frame slots.
        /// <summary>True for the server to give each observer slots for the objects it sees (see the type remarks).</summary>
        public static bool UseSlots = true;

        /// <summary>How long a freed slot is kept from reuse, so no update still in flight for its old object finds a new one.</summary>
        public const double SlotQuarantineSeconds = 5d;

        /// <summary>One observer's slots, on the server.</summary>
        private sealed class ConnectionSlots
        {
            public readonly Dictionary<int, int> SlotByObject = new();
            public readonly SortedSet<int> Free = new();
            public readonly Queue<(int Slot, long ReleaseAt)> Quarantine = new();
            public int Next;
        }

        private static readonly Dictionary<NetworkConnection, ConnectionSlots> _slotsByConnection = new();
        private static readonly List<NetworkConnection> _deadSlotConnections = new();

        /// <summary>The client's transform for each slot it was given.</summary>
        private static readonly Dictionary<int, NetworkTransform> _clientSlots = new();

        /// <summary>
        /// The slot <paramref name="connection"/> knows <paramref name="objectId"/> by, assigning the lowest free one; -1 when
        /// slots are off. Called while writing the object's spawn for that connection.
        /// </summary>
        internal static int AssignSlot(NetworkConnection connection, int objectId)
        {
            if (!UseSlots || connection == null || !connection.IsValid)
                return -1;
            if (!_slotsByConnection.TryGetValue(connection, out ConnectionSlots slots))
            {
                PruneDeadSlotConnections();
                slots = new ConnectionSlots();
                _slotsByConnection[connection] = slots;
            }
            if (slots.SlotByObject.TryGetValue(objectId, out int existing))
                return existing;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            while (slots.Quarantine.Count > 0 && slots.Quarantine.Peek().ReleaseAt <= now)
                slots.Free.Add(slots.Quarantine.Dequeue().Slot);
            int slot;
            if (slots.Free.Count > 0)
            {
                slot = slots.Free.Min;
                slots.Free.Remove(slot);
            }
            else
            {
                slot = slots.Next++;
            }
            slots.SlotByObject[objectId] = slot;
            return slot;
        }

        /// <summary>Frees <paramref name="objectId"/>'s slot for <paramref name="connection"/>, for reuse after the quarantine.</summary>
        internal static void ReleaseSlot(NetworkConnection connection, int objectId)
        {
            if (connection == null || !_slotsByConnection.TryGetValue(connection, out ConnectionSlots slots))
                return;
            if (!slots.SlotByObject.Remove(objectId, out int slot))
                return;
            long releaseAt = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(SlotQuarantineSeconds * System.Diagnostics.Stopwatch.Frequency);
            slots.Quarantine.Enqueue((slot, releaseAt));
        }

        /// <summary>Frees every slot the object holds, for each connection in <paramref name="connections"/>.</summary>
        internal static void ReleaseSlots(IEnumerable<NetworkConnection> connections, int objectId)
        {
            if (_slotsByConnection.Count == 0)
                return;
            foreach (NetworkConnection connection in connections)
                ReleaseSlot(connection, objectId);
        }

        private static bool TryGetSlot(NetworkConnection connection, int objectId, out int slot)
        {
            slot = -1;
            return _slotsByConnection.Count > 0 && _slotsByConnection.TryGetValue(connection, out ConnectionSlots slots) &&
                slots.SlotByObject.TryGetValue(objectId, out slot);
        }

        /// <summary>Forgets connections that are gone.</summary>
        private static void PruneDeadSlotConnections()
        {
            _deadSlotConnections.Clear();
            foreach (NetworkConnection connection in _slotsByConnection.Keys)
            {
                if (connection == null || !connection.IsValid || !connection.IsActive)
                    _deadSlotConnections.Add(connection);
            }
            foreach (NetworkConnection connection in _deadSlotConnections)
                _slotsByConnection.Remove(connection);
            _deadSlotConnections.Clear();
        }

        /// <summary>On a client: <paramref name="networkTransform"/> is known by <paramref name="slot"/> from now on.</summary>
        internal static void BindClientSlot(int slot, NetworkTransform networkTransform) => _clientSlots[slot] = networkTransform;

        /// <summary>On a client: <paramref name="slot"/> no longer names <paramref name="networkTransform"/> (unless rebound since).</summary>
        internal static void UnbindClientSlot(int slot, NetworkTransform networkTransform)
        {
            if (_clientSlots.TryGetValue(slot, out NetworkTransform bound) && bound == networkTransform)
                _clientSlots.Remove(slot);
        }

        /// <summary>Drops every slot, both sides. For a server or client that stops.</summary>
        internal static void ClearSlots()
        {
            _slotsByConnection.Clear();
            _clientSlots.Clear();
        }
        #endregion

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

        #region FISHMMO EDIT: diagnostics, not proposed upstream.
        /// <summary>Updates queued (each counted once however many observers receive it), and how many changed only the rotation.</summary>
        public static long UpdatesWritten, RotationOnlyUpdates;
        /// <summary>Per-observer copies queued, their payload bytes, and the frame bytes written for them (headers included).</summary>
        public static long UpdatesQueued, PayloadBytesQueued, FrameBytesWritten, FramesWritten;

        /// <summary>Counts one update before it is queued for its observers. Its first byte is the update's flags.</summary>
        internal static void CountUpdate(ArraySegment<byte> payload)
        {
            UpdatesWritten++;
            // Rotation (64) set, no position flag (the low six bits), no extended flags (128).
            if (payload.Count > 0 && (payload.Array[payload.Offset] & 0x40) != 0 && (payload.Array[payload.Offset] & 0xBF) == 0)
                RotationOnlyUpdates++;
        }
        #endregion

        /// <summary>Queues a stored payload for <paramref name="connection"/>'s frame this tick.</summary>
        internal static void Append(NetworkConnection connection, int objectId, byte componentIndex, int offset, int length)
        {
            UpdatesQueued++;
            PayloadBytesQueued += length;
            if (!_entriesByConnection.TryGetValue(connection, out List<Entry> entries))
            {
                entries = _entryListCache.Count > 0 ? _entryListCache.Pop() : new();
                _entriesByConnection[connection] = entries;
                _connections.Add(connection);
            }
            int slot = -1;
            if (componentIndex == FirstTransform)
                TryGetSlot(connection, objectId, out slot);
            entries.Add(new() { ObjectId = objectId, ComponentIndex = componentIndex, Offset = offset, Length = length, Slot = slot });
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
            if (_slotsByConnection.Count > 0 && ++_flushesSincePrune >= 300)
            {
                _flushesSincePrune = 0;
                PruneDeadSlotConnections();
            }
        }

        private static int _flushesSincePrune;

        /// <summary>
        /// One connection's entries as frame messages: slotted updates by slot, then the rest by object, split before a frame
        /// would pass MaximumFrameBytes. Each message is sent to <paramref name="connection"/>, or added to <paramref name="testSink"/>.
        /// </summary>
        /// <remarks>
        /// A frame's body is the count of its slotted updates, then each slotted update as its slot's distance from the
        /// previous slot and its payload, then each other update as before: the object id's distance from the previous one
        /// with the first-transform bit, the component index unless that bit is set, and its payload.
        /// </remarks>
        private static void WriteFrames(List<Entry> entries, PooledWriter body, PooledWriter message, TransportManager transportManager, NetworkConnection connection, List<byte[]> testSink)
        {
            entries.Sort(_byObjectThenComponent);
            body.Clear();
            int previousId = 0;
            int previousSlot = 0;
            int slotted = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                // The header is at most 6 bytes (a 5 byte distance and a component index); the slotted count at most 5.
                if (body.Length > 0 && body.Length + e.Length + 6 + 5 > MaximumFrameBytes)
                {
                    Send(body, slotted, message, transportManager, connection, testSink);
                    body.Clear();
                    previousId = 0;
                    previousSlot = 0;
                    slotted = 0;
                }
                if (e.Slot >= 0)
                {
                    body.WriteUnsignedPackedWhole((ulong)(uint)(e.Slot - previousSlot));
                    previousSlot = e.Slot;
                    slotted++;
                }
                else
                {
                    bool first = e.ComponentIndex == FirstTransform;
                    body.WriteUnsignedPackedWhole(((ulong)(uint)(e.ObjectId - previousId) << 1) | (first ? 1UL : 0UL));
                    if (!first)
                        body.WriteUInt8Unpacked(e.ComponentIndex);
                    previousId = e.ObjectId;
                }
                body.WriteUInt8Array(_payloads, e.Offset, e.Length);
            }
            if (body.Length > 0)
                Send(body, slotted, message, transportManager, connection, testSink);
        }

        private static void Send(PooledWriter body, int slotted, PooledWriter message, TransportManager transportManager, NetworkConnection connection, List<byte[]> testSink)
        {
            message.Clear();
            message.WritePacketIdUnpacked(PacketId.TransformFrame);
            int countLength = PackedLength((ulong)slotted);
            message.WriteInt32(body.Length + countLength);
            message.WriteUnsignedPackedWhole((ulong)slotted);
            message.WriteArraySegment(body.GetArraySegment());
            FramesWritten++;
            FrameBytesWritten += message.Length;
            if (testSink != null)
                testSink.Add(message.GetArraySegment().ToArray());
            else
                transportManager.SendToClient((byte)Channel.Unreliable, message.GetArraySegment(), connection);
        }

        /// <summary>Bytes WriteUnsignedPackedWhole takes for <paramref name="value"/>: seven bits a byte.</summary>
        private static int PackedLength(ulong value)
        {
            int length = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                length++;
            }
            return length;
        }

        /// <summary>For tests: the frame messages these updates would be sent as (each starts with its PacketId).</summary>
        internal static List<byte[]> EncodeForTests(IList<(int ObjectId, byte ComponentIndex, byte[] Payload)> updates)
        {
            var slotted = new List<(int, byte, int, byte[])>(updates.Count);
            foreach ((int objectId, byte componentIndex, byte[] payload) in updates)
                slotted.Add((objectId, componentIndex, -1, payload));
            return EncodeForTests(slotted);
        }

        /// <summary>For tests: as above, with each update's slot (-1 for none).</summary>
        internal static List<byte[]> EncodeForTests(IList<(int ObjectId, byte ComponentIndex, int Slot, byte[] Payload)> updates)
        {
            List<Entry> entries = new();
            int start = _payloadLength;
            foreach ((int objectId, byte componentIndex, int slot, byte[] payload) in updates)
                entries.Add(new() { ObjectId = objectId, ComponentIndex = componentIndex, Offset = AddPayload(new(payload)), Length = payload.Length, Slot = slot });
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
        internal static void Parse(PooledReader reader, ClientObjects objects) => Parse(reader, objects, null, null);

        /// <summary>For tests: <see cref="Parse(PooledReader, ClientObjects, Func{int, byte, ArraySegment{byte}, int}, Func{int, int})"/> without slots.</summary>
        internal static void Parse(PooledReader reader, ClientObjects objects, Func<int, byte, ArraySegment<byte>, int> testRead) => Parse(reader, objects, testRead, null);

        /// <summary>
        /// <see cref="Parse(PooledReader, ClientObjects)"/>, or with <paramref name="testRead"/> standing in for the
        /// NetworkTransforms: given an object id, component index and the rest of the frame, it returns how many bytes
        /// that update took, or 0 when the object is unknown. <paramref name="testSlotObject"/> names the object id a slot
        /// stands for in a test (or -1 when unknown).
        /// </summary>
        internal static void Parse(PooledReader reader, ClientObjects objects, Func<int, byte, ArraySegment<byte>, int> testRead, Func<int, int> testSlotObject)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.Remaining)
            {
                reader.Skip(reader.Remaining);
                return;
            }
            int end = reader.Position + length;
            int slotted = length > 0 ? (int)reader.ReadUnsignedPackedWhole() : 0;
            int slot = 0;
            int objectId = 0;
            for (int index = 0; reader.Position < end; index++)
            {
                byte componentIndex;
                bool bySlot = index < slotted;
                if (bySlot)
                {
                    slot += (int)(uint)reader.ReadUnsignedPackedWhole();
                    componentIndex = FirstTransform;
                }
                else
                {
                    ulong header = reader.ReadUnsignedPackedWhole();
                    objectId += (int)(uint)(header >> 1);
                    componentIndex = (header & 1UL) != 0UL ? FirstTransform : reader.ReadUInt8Unpacked();
                }
                if (reader.Position >= end)
                    break;

                ArraySegment<byte> rest = new(reader.GetBuffer(), reader.Position, end - reader.Position);
                int read;
                if (testRead != null)
                {
                    int id = bySlot ? (testSlotObject != null ? testSlotObject(slot) : -1) : objectId;
                    read = id < 0 ? 0 : testRead(id, componentIndex, rest);
                }
                else
                {
                    NetworkTransform networkTransform = null;
                    if (bySlot)
                    {
                        _clientSlots.TryGetValue(slot, out networkTransform);
                    }
                    else if (objects.Spawned.TryGetValueIL2CPP(objectId, out NetworkObject nob) && nob != null)
                    {
                        if (componentIndex == FirstTransform)
                            networkTransform = FirstTransformOf(nob);
                        else if (componentIndex < nob.NetworkBehaviours.Count)
                            networkTransform = nob.NetworkBehaviours[componentIndex] as NetworkTransform;
                    }
                    // Not spawned here yet (its spawn is still in flight), or its slot freed: the rest of this frame cannot be read.
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
