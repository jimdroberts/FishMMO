using FishNet.Managing;
using FishNet.Serializing;
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
			byte flags = 0;

			/* fullSerialize forces every field onto the wire; fieldOption is what the per-field
			 * helpers are handed. There is deliberately no "emit nothing" path here — see below. */
			bool fullSerialize = option.FastContains(DeltaSerializerOption.FullSerialize);
			DeltaSerializerOption fieldOption = fullSerialize ? option : DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			if (writer.WriteDeltaVector3(prev.Position, next.Position, fieldOption))
			{
				flags |= POSITION_BIT;
			}

			if (writer.WriteDeltaUInt8(prev.GoalIndex, next.GoalIndex, fieldOption))
			{
				flags |= GOAL_INDEX_BIT;
			}

			/* The mask is always written, even when nothing changed.
			 *
			 * This is a root type, not a field nested inside another struct. The nested serializers
			 * beside this one may write nothing and return false, because their parent records that
			 * in its own mask and the reader knows to skip them. Nothing plays that role here:
			 * ReadDelta unconditionally reads a mask byte, so a writer that emitted zero bytes
			 * would desynchronise the stream.
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
