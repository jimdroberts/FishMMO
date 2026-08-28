using FishNet.Serializing;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Delta serializers for <see cref="KCCPlatform.ReplicateData"/>.
	/// </summary>
	/// <remarks>
	/// Without these FishNet has no delta method for the type and logs
	/// <c>"Write delta method not found"</c> on every tick it serializes one. A platform is a
	/// <c>TickNetworkBehaviour</c> that ticks whether or not anybody is near it, so that is a
	/// permanent error stream for as long as the scene is loaded — 9,627 of them, each with a full
	/// stack trace, in a four-minute session before this existed.
	/// </remarks>
	public static class KCCPlatformReplicateDataDeltaSerializer
	{
		/// <summary>
		/// Writes the replicate payload, which is empty.
		/// </summary>
		/// <remarks>
		/// <see cref="KCCPlatform.ReplicateData"/> carries nothing but its tick, and the tick is
		/// FishNet's to write — the character serializers beside this one do not write theirs
		/// either. Platform movement is autonomous, so there is no input to send: the struct exists
		/// to satisfy the <c>IReplicateData</c> contract, not to carry data.
		///
		/// <para>The method still has to exist. An empty body and no method at all are very
		/// different things here — the second is what produces the error this file removes.</para>
		/// </remarks>
		public static void WriteKCCPlatformReplicateData(this Writer writer, KCCPlatform.ReplicateData value)
		{
		}

		/// <summary>
		/// Reads the replicate payload, which is empty.
		/// </summary>
		public static KCCPlatform.ReplicateData ReadKCCPlatformReplicateData(this Reader reader)
		{
			return new KCCPlatform.ReplicateData();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterSerializers()
		{
			GenericWriter<KCCPlatform.ReplicateData>.SetWrite(WriteKCCPlatformReplicateData);
			GenericReader<KCCPlatform.ReplicateData>.SetRead(ReadKCCPlatformReplicateData);
			GenericDeltaWriter<KCCPlatform.ReplicateData>.SetWrite(WriteDelta);
			GenericDeltaReader<KCCPlatform.ReplicateData>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer. There are no fields to compare, so this writes nothing.
		/// </summary>
		/// <remarks>
		/// Both of FishNet's call sites — <c>WriteDeltaReplicateEntry</c> and
		/// <c>WriteDeltaReconcile</c> — discard this return value, so it steers nothing today. It
		/// reports "emitted" whenever the caller asked for an emission, which is the answer that
		/// stays correct if a future call site does start reading it: the reader consumes nothing
		/// either way, so writer and reader agree at zero bytes.
		/// </remarks>
		internal static bool WriteDelta(
			Writer writer,
			KCCPlatform.ReplicateData prev,
			KCCPlatform.ReplicateData next,
			DeltaSerializerOption option)
		{
			return option != DeltaSerializerOption.Unset;
		}

		/// <summary>
		/// Delta reader. Mirrors the writer by consuming nothing.
		/// </summary>
		internal static KCCPlatform.ReplicateData ReadDelta(
			Reader reader,
			KCCPlatform.ReplicateData prev)
		{
			return prev;
		}
	}

	/// <summary>
	/// Delta serializers for <see cref="KCCPlatform.ReconcileData"/>.
	/// </summary>
	public static class KCCPlatformReconcileDataDeltaSerializer
	{
		/// <summary>Bit flag for Position changes.</summary>
		private const byte POSITION_BIT = 1 << 0;
		/// <summary>Bit flag for GoalIndex changes.</summary>
		private const byte GOAL_INDEX_BIT = 1 << 1;

		/// <summary>Leading byte: the payload is a delta against the reader's baseline.</summary>
		private const byte MODE_DELTA = 0;
		/// <summary>Leading byte: the payload is an absolute snapshot and ignores the baseline.</summary>
		private const byte MODE_FULL_SNAPSHOT = 1;

		/// <summary>
		/// Writes every field of <see cref="KCCPlatform.ReconcileData"/>.
		/// </summary>
		/// <remarks>
		/// The tick is not written here. FishNet carries it separately, which is why the character
		/// serializers in <c>KCCPredictionDeltaSerializers</c> do not write theirs either.
		/// </remarks>
		public static void WriteKCCPlatformReconcileData(this Writer writer, KCCPlatform.ReconcileData value)
		{
			writer.WriteVector3(value.Position);
			writer.WriteUInt8Unpacked(value.GoalIndex);
		}

		/// <summary>
		/// Reads every field of <see cref="KCCPlatform.ReconcileData"/>.
		/// </summary>
		public static KCCPlatform.ReconcileData ReadKCCPlatformReconcileData(this Reader reader)
		{
			Vector3 position = reader.ReadVector3();
			byte goalIndex = reader.ReadUInt8Unpacked();

			return new KCCPlatform.ReconcileData(position, goalIndex);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterSerializers()
		{
			GenericWriter<KCCPlatform.ReconcileData>.SetWrite(WriteKCCPlatformReconcileData);
			GenericReader<KCCPlatform.ReconcileData>.SetRead(ReadKCCPlatformReconcileData);
			GenericDeltaWriter<KCCPlatform.ReconcileData>.SetWrite(WriteDelta);
			GenericDeltaReader<KCCPlatform.ReconcileData>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer: a one-byte field mask, then only the fields that changed.
		/// </summary>
		/// <remarks>
		/// A platform is the best case a per-field delta gets. It moves along one axis at a
		/// constant rate, so two of the position's three components are usually unchanged and the
		/// third moves by a small amount; the goal index changes once per leg of the route and is
		/// otherwise absent from the wire entirely.
		/// </remarks>
		internal static bool WriteDelta(
			Writer writer,
			KCCPlatform.ReconcileData prev,
			KCCPlatform.ReconcileData next,
			DeltaSerializerOption option)
		{
			/* A full serialize is written as an ABSOLUTE snapshot, not as a delta against prev.
			 *
			 * FishNet's scalar delta primitives are difference-based — WriteDifference8_16_32 writes
			 * valueB - valueA, and WriteUDeltaSingle a quantised float difference — so a payload is
			 * only decodable by a peer holding the same baseline the writer used. Forcing every
			 * field through them does not produce a self-contained payload; it just guarantees
			 * every field is present, still relative to a baseline the receiver may not have.
			 *
			 * NOTE (2026-08-28 audit): none of this reaches the wire today. With no owner and state
			 * forwarding off, Server_SendReconcileRpc returns before writing anything, so this
			 * reconcile serializer is dead code — a client advances the platform by calling
			 * KCCPlatform.Step directly from its own tick. It is kept, correct and tested, against
			 * forwarding being enabled later; the reasoning below is what it would need to do then.
			 *
			 * That matters more here than anywhere else. A platform is a scene object that starts
			 * ticking when the scene loads and never stops, so EVERY client connects to a chain
			 * already far from its starting baseline: without this branch a joining client decodes
			 * the platform at roughly the world origin and stays there, and PerformReconcile
			 * assigns transform.position directly. It also bounds the quantisation error that
			 * WriteUDeltaSingle accumulates — the writer diffs its exact values while the reader
			 * accumulates decoded ones, so without a periodic absolute resync the two drift apart
			 * for the lifetime of the scene.
			 *
			 * FishNet emits FullSerialize on the tick an observer is added and once per second
			 * thereafter (GetDeltaSerializeOption), so this doubles as the bootstrap and the repair.
			 * The mode byte is what lets ReadDelta tell the two forms apart, since delta readers
			 * receive no DeltaSerializerOption. This mirrors CharacterReconcileDataDeltaSerializer,
			 * which carries the same branch for the same reason. */
			if (option.FastContains(DeltaSerializerOption.FullSerialize))
			{
				writer.WriteUInt8Unpacked(MODE_FULL_SNAPSHOT);
				WriteKCCPlatformReconcileData(writer, next);
				return true;
			}

			writer.WriteUInt8Unpacked(MODE_DELTA);

			byte flags = 0;
			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			/* Unset, not the incoming option: these helpers emit unconditionally when handed
			 * anything else, which would put every field on the wire every tick and cost the whole
			 * saving. The flags word is what tells the reader which fields are actually present. */
			if (writer.WriteDeltaVector3(prev.Position, next.Position, DeltaSerializerOption.Unset))
			{
				flags |= POSITION_BIT;
			}

			if (writer.WriteDeltaUInt8(prev.GoalIndex, next.GoalIndex, DeltaSerializerOption.Unset))
			{
				flags |= GOAL_INDEX_BIT;
			}

			/* The mask is always written, even when nothing changed.
			 *
			 * This is a root type, not a field nested inside another struct. The nested serializers
			 * beside this one may write nothing and return false, because their parent records that
			 * in its own mask and the reader knows to skip them. Nothing plays that role here:
			 * ReadDelta unconditionally reads a mode byte and then a mask byte, so a writer that
			 * emitted zero bytes would desynchronise the stream.
			 *
			 * Today it could not happen anyway — GetDeltaSerializeOption only ever returns
			 * FullSerialize or RootSerialize, never Unset — but a correctness argument that rests
			 * on a caller's current behaviour is one upgrade away from being wrong, and the cost of
			 * not relying on it is one byte.
			 *
			 * Insert rather than seek-write-seek: the Insert* helpers are fixed width and cannot
			 * silently change size, whereas a packed backfill could overrun the placeholder and
			 * corrupt the first field written after it — see the note in
			 * CharacterTransientGroundingReportDeltaSerializer. */
			writer.InsertUInt8Unpacked(flags, flagPos);
			return true;
		}

		/// <summary>
		/// Delta reader: reads the mask and rebuilds only the fields it names.
		/// </summary>
		internal static KCCPlatform.ReconcileData ReadDelta(
			Reader reader,
			KCCPlatform.ReconcileData prev)
		{
			/* Mode first — see WriteDelta. A full snapshot is absolute and self-contained, so prev
			 * is deliberately ignored; a delta is relative to prev. */
			byte mode = reader.ReadUInt8Unpacked();
			if (mode == MODE_FULL_SNAPSHOT)
			{
				return ReadKCCPlatformReconcileData(reader);
			}
			if (mode != MODE_DELTA)
			{
				Log.Error("KCCPlatformReconcileDataDeltaSerializer",
					$"ReadDelta: unknown payload mode {mode}. The reconcile stream is corrupt; " +
					"returning the previous snapshot unchanged.");
				return prev;
			}

			byte flags = reader.ReadUInt8Unpacked();

			// Fields the mask does not name are unchanged, so they carry forward from prev.
			Vector3 position = (flags & POSITION_BIT) != 0
				? reader.ReadDeltaVector3(prev.Position)
				: prev.Position;

			byte goalIndex = (flags & GOAL_INDEX_BIT) != 0
				? reader.ReadDeltaUInt8(prev.GoalIndex)
				: prev.GoalIndex;

			return new KCCPlatform.ReconcileData(position, goalIndex);
		}
	}
}
