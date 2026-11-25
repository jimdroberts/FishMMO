using System;
using FishNet.Managing;
using FishNet.Serializing;

namespace FishNet.Object.Prediction
{
    /// <summary>
    /// FISHMMO EDIT: (upstream PR feat/acknowledged-delta-reconciles, on feat/delta-prediction-beta) what the server and a
    /// reconciling client keep so delta reconciles are written against a reconcile the client is known to hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every reconcile written gets a 7-bit id. A full reconcile's header is its id with the full flag. A delta's header is
    /// its id and one more byte: how many writes back its baseline is. Both sides keep what they decoded for each id.
    /// </para>
    /// <para>
    /// The owner acknowledges the newest reconcile it decoded with one byte on the replicates it already sends. While
    /// acknowledgements arrive, the server writes each reconcile as a delta against the newest acknowledged one, and a lost
    /// packet only makes the next delta a little larger. A full reconcile is still written every refresh interval (ten
    /// seconds in NetworkBehaviour), so a reader whose state went wrong for any reason recovers. Without acknowledgements
    /// (state forwarding, where several clients read one message, or an owner not sending replicates) it writes deltas
    /// against its last full reconcile and a new full reconcile at the full interval, which is how delta prediction worked
    /// before.
    /// </para>
    /// <para>
    /// Ids wrap every 128 writes. At most one reconcile is written per tick, so an id written 128 writes ago is at least
    /// 128 ticks old: a baseline is never more than <see cref="MaximumBaselineWrites"/> writes or
    /// <see cref="MaximumBaselineTicks"/> ticks behind, and a reader accepts it only if it holds that id from within that
    /// many ticks. An id from an earlier cycle can never be taken for a recent one.
    /// </para>
    /// </remarks>
    public static class ReconcileDeltas
    {
        /// <summary>Number of reconcile ids; ids wrap after this many writes.</summary>
        public const int IdCount = 128;

        /// <summary>Most writes a baseline may be behind the reconcile written against it.</summary>
        public const int MaximumBaselineWrites = 64;

        /// <summary>Most ticks a baseline may be behind the reconcile written against it, or an acknowledgement behind the server.</summary>
        public const uint MaximumBaselineTicks = 120;

        /// <summary>Set in an acknowledgement byte when it acknowledges an id (the low 7 bits).</summary>
        public const byte AcknowledgementFlag = 1 << 7;

        /// <summary>Writes after <paramref name="id"/> up to and including <paramref name="newestId"/>; 0 when they are the same.</summary>
        internal static int WritesSince(int id, int newestId) => (newestId - id) & (IdCount - 1);
    }

    /// <summary>FISHMMO EDIT: takes an owner's reconcile acknowledgements on the server.</summary>
    public interface IReconcileAcknowledgementTarget
    {
        /// <summary>The owner holds the reconcile written with <paramref name="id"/>.</summary>
        void Acknowledge(byte id);
    }

    /// <summary>FISHMMO EDIT: gives the acknowledgement a reconciling client sends with its replicates.</summary>
    public interface IReconcileAcknowledgementSource
    {
        /// <summary>
        /// The byte to send: <see cref="ReconcileDeltas.AcknowledgementFlag"/> and the newest id decoded, or 0 when there is
        /// none recent enough. <paramref name="latestServerTick"/> is the newest server tick this client has heard.
        /// </summary>
        byte GetAcknowledgement(uint latestServerTick);
    }

    /// <summary>
    /// FISHMMO EDIT: the server's side of <see cref="ReconcileDeltas"/> for one reconcile stream.
    /// </summary>
    public sealed class ReconcileDeltaWriter<T> : IReconcileAcknowledgementTarget
    {
        private readonly T[] _decoded = new T[ReconcileDeltas.IdCount];
        private readonly uint[] _ticks = new uint[ReconcileDeltas.IdCount];
        private readonly bool[] _held = new bool[ReconcileDeltas.IdCount];
        /// <summary>Id the next reconcile is written with.</summary>
        private int _nextId;
        private int _lastFullId = -1;
        private uint _lastFullTick;
        private int _acknowledgedId = -1;

        /// <summary>The last reconcile written, as readers decode it.</summary>
        public T LastWritten { get; private set; }

        /// <summary>True if the last reconcile was written in full.</summary>
        public bool LastWasFull { get; private set; }

        /// <summary>Writes back the last delta's baseline was; 0 for a full reconcile.</summary>
        public int LastBaselineDistance { get; private set; }

        /// <summary>The newest acknowledged id still usable as a baseline, or -1.</summary>
        public int AcknowledgedId => IsUsableBaseline(_acknowledgedId, _latestTick) ? _acknowledgedId : -1;

        /// <summary>Id of the last full reconcile written, or -1.</summary>
        public int LastFullId => _lastFullId;

        /// <summary>What was written with <paramref name="id"/>, as readers decode it, while it is still held.</summary>
        public bool TryGetWritten(int id, out T value)
        {
            bool held = id >= 0 && id < ReconcileDeltas.IdCount && _held[id];
            value = held ? _decoded[id] : default;
            return held;
        }

        private uint _latestTick;

        /// <inheritdoc />
        public void Acknowledge(byte id)
        {
            int value = id & (ReconcileDeltas.IdCount - 1);
            if (!IsUsableBaseline(value, _latestTick))
                return;
            // Keep the newer of the two; acknowledgements can arrive out of order.
            if (IsUsableBaseline(_acknowledgedId, _latestTick) && WritesBehind(_acknowledgedId) <= WritesBehind(value))
                return;
            _acknowledgedId = value;
        }

        /// <summary>
        /// Writes <paramref name="value"/> for <paramref name="tick"/>. With <paramref name="useAcknowledgements"/> and a usable
        /// acknowledgement: a delta against the newest acknowledged reconcile, or in full once <paramref name="refreshInterval"/>
        /// ticks have passed since the last full reconcile. Otherwise: a delta against the last full reconcile while it is
        /// younger than <paramref name="fullInterval"/> ticks, or in full. <paramref name="forceFull"/> always writes in full.
        /// </summary>
        public void Write(Writer writer, T value, uint tick, bool useAcknowledgements, uint fullInterval, uint refreshInterval, bool forceFull, NetworkManager networkManager = null)
        {
            _latestTick = tick;
            if (!Writer.HasDeltaSerializers<T>())
            {
                writer.WriteReconcile(value);
                LastWritten = value;
                LastWasFull = true;
                LastBaselineDistance = 0;
                return;
            }

            int baseline = -1;
            if (!forceFull)
            {
                /* Acknowledged: what the reader is known to hold, always preferred, refreshed in full now and then. A lost
                 * refresh then costs nothing: deltas carry on against the acknowledged reconcile until a newer one is. */
                if (useAcknowledgements && IsUsableBaseline(_acknowledgedId, tick))
                {
                    if (_lastFullId >= 0 && tick - _lastFullTick < refreshInterval)
                        baseline = _acknowledgedId;
                }
                // Not acknowledged: the last full reconcile, if the reader can be expected to hold it (it was not lost).
                else if (_lastFullId >= 0 && tick - _lastFullTick < fullInterval && IsUsableBaseline(_lastFullId, tick))
                {
                    baseline = _lastFullId;
                }
            }

            int id = _nextId;
            byte distance = baseline < 0 ? (byte)0 : (byte)WritesBehind(baseline);
            T baselineValue = baseline < 0 ? default : _decoded[baseline];

            int start = writer.Position;
            writer.WriteDeltaReconcile(baselineValue, value, (byte)id, distance);

            /* Readers apply later deltas to what they decoded, which differs from value if the serializer is lossy. Decode
             * what was written and keep that. */
            ArraySegment<byte> written = writer.GetArraySegment();
            written = new(written.Array, written.Offset + start, written.Count - start);
            PooledReader reader = ReaderPool.Retrieve(written, networkManager, Reader.DataSource.Server);
            reader.ReadDeltaReconcileHeader(out bool isFull, out _, out _);
            T decoded = reader.ReadDeltaReconcileBody(baselineValue, isFull);
            ReaderPool.Store(reader);

            _decoded[id] = decoded;
            _ticks[id] = tick;
            _held[id] = true;
            if (distance == 0)
            {
                _lastFullId = id;
                _lastFullTick = tick;
            }
            _nextId = (id + 1) & (ReconcileDeltas.IdCount - 1);
            // An acknowledgement of the id just reused names an older write; it no longer names anything held.
            if (_acknowledgedId == _nextId)
                _acknowledgedId = -1;

            LastWritten = decoded;
            LastWasFull = distance == 0;
            LastBaselineDistance = distance;
        }

        /// <summary>Forgets everything written and acknowledged; the next reconcile is written in full.</summary>
        public void Reset()
        {
            Array.Clear(_held, 0, _held.Length);
            Array.Clear(_decoded, 0, _decoded.Length);
            _lastFullId = -1;
            _acknowledgedId = -1;
            LastWritten = default;
        }

        /// <summary>Writes since <paramref name="id"/>: 1 for the last reconcile written.</summary>
        private int WritesBehind(int id) => ReconcileDeltas.WritesSince(id, _nextId - 1) + 1;

        private bool IsUsableBaseline(int id, uint tick)
        {
            if (id < 0 || !_held[id])
                return false;
            if (WritesBehind(id) > ReconcileDeltas.MaximumBaselineWrites)
                return false;
            return tick - _ticks[id] <= ReconcileDeltas.MaximumBaselineTicks;
        }
    }

    /// <summary>
    /// FISHMMO EDIT: a reconciling client's side of <see cref="ReconcileDeltas"/> for one reconcile stream.
    /// </summary>
    public sealed class ReconcileDeltaReader<T> : IReconcileAcknowledgementSource
    {
        private readonly T[] _decoded = new T[ReconcileDeltas.IdCount];
        private readonly uint[] _serverTicks = new uint[ReconcileDeltas.IdCount];
        private readonly bool[] _held = new bool[ReconcileDeltas.IdCount];
        private int _newestId = -1;
        private uint _newestServerTick;

        /// <summary>Id of the newest reconcile decoded, or -1.</summary>
        public int NewestId => _newestId;

        /// <summary>What was decoded for <paramref name="id"/>, while it is held.</summary>
        public bool TryGetDecoded(int id, out T value)
        {
            bool held = id >= 0 && id < ReconcileDeltas.IdCount && _held[id];
            value = held ? _decoded[id] : default;
            return held;
        }

        /// <summary>
        /// Reads one reconcile written for <paramref name="serverTick"/>. False when it is a delta against a reconcile this
        /// reader does not hold, in which case it must not be used. The payload is consumed exactly either way.
        /// </summary>
        public bool Read(Reader reader, uint serverTick, out T value)
        {
            if (!Writer.HasDeltaSerializers<T>())
            {
                value = reader.ReadReconcile<T>();
                return true;
            }

            reader.ReadDeltaReconcileHeader(out bool isFull, out byte id, out byte distance);
            bool usable = true;
            T baseline = default;
            if (!isFull)
            {
                int baselineId = (id - distance) & (ReconcileDeltas.IdCount - 1);
                uint baselineTick = _serverTicks[baselineId];
                /* Held, and written within the baseline window before this one: an id from an earlier cycle is at least
                 * IdCount ticks older and fails the tick check. */
                usable = distance > 0 && _held[baselineId] && serverTick != baselineTick && serverTick - baselineTick <= ReconcileDeltas.MaximumBaselineTicks;
                if (usable)
                    baseline = _decoded[baselineId];
            }

            // Delta serializers consume the same bytes whatever the baseline, so an unusable delta is still read through.
            value = reader.ReadDeltaReconcileBody(baseline, isFull);
            if (!usable)
                return false;

            _decoded[id] = value;
            _serverTicks[id] = serverTick;
            _held[id] = true;
            if (_newestId < 0 || serverTick - _newestServerTick < uint.MaxValue / 2)
            {
                _newestId = id;
                _newestServerTick = serverTick;
            }
            return true;
        }

        /// <inheritdoc />
        public byte GetAcknowledgement(uint latestServerTick)
        {
            if (_newestId < 0 || latestServerTick - _newestServerTick > ReconcileDeltas.MaximumBaselineTicks)
                return 0;
            return (byte)(ReconcileDeltas.AcknowledgementFlag | _newestId);
        }

        /// <summary>The newest reconcile decoded, or false when there is none.</summary>
        public bool TryGetNewest(out T value)
        {
            value = _newestId >= 0 ? _decoded[_newestId] : default;
            return _newestId >= 0;
        }

        /// <summary>Forgets everything read.</summary>
        public void Reset()
        {
            Array.Clear(_held, 0, _held.Length);
            Array.Clear(_decoded, 0, _decoded.Length);
            _newestId = -1;
        }
    }
}
