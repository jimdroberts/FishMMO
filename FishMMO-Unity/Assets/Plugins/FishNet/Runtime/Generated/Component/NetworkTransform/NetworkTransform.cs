#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define DEVELOPMENT
#endif
using FishNet.Connection;
using FishNet.Documenting;
using FishNet.Managing;
using FishNet.Managing.Logging;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using GameKit.Dependencies.Utilities;
using System;
using System.Collections.Generic;
using FishNet.Managing.Timing;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Scripting;
using static FishNet.Object.NetworkObject;

namespace FishNet.Component.Transforming
{
    [DisallowMultipleComponent]
    [AddComponentMenu("FishNet/Component/NetworkTransform")]
    public sealed class NetworkTransform : NetworkBehaviour
    {
        #region Types.
        [Serializable]
        public enum ComponentConfigurationType
        {
            Disabled = 0,
            CharacterController = 1,
            Rigidbody = 2,
            Rigidbody2D = 3
        }

        #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
        /// <summary>
        /// Width of each packed position axis.
        /// </summary>
        public enum PositionPackingBits : byte
        {
            /// <summary>
            /// 2 bytes per axis. This is the stock format.
            /// </summary>
            Sixteen = 0,
            /// <summary>
            /// 3 bytes per axis.
            /// </summary>
            TwentyFour = 1
        }
        #endif

        private struct ReceivedClientData
        {
            /// <summary>
            /// Level of detail indexes which have data.
            /// </summary>
            public bool HasData;
            /// <summary>
            /// Most recent data.
            /// </summary>
            public PooledWriter Writer;
            /// <summary>
            /// Channel the current data arrived on.
            /// </summary>
            public Channel Channel;
            /// <summary>
            /// LocalTick of the side receiving this update, typically the server.
            /// </summary>
            public uint LocalTick;

            /// <summary>
            /// Updates current values.
            /// </summary>
            /// <param name = "updateHasData">True to set all HasData to true.</param>
            public void Update(ArraySegment<byte> data, Channel channel, bool updateHasData, uint localTick)
            {
                if (Writer == null)
                    Writer = WriterPool.Retrieve();

                Writer.Clear();
                Writer.WriteArraySegment(data);
                Channel = channel;
                LocalTick = localTick;

                if (updateHasData)
                    HasData = true;
            }

            /// <summary>
            /// Will cause this data to send on the reliable channel once even if data is unchanged.
            /// </summary>
            public void SendReliably()
            {
                HasData = true;
                Channel = Channel.Reliable;
            }

            public void ResetState()
            {
                HasData = false;
                WriterPool.StoreAndDefault(ref Writer);
            }
        }

        [Serializable]
        public struct SnappedAxes
        {
            public bool X;
            public bool Y;
            public bool Z;
        }

        [Flags]
        private enum ChangedDelta : uint
        {
            Unset = 0,
            PositionX = 1,
            PositionY = 2,
            PositionZ = 4,
            Rotation = 8,
            Extended = 16,
            ScaleX = 32,
            ScaleY = 64,
            ScaleZ = 128,
            Nested = 256,
            All = ~0u
        }

        [Flags]
        private enum ChangedFull
        {
            Unset = 0,
            Position = 1,
            Rotation = 2,
            Scale = 4,
            Childed = 8,
            Teleport = 16
        }

        [Flags]
        private enum UpdateFlagA : byte
        {
            Unset = 0,
            X2 = 1,
            X4 = 2,
            Y2 = 4,
            Y4 = 8,
            Z2 = 16,
            Z4 = 32,
            Rotation = 64,
            Extended = 128
        }

        [Flags]
        private enum UpdateFlagB : byte
        {
            Unset = 0,
            X2 = 1,
            X4 = 2,
            Y2 = 4,
            Y4 = 8,
            Z2 = 16,
            Z4 = 32,
            Child = 64,
            Teleport = 128
        }

        public class GoalData : IResettable
        {
            public uint ReceivedTick;
            /// <summary>
            /// FISHMMO EDIT: the server tick this data describes, kept even for a reliable settle (whose Transforms.Tick is
            /// cleared). Timed playback reaches this data exactly when the playback clock passes it.
            /// </summary>
            public uint PlaybackTick;
            public RateData Rates = new();
            public TransformData Transforms = new();

            [Preserve]
            public GoalData() { }

            public void ResetState()
            {
                ReceivedTick = 0;
                PlaybackTick = 0;
                Transforms.ResetState();
                Rates.ResetState();
            }

            public void InitializeState() { }
        }

        public class RateData : IResettable
        {
            /// <summary>
            /// Rate for position after smart calculations.
            /// </summary>
            public float Position;
            /// <summary>
            /// Rate for rotation after smart calculations.
            /// </summary>
            public float Rotation;
            /// <summary>
            /// Rate for scale after smart calculations.
            /// </summary>
            public float Scale;
            /// <summary>
            /// Unaltered rate for position calculated through position change and tickspan.
            /// </summary>
            public float LastUnalteredPositionRate;
            /// <summary>
            /// Number of ticks the rates are calculated for.
            /// If TickSpan is 2 then the rates are calculated under the assumption the transform changed over 2 ticks.
            /// </summary>
            public uint TickSpan;
            /// <summary>
            /// Time remaining until transform is expected to reach it's goal.
            /// </summary>
            internal float TimeRemaining;

            [Preserve]
            public RateData() { }

            public void Update(RateData rd)
            {
                Update(rd.Position, rd.Rotation, rd.Scale, rd.LastUnalteredPositionRate, rd.TickSpan, rd.TimeRemaining);
            }

            /// <summary>
            /// Updates rates.
            /// </summary>
            public void Update(float position, float rotation, float scale, float unalteredPositionRate, uint tickSpan, float timeRemaining)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
                LastUnalteredPositionRate = unalteredPositionRate;
                TickSpan = tickSpan;
                TimeRemaining = timeRemaining;
            }

            public void ResetState()
            {
                Position = 0f;
                Rotation = 0f;
                Scale = 0f;
                LastUnalteredPositionRate = 0f;
                TickSpan = 0;
                TimeRemaining = 0f;
            }

            public void InitializeState() { }
        }

        public class TransformData : IResettable
        {
            public enum ExtrapolateState : byte
            {
                Disabled = 0,
                Available = 1,
                Active = 2
            }

            /// <summary>
            /// True if default state. This becomes false during an update and true when resetting state.
            /// </summary>
            public bool IsDefault { get; private set; } = true;
            /// <summary>
            /// Tick this data was received or created.
            /// </summary>
            public uint Tick;
            /// <summary>
            /// True if this data has already been checked for snapping.
            /// Snapping calls may occur multiple times when data is received, depending why or how it came in.
            /// This check prevents excessive work.
            /// </summary>
            public bool SnappingChecked;
            /// <summary>
            /// Local position in the data.
            /// </summary>
            public Vector3 Position;
            /// <summary>
            /// Local rotation in the data.
            /// </summary>
            public Quaternion Rotation;
            /// <summary>
            /// Local scale in the data.
            /// </summary>
            public Vector3 Scale;
            /// <summary>
            /// Position to extrapolate towards.
            /// </summary>
            public Vector3 ExtrapolatedPosition;
            /// <summary>
            /// Current state of extrapolation.
            /// </summary>
            public ExtrapolateState ExtrapolationState;
            /// <summary>
            /// NetworkBehaviour which is the parent of this object for Tick.
            /// </summary>
            public NetworkBehaviour ParentBehaviour;

            [Preserve]
            public TransformData() { }

            internal void SetIsDefaultToFalse() => IsDefault = false;

            internal void Update(TransformData copy)
            {
                Update(copy.Tick, copy.Position, copy.Rotation, copy.Scale, copy.ExtrapolatedPosition, copy.ParentBehaviour);
            }

            internal void Update(uint tick, Vector3 position, Quaternion rotation, Vector3 scale, Vector3 extrapolatedPosition, NetworkBehaviour parentBehaviour)
            {
                IsDefault = false;
                Tick = tick;
                Position = position;
                Rotation = rotation;
                Scale = scale;
                ExtrapolatedPosition = extrapolatedPosition;
                ParentBehaviour = parentBehaviour;
            }

            public void ResetState()
            {
                IsDefault = true;
                Tick = 0;
                SnappingChecked = false;
                Position = Vector3.zero;
                Rotation = Quaternion.identity;
                Scale = Vector3.zero;
                ExtrapolatedPosition = Vector3.zero;
                ExtrapolationState = ExtrapolateState.Disabled;
                ParentBehaviour = null;
            }

            public void InitializeState() { }
        }
        #endregion

        #region Public.
        /// <summary>
        /// </summary>
        /// <param name = "prev"></param>
        /// <param name = "next"></param>
        [APIExclude]
        public delegate void DataReceivedChanged(TransformData prev, TransformData next);

        /// <summary>
        /// Called when new data is received. Previous and next data are provided. Next data may be manipulated.
        /// </summary>
        public event DataReceivedChanged OnDataReceived;
        /// <summary>
        /// Called when GoalData is updated.
        /// </summary>
        public event Action<GoalData> OnNextGoal;
        /// <summary>
        /// Called when the transform has reached it's goal.
        /// </summary>
        public event Action OnInterpolationComplete;
        /// <summary>
        /// True if the local client used TakeOwnership and is awaiting an ownership change.
        /// </summary>
        public bool TakenOwnership { get; private set; }
        /// <summary>
        /// NetworkBehaviour this transform is a child of.
        /// </summary>
        public NetworkBehaviour ParentBehaviour { get; private set; }
        #endregion

        #region Serialized.
        /// <summary>
        /// Attached movement component to automatically configure.
        /// </summary>
        [Tooltip("Attached movement component to automatically configure.")]
        [SerializeField]
        private ComponentConfigurationType _componentConfiguration = ComponentConfigurationType.Disabled;
        /// <summary>
        /// True to synchronize when this transform changes parent.
        /// </summary>
        [Tooltip("True to synchronize when this transform changes parent.")]
        [SerializeField]
        private bool _synchronizeParent;
        /// <summary>
        /// How much to compress each transform property.
        /// </summary>
        [Tooltip("How much to compress each transform property.")]
        [SerializeField]
        private TransformPackingData _packing = new()
        {
            Position = AutoPackType.Packed,
            Rotation = AutoPackType.Packed,
            Scale = AutoPackType.Unpacked
        };
        #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
        /// <summary>
        /// Bits used for each packed position axis. Positions outside the packed range are sent unpacked.
        /// </summary>
        [Tooltip("Bits used for each packed position axis. Sixteen is 2 bytes per axis and TwentyFour is 3. Positions outside the packed range are sent unpacked, 4 bytes per axis. The server and clients must use the same value.")]
        [SerializeField]
        private PositionPackingBits _positionPackingBits = PositionPackingBits.Sixteen;
        /// <summary>
        /// Positions are multiplied by this value before they are packed. Resolution is 1 divided by this value, and the packed range is the largest packed value divided by it.
        /// </summary>
        [Tooltip("Positions are multiplied by this value before they are packed. Resolution is 1 divided by this value. The packed range is 32766 (Sixteen) or 8388606 (TwentyFour) divided by this value, so the default 100 gives 0.01 resolution within +/-327.66 or +/-83886.06. The server and clients must use the same value.")]
        [SerializeField]
        private float _positionCompressionScale = 100f;
        #endif
        /// <summary>
        /// True to use scaled deltaTime when smoothing.
        /// </summary>
        [Tooltip("True to use scaled deltaTime when smoothing.")]
        [SerializeField]
        private bool _useScaledTime = true;
        /// <summary>
        /// How many ticks to interpolate.
        /// </summary>
        [Tooltip("How many ticks to interpolate.")]
        [Range(1, MAX_INTERPOLATION)]
        [SerializeField]
        private ushort _interpolation = 2;
        /// <summary>
        /// How many ticks to extrapolate.
        /// </summary>
        [Tooltip("How many ticks to extrapolate.")]
        [Range(0, 1024)]
        [SerializeField]
        #pragma warning disable CS0414 // Not in use.
        private ushort _extrapolation = 2;
        #pragma warning restore CS0414 // Not in use.
        /// <summary>
        /// True to enable teleport threshhold.
        /// </summary>
        [Tooltip("True to enable teleport threshhold.")]
        [SerializeField]
        private bool _enableTeleport;
        /// <summary>
        /// How far the transform must travel in a single update to cause a teleport rather than smoothing. Using 0f will teleport every update.
        /// </summary>
        [Tooltip("How far the transform must travel in a single update to cause a teleport rather than smoothing. Using 0f will teleport every update.")]
        [Range(0f, float.MaxValue)]
        [SerializeField]
        private float _teleportThreshold = 1f;
        /// <summary>
        /// True if owner controls how the object is synchronized.
        /// </summary>
        [Tooltip("True if owner controls how the object is synchronized.")]
        [SerializeField]
        private bool _clientAuthoritative = true;
        /// <summary>
        /// True to synchronize movements on server to owner when not using client authoritative movement.
        /// </summary>
        [Tooltip("True to synchronize movements on server to owner when not using client authoritative movement.")]
        [SerializeField]
        private bool _sendToOwner = true;

        /// <summary>
        /// Gets SendToOwner.
        /// </summary>
        public bool GetSendToOwner() => _sendToOwner;

        /// <summary>
        /// True if the owner discards transform updates from the server. ObserversUpdateClientAuthoritativeTransform returns early for the owner in exactly these cases, so the server does not send updates to the owner.
        /// </summary>
        internal override bool ExcludeOwnerFromUnbufferedObserversRpcs => _clientAuthoritative || !_sendToOwner;

        /// <summary>
        /// Sets SendToOwner. Only the server may call this method.
        /// </summary>
        /// <param name = "value">New value.</param>
        public void SetSendToOwner(bool value)
        {
            _sendToOwner = value;
            if (IsServerInitialized)
                ObserversSetSendToOwner(value);
        }

        /// <summary>
        /// How often in ticks to synchronize. This is default to 1 but can be set longer to send less often. This value may also be changed at runtime. Enabling Network level of detail for this NetworkTransform disables manual control of this feature as it will be handled internally.
        /// </summary>
        [Tooltip("How often in ticks to synchronize. This is default to 1 but can be set longer to send less often. This value may also be changed at runtime. Enabling Network level of detail for this NetworkTransform disables manual control of this feature as it will be handled internally.")]
        [Range(1, byte.MaxValue)]
        [SerializeField]
        private byte _interval = 1;
        /// <summary>
        /// True to synchronize position. Even while checked only changed values are sent.
        /// </summary>
        [Tooltip("True to synchronize position. Even while checked only changed values are sent.")]
        [SerializeField]
        private bool _synchronizePosition = true;

        /// <summary>
        /// Sets if to synchronize position.
        /// </summary>
        /// <param name = "value">New value.</param>
        public void SetSynchronizePosition(bool value) => _synchronizePosition = value;

        /// <summary>
        /// Distance sensitivity on position checks.
        /// </summary>
        [Tooltip("Distance sensitivity on position checks.")]
        [Range(0.00001f, 1.25f)]
        [SerializeField]
        private float _positionSensitivity = 0.001f;
        /// <summary>
        /// Axes to snap on position.
        /// </summary>
        [Tooltip("Axes to snap on position.")]
        [SerializeField]
        private SnappedAxes _positionSnapping = new();

        /// <summary>
        /// Sets which Position axes to snap.
        /// </summary>
        /// <param name = "axes">Axes to snap.</param>
        public void SetPositionSnapping(SnappedAxes axes) => _positionSnapping = axes;

        /// <summary>
        /// True to synchronize rotation. Even while checked only changed values are sent.
        /// </summary>
        [Tooltip("True to synchronize rotation. Even while checked only changed values are sent.")]
        [SerializeField]
        private bool _synchronizeRotation = true;

        /// <summary>
        /// Sets if to synchronize rotation.
        /// </summary>
        /// <param name = "value">New value.</param>
        public void SetSynchronizeRotation(bool value) => _synchronizeRotation = value;

        /// <summary>
        /// Axes to snap on rotation.
        /// </summary>
        [Tooltip("Axes to snap on rotation.")]
        [SerializeField]
        private SnappedAxes _rotationSnapping = new();

        /// <summary>
        /// Sets which Scale axes to snap.
        /// </summary>
        /// <param name = "axes">Axes to snap.</param>
        public void SetRotationSnapping(SnappedAxes axes) => _rotationSnapping = axes;

        /// <summary>
        /// True to synchronize scale. Even while checked only changed values are sent.
        /// </summary>
        [Tooltip("True to synchronize scale. Even while checked only changed values are sent.")]
        [SerializeField]
        private bool _synchronizeScale = true;
        /// <summary>
        /// Distance sensitivity on scale checks.
        /// </summary>
        [Tooltip("Distance sensitivity on scale checks.")]
        [Range(0.00001f, 1.25f)]
        [SerializeField]
        private float _scaleSensitivity = 0.001f;

        /// <summary>
        /// Sets if to synchronize scale.
        /// </summary>
        /// <param name = "value">New value.</param>
        public void SetSynchronizeScale(bool value) => _synchronizeScale = value;

        /// <summary>
        /// Axes to snap on scale.
        /// </summary>
        [Tooltip("Axes to snap on scale.")]
        [SerializeField]
        private SnappedAxes _scaleSnapping = new();

        /// <summary>
        /// Sets which Scale axes to snap.
        /// </summary>
        /// <param name = "axes">Axes to snap.</param>
        public void SetScaleSnapping(SnappedAxes axes) => _scaleSnapping = axes;
        #endregion

        #region Private.
        /// <summary>
        /// Packing data with all values set to uncompressed.
        /// </summary>
        private TransformPackingData _unpacked = new()
        {
            Position = AutoPackType.Unpacked,
            Rotation = AutoPackType.Unpacked,
            Scale = AutoPackType.Unpacked
        };
        /// <summary>
        /// True if the last DataReceived was on the reliable channel. Default to true so initial values do not extrapolate.
        /// </summary>
        private bool _lastReceiveReliable = true;
        /// <summary>
        /// Last transform which this object was a child of.
        /// </summary>
        private Transform _parentTransform;
        /// <summary>
        /// Values changed over time that server has sent to clients since last reliable has been sent.
        /// </summary>
        private ChangedDelta _serverChangedSinceReliable;
        /// <summary>
        /// Values changed over time that client has sent to server since last reliable has been sent.
        /// </summary>
        private ChangedDelta _clientChangedSinceReliable = ChangedDelta.Unset;
        /// <summary>
        /// Last tick an ObserverRpc passed checks.
        /// </summary>
        private uint _lastObserversRpcTick;
        /// <summary>
        /// Last tick a ServerRpc passed checks.
        /// </summary>
        private uint _lastServerRpcTick;
        /// <summary>
        /// Last received data from an authoritative client.
        /// </summary>
        private ReceivedClientData _authoritativeClientData = new();
        /// <summary>
        /// True if subscribed to TimeManager for ticks.
        /// </summary>
        private bool _subscribedToTicks;
        /// <summary>
        /// True if subscribed to TimeManager for update.
        /// </summary>
        private bool _subscribedToUpdate;
        /// <summary>
        /// Starting interpolation on the rigidbody.
        /// </summary>
        private RigidbodyInterpolation? _initializedRigidbodyInterpolation;
        /// <summary>
        /// Starting interpolation on the rigidbody2d.
        /// </summary>
        private RigidbodyInterpolation2D? _initializedRigidbodyInterpolation2d;
        /// <summary>
        /// Last TransformData to be received from the server.
        /// </summary>
        private TransformData _lastReceivedServerTransformData;
        /// <summary>
        /// Last TransformData to be received from the server.
        /// </summary>
        private TransformData _lastReceivedClientTransformData;
        /// <summary>
        /// Last RateData to be calculated from LastReceivedTransformData.
        /// </summary>
        private readonly RateData _lastCalculatedRateData = new();
        /// <summary>
        /// GoalDatas to move towards.
        /// </summary>
        private readonly Queue<GoalData> _goalDataQueue = new();
        /// <summary>
        /// Current GoalData being used.
        /// </summary>
        private GoalData _currentGoalData;

        #region FISHMMO EDIT: compact transforms.
        /* Two smaller encodings for the unreliable updates observers receive most of.
         *
         * Positions (24-bit packing only): an axis is sent as the low 16 bits of its 24-bit value, flagged with both of
         * its size bits (a combination stock FishNet never writes). The reader restores the high bits from the last value
         * it holds for that axis, choosing the value nearest to it, which is exact while the object is within 327 m of
         * what the reader last received. That reference is ANY earlier update, not the previous one, so a lost or
         * reordered packet costs nothing. The writer only wraps when it has a sent value to measure against and the axis
         * moved less than WrappedPositionSafeUnits since it, leaving half the range for readers that missed updates; a
         * teleport, a first send or a huge jump goes out in full as before.
         *
         * Rotation (Packed only): a rotation about the up axis alone, which is every upright character, is sent as a
         * 16-bit heading (0.0055 degrees, finer than the 32-bit quaternion it replaces). Anything tilted sends an escape
         * value and then the quaternion as before, so nothing is lost. Both peers must agree, so this one is a static
         * set the same on both. */
        /// <summary>FISHMMO EDIT: true to send wrapped 16-bit positions where safe (see the region note). Readers always understand them.</summary>
        public static bool WrappedPositions = true;
        /// <summary>FISHMMO EDIT: true to send upright rotations as a 16-bit heading. Must be the same on server and clients.</summary>
        public static bool CompactRotation = true;
        /// <summary>Largest move, in packed units, from the last sent value that is still wrapped: a quarter of the 16-bit range.</summary>
        public const int WrappedPositionSafeUnits = 16384;
        /// <summary>Heading code that announces a full quaternion follows.</summary>
        public const ushort CompactRotationEscape = ushort.MaxValue;
        /// <summary>Largest x or z component of a rotation still sent as a heading (about 0.01 degrees of tilt).</summary>
        public const float CompactRotationTiltTolerance = 1e-4f;

        #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
        /// <summary>True when this send may wrap its positions: 24-bit packing, not childed, and a previous send to measure against.</summary>
        private bool CanWrapPositions(TransformPackingData packing, TransformData lastSent)
        {
            return WrappedPositions && lastSent != null && !lastSent.IsDefault && packing.Position != AutoPackType.Unpacked &&
                   _positionPackingBits == PositionPackingBits.TwentyFour;
        }

        /// <summary>Writes the low 16 bits of a scaled position axis when it is close enough to the last sent value; false writes nothing.</summary>
        public static bool TryWriteWrappedPosition(Writer writer, float compressed, float previousCompressed)
        {
            if (Math.Abs(compressed) > POSITION_PACKING_24_MAX_VALUE || Math.Abs(compressed - previousCompressed) >= WrappedPositionSafeUnits)
                return false;
            int value = (int)Math.Round(compressed, MidpointRounding.AwayFromZero);
            writer.WriteUInt16Unpacked((ushort)value);
            return true;
        }

        /// <summary>Reads a wrapped position axis against the reader's last value for it.</summary>
        private float ReadWrappedPosition(PooledReader reader, float reference) => ReadWrappedPosition(reader, reference, GetPositionCompressionScale());

        /// <summary>Restores a wrapped axis: the value with these low 16 bits nearest to <paramref name="reference"/>.</summary>
        public static float ReadWrappedPosition(Reader reader, float reference, float scale)
        {
            ushort low = reader.ReadUInt16Unpacked();
            int referenceValue = (int)Math.Round(reference * scale, MidpointRounding.AwayFromZero);
            int value = referenceValue + (short)(ushort)(low - (ushort)referenceValue);
            return value / scale;
        }
        #endif

        /// <summary>Writes a rotation as a 16-bit heading when it is upright, else the escape and a packed quaternion.</summary>
        public static void WriteCompactRotation(Writer writer, Quaternion rotation)
        {
            if (Math.Abs(rotation.x) <= CompactRotationTiltTolerance && Math.Abs(rotation.z) <= CompactRotationTiltTolerance &&
                (rotation.y != 0f || rotation.w != 0f))
            {
                double degrees = 2d * Math.Atan2(rotation.y, rotation.w) * (180d / Math.PI);
                degrees %= 360d;
                if (degrees < 0d)
                    degrees += 360d;
                int code = (int)Math.Round(degrees / 360d * CompactRotationEscape, MidpointRounding.AwayFromZero);
                if (code >= CompactRotationEscape)
                    code = 0;
                writer.WriteUInt16Unpacked((ushort)code);
                return;
            }
            writer.WriteUInt16Unpacked(CompactRotationEscape);
            writer.WriteQuaternion32(rotation);
        }

        /// <summary>Reads a rotation written by <see cref="WriteCompactRotation"/>.</summary>
        public static Quaternion ReadCompactRotation(Reader reader)
        {
            ushort code = reader.ReadUInt16Unpacked();
            if (code == CompactRotationEscape)
                return reader.ReadQuaternion32();
            return Quaternion.Euler(0f, (float)(code * (360d / CompactRotationEscape)), 0f);
        }
        #endregion

        #region FISHMMO EDIT: timed playback.
        /* Observed transforms are drawn at ONE shared point in the past: the newest server tick this client has received
         * (smoothed), minus Interpolation ticks, or more while the connection has recently been late (PlaybackClock). FishNet's own playback starts when Interpolation goals are queued and then
         * consumes them at a fixed rate without ever looking at a clock, so each object's delay was whatever it happened to
         * be when its playback last (re)started: about 3 ticks after spawning, 3 or 4 after a stop-and-go (one more with
         * batched sends), about 1 after a reliable settle. Lag compensation rewinds every character by one fixed delay, so a
         * predicted hit on a character that had just started moving missed on the server. Here each goal is reached when the
         * clock passes its tick, a late or early object is sped up or slowed down back onto the clock, and a far-off one
         * snaps. Clients only; the server's own playback of client-authoritative data is unchanged. */
        /// <summary>
        /// FISHMMO EDIT: true to draw observed transforms on the shared playback clock (see the region note). False restores
        /// FishNet's queue-count playback.
        /// </summary>
        public static bool TimedPlayback = true;
        /// <summary>The draw tick timed playback is steering to this frame (set before MoveToTarget runs).</summary>
        private double _timedTarget;
        /// <summary>True while MoveToTarget runs under timed playback: no queue-count speed-up, and goals wait for their tick.</summary>
        private bool _timedStepActive;
        /// <summary>Ticks behind the clock beyond which an object snaps instead of catching up.</summary>
        private const double TimedSnapTicks = 6d;
        /// <summary>Queued goals kept under timed playback; older ones are dropped (the object is then far behind and snaps).</summary>
        private const int TimedMaximumQueue = 32;
        /// <summary>Most a newly started object is fast-forwarded to reach the clock, in ticks.</summary>
        private const double TimedMaximumCatchUpTicks = 4d;
        /// <summary>Speed change per tick of error: an object one tick behind plays at 1.5x until it is back on the clock.</summary>
        private const float TimedCorrectionGain = 0.5f;
        private const float TimedMinimumRate = 0.25f;
        private const float TimedMaximumRate = 3f;

        /// <summary>True when this object is drawn on the shared playback clock: a client's view of data it receives.</summary>
        private bool IsTimedPlayback => TimedPlayback && !IsServerStarted && IsClientInitialized;

        /// <summary>
        /// FISHMMO EDIT: the server tick this client draws observed transforms at this frame, or 0 before it has received
        /// anything. Never newer than <paramref name="minimumDelayTicks"/> behind the newest data, and further behind when
        /// data has recently arrived late (see PlaybackClock). Lag compensation sends this tick with every input, so the
        /// server rewinds to exactly what was on screen whatever the delay is.
        /// </summary>
        public static double PlaybackDrawTick(TimeManager timeManager, double minimumDelayTicks)
        {
            return PlaybackClock.DrawTick(timeManager, minimumDelayTicks);
        }

        /// <summary>
        /// The shared playback clock: the newest server tick this client has received, smoothed so it advances steadily
        /// although data arrives in steps (a send interval above 1 delivers two ticks at once). Computed once per frame.
        /// </summary>
        /// <remarks>
        /// Also the ADAPTIVE DELAY. Data that arrives later than the delay leaves an object with nothing to move toward, so
        /// it pauses and catches up. The clock records how far behind itself each arrival was and keeps the delay above the
        /// recent worst: it grows at once when data comes in late and shrinks back slowly (DelayRecoveryTicksPerSecond)
        /// when the connection is steady, capped at MaximumDelayTicks. A steady connection plays at the minimum delay; a
        /// jittery one plays a little further behind and does not pause. Safe for hits because the delay in force is part
        /// of the tick every input claims.
        /// </remarks>
        private static class PlaybackClock
        {
            /// <summary>Most the delay grows to, in ticks: beyond this the connection is broken, not jittery.</summary>
            private const double MaximumDelayTicks = 8d;
            /// <summary>Margin kept above the latest arrival's lateness, in ticks.</summary>
            private const double DelayMarginTicks = 0.5d;
            /// <summary>How fast the remembered worst lateness fades once arrivals are steady, ticks per second.</summary>
            private const double LatenessFadeTicksPerSecond = 0.5d;
            /// <summary>How fast the delay shrinks toward what the connection needs, ticks per second.</summary>
            private const double DelayRecoveryTicksPerSecond = 0.5d;
            private static double _worstLateness;
            private static double _extraDelay;

            /// <summary>The draw tick: the clock less the larger of the minimum delay and the adaptive one; 0 before any data.</summary>
            public static double DrawTick(TimeManager timeManager, double minimumDelayTicks)
            {
                double reference = Reference(timeManager);
                if (reference <= 0d)
                    return 0d;
                return reference - System.Math.Max(minimumDelayTicks, _extraDelay);
            }

            /// <summary>How quickly the clock follows the arrival pattern, in seconds.</summary>
            private const double TimeConstantSeconds = 1d;
            /// <summary>An arrival this far from the smoothed clock (a stall, a reconnect) resets it instead of being averaged in.</summary>
            private const double ResyncTicks = 10d;
            private static int _frame = -1;
            private static TimeManager _timeManager;
            private static double _offset;
            private static bool _hasOffset;
            private static double _reference;
            private static double _lastTime;

            /// <summary>The smoothed newest received server tick, or 0 when the client has received nothing yet.</summary>
            public static double Reference(TimeManager timeManager)
            {
                if (timeManager == null)
                    return 0d;
                if (_frame == Time.frameCount && ReferenceEquals(_timeManager, timeManager))
                    return _reference;

                _frame = Time.frameCount;
                if (!ReferenceEquals(_timeManager, timeManager))
                {
                    _timeManager = timeManager;
                    _hasOffset = false;
                    _worstLateness = 0d;
                    _extraDelay = 0d;
                }

                uint remote = timeManager.LastPacketTick.LastRemoteTick;
                double tickDelta = timeManager.TickDelta;
                if (remote == TimeManager.UNSET_TICK || tickDelta <= 0d)
                {
                    _hasOffset = false;
                    _reference = 0d;
                    return 0d;
                }

                double now = Time.unscaledTimeAsDouble;
                double clock = now / tickDelta;
                double sample = clock - remote;
                if (!_hasOffset || System.Math.Abs(sample - _offset) > ResyncTicks)
                {
                    _offset = sample;
                    _hasOffset = true;
                }
                else
                {
                    double elapsed = System.Math.Max(0d, now - _lastTime);
                    _offset += (sample - _offset) * (1d - System.Math.Exp(-elapsed / TimeConstantSeconds));
                }
                double frameSeconds = System.Math.Max(0d, now - _lastTime);
                _lastTime = now;
                _reference = clock - _offset;

                /* Staleness, every frame: how far the clock has run past the newest tick received. That is exactly what
                 * runs an object out of data once it exceeds the delay. Steady batched arrivals peak about a tick past it;
                 * data that is late keeps growing it until it arrives, and the delay grows with it, so playback slows
                 * instead of stopping. */
                double staleness = _reference - remote;
                if (staleness > _worstLateness)
                    _worstLateness = staleness;
                _worstLateness = System.Math.Max(0d, _worstLateness - LatenessFadeTicksPerSecond * frameSeconds);
                double needed = System.Math.Min(MaximumDelayTicks, _worstLateness + DelayMarginTicks);
                if (needed > _extraDelay)
                    _extraDelay = needed;
                else
                    _extraDelay = System.Math.Max(needed, _extraDelay - DelayRecoveryTicksPerSecond * frameSeconds);
                return _reference;
            }
        }

        /// <summary>The tick this object is drawn at now: its current goal's tick less the time still to go.</summary>
        private double TimedDrawnTick(double tickDelta)
        {
            GoalData current = _currentGoalData;
            return current.PlaybackTick - System.Math.Max(0d, current.Rates.TimeRemaining) / tickDelta;
        }

        /// <summary>The tick at which a goal starts: when the object leaves the previous goal for it.</summary>
        private static double TimedStartTick(GoalData goal)
        {
            uint span = goal.Rates.TickSpan < 1 ? 1u : goal.Rates.TickSpan;
            return (double)goal.PlaybackTick - span;
        }

        /// <summary>
        /// One frame of timed playback: start the next goal when its tick comes, keep a playing object on the clock by
        /// changing its speed, and snap one that has fallen far behind.
        /// </summary>
        private void MoveToTargetTimed(float delta)
        {
            if (!DoSettingsAllowSmoothing())
                return;

            double target = PlaybackClock.DrawTick(_timeManager, _interpolation);
            if (target <= 0d)
            {
                // Nothing received yet: FishNet's own playback until the clock exists.
                MoveToTarget(delta);
                return;
            }

            double tickDelta = _timeManager.TickDelta;
            _timedTarget = target;
            _timedStepActive = true;
            try
            {
                if (_currentGoalData == null)
                {
                    if (_goalDataQueue.Count == 0)
                        return;
                    double start = TimedStartTick(_goalDataQueue.Peek());
                    if (target < start)
                        return;
                    // Its tick has come (or passed): start it, already as far along as the clock says it should be.
                    SetCurrentGoalData(_goalDataQueue.Dequeue());
                    double behind = target - start;
                    if (behind > TimedSnapTicks)
                    {
                        SnapTimed(target);
                        return;
                    }
                    MoveToTarget((float)(System.Math.Min(behind, TimedMaximumCatchUpTicks) * tickDelta));
                    return;
                }

                double error = target - TimedDrawnTick(tickDelta);
                if (error > TimedSnapTicks)
                {
                    SnapTimed(target);
                    return;
                }
                float rate = Mathf.Clamp(1f + (float)error * TimedCorrectionGain, TimedMinimumRate, TimedMaximumRate);
                MoveToTarget(delta * rate);
            }
            finally
            {
                _timedStepActive = false;
            }
        }

        /// <summary>Puts a far-behind object straight onto the newest goal at or before the clock; drops the goals in between.</summary>
        private void SnapTimed(double target)
        {
            GoalData latest = null;
            while (_goalDataQueue.Count > 0 && _goalDataQueue.Peek().PlaybackTick <= target)
            {
                if (latest != null)
                    ResettableObjectCaches<GoalData>.Store(latest);
                latest = _goalDataQueue.Dequeue();
            }
            if (latest != null)
                SetCurrentGoalData(latest);
            if (_currentGoalData == null)
                return;
            if (_synchronizeParent)
                SetParent(_currentGoalData.Transforms.ParentBehaviour, _currentGoalData.Rates);
            SetInstantRates(_currentGoalData.Rates, 1, 0f);
            SnapProperties(_currentGoalData.Transforms, true);
        }
        #endregion
        /// <summary>
        /// True if the transform has changed since it started.
        /// </summary>
        private bool _changedSinceStart;
        /// <summary>
        /// Number of intervals remaining before synchronization.
        /// </summary>
        private short _intervalsRemaining;
        /// <summary>
        /// Last sent transform data.
        /// </summary>
        private TransformData _lastSentTransformData;
        /// <summary>
        /// Writers for changed data.
        /// </summary>
        private PooledWriter _toClientChangedWriter;
        /// <summary>
        /// If not unset a force send will occur on or after this tick.
        /// </summary>
        private uint _forceSendTick = TimeManager.UNSET_TICK;
        /// <summary>
        /// Returns all properties as changed.
        /// </summary>
        private ChangedDelta _fullChanged => ChangedDelta.All;
        /// <summary>
        /// When true teleport will be sent with the next changed data.
        /// </summary>
        private bool _teleport;
        /// <summary>
        /// Cached transform
        /// </summary>
        private Transform _cachedTransform;
        /// <summary>
        /// Cached TimeManager reference for performance.
        /// </summary>
        private TimeManager _timeManager;
        #endregion

        #region Private Profiler Markers
        private static readonly ProfilerMarker _pm_OnUpdate = new("NetworkTransform.TimeManager_OnUpdate()");
        private static readonly ProfilerMarker _pm_OnPostTick = new("NetworkTransform.TimeManager_OnPostTick()");
        private static readonly ProfilerMarker _pm_MoveToTarget = new("NetworkTransform.MoveToTarget(float)");
        private static readonly ProfilerMarker _pm_UpdateTransformData = new("NetworkTransform.UpdateTransformData(ArraySegment<byte>, TransformData, TransformData, ref ChangedFull)");
        private static readonly ProfilerMarker _pm_ForceSend0 = new("NetworkTransform.ForceSend()");
        private static readonly ProfilerMarker _pm_ForceSend1 = new("NetworkTransform.ForceSend(uint)");
        private static readonly ProfilerMarker _pm_SendToClients = new("NetworkTransform.SendToClients()");
        private static readonly ProfilerMarker _pm_SendToServer = new("NetworkTransform.SendToServer(TransformData)");
        #endregion

        #region Const.
        /// <summary>
        /// Maximum possible interpolation value.
        /// </summary>
        public const ushort MAX_INTERPOLATION = 250;
        #endregion

        private void Awake()
        {
            _interval = Math.Max(_interval, (byte)1);
        }

        private void OnDestroy()
        {
            base.ResetState(true);
            ResetState_OnDestroy();
        }

        public override void OnStartNetwork()
        {
            _cachedTransform = transform;
            _timeManager = TimeManager;

            ChangeTickSubscription(true);
        }

        public override void OnStartServer()
        {
            _lastReceivedClientTransformData = ObjectCaches<TransformData>.Retrieve();
            InitializeFields(true);
            SetDefaultGoalData();
        }

        public override void OnSpawnServer(NetworkConnection connection)
        {
            base.OnSpawnServer(connection);
            /* If not on the root then the initial properties may need to be synchronized
             * since the spawn message only sends root information. If initial
             * properties have changed update spawning connection. */
            if (NetworkObject.gameObject != gameObject && _changedSinceStart)
            {
                // Send latest.
                PooledWriter writer = WriterPool.Retrieve();
                SerializeChanged(_fullChanged, writer);
                TargetUpdateTransform(connection, writer.GetArraySegment(), Channel.Reliable);
                writer.Store();
            }
        }

        public override void OnStartClient()
        {
            _lastReceivedServerTransformData = ObjectCaches<TransformData>.Retrieve();
            ChangeUpdateSubscription(subscribe: true);
            ConfigureComponents();
            InitializeFields(false);
            SetDefaultGoalData();
        }

        public override void OnOwnershipServer(NetworkConnection prevOwner)
        {
            ConfigureComponents();
            _intervalsRemaining = 0;
            // Reset last tick since each client sends their own ticks.
            _lastServerRpcTick = 0;

            TryClearGoalDatas_OwnershipChange(prevOwner, true);
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            ConfigureComponents();
            _intervalsRemaining = 0;

            // Not new owner.
            if (!IsOwner)
            {
                /* If client authoritative and ownership was lost
                 * then default goals must be set to force the
                 * object to it's last transform. */
                if (_clientAuthoritative)
                    SetDefaultGoalData();
            }

            TryClearGoalDatas_OwnershipChange(prevOwner, false);
        }

        public override void OnStopNetwork()
        {
            ResetState();
            ChangeUpdateSubscription(subscribe: false);
        }

        /// <summary>
        /// Tries to clear the GoalDatas queue during an ownership change.
        /// </summary>
        private void TryClearGoalDatas_OwnershipChange(NetworkConnection prevOwner, bool asServer)
        {
            if (_clientAuthoritative)
            {
                // If not server
                if (!asServer)
                {
                    // If owner now then clear as the owner controls the object now and shouldnt use past datas.
                    if (IsOwner)
                        _goalDataQueue.Clear();
                }
                // as Server.
                else
                {
                    // If new owner is valid then clear to allow new owner datas.
                    if (Owner.IsValid)
                        _goalDataQueue.Clear();
                }
            }
            /* Server authoritative never clears because the
             * clients do not control this object thus should always
             * follow the queue. */
        }

        private void TimeManager_OnUpdate()
        {
            using (_pm_OnUpdate.Auto())
            {
                float deltaTime = _useScaledTime ? Time.deltaTime : Time.unscaledDeltaTime;
                // FISHMMO EDIT: a client draws what it receives on the shared playback clock.
                if (IsTimedPlayback)
                    MoveToTargetTimed(deltaTime);
                else
                    MoveToTarget(deltaTime);
            }
        }

        /// <summary>
        /// Adds collections required.
        /// </summary>
        private void InitializeFields(bool asServer)
        {
            bool asClientAndNotHost = !asServer && !IsServerStarted;

            /* Even though these collections are nullified on clean up
             * they could still exist on the reinitialization for clientHost if
             * an object is despawned to a pool then very quickly respawned
             * before the clientHost side has not processed the despawn yet.
             * Because of this check count rather than null. */
            if (asClientAndNotHost || asServer)
            {
                // Prefer to reset existing.
                if (_lastSentTransformData != null)
                    _lastSentTransformData.ResetState();
                else
                    _lastSentTransformData = ResettableObjectCaches<TransformData>.Retrieve();
            }

            if (asServer)
            {
                if (_toClientChangedWriter != null)
                    _toClientChangedWriter.Clear();
                else
                    _toClientChangedWriter = WriterPool.Retrieve();
            }
        }

        /// <summary>
        /// Configures components automatically.
        /// </summary>
        /// <summary>
        /// Configures components automatically.
        /// </summary>
        private void ConfigureComponents()
        {
            // Disabled.
            if (_componentConfiguration == ComponentConfigurationType.Disabled)
                return;

            // RB.
            if (_componentConfiguration == ComponentConfigurationType.Rigidbody)
            {
                if (TryGetComponent(out Rigidbody c))
                {
                    //If first time set starting interpolation.
                    if (_initializedRigidbodyInterpolation == null)
                        _initializedRigidbodyInterpolation = c.interpolation;

                    bool isKinematic = CanMakeKinematic();
                    c.isKinematic = isKinematic;

                    if (isKinematic)
                        c.interpolation = RigidbodyInterpolation.None;
                    else
                        c.interpolation = _initializedRigidbodyInterpolation.Value;
                }
            }
            //RB2D
            else if (_componentConfiguration == ComponentConfigurationType.Rigidbody2D)
            {
                if (TryGetComponent(out Rigidbody2D c))
                {
                    //If first time set starting interpolation.
                    if (_initializedRigidbodyInterpolation2d == null)
                        _initializedRigidbodyInterpolation2d = c.interpolation;

                    bool isKinematic = CanMakeKinematic();

                    #if UNITY_6000_1_OR_NEWER
                    c.bodyType = isKinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
                    #else
                    c.isKinematic = isKinematic;
                    #endif

                    c.simulated = !isKinematic;

                    if (isKinematic)
                        c.interpolation = RigidbodyInterpolation2D.None;
                    else
                        c.interpolation = _initializedRigidbodyInterpolation2d.Value;
                }
            }
            //CC
            else if (_componentConfiguration == ComponentConfigurationType.CharacterController)
            {
                if (TryGetComponent(out CharacterController c))
                {
                    //Client auth.
                    if (_clientAuthoritative)
                    {
                        c.enabled = IsController;
                    }
                    //Server auth.
                    else
                    {
                        //Not CSP.
                        if (_sendToOwner)
                            c.enabled = IsServerInitialized;
                        //Most likely CSP.
                        else
                            c.enabled = IsServerInitialized || IsOwner;
                    }
                }
            }

            bool CanMakeKinematic()
            {
                bool isServerStarted = IsServerStarted;

                //When not client auth, kinematic is always true if not server.
                if (!_clientAuthoritative)
                    return !isServerStarted;

                /* If here then is client-auth. */

                //Owner shouldn't be kinematic as they are controller.
                if (IsOwner)
                    return false;

                //Is server, and there is no owner.
                if (isServerStarted && !Owner.IsActive)
                    return false;

                return true;
            }
        }

        /// <summary>
        /// Called when a tick occurs.
        /// </summary>
        private void TimeManager_OnPostTick()
        {
            using (_pm_OnPostTick.Auto())
            {
                //If to force send via tick delay do so and reset force send tick.
                if (_forceSendTick != TimeManager.UNSET_TICK && _timeManager.LocalTick > _forceSendTick)
                {
                    _forceSendTick = TimeManager.UNSET_TICK;
                    ForceSend();
                }

                UpdateParentBehaviour();

                /* Intervals remaining is only used when the interval value
                 * is set higher than 1. An interval of 1 indicates to send
                 * every tick. Only check to wait more ticks if interval
                 * is larger than 1. */
                if (_interval > 1)
                {
                    /* If intervalsRemaining is unset then that means the transform
                     * did not change last tick. See if transform changed and if so then
                     * update remaining to _interval. */
                    if (_intervalsRemaining == -1)
                    {
                        //Transform didn't change, no reason to start remaining.
                        if (!_cachedTransform.hasChanged)
                            return;

                        _intervalsRemaining = _interval;
                    }

                    //If here then intervalsRemaining can be deducted.
                    _intervalsRemaining--;
                    //Interval not met yet.
                    if (_intervalsRemaining > 0)
                        return;

                    //Intervals remainin is met. Reset to -1 to await new change.
                    _intervalsRemaining = -1;
                }

                bool isServerInitialized = IsServerInitialized;
                bool isClientInitialized = IsClientInitialized;

                if (isServerInitialized)
                {
                    /* If client is not initialized then
                     * call a move to targe ton post tick to ensure
                     * anything with instant rates gets moved. */
                    if (!isClientInitialized)
                        MoveToTarget((float)_timeManager.TickDelta);
                    //
                    SendToClients();
                }

                if (isClientInitialized)
                    SendToServer(_lastSentTransformData);
            }
        }

        /// <summary>
        /// Tries to subscribe to TimeManager ticks.
        /// </summary>
        private void ChangeTickSubscription(bool subscribe)
        {
            if (subscribe == _subscribedToTicks || NetworkManager == null)
                return;

            _subscribedToTicks = subscribe;
            if (subscribe)
                NetworkManager.TimeManager.OnPostTick += TimeManager_OnPostTick;
            else
                NetworkManager.TimeManager.OnPostTick -= TimeManager_OnPostTick;
        }

        private void ChangeUpdateSubscription(bool subscribe)
        {
            if (subscribe == _subscribedToUpdate || _timeManager == null)
                return;

            _subscribedToUpdate = subscribe;
            if (subscribe)
                _timeManager.OnUpdate += TimeManager_OnUpdate;
            else
                _timeManager.OnUpdate -= TimeManager_OnUpdate;
        }

        /// <summary>
        /// Sets the interpolation value.
        /// </summary>
        public void SetInterpolation(ushort value)
        {
            if (value < 1)
                value = 1;

            _interpolation = value;
        }

        /// <summary>
        /// Sets the extrapolation value.
        /// </summary>
        public void SetExtrapolation(ushort value)
        {
            _extrapolation = value;
        }

        /// <summary>
        /// Returns if controlling logic can be run. This may be the server when there is no owner, even if client authoritative, and more.
        /// </summary>
        /// <returns></returns>
        private bool CanControl()
        {
            //Client auth.
            if (_clientAuthoritative)
                return IsController;


            //Server auth.
            if (IsServerInitialized)
                return true;

            //Fall through.
            return false;
        }

        /// <summary>
        /// When called by the controller of this object the next changed data will be teleported to by spectators.
        /// </summary>
        public void Teleport()
        {
            if (CanControl())
                _teleport = true;
        }

        /// <summary>
        /// Sets SendToOwner value.
        /// </summary>
        /// <param name = "value"></param>
        [ObserversRpc(BufferLast = true, ExcludeServer = true)]
        private void ObserversSetSendToOwner(bool value)
        {
            _sendToOwner = value;
        }

        /// <summary>
        /// Resets last sent information to force a resend of current values after a number of ticks.
        /// </summary>
        public void ForceSend(uint ticks)
        {
            using (_pm_ForceSend1.Auto())
            {
                /* If there is a pending delayed force send then queue it
                 * immediately and set a new delay tick. */
                if (_forceSendTick != TimeManager.UNSET_TICK)
                    ForceSend();
                _forceSendTick = _timeManager.LocalTick + ticks;
            }
        }

        /// <summary>
        /// Resets last sent information to force a resend of current values.
        /// </summary>
        public void ForceSend()
        {
            using (_pm_ForceSend0.Auto())
            {
                _lastSentTransformData.ResetState();
                if (_authoritativeClientData.Writer != null)
                    _authoritativeClientData.SendReliably();
            }
        }

        /// <summary>
        /// Updates the interval value over the network.
        /// </summary>
        /// <param name = "value">New interval.</param>
        public void SetInterval(byte value)
        {
            bool canSet = (IsServerInitialized && !_clientAuthoritative) || (IsServerInitialized && _clientAuthoritative && !Owner.IsValid) || (_clientAuthoritative && IsOwner);

            if (!canSet)
                return;

            if (IsServerInitialized)
                ObserversSetInterval(value);
            else
                ServerSetInterval(value);
        }

        /// <summary>
        /// Updates the interval value.
        /// </summary>
        /// <param name = "value"></param>
        private void SetIntervalInternal(byte value)
        {
            value = (byte)Mathf.Max(value, 1);
            _interval = value;
        }

        /// <summary>
        /// Sets interval over the network.
        /// </summary>
        [ServerRpc(RunLocally = true)]
        private void ServerSetInterval(byte value)
        {
            if (!_clientAuthoritative)
            {
                Owner.Kick(KickReason.ExploitAttempt, LoggingType.Common, $"Connection Id {Owner.ClientId} has been kicked for trying to update this object without client authority.");
                return;
            }

            SetIntervalInternal(value);
        }

        /// <summary>
        /// Sets interval over the network.
        /// </summary>
        [ObserversRpc(BufferLast = true, RunLocally = true)]
        private void ObserversSetInterval(byte value)
        {
            SetIntervalInternal(value);
        }

        /// <summary>
        /// Creates goal data using current position.
        /// </summary>
        private void SetDefaultGoalData()
        {
            Transform t = _cachedTransform;
            NetworkBehaviour parentBehaviour = null;
            //If there is a parent try to output the behaviour on it.
            if (_synchronizeParent)
            {
                if (NetworkObject.CurrentParentNetworkBehaviour != null)
                {
                    t.parent.TryGetComponent(out parentBehaviour);
                    if (parentBehaviour == null)
                    {
                        LogInvalidParent();
                    }
                    else
                    {
                        _parentTransform = t.parent;
                        ParentBehaviour = parentBehaviour;
                    }
                }
            }

            _teleport = false;
            SetLastReceived(_lastReceivedServerTransformData);
            SetLastReceived(_lastReceivedClientTransformData);
            //SetInstantRates(_currentGoalData.Rates, 0, -1f);

            void SetLastReceived(TransformData td)
            {
                //Could be null if not initialized due to server or client side not being used.
                if (td == null)
                    return;

                td.Update(0, t.localPosition, t.localRotation, t.localScale, t.localPosition, parentBehaviour);
            }
        }

        /// <summary>
        /// Prints an invalid parent debug.
        /// </summary>
        private void LogInvalidParent()
        {
            NetworkManager.LogWarning($"{gameObject.name} [Id {ObjectId}] is childed but the parent {_cachedTransform.parent.name} does not contain a NetworkBehaviour component. To synchronize parents the parent object must have a NetworkBehaviour component, even if empty.");
        }

        #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
        /// <summary>
        /// Largest scaled value sent as a 24-bit packed position. Like the 16-bit maximum, it is one below the type's maximum.
        /// </summary>
        private const float POSITION_PACKING_24_MAX_VALUE = (1 << 23) - 2;

        /// <summary>
        /// Returns the position compression scale, or 100 if the serialized value is not positive.
        /// </summary>
        private float GetPositionCompressionScale() => _positionCompressionScale > 0f ? _positionCompressionScale : 100f;
        #endif

        /// <summary>
        /// Writes a position axis which has already been scaled and range checked.
        /// </summary>
        private void WritePackedPosition(PooledWriter writer, float compressed)
        {
            #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
            if (_positionPackingBits == PositionPackingBits.TwentyFour)
            {
                //Round rather than truncate so the error is at most half a step, with no bias toward the origin.
                int value = (int)Math.Round(compressed, MidpointRounding.AwayFromZero);
                writer.WriteUInt16Unpacked((ushort)value);
                writer.WriteUInt8Unpacked((byte)(value >> 16));
                return;
            }
            #endif
            writer.WriteInt16((short)compressed);
        }

        /// <summary>
        /// Reads a position axis written by WritePackedPosition.
        /// </summary>
        private float ReadPackedPosition(PooledReader reader)
        {
            #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
            if (_positionPackingBits == PositionPackingBits.TwentyFour)
            {
                int value = reader.ReadUInt16Unpacked() | (reader.ReadUInt8Unpacked() << 16);
                //Sign extend from 24 bits.
                value = (value << 8) >> 8;
                return value / GetPositionCompressionScale();
            }
            return reader.ReadInt16() / GetPositionCompressionScale();
            #else
            return reader.ReadInt16() / 100f;
            #endif
        }

        /// <summary>
        /// Serializes only changed data into writer.
        /// </summary>
        private void SerializeChanged(ChangedDelta changed, PooledWriter writer, TransformData dataToUpdate = null)
        {
            bool canUpdateData = dataToUpdate != null;
            if (canUpdateData && changed != ChangedDelta.Unset)
                dataToUpdate.SetIsDefaultToFalse();

            UpdateFlagA flagsA = UpdateFlagA.Unset;
            UpdateFlagB flagsB = UpdateFlagB.Unset;
            /* Do not use compression when childed. Depending
             * on the scale of the parent compression may
             * not be accurate enough. */
            TransformPackingData packing = ChangedContains(changed, ChangedDelta.Nested) ? _unpacked : _packing;

            int startIndexA = writer.Position;
            writer.Skip(1);
            //Original axis value.
            float original;
            //Compressed axis value.
            float compressed;
            //Multiplier for compression.
            float multiplier = 100f;
            /* Maximum value compressed may be
             * to send as compressed. */
            float maxValue = short.MaxValue - 1;
            #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
            //Positions use their own scale and width. Scale uses the values above.
            float positionMultiplier = GetPositionCompressionScale();
            float positionMaxValue = _positionPackingBits == PositionPackingBits.TwentyFour ? POSITION_PACKING_24_MAX_VALUE : maxValue;
            #else
            float positionMultiplier = multiplier;
            float positionMaxValue = maxValue;
            #endif

            Transform t = _cachedTransform;
            /* Position. */
            if (_synchronizePosition)
            {
                AutoPackType localPacking = packing.Position;
                #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
                // FISHMMO EDIT: compact transforms. What was last sent, read before it is overwritten below.
                bool wrapPositions = CanWrapPositions(packing, dataToUpdate);
                Vector3 previousSent = wrapPositions ? dataToUpdate.Position : default;
                #endif
                //PositionX
                if (ChangedContains(changed, ChangedDelta.PositionX))
                {
                    original = t.localPosition.x;

                    if (canUpdateData)
                        dataToUpdate.Position.x = original;

                    compressed = original * positionMultiplier;
                    #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
                    // FISHMMO EDIT: compact transforms.
                    if (wrapPositions && TryWriteWrappedPosition(writer, compressed, previousSent.x * positionMultiplier))
                    {
                        flagsA |= UpdateFlagA.X2 | UpdateFlagA.X4;
                    }
                    else
                    #endif
                    if (localPacking != AutoPackType.Unpacked && Math.Abs(compressed) <= positionMaxValue)
                    {
                        flagsA |= UpdateFlagA.X2;
                        WritePackedPosition(writer, compressed);
                    }
                    else
                    {
                        flagsA |= UpdateFlagA.X4;
                        writer.WriteSingle(original);
                    }
                }

                //PositionY
                if (ChangedContains(changed, ChangedDelta.PositionY))
                {
                    original = t.localPosition.y;

                    if (canUpdateData)
                        dataToUpdate.Position.y = original;

                    compressed = original * positionMultiplier;
                    #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
                    // FISHMMO EDIT: compact transforms.
                    if (wrapPositions && TryWriteWrappedPosition(writer, compressed, previousSent.y * positionMultiplier))
                    {
                        flagsA |= UpdateFlagA.Y2 | UpdateFlagA.Y4;
                    }
                    else
                    #endif
                    if (localPacking != AutoPackType.Unpacked && Math.Abs(compressed) <= positionMaxValue)
                    {
                        flagsA |= UpdateFlagA.Y2;
                        WritePackedPosition(writer, compressed);
                    }
                    else
                    {
                        flagsA |= UpdateFlagA.Y4;
                        writer.WriteSingle(original);
                    }
                }

                //PositionZ
                if (ChangedContains(changed, ChangedDelta.PositionZ))
                {
                    original = t.localPosition.z;

                    if (canUpdateData)
                        dataToUpdate.Position.z = original;

                    compressed = original * positionMultiplier;
                    #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
                    // FISHMMO EDIT: compact transforms.
                    if (wrapPositions && TryWriteWrappedPosition(writer, compressed, previousSent.z * positionMultiplier))
                    {
                        flagsA |= UpdateFlagA.Z2 | UpdateFlagA.Z4;
                    }
                    else
                    #endif
                    if (localPacking != AutoPackType.Unpacked && Math.Abs(compressed) <= positionMaxValue)
                    {
                        flagsA |= UpdateFlagA.Z2;
                        WritePackedPosition(writer, compressed);
                    }
                    else
                    {
                        flagsA |= UpdateFlagA.Z4;
                        writer.WriteSingle(original);
                    }
                }
            }

            /* Rotation. */
            if (_synchronizeRotation)
            {
                if (ChangedContains(changed, ChangedDelta.Rotation))
                {
                    if (canUpdateData)
                        dataToUpdate.Rotation = t.localRotation;

                    flagsA |= UpdateFlagA.Rotation;
                    /* Rotation can always use pack settings even
                     * if childed. Unsual transform scale shouldn't affect rotation. */
                    // FISHMMO EDIT: compact transforms.
                    if (CompactRotation && _packing.Rotation == AutoPackType.Packed)
                        WriteCompactRotation(writer, t.localRotation);
                    else
                        writer.WriteQuaternion(t.localRotation, _packing.Rotation);
                }
            }

            /* If there is a teleport pending then apply
             * extended flag since thats where teleport resides. */
            bool teleport = _teleport;
            if (teleport)
                changed |= ChangedDelta.Extended;

            if (ChangedContains(changed, ChangedDelta.Extended))
            {
                AutoPackType localPacking = packing.Scale;
                flagsA |= UpdateFlagA.Extended;
                int startIndexB = writer.Position;
                writer.Skip(1);

                /* Redundant to do the teleport check here since it was done
                 * just above, but for code consistency the teleport updateflag
                 * is set within this conditional with rest of the extended
                 * data. */
                if (teleport)
                {
                    flagsB |= UpdateFlagB.Teleport;
                    _teleport = false;
                }

                /* Scale. */
                if (_synchronizeScale)
                {
                    //ScaleX
                    if (ChangedContains(changed, ChangedDelta.ScaleX))
                    {
                        original = t.localScale.x;

                        if (canUpdateData)
                            dataToUpdate.Scale.x = original;

                        compressed = original * multiplier;
                        if (localPacking != AutoPackType.Unpacked && Math.Abs(compressed) <= maxValue)
                        {
                            flagsB |= UpdateFlagB.X2;
                            writer.WriteInt16((short)compressed);
                        }
                        else
                        {
                            flagsB |= UpdateFlagB.X4;
                            writer.WriteSingle(original);
                        }
                    }

                    //ScaleY
                    if (ChangedContains(changed, ChangedDelta.ScaleY))
                    {
                        original = t.localScale.y;

                        if (canUpdateData)
                            dataToUpdate.Scale.y = original;

                        compressed = original * multiplier;
                        if (localPacking != AutoPackType.Unpacked && Math.Abs(compressed) <= maxValue)
                        {
                            flagsB |= UpdateFlagB.Y2;
                            writer.WriteInt16((short)compressed);
                        }
                        else
                        {
                            flagsB |= UpdateFlagB.Y4;
                            writer.WriteSingle(original);
                        }
                    }

                    //ScaleZ
                    if (ChangedContains(changed, ChangedDelta.ScaleZ))
                    {
                        original = t.localScale.z;

                        if (canUpdateData)
                            dataToUpdate.Scale.z = original;

                        compressed = original * multiplier;
                        if (localPacking != AutoPackType.Unpacked && Math.Abs(compressed) <= maxValue)
                        {
                            flagsB |= UpdateFlagB.Z2;
                            writer.WriteInt16((short)compressed);
                        }
                        else
                        {
                            flagsB |= UpdateFlagB.Z4;
                            writer.WriteSingle(original);
                        }
                    }
                }

                //Childed.
                if (ChangedContains(changed, ChangedDelta.Nested) && ParentBehaviour != null)
                {
                    if (canUpdateData)
                        dataToUpdate.ParentBehaviour = ParentBehaviour;

                    flagsB |= UpdateFlagB.Child;
                    writer.WriteNetworkBehaviour(ParentBehaviour);
                }

                writer.InsertUInt8Unpacked((byte)flagsB, startIndexB);
            }

            //Insert flags.
            writer.InsertUInt8Unpacked((byte)flagsA, startIndexA);

            bool ChangedContains(ChangedDelta whole, ChangedDelta part)
            {
                return (whole & part) == part;
            }
        }

        /// <summary>
        /// Deerializes a received packet.
        /// </summary>
        private void DeserializePacket(ArraySegment<byte> data, TransformData prevTransformData, TransformData nextTransformData, ref ChangedFull changedFull)
        {
            PooledReader reader = ReaderPool.Retrieve(data, NetworkManager);
            UpdateFlagA flagsA = (UpdateFlagA)reader.ReadUInt8Unpacked();

            int readerRemaining;
            readerRemaining = reader.Remaining;
            //X
            #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
            // FISHMMO EDIT: compact transforms. Both flags = the low 16 bits of a 24-bit value; see ReadWrappedPosition.
            if (UpdateFlagAContains(flagsA, UpdateFlagA.X2) && UpdateFlagAContains(flagsA, UpdateFlagA.X4))
                nextTransformData.Position.x = ReadWrappedPosition(reader, prevTransformData.Position.x);
            else
            #endif
            if (UpdateFlagAContains(flagsA, UpdateFlagA.X2))
                nextTransformData.Position.x = ReadPackedPosition(reader);
            else if (UpdateFlagAContains(flagsA, UpdateFlagA.X4))
                nextTransformData.Position.x = reader.ReadSingle();
            else
                nextTransformData.Position.x = prevTransformData.Position.x;
            //Y
            #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
            // FISHMMO EDIT: compact transforms. Both flags = the low 16 bits of a 24-bit value; see ReadWrappedPosition.
            if (UpdateFlagAContains(flagsA, UpdateFlagA.Y2) && UpdateFlagAContains(flagsA, UpdateFlagA.Y4))
                nextTransformData.Position.y = ReadWrappedPosition(reader, prevTransformData.Position.y);
            else
            #endif
            if (UpdateFlagAContains(flagsA, UpdateFlagA.Y2))
                nextTransformData.Position.y = ReadPackedPosition(reader);
            else if (UpdateFlagAContains(flagsA, UpdateFlagA.Y4))
                nextTransformData.Position.y = reader.ReadSingle();
            else
                nextTransformData.Position.y = prevTransformData.Position.y;
            //Z
            #if FISHNET_NETWORKTRANSFORM_POSITION_PACKING
            // FISHMMO EDIT: compact transforms. Both flags = the low 16 bits of a 24-bit value; see ReadWrappedPosition.
            if (UpdateFlagAContains(flagsA, UpdateFlagA.Z2) && UpdateFlagAContains(flagsA, UpdateFlagA.Z4))
                nextTransformData.Position.z = ReadWrappedPosition(reader, prevTransformData.Position.z);
            else
            #endif
            if (UpdateFlagAContains(flagsA, UpdateFlagA.Z2))
                nextTransformData.Position.z = ReadPackedPosition(reader);
            else if (UpdateFlagAContains(flagsA, UpdateFlagA.Z4))
                nextTransformData.Position.z = reader.ReadSingle();
            else
                nextTransformData.Position.z = prevTransformData.Position.z;
            //If remaining has changed then a position was read.
            if (readerRemaining != reader.Remaining)
                changedFull |= ChangedFull.Position;

            //Rotation.
            if (UpdateFlagAContains(flagsA, UpdateFlagA.Rotation))
            {
                //Always use _packing value even if childed.
                // FISHMMO EDIT: compact transforms.
                if (CompactRotation && _packing.Rotation == AutoPackType.Packed)
                    nextTransformData.Rotation = ReadCompactRotation(reader);
                else
                    nextTransformData.Rotation = reader.ReadQuaternion(_packing.Rotation);
                changedFull |= ChangedFull.Rotation;
            }
            else
            {
                nextTransformData.Rotation = prevTransformData.Rotation;
            }

            //Extended settings.
            if (UpdateFlagAContains(flagsA, UpdateFlagA.Extended))
            {
                UpdateFlagB flagsB = (UpdateFlagB)reader.ReadUInt8Unpacked();
                readerRemaining = reader.Remaining;

                //X
                if (UpdateFlagBContains(flagsB, UpdateFlagB.X2))
                    nextTransformData.Scale.x = reader.ReadInt16() / 100f;
                else if (UpdateFlagBContains(flagsB, UpdateFlagB.X4))
                    nextTransformData.Scale.x = reader.ReadSingle();
                else
                    nextTransformData.Scale.x = prevTransformData.Scale.x;
                //Y
                if (UpdateFlagBContains(flagsB, UpdateFlagB.Y2))
                    nextTransformData.Scale.y = reader.ReadInt16() / 100f;
                else if (UpdateFlagBContains(flagsB, UpdateFlagB.Y4))
                    nextTransformData.Scale.y = reader.ReadSingle();
                else
                    nextTransformData.Scale.y = prevTransformData.Scale.y;
                //X
                if (UpdateFlagBContains(flagsB, UpdateFlagB.Z2))
                    nextTransformData.Scale.z = reader.ReadInt16() / 100f;
                else if (UpdateFlagBContains(flagsB, UpdateFlagB.Z4))
                    nextTransformData.Scale.z = reader.ReadSingle();
                else
                    nextTransformData.Scale.z = prevTransformData.Scale.z;

                if (reader.Remaining != readerRemaining)
                    changedFull |= ChangedFull.Scale;
                else
                    nextTransformData.Scale = prevTransformData.Scale;

                if (UpdateFlagBContains(flagsB, UpdateFlagB.Teleport))
                    changedFull |= ChangedFull.Teleport;

                if (UpdateFlagBContains(flagsB, UpdateFlagB.Child))
                {
                    nextTransformData.ParentBehaviour = reader.ReadNetworkBehaviour();
                    changedFull |= ChangedFull.Childed;
                }
                else
                {
                    Unnest();
                }
            }
            //No extended settings.
            else
            {
                nextTransformData.Scale = prevTransformData.Scale;
                Unnest();
            }

            void Unnest()
            {
                nextTransformData.ParentBehaviour = null;
            }

            //Returns if whole contains part.
            bool UpdateFlagAContains(UpdateFlagA whole, UpdateFlagA part)
            {
                return (whole & part) == part;
            }

            //Returns if whole contains part.
            bool UpdateFlagBContains(UpdateFlagB whole, UpdateFlagB part)
            {
                return (whole & part) == part;
            }

            reader.Store();
        }

        /// <summary>
        /// Updates the ParentBehaviour field when able to.
        /// </summary>
        private void UpdateParentBehaviour()
        {
            if (!_synchronizeParent)
                return;
            //No permissions to set.
            if (!CanControl())
                return;
            Transform parent = _cachedTransform.parent;

            //No parent.
            if (parent == null)
            {
                /* Check for being set without using nob.SetParent.
                 * Only check if was previously set inside this component; otherwise
                 * this would spam anytime the parent was null. */
                if (NetworkObject.RuntimeParentNetworkBehaviour != null)
                    NetworkManager.LogWarning($"{gameObject.name} parent object was removed without calling UnsetParent. Use networkObject.UnsetParent() to remove a NetworkObject from it's parent. This is being made a requirement in Fish-Networking v4.");

                ParentBehaviour = null;
                _parentTransform = null;
            }
            //Has a parent, see if eligible.
            else
            {
                //No change.
                if (_parentTransform == parent)
                    return;

                _parentTransform = parent;
                NetworkBehaviour outParentBehaviour;

                if (!parent.TryGetComponent(out outParentBehaviour))
                {
                    ParentBehaviour = null;
                    LogInvalidParent();
                }
                else
                {
                    ParentBehaviour = outParentBehaviour;
                    //Check for being set without using nob.SetParent.
                    if (NetworkObject.CurrentParentNetworkBehaviour != ParentBehaviour)
                        NetworkManager.LogWarning($"{gameObject.name} parent was set without calling SetParent. Use networkObject.SetParent(obj) to assign a NetworkObject a new parent. This is being made a requirement in Fish-Networking v4.");
                }
            }
        }

        /// <summary>
        /// Sets the transforms parent if it's changed.
        /// </summary>
        /// <param name = "parent"></param>
        private void SetParent(NetworkBehaviour parent, RateData rd)
        {
            Transform target = parent == null ? null : parent.transform;
            Transform t = _cachedTransform;
            //Unchanged.
            if (target == t.parent)
                return;

            Vector3 scale = t.localScale;
            //Set parent after scale is cached so scale can be maintained after changing parent.
            if (target != null)
                NetworkObject.SetParent(parent);
            else
                NetworkObject.UnsetParent();

            t.localScale = scale;

            /* Set ratedata to immediate so there's no blending between transform values when
             * getting on or off platforms. */
            if (rd != null)
                rd.Update(-1f, -1f, -1f, rd.LastUnalteredPositionRate, rd.TickSpan, rd.TimeRemaining);
        }

        /// <summary>
        /// Moves to a GoalData. Automatically determins if to use data from server or client.
        /// </summary>
        private void MoveToTarget(float delta)
        {
            using (_pm_MoveToTarget.Auto())
            {
                if (_currentGoalData == null)
                    return;

                //Cannot move if neither is active.
                if (!IsServerInitialized && !IsClientInitialized)
                    return;

                if (!DoSettingsAllowSmoothing())
                    return;

                /* Once here it's safe to assume the object will be moving.
                 * Any checks which would stop it from moving be it client
                 * auth and owner, or server controlled and server, ect,
                 * would have already been run. */
                TransformData td = _currentGoalData.Transforms;
                RateData rd = _currentGoalData.Rates;

                //Set parent.
                if (_synchronizeParent)
                    SetParent(td.ParentBehaviour, rd);

                float multiplier = 1f;
                int queueCount = _goalDataQueue.Count;
                //Increase move rate slightly if over queue count.
                // FISHMMO EDIT: not under timed playback, whose caller already sets the speed from the clock.
                if (queueCount > _interpolation + 1 && !_timedStepActive)
                    multiplier += 0.05f;

                //Rate to update. Changes per property.
                float rate;
                Transform t = _cachedTransform;

                //Snap any bits of the transform that should be.
                SnapProperties(td);

                //Position.
                if (_synchronizePosition)
                {
                    rate = rd.Position;
                    Vector3 posGoal = td.ExtrapolationState == TransformData.ExtrapolateState.Active && !_lastReceiveReliable ? td.ExtrapolatedPosition : td.Position;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (rate == -1f)
                        t.localPosition = td.Position;
                    else
                        t.localPosition = Vector3.MoveTowards(t.localPosition, posGoal, rate * delta * multiplier);
                }

                //Rotation.
                if (_synchronizeRotation)
                {
                    rate = rd.Rotation;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (rate == -1f)
                        t.localRotation = td.Rotation;
                    else
                        t.localRotation = Quaternion.RotateTowards(t.localRotation, td.Rotation, rate * delta);
                }

                //Scale.
                if (_synchronizeScale)
                {
                    rate = rd.Scale;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (rate == -1f)
                        t.localScale = td.Scale;
                    else
                        t.localScale = Vector3.MoveTowards(t.localScale, td.Scale, rate * delta);
                }

                float timeRemaining = rd.TimeRemaining - delta * multiplier;
                if (timeRemaining < -delta)
                    timeRemaining = -delta;
                rd.TimeRemaining = timeRemaining;

                if (rd.TimeRemaining <= 0f)
                {
                    float leftOver = Mathf.Abs(rd.TimeRemaining);
                    //If more in buffer then run next buffer.
                    // FISHMMO EDIT: under timed playback the next goal also waits for its tick.
                    if (queueCount > 0 && (!_timedStepActive || TimedStartTick(_goalDataQueue.Peek()) <= _timedTarget))
                    {
                        SetCurrentGoalData(_goalDataQueue.Dequeue());
                        if (leftOver > 0f)
                            MoveToTarget(leftOver);
                    }
                    // FISHMMO EDIT: a queued goal whose tick has not come yet; hold here until it does.
                    else if (queueCount > 0)
                    {
                    }
                    //No more in buffer, see if can extrapolate.
                    else
                    {
                        /* If everything matches up then end queue.
                             * Otherwise let it play out until stuff
                             * aligns. Generally the time remaining is enough
                             * but every once in awhile something goes funky
                             * and it's thrown off. */
                            if (!HasChanged(td))
                                _currentGoalData = null;
                            OnInterpolationComplete?.Invoke();
                            }
                }
            }
        }

        /// <summary>
        /// True if settings are configured to smooth with the current network state.
        /// </summary>
        /// <returns></returns>
        public bool DoSettingsAllowSmoothing() 
        {
            //If client auth and the owner don't move towards target.
            if (_clientAuthoritative)
            {
                if (IsOwner || TakenOwnership)
                    return false;
            }
            else
            {
                //If not client authoritative, is owner, and don't sync to owner.
                if (IsOwner && !_sendToOwner)
                    return false;
            }

            //True if not client controlled.
            bool controlledByClient = _clientAuthoritative && Owner.IsActive;
            //If not controlled by client and is server then no reason to move.
            if (!controlledByClient && IsServerInitialized)
                return false;

            return true;
        }

        /// <summary>
        /// Sends transform data to clients if needed.
        /// </summary>
        private void SendToClients()
        {
            using (_pm_SendToClients.Auto())
            {
                //True if clientAuthoritative and there is an owner.
                bool clientAuthoritativeWithOwner = _clientAuthoritative && Owner.IsValid;
                //Channel to send rpc on.
                Channel channel = Channel.Unreliable;
                /* If relaying from client and owner isnt clientHost.
                 * If owner is clientHost just send current server values. */
                if (clientAuthoritativeWithOwner && !Owner.IsLocalClient)
                {
                    /* If there is not new data yet and the last received was not reliable
                     * then a packet maybe did not arrive when expected. See if we need
                     * to force a reliable with the last data based on ticks passed since
                     * last update.*/
                    if (!_authoritativeClientData.HasData && _authoritativeClientData.Channel != Channel.Reliable && _authoritativeClientData.Writer != null)
                    {
                        /* If ticks have passed beyond interpolation then force
                         * to send reliably. */
                        uint maxPassedTicks = (uint)(1 + _interpolation + _extrapolation);
                        uint localTick = _timeManager.LocalTick;
                        if (localTick - _authoritativeClientData.LocalTick > maxPassedTicks)
                            _authoritativeClientData.SendReliably();
                        //Not enough time to send reliably, just don't need update.
                        else
                            return;
                    }

                    if (_authoritativeClientData.HasData)
                    {
                        _changedSinceStart = true;
                        //Resend data from clients.
                        ObserversUpdateClientAuthoritativeTransform(_authoritativeClientData.Writer.GetArraySegment(), _authoritativeClientData.Channel);
                        //Now being sent data can unset.
                        _authoritativeClientData.HasData = false;
                    }
                }
                //Sending server transform state.
                else
                {
                    PooledWriter writer = _toClientChangedWriter;

                    TransformData lastSentData = _lastSentTransformData;
                    ChangedDelta changed = GetChanged(lastSentData);

                    //If no change.
                    if (changed == ChangedDelta.Unset)
                    {
                        //No changes since last reliable; transform is up to date.
                        if (_serverChangedSinceReliable == ChangedDelta.Unset)
                            return;

                        _serverChangedSinceReliable = ChangedDelta.Unset;
                        writer = _toClientChangedWriter;
                        /* If here then current is unset but last was not.
                         * Send last as reliable so clients have the latest sent through. */
                        channel = Channel.Reliable;
                    }
                    //There is change.
                    else
                    {
                        //Since this is writing new data, reset the writer.
                        writer.Clear();

                        _serverChangedSinceReliable |= changed;

                        _changedSinceStart = true;

                        /* If here a send for transform values will occur. Update last values.
                         * Tick doesn't need to be set for whoever controls transform. */
                        //Transform t = _cachedTransform;
                        //lastSentData.Update(0, t.localPosition, t.localRotation, t.localScale, t.localPosition, ParentBehaviour);
                        lastSentData.Tick = 0;

                        SerializeChanged(changed, writer, lastSentData);
                    }

                    ObserversUpdateClientAuthoritativeTransform(writer.GetArraySegment(), channel);
                }
            }
        }

        /// <summary>
        /// Sends transform data to server if needed.
        /// </summary>
        private void SendToServer(TransformData lastSentTransformData)
        {
            using (_pm_SendToServer.Auto())
            {
                /* ClientHost does not need to send to the server.
                 * Ideally this would still occur and the data be ignored
                 * for statistics tracking but to keep the code more simple
                 * we won't be doing that. Server out however still is tracked,
                 * which is generally considered more important data. */
                if (IsServerInitialized)
                    return;

                //Not client auth or not owner.
                if (!_clientAuthoritative || !IsOwner)
                    return;

                //Channel to send on.
                Channel channel = Channel.Unreliable;
                //Values changed since last check.
                ChangedDelta changed = GetChanged(lastSentTransformData);

                //If no change.
                if (changed == ChangedDelta.Unset)
                {
                    //No changes since last reliable; transform is up to date.
                    if (_clientChangedSinceReliable == ChangedDelta.Unset)
                        return;

                    //Set changed to all changes over time and unset changes over time.
                    changed = _clientChangedSinceReliable;
                    _clientChangedSinceReliable = ChangedDelta.Unset;
                    channel = Channel.Reliable;
                }
                //There is change.
                else
                {
                    _clientChangedSinceReliable |= changed;
                }

                /* If here a send for transform values will occur. Update last values.
                 * Tick doesn't need to be set for whoever controls transform. */
                Transform t = _cachedTransform;

                //lastSentData.Update(0, t.localPosition, t.localRotation, t.localScale, t.localPosition, ParentBehaviour);
                lastSentTransformData.Tick = 0;

                //Send latest.
                PooledWriter writer = WriterPool.Retrieve();
                SerializeChanged(changed, writer, lastSentTransformData);

                ServerUpdateTransform(writer.GetArraySegment(), channel);

                writer.Store();
            }
        }

        #region GetChanged.
        /// <summary>
        /// Returns if the transform differs from td.
        /// </summary>
        private bool HasChanged(TransformData td)
        {
            Transform t = _cachedTransform;
            bool changed = td.Position != t.localPosition || td.Rotation != t.localRotation || td.Scale != t.localScale;

            return changed;
        }

        /// <summary>
        /// Returns if there is any change between two datas.
        /// </summary>
        private bool HasChanged(TransformData a, TransformData b)
        {
            return a.Position != b.Position || a.Rotation != b.Rotation || a.Scale != b.Scale || a.ParentBehaviour != b.ParentBehaviour;
        }
        ///// <summary>
        ///// Returns if there is any change between two datas and outputs what has changed.
        ///// </summary>
        //private bool HasChanged(TransformData a, TransformData b, ref ChangedFull changedFull)
        //{
        //    bool hasChanged = false;

        //    if (a.Position != b.Position)
        //    {
        //        hasChanged = true;
        //        changedFull |= ChangedFull.Position;
        //    }
        //    if (a.Rotation != b.Rotation)
        //    {
        //        hasChanged = true;
        //        changedFull |= ChangedFull.Rotation;
        //    }
        //    if (a.Scale != b.Scale)
        //    {
        //        hasChanged = true;
        //        changedFull |= ChangedFull.Scale;
        //    }
        //    if (a.ParentBehaviour != b.ParentBehaviour)
        //    {
        //        hasChanged = true;
        //        changedFull |= ChangedFull.Childed;
        //    }

        //    return hasChanged;
        //}
        /// <summary>
        /// Gets transform values that have changed against goalData.
        /// </summary>
        private ChangedDelta GetChanged(TransformData transformData)
        {
            //If default return full changed.
            if (transformData == null || transformData.IsDefault)
                return _fullChanged;

            /* If parent behaviour exist.
             * Parent isn't sent as a delta so
             * if it exists always send regardless
             * of the previously sent transform
             * data. */
            return GetChanged(transformData.Position, transformData.Rotation, transformData.Scale, transformData.ParentBehaviour);
        }

        /// <summary>
        /// Gets transform values that have changed against specified proprties.
        /// </summary>
        private ChangedDelta GetChanged(Vector3 lastPosition, Quaternion lastRotation, Vector3 lastScale, NetworkBehaviour lastParentBehaviour)
        {
            ChangedDelta changed = ChangedDelta.Unset;
            Transform t = _cachedTransform;

            if (_synchronizePosition)
            {
                Vector3 position = t.localPosition;
                if (Mathf.Abs(position.x - lastPosition.x) >= _positionSensitivity)
                    changed |= ChangedDelta.PositionX;
                if (Mathf.Abs(position.y - lastPosition.y) >= _positionSensitivity)
                    changed |= ChangedDelta.PositionY;
                if (Mathf.Abs(position.z - lastPosition.z) >= _positionSensitivity)
                    changed |= ChangedDelta.PositionZ;
            }

            if (_synchronizeRotation)
            {
                Quaternion rotation = t.localRotation;
                if (!rotation.Matches(lastRotation, true))
                    changed |= ChangedDelta.Rotation;
            }
            
            ChangedDelta startChanged = changed;

            if (_synchronizeScale)
            {
                Vector3 scale = t.localScale;
                if (Mathf.Abs(scale.x - lastScale.x) >= _scaleSensitivity)
                    changed |= ChangedDelta.ScaleX;
                if (Mathf.Abs(scale.y - lastScale.y) >= _scaleSensitivity)
                    changed |= ChangedDelta.ScaleY;
                if (Mathf.Abs(scale.z - lastScale.z) >= _scaleSensitivity)
                    changed |= ChangedDelta.ScaleZ;
            }

            if (_synchronizeParent && changed != ChangedDelta.Unset && ParentBehaviour != null)
                changed |= ChangedDelta.Nested;

            //If added scale or childed then also add extended.
            if (startChanged != changed)
                changed |= ChangedDelta.Extended;

            return changed;
        }
        #endregion

        #region Rates.
        /// <summary>
        /// Snaps transform properties using snapping settings.
        /// </summary>
        private void SnapProperties(TransformData transformData, bool force = false)
        {
            //Already snapped.
            if (transformData.SnappingChecked)
                return;

            transformData.SnappingChecked = true;
            Transform t = _cachedTransform;

            //Position.
            if (_synchronizePosition)
            {
                Vector3 startPosition = t.localPosition;
                Vector3 position;
                position.x = force || _positionSnapping.X ? transformData.Position.x : t.localPosition.x;
                position.y = force || _positionSnapping.Y ? transformData.Position.y : t.localPosition.y;
                position.z = force || _positionSnapping.Z ? transformData.Position.z : t.localPosition.z;
                t.localPosition = position;
            }

            //Rotation.
            if (_synchronizeRotation)
            {
                Vector3 eulers;
                Vector3 goalEulers = transformData.Rotation.eulerAngles;
                eulers.x = force || _rotationSnapping.X ? goalEulers.x : t.localEulerAngles.x;
                eulers.y = force || _rotationSnapping.Y ? goalEulers.y : t.localEulerAngles.y;
                eulers.z = force || _rotationSnapping.Z ? goalEulers.z : t.localEulerAngles.z;
                t.localEulerAngles = eulers;
            }

            //Scale.
            if (_synchronizeScale)
            {
                Vector3 scale;
                scale.x = force || _scaleSnapping.X ? transformData.Scale.x : t.localScale.x;
                scale.y = force || _scaleSnapping.Y ? transformData.Scale.y : t.localScale.y;
                scale.z = force || _scaleSnapping.Z ? transformData.Scale.z : t.localScale.z;
                t.localScale = scale;
            }
        }

        /// <summary>
        /// Sets move rates which will occur instantly.
        /// </summary>
        private void SetInstantRates(RateData rd, uint tickDifference, float timeRemaining)
        {
            //Was default to 1 tickDiff and -1 time remaining.
            rd.Update(-1f, -1f, -1f, -1f, tickDifference, timeRemaining);
        }

        /// <summary>
        /// Sets move rates which will occur over time.
        /// </summary>
        private void SetCalculatedRates(TransformData prevTd, RateData prevRd, GoalData nextGd, ChangedFull changedFull, bool hasChanged, Channel channel)
        {
            /* Only update rates if data has changed.
             * When data comes in reliably for eventual consistency
             * it's possible that it will be the same as the last
             * unreliable packet. When this happens no change has occurred
             * and the distance of change woudl also be 0; this prevents
             * the NT from moving. Only need to compare data if channel is reliable. */
            TransformData td = nextGd.Transforms;
            if (channel == Channel.Reliable && !hasChanged)
            {
                nextGd.Rates.Update(prevRd);
                return;
            }

            float timePassed;
            uint tickDifference = GetTickDifference(prevTd, nextGd, 1, out timePassed);

            //Distance between properties.
            float distance;
            float positionRate = 0f;
            float rotationRate = 0f;
            float scaleRate = 0f;

            RateData rd = nextGd.Rates;

            //Quick exit/check for teleport.
            if (ChangedFullContains(changedFull, ChangedFull.Teleport))
            {
                SetInstantRates(rd, tickDifference, timePassed);
                return;
            }

            //Correction to apply towards rates when a rate change is detected as abnormal.
            float abnormalCorrection = 1f;
            float unalteredPositionRate = rd.LastUnalteredPositionRate;

            //Position.
            if (ChangedFullContains(changedFull, ChangedFull.Position))
            {
                Vector3 lastPosition = prevTd.Position;
                distance = Vector3.Distance(lastPosition, td.Position);

                //If distance teleports assume rest do.
                if (_enableTeleport)
                {
                    //Over threshold.
                    if (distance >= _teleportThreshold)
                    {
                        SetInstantRates(rd, tickDifference, timePassed);
                        return;
                    }
                }

                //Check to teleport only position due to low distance.
                if (LowDistance(distance, false))
                {
                    unalteredPositionRate = -1f;
                    positionRate = -1f;
                }
                else
                {
                    //Check position rates now.
                    //Position distance already calculated.
                    unalteredPositionRate = distance / timePassed;
                    /* Try to detect abnormal rate changes.
                     *
                     * This won't occur if the user
                     * is moving using the tick system but will likely happen when the transform
                     * is being moved in update.
                     *
                     * Update will iterate a varying amount of times per tick,
                     * which will result in distances being slightly different. This is
                     * rarely an issue when the frame rate is high and the distance
                     * variance is very little, but for games which are running at about
                     * the same frame rate as the tick it's possible the object will
                     * move twice the distance every few ticks. EG: if running 60 fps/50 tick.
                     * Execution may look like this..
                     * frame, tick, frame, tick, frame, frame, tick. The frame, frame would
                     * result in double movement distance. */

                    //If last position rate is known then compare against it.
                    if (unalteredPositionRate > 0f && rd.LastUnalteredPositionRate > 0f)
                    {
                        float percentage = Mathf.Abs(1f - unalteredPositionRate / rd.LastUnalteredPositionRate);
                        /* If percentage change is more than 25% then speed is considered
                         * to have changed drastically. */
                        if (percentage > 0.25f)
                        {
                            float c = rd.LastUnalteredPositionRate / unalteredPositionRate;
                            /* Sometimes stop and goes can incorrectly trigger
                             * an abnormal detection. Fortunately abnornalties tend
                             * to either skip a tick or send twice in one tick.
                             * Because of this it's fairly safe to assume that if the calculated
                             * correction is not ~0.5f or ~2f then it's a false detection. */
                            float allowedDifference = 0.1f;
                            if ((c < 1f && Mathf.Abs(0.5f - c) < allowedDifference) || (c > 1f && Mathf.Abs(2f - c) < allowedDifference))
                            {
                                abnormalCorrection = c;
                            }
                            /* If an abnormality has been marked then assume new rate
                             * is proper. When an abnormal rate occurs unintentionally
                             * the values will fix themselves next tick, therefor when
                             * rate changes drastically twice assume its intentional or
                             * that the rate had simply fixed itself, both which would unset
                             * abnormal rate detected. */
                        }
                    }

                    //abnormalCorrection = 1f;
                    positionRate = unalteredPositionRate * abnormalCorrection;
                    if (positionRate <= 0f)
                        positionRate = -1f;
                }
            }

            //Rotation.
            if (ChangedFullContains(changedFull, ChangedFull.Rotation))
            {
                Quaternion lastRotation = prevTd.Rotation;
                distance = lastRotation.Angle(td.Rotation, true);
                if (LowDistance(distance, true))
                {
                    rotationRate = -1f;
                }
                else
                {
                    rotationRate = distance / timePassed * abnormalCorrection;
                    if (rotationRate <= 0f)
                        rotationRate = -1f;
                }
            }

            //Scale.
            if (ChangedFullContains(changedFull, ChangedFull.Scale))
            {
                Vector3 lastScale = prevTd.Scale;
                distance = Vector3.Distance(lastScale, td.Scale);
                if (LowDistance(distance, false))
                {
                    scaleRate = -1f;
                }
                else
                {
                    scaleRate = distance / timePassed * abnormalCorrection;
                    if (scaleRate <= 0f)
                        scaleRate = -1f;
                }
            }

            rd.Update(positionRate, rotationRate, scaleRate, unalteredPositionRate, tickDifference, timePassed);

            //Returns if whole contains part.
            bool ChangedFullContains(ChangedFull whole, ChangedFull part)
            {
                return (whole & part) == part;
            }

            /* Returns if the provided distance is extremely small.
             * This is used to decide if a property should be teleported.
             * When distances are exceptionally small smoothing rate
             * calculations may result as an invalid value. */
            bool LowDistance(float dist, bool rotation)
            {
                if (rotation)
                    return dist < 1f;
                else
                    return dist < 0.0001f;
            }
        }

        /// <summary>
        /// Gets the tick difference between two GoalDatas.
        /// </summary>
        private uint GetTickDifference(TransformData prevTd, GoalData nextGd, uint minimum, out float timePassed)
        {
            TransformData nextTd = nextGd.Transforms;

            uint lastTick = prevTd.Tick;
            /* Ticks passed between datas. If 0 then the last data
             * was either not set or reliable, in which case the tick
             * difference should be considered 1. */
            if (lastTick == 0)
                lastTick = nextTd.Tick - _interval;

            long tickDifference = nextTd.Tick - lastTick;
            if (tickDifference < minimum)
                tickDifference = minimum;

            timePassed = (float)NetworkManager.TimeManager.TicksToTime((uint)tickDifference);
            return (uint)tickDifference;
        }
        #endregion

        /// <summary>
        /// Sets extrapolation data on next.
        /// </summary>
        private void SetExtrapolatedData(TransformData prev, TransformData next, Channel channel)
        {
            //Default value.
            next.ExtrapolationState = TransformData.ExtrapolateState.Disabled;

            }

        /// <summary>
        /// Updates a client with transform data.
        /// </summary>
        [TargetRpc(ValidateTarget = false)]
        private void TargetUpdateTransform(NetworkConnection conn, ArraySegment<byte> data, Channel channel)
        {
            #if DEVELOPMENT
            //If receiver is client host then do nothing, clientHost need not process.
            if (IsServerInitialized && conn.IsLocalClient)
                return;
            #endif
            /* Zero data was sent, this should not be possible.
             * This is a patch to a NetworkLOD bug until it can
             * be resolved properly. */
            if (data.Count == 0)
                return;

            DataReceived(data, channel, false);
        }

        /// <summary>
        /// Updates clients with transform data.
        /// </summary>
        [ObserversRpc]
        private void ObserversUpdateClientAuthoritativeTransform(ArraySegment<byte> data, Channel channel)
        {
            if (!_clientAuthoritative && IsOwner && !_sendToOwner)
                return;
            if (_clientAuthoritative && IsOwner)
                return;
            if (IsServerInitialized)
                return;
            //Not new data.
            uint lastPacketTick = _timeManager.LastPacketTick.LastRemoteTick;
            if (lastPacketTick <= _lastObserversRpcTick)
                return;

            _lastObserversRpcTick = lastPacketTick;
            DataReceived(data, channel, false);
        }

        /// <summary>
        /// Updates the transform on the server.
        /// </summary>
        [ServerRpc]
        private void ServerUpdateTransform(ArraySegment<byte> data, Channel channel)
        {
            if (!_clientAuthoritative)
            {
                Owner.Kick(KickReason.ExploitAttempt, LoggingType.Common, $"Connection Id {Owner.ClientId} has been kicked for trying to update this object without client authority.");
                return;
            }

            TimeManager tm = TimeManager;
            //Not new data.
            uint lastPacketTick = tm.LastPacketTick.LastRemoteTick;
            if (lastPacketTick <= _lastServerRpcTick)
                return;
            _lastServerRpcTick = lastPacketTick;

            _authoritativeClientData.Update(data, channel, updateHasData: true, tm.LocalTick);
            DataReceived(data, channel, true);
        }

        /// <summary>
        /// Processes received data for lcients and server.
        /// </summary>
        private void DataReceived(ArraySegment<byte> data, Channel channel, bool asServer)
        {
            if (IsDeinitializing)
                return;

            TransformData prevTd = asServer ? _lastReceivedClientTransformData : _lastReceivedServerTransformData;
            RateData prevRd = _lastCalculatedRateData;

            ChangedFull changedFull = ChangedFull.Unset;
            GoalData nextGd = ResettableObjectCaches<GoalData>.Retrieve();
            TransformData nextTd = nextGd.Transforms;
            UpdateTransformData(data, prevTd, nextTd, ref changedFull);

            OnDataReceived?.Invoke(prevTd, nextTd);
            SetExtrapolatedData(prevTd, nextTd, channel);

            bool hasChanged = HasChanged(prevTd, nextTd);

            //If server only teleport.
            if (asServer && !IsClientStarted)
            {
                uint tickDifference = GetTickDifference(prevTd, nextGd, 1, out float timePassed);
                SetInstantRates(nextGd.Rates, tickDifference, timePassed);
            }
            //Otherwise use timed.
            else
            {
                SetCalculatedRates(prevTd, prevRd, nextGd, changedFull, hasChanged, channel);
            }

            _lastReceiveReliable = channel == Channel.Reliable;
            // FISHMMO EDIT: the tick this data describes, before a reliable settle clears Transforms.Tick.
            nextGd.PlaybackTick = nextTd.Tick;
            /* If channel is reliable then this is a settled packet.
             * Set tick to UNSET. When this occurs time calculations
             * assume only 1 tick has passed. */
            if (channel == Channel.Reliable)
                nextTd.Tick = TimeManager.UNSET_TICK;

            prevTd.Update(nextTd);
            prevRd.Update(nextGd.Rates);

            nextGd.ReceivedTick = _timeManager.LocalTick;

            bool currentDataNull = _currentGoalData == null;
            // FISHMMO EDIT: a client's received data is queued and reached on the playback clock (MoveToTargetTimed).
            bool timed = !asServer && IsTimedPlayback;
            /* If extrapolating then immediately break the extrapolation
             * in favor of newest results. This will keep the buffer
             * at 0 until the transform settles but the only other option is
             * to stop the movement, which would defeat purpose of extrapolation,
             * or slow down the transform while buffer rebuilds. Neither choice
             * is great but later on I might try slowing down the transform slightly
             * to give the buffer a chance to rebuild. */
            if (!currentDataNull && _currentGoalData.Transforms.ExtrapolationState == TransformData.ExtrapolateState.Active)
            {
                SetCurrentGoalData(nextGd);
            }
            else if (timed)
            {
                _goalDataQueue.Enqueue(nextGd);
            }
            /* If queue isn't started and its buffered enough
             * to satisfy interpolation then set ready
             * and set current data.
             *
             * Also if reliable then begin moving. */
            else if ((currentDataNull && _goalDataQueue.Count >= _interpolation) || channel == Channel.Reliable)
            {
                if (_goalDataQueue.Count > 0)
                {
                    SetCurrentGoalData(_goalDataQueue.Dequeue());
                    /* If is reliable and has changed then also
                     * enqueue latest. */
                    if (hasChanged)
                        _goalDataQueue.Enqueue(nextGd);
                }
                else
                {
                    SetCurrentGoalData(nextGd);
                }
            }
            /* If here then there's not enough in buffer to begin
             * so add onto the buffer. */
            else
            {
                _goalDataQueue.Enqueue(nextGd);
            }

            /* If the queue is excessive beyond interpolation then
             * dequeue extras to prevent from dropping behind too
             * quickly. This shouldn't be an issue with normal movement
             * as the NT speeds up if the buffer unexpectedly grows, but
             * when connections are unstable results may come in chunks
             * and for a better experience the older parts of the chunks
             * will be dropped. */
            // FISHMMO EDIT: under timed playback only a runaway queue is trimmed (oldest first); the clock catches up or snaps.
            if (timed)
            {
                while (_goalDataQueue.Count > TimedMaximumQueue)
                    ResettableObjectCaches<GoalData>.Store(_goalDataQueue.Dequeue());
            }
            else if (_goalDataQueue.Count > _interpolation + 3)
            {
                while (_goalDataQueue.Count > _interpolation)
                {
                    GoalData tmpGd = _goalDataQueue.Dequeue();
                    ResettableObjectCaches<GoalData>.Store(tmpGd);
                }

                //Snap to the next data to fix any smoothing timings.
                SetCurrentGoalData(_goalDataQueue.Dequeue());
                SetInstantRates(_currentGoalData!.Rates, 1, -1f);
                SnapProperties(_currentGoalData.Transforms, true);
            }
        }

        /// <summary>
        /// Sets CurrentGoalData value.
        /// </summary>
        private void SetCurrentGoalData(GoalData data)
        {
            if (_currentGoalData != null)
                ResettableObjectCaches<GoalData>.Store(_currentGoalData);

            _currentGoalData = data;
            OnNextGoal?.Invoke(data);
        }

        /// <summary>
        /// Updates a TransformData from packetData.
        /// </summary>
        private void UpdateTransformData(ArraySegment<byte> packetData, TransformData prevTransformData, TransformData nextTransformData, ref ChangedFull changedFull)
        {
            using (_pm_UpdateTransformData.Auto())
            {
                DeserializePacket(packetData, prevTransformData, nextTransformData, ref changedFull);
                nextTransformData.Tick = _timeManager.LastPacketTick.LastRemoteTick;
            }
        }

        /// <summary>
        /// Configures this NetworkTransform for CSP.
        /// </summary>
        internal void ConfigureForPrediction(PredictionType predictionType)
        {
            _clientAuthoritative = false;
            _sendToOwner = false;

            //Do not try to change component configuration if its already specified.
            if (_componentConfiguration != ComponentConfigurationType.Disabled)
            {
                if (predictionType == PredictionType.Rigidbody)
                    _componentConfiguration = ComponentConfigurationType.Rigidbody;
                else if (predictionType == PredictionType.Rigidbody2D)
                    _componentConfiguration = ComponentConfigurationType.Rigidbody2D;
                else if (predictionType == PredictionType.Other)
                    /* If other or CC then needs to be configured.
                     * When CC it will be configured properly, if there
                     * is no CC then no action will be taken. */
                    _componentConfiguration = ComponentConfigurationType.CharacterController;
            }

            ConfigureComponents();
        }

        /// <summary>
        /// Updates which properties are synchronized.
        /// </summary>
        /// <param name = "value">Properties to synchronize.</param>
        public void SetSynchronizedProperties(SynchronizedProperty value)
        {
            //If sending from the server.
            if (IsServerInitialized)
            {
                //If no owner, or not client auth.
                if (IsController || !_clientAuthoritative)
                    ObserversSetSynchronizedProperties(value);
                else
                    return;
            }
            //Sending from client.
            else if (_clientAuthoritative && IsOwner)
            {
                ServerSetSynchronizedProperties(value);
            }
            //Cannot change.
            else
            {
                return;
            }

            //Update locally.
            SetSynchronizedPropertiesInternal(value);
        }

        /// <summary>
        /// Sets synchronized values based on value.
        /// </summary>
        [ServerRpc]
        private void ServerSetSynchronizedProperties(SynchronizedProperty value)
        {
            if (!_clientAuthoritative)
            {
                Owner.Kick(KickReason.ExploitAttempt, LoggingType.Common, $"Connection Id {Owner.ClientId} has been kicked for trying to update this object without client authority.");
                return;
            }

            SetSynchronizedPropertiesInternal(value);
            //Send to observers.
            ObserversSetSynchronizedProperties(value);
        }

        /// <summary>
        /// Sets synchronized values based on value.
        /// </summary>
        [ObserversRpc(BufferLast = true, ExcludeServer = true)]
        private void ObserversSetSynchronizedProperties(SynchronizedProperty value)
        {
            SetSynchronizedPropertiesInternal(value);
        }

        /// <summary>
        /// Sets synchronized values based on value.
        /// </summary>
        private void SetSynchronizedPropertiesInternal(SynchronizedProperty value)
        {
            _synchronizeParent = SynchronizedPropertyContains(value, SynchronizedProperty.Parent);
            _synchronizePosition = SynchronizedPropertyContains(value, SynchronizedProperty.Position);
            _synchronizeRotation = SynchronizedPropertyContains(value, SynchronizedProperty.Rotation);
            _synchronizeScale = SynchronizedPropertyContains(value, SynchronizedProperty.Scale);

            bool SynchronizedPropertyContains(SynchronizedProperty whole, SynchronizedProperty part)
            {
                return (whole & part) == part;
            }
        }

        /// <summary>
        /// Deinitializes this component.
        /// </summary>
        private void ResetState()
        {
            _teleport = false;
            ChangeTickSubscription(false);
            /* Reset server and client side since this is called from
             * OnStopNetwork. */

            _lastObserversRpcTick = TimeManager.UNSET_TICK;
            _authoritativeClientData.ResetState();

            WriterPool.StoreAndDefault(ref _toClientChangedWriter);

            ObjectCaches<bool>.StoreAndDefault(ref _authoritativeClientData.HasData);
            ObjectCaches<ChangedDelta>.StoreAndDefault(ref _serverChangedSinceReliable);

            ResettableObjectCaches<TransformData>.StoreAndDefault(ref _lastReceivedClientTransformData);
            ResettableObjectCaches<TransformData>.StoreAndDefault(ref _lastReceivedServerTransformData);
            //Goaldatas. Would only exist if client or clientHost.
            while (_goalDataQueue.Count > 0)
                ResettableObjectCaches<GoalData>.Store(_goalDataQueue.Dequeue());

            if (_lastSentTransformData != null)
                _lastSentTransformData.ResetState();
            ResettableObjectCaches<GoalData>.StoreAndDefault(ref _currentGoalData);
        }

        /// <summary>
        /// Deinitializes this component for OnDestroy.
        /// </summary>
        private void ResetState_OnDestroy()
        {
            ResettableObjectCaches<TransformData>.StoreAndDefault(ref _lastSentTransformData);
            WriterPool.StoreAndDefault(ref _toClientChangedWriter);
        }
    }
}