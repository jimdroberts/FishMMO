using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Delta serializers for <see cref="KCCPlatform.ReplicateData"/>.
	/// </summary>
	/// <remarks>
	/// On the 4.6.12 fork a type without these logged <c>"Write delta method not found"</c> on
	/// every tick it serialized one — 9,627 errors with stack traces in a four-minute session,
	/// because a platform ticks whether or not anybody is near it. FishNet's delta prediction now
	/// writes a type without both delta serializers with its regular serializer instead, so they are
	/// no longer what stops an error stream; they are kept so the platform's replicate packet takes
	/// the same delta path as every other prediction type, and DeltaSerializerRegistrationTests
	/// can keep requiring a delta pair for every <c>[Replicate]</c>/<c>[Reconcile]</c> payload.
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
		/// The one deliberate exception to "always write something at RootSerialize". That rule
		/// exists so FishNet's unconditional <c>ReadDelta</c> at the root stays aligned with the
		/// writer, and a payload of zero bytes meets it trivially: the reader consumes nothing
		/// either, whatever its baseline. FishNet's call site (<c>WriteDeltaReplicateDataContainer</c>)
		/// discards the return value; it reports "emitted" whenever the caller asked for an emission,
		/// the answer that stays correct if a future call site starts reading it.
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
			/* FishNet writes the platform's FULL reconciles — the first, one per observer added, and
			 * one at least every second — with the regular serializer above, and every other one
			 * through here as a delta against the last full reconcile as readers decoded it. That is
			 * what the fork's mode byte and chain sequence used to provide: a client connecting to a
			 * platform that has ticked since scene load (every client) is bootstrapped by the full
			 * reconcile sent when it was added as an observer, a delta against a full reconcile the
			 * client lost is discarded by FishNet rather than decoded against the wrong baseline, and
			 * WriteDeltaVector3's quantisation cannot accumulate because no delta builds on another.
			 * This is the one reconcile written once and sent to EVERY observer (since issue #228 the
			 * platform forwards state), so those properties matter more here than anywhere else.
			 *
			 * FullSerialize, which FishNet no longer passes here, forces both fields out — still
			 * relative to prev, since the primitives are difference-based. */
			DeltaSerializerOption fieldOption = option.FastContains(DeltaSerializerOption.FullSerialize)
				? option
				: DeltaSerializerOption.Unset;

			byte flags = 0;
			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			/* Unset unless forced: these helpers emit unconditionally when handed anything else,
			 * which would put every field on the wire every tick and cost the whole saving. The flags
			 * word is what tells the reader which fields are actually present. */
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
			 * ReadDelta unconditionally reads a mask byte, so a writer that emitted zero bytes would
			 * desynchronise the stream.
			 *
			 * Today it could not happen anyway — FishNet only ever passes RootSerialize here — but a
			 * correctness argument that rests on a caller's current behaviour is one upgrade away
			 * from being wrong, and the cost of not relying on it is one byte.
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
		/// <remarks>
		/// Consumes the same bytes whatever <paramref name="prev"/> is — the mask alone decides what
		/// is read — because FishNet decodes, then discards, a delta against a full reconcile this
		/// client does not hold.
		/// </remarks>
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
