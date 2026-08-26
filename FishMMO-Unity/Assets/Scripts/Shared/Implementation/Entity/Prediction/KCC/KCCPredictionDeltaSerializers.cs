using FishNet.Serializing;
using KinematicCharacterController;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom delta serializers for <see cref="CharacterReplicateData"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 1-byte bitmask (7 bits for 7 fields)
	/// followed by delta-encoded values for only the changed fields.
	/// On idle ticks where the player stands still and no ability input, this sends 1 byte.
	/// On a typical walking tick, this sends ~7 bytes.
	/// </para>
	/// </summary>
	public static class CharacterReplicateDataDeltaSerializer
	{
		/// <summary>Bit flag for forward axis changes.</summary>
		private const byte FORWARD_BIT = 1 << 0;
		/// <summary>Bit flag for right axis changes.</summary>
		private const byte RIGHT_BIT = 1 << 1;
		/// <summary>Bit flag for move flags changes.</summary>
		private const byte MOVE_FLAGS_BIT = 1 << 2;
		/// <summary>Bit flag for camera position changes.</summary>
		private const byte POSITION_BIT = 1 << 3;
		/// <summary>Bit flag for camera rotation changes.</summary>
		private const byte ROTATION_BIT = 1 << 4;
		/// <summary>Bit flag for activation flags changes.</summary>
		private const byte ACTIVATION_FLAGS_BIT = 1 << 5;
		/// <summary>Bit flag for queued ability ID changes.</summary>
		private const byte QUEUED_ABILITY_BIT = 1 << 6;

		/// <summary>
		/// Registers the custom delta serializers at runtime, before any scene loads.
		/// </summary>
		/// <summary>
		/// Custom full serializer: writes all fields of <see cref="CharacterReplicateData"/>.
		/// Extension method discovered by FishNet codegen via naming convention.
		/// </summary>
		public static void WriteCharacterReplicateData(this Writer writer, CharacterReplicateData value)
		{
			writer.WriteSingle(value.MoveAxisForward);
			writer.WriteSingle(value.MoveAxisRight);
			writer.WriteInt32(value.MoveFlags);
			writer.WriteVector3(value.CameraPosition);
			writer.WriteQuaternion32(value.CameraRotation);
			writer.WriteInt32(value.ActivationFlags);
			writer.WriteInt64(value.QueuedAbilityID);
		}

		/// <summary>
		/// Custom full deserializer: reads all fields of <see cref="CharacterReplicateData"/>.
		/// </summary>
		public static CharacterReplicateData ReadCharacterReplicateData(this Reader reader)
		{
			return new CharacterReplicateData
			{
				MoveAxisForward = reader.ReadSingle(),
				MoveAxisRight = reader.ReadSingle(),
				MoveFlags = reader.ReadInt32(),
				CameraPosition = reader.ReadVector3(),
				CameraRotation = reader.ReadQuaternion32(),
				ActivationFlags = reader.ReadInt32(),
				QueuedAbilityID = reader.ReadInt64(),
			};
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterSerializers()
		{
			GenericWriter<CharacterReplicateData>.SetWrite(WriteCharacterReplicateData);
			GenericReader<CharacterReplicateData>.SetRead(ReadCharacterReplicateData);
			GenericDeltaWriter<CharacterReplicateData>.SetWrite(WriteDelta);
			GenericDeltaReader<CharacterReplicateData>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer for <see cref="CharacterReplicateData"/>. Writes a 1-byte bitmask
		/// followed by delta-encoded values for only the changed fields.
		/// </summary>
		/// <param name="writer">Writer to serialize to.</param>
		/// <param name="prev">Previous replicate data.</param>
		/// <param name="next">Current replicate data.</param>
		/// <param name="option">Delta serializer option (Unset = auto).</param>
		/// <returns>True if any data was written.</returns>
		private static bool WriteDelta(
			Writer writer,
			CharacterReplicateData prev,
			CharacterReplicateData next,
			DeltaSerializerOption option)
		{
			byte flags = 0;
			/* Two distinct questions, which this used to conflate into one `forceWrite`.
			 *
			 * `mustEmit` — any option other than Unset — means the caller needs this writer to emit
			 * SOMETHING and return true, so the reader stays aligned. It does not mean "send every
			 * field": the flags word already tells the reader which fields are present, so unchanged
			 * fields can still be skipped. This is exactly how FishNet's own composite writers behave
			 * (see Writer.WriteDeltaVector3, which emits an all-unset flags byte and returns true).
			 *
			 * `fullSerialize` means the receiver's previous value cannot be trusted — a new observer,
			 * or the periodic resend — so every field must go out regardless of whether it changed.
			 *
			 * Treating RootSerialize as "write everything" cost most of the compression: FishNet passes
			 * RootSerialize for every reconcile that is not a periodic full resend, and for every
			 * replicate entry after the first, so the common case was writing a full snapshot plus a
			 * flags word. Nested primitives are handed Unset unless this is a full serialize, for the
			 * same reason — FishNet's scalar delta writers always emit when handed a non-Unset option. */
			bool fullSerialize = option.FastContains(DeltaSerializerOption.FullSerialize);
			bool mustEmit = option != DeltaSerializerOption.Unset;
			DeltaSerializerOption fieldOption = fullSerialize ? option : DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			int startLength = writer.Length;
			writer.WriteUInt8Unpacked(0);

			if (fullSerialize || prev.MoveAxisForward != next.MoveAxisForward)
			{
				writer.WriteSingle(next.MoveAxisForward);
				flags |= FORWARD_BIT;
			}

			if (fullSerialize || prev.MoveAxisRight != next.MoveAxisRight)
			{
				writer.WriteSingle(next.MoveAxisRight);
				flags |= RIGHT_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MoveFlags, next.MoveFlags, fieldOption))
				flags |= MOVE_FLAGS_BIT;

			if (writer.WriteDeltaVector3(prev.CameraPosition, next.CameraPosition, fieldOption))
				flags |= POSITION_BIT;

			if (writer.WriteDeltaQuaternion(prev.CameraRotation, next.CameraRotation, option: fieldOption))
				flags |= ROTATION_BIT;

			if (writer.WriteDeltaInt32(prev.ActivationFlags, next.ActivationFlags, fieldOption))
				flags |= ACTIVATION_FLAGS_BIT;

			if (writer.WriteDeltaInt64(prev.QueuedAbilityID, next.QueuedAbilityID, fieldOption))
				flags |= QUEUED_ABILITY_BIT;

			if (flags != 0 || mustEmit)
			{
				/* Insert rather than seek-write-seek: the Insert* helpers are fixed width and
				 * cannot silently change size, whereas WriteUInt16 is only unpacked today
				 * because of a standing 'todo: should be using WritePackedWhole' in FishNet's
				 * Writer. A packed backfill would overrun the placeholder and corrupt the
				 * first field written after it. */
				writer.InsertUInt8Unpacked(flags, flagPos);
				return true;
			}

			/* Rewind Length as well as Position. Writer.Length only ever grows — every write
			 * does Length = Max(Length, Position) — and GetArraySegment sends 0..Length, so
			 * restoring Position alone left this placeholder's bytes inside the sent segment
			 * as trailing garbage whenever nothing was written after it. */
			writer.Position = flagPos;
			writer.Length = startLength;
			return false;
		}

		/// <summary>
		/// Delta reader for <see cref="CharacterReplicateData"/>. Reads the bitmask and
		/// reconstructs only the changed fields from their deltas.
		/// </summary>
		/// <param name="reader">Reader to deserialize from.</param>
		/// <param name="prev">Previous replicate data.</param>
		/// <returns>Reconstructed replicate data.</returns>
		private static CharacterReplicateData ReadDelta(
			Reader reader,
			CharacterReplicateData prev)
		{
			byte flags = reader.ReadUInt8Unpacked();
			CharacterReplicateData result = prev;

			if ((flags & FORWARD_BIT) != 0)
				result.MoveAxisForward = reader.ReadSingle();

			if ((flags & RIGHT_BIT) != 0)
				result.MoveAxisRight = reader.ReadSingle();

			if ((flags & MOVE_FLAGS_BIT) != 0)
				result.MoveFlags = reader.ReadDeltaInt32(prev.MoveFlags);

			if ((flags & POSITION_BIT) != 0)
				result.CameraPosition = reader.ReadDeltaVector3(prev.CameraPosition);

			if ((flags & ROTATION_BIT) != 0)
				result.CameraRotation = reader.ReadDeltaQuaternion(prev.CameraRotation);

			if ((flags & ACTIVATION_FLAGS_BIT) != 0)
				result.ActivationFlags = reader.ReadDeltaInt32(prev.ActivationFlags);

			if ((flags & QUEUED_ABILITY_BIT) != 0)
				result.QueuedAbilityID = reader.ReadDeltaInt64(prev.QueuedAbilityID);

			return result;
		}
	}

	/// <summary>
	/// Custom delta serializers for <see cref="CharacterTransientGroundingReport"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 1-byte bitmask (6 bits for 6 fields)
	/// followed by delta-encoded values for only the changed fields.
	/// When grounding status is stable and unchanged, this sends 1 byte instead of 39.
	/// </para>
	/// </summary>
	public static class CharacterTransientGroundingReportDeltaSerializer
	{
		/// <summary>Bit flag for FoundAnyGround changes.</summary>
		private const byte FOUND_GROUND_BIT = 1 << 0;
		/// <summary>Bit flag for IsStableOnGround changes.</summary>
		private const byte STABLE_BIT = 1 << 1;
		/// <summary>Bit flag for SnappingPrevented changes.</summary>
		private const byte SNAPPING_BIT = 1 << 2;
		/// <summary>Bit flag for GroundNormal changes.</summary>
		private const byte GROUND_NORMAL_BIT = 1 << 3;
		/// <summary>Bit flag for InnerGroundNormal changes.</summary>
		private const byte INNER_NORMAL_BIT = 1 << 4;
		/// <summary>Bit flag for OuterGroundNormal changes.</summary>
		private const byte OUTER_NORMAL_BIT = 1 << 5;

		/// <summary>
		/// Registers the custom delta serializers at runtime.
		/// </summary>
		/// <summary>
		/// Custom full serializer: writes all fields of <see cref="CharacterTransientGroundingReport"/>.
		/// Called from <see cref="WriteKinematicCharacterMotorState"/> for its nested GroundingStatus.
		/// </summary>
		internal static void WriteCharacterTransientGroundingReport(this Writer writer, CharacterTransientGroundingReport value)
		{
			writer.WriteBoolean(value.FoundAnyGround);
			writer.WriteBoolean(value.IsStableOnGround);
			writer.WriteBoolean(value.SnappingPrevented);
			writer.WriteVector3(value.GroundNormal);
			writer.WriteVector3(value.InnerGroundNormal);
			writer.WriteVector3(value.OuterGroundNormal);
		}

		/// <summary>
		/// Custom full deserializer: reads all fields of <see cref="CharacterTransientGroundingReport"/>.
		/// </summary>
		internal static CharacterTransientGroundingReport ReadCharacterTransientGroundingReport(this Reader reader)
		{
			return new CharacterTransientGroundingReport
			{
				FoundAnyGround = reader.ReadBoolean(),
				IsStableOnGround = reader.ReadBoolean(),
				SnappingPrevented = reader.ReadBoolean(),
				GroundNormal = reader.ReadVector3(),
				InnerGroundNormal = reader.ReadVector3(),
				OuterGroundNormal = reader.ReadVector3(),
			};
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterSerializers()
		{
			GenericWriter<CharacterTransientGroundingReport>.SetWrite(CharacterTransientGroundingReportDeltaSerializer.WriteCharacterTransientGroundingReport);
			GenericReader<CharacterTransientGroundingReport>.SetRead(CharacterTransientGroundingReportDeltaSerializer.ReadCharacterTransientGroundingReport);
			GenericDeltaWriter<CharacterTransientGroundingReport>.SetWrite(WriteDelta);
			GenericDeltaReader<CharacterTransientGroundingReport>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer for <see cref="CharacterTransientGroundingReport"/>.
		/// Writes a 1-byte bitmask indicating which of the 6 fields changed,
		/// followed by delta-encoded values for only those fields.
		/// </summary>
		internal static bool WriteDelta(
			Writer writer,
			CharacterTransientGroundingReport prev,
			CharacterTransientGroundingReport next,
			DeltaSerializerOption option)
		{
			byte flags = 0;
			// See CharacterReplicateDataDeltaSerializer.WriteDelta for why these are three separate
			// values rather than one forceWrite.
			bool fullSerialize = option.FastContains(DeltaSerializerOption.FullSerialize);
			bool mustEmit = option != DeltaSerializerOption.Unset;
			DeltaSerializerOption fieldOption = fullSerialize ? option : DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			int startLength = writer.Length;
			writer.WriteUInt8Unpacked(0);

			/* Booleans are written and read explicitly rather than through FishNet's
			 * Writer.WriteDeltaBoolean / Reader.ReadDeltaBoolean pair, which is not symmetric:
			 * WriteDeltaBoolean calls WriteBoolean and so emits a byte, while ReadDeltaBoolean is
			 * `return !valueA;` and consumes nothing. Every boolean routed through that pair
			 * therefore left a stray byte in the stream and, on a forced serialize where the
			 * writer emits a value equal to the previous one, handed back the inverse of the
			 * correct answer. Both faults are in the vendored FishNet, so they are avoided here
			 * rather than patched there — a plugin fix would be lost on the next upgrade. */
			if (fullSerialize || prev.FoundAnyGround != next.FoundAnyGround)
			{
				writer.WriteBoolean(next.FoundAnyGround);
				flags |= FOUND_GROUND_BIT;
			}

			if (fullSerialize || prev.IsStableOnGround != next.IsStableOnGround)
			{
				writer.WriteBoolean(next.IsStableOnGround);
				flags |= STABLE_BIT;
			}

			if (fullSerialize || prev.SnappingPrevented != next.SnappingPrevented)
			{
				writer.WriteBoolean(next.SnappingPrevented);
				flags |= SNAPPING_BIT;
			}

			if (writer.WriteDeltaVector3(prev.GroundNormal, next.GroundNormal, fieldOption))
				flags |= GROUND_NORMAL_BIT;

			if (writer.WriteDeltaVector3(prev.InnerGroundNormal, next.InnerGroundNormal, fieldOption))
				flags |= INNER_NORMAL_BIT;

			if (writer.WriteDeltaVector3(prev.OuterGroundNormal, next.OuterGroundNormal, fieldOption))
				flags |= OUTER_NORMAL_BIT;

			if (flags != 0 || mustEmit)
			{
				/* Insert rather than seek-write-seek: the Insert* helpers are fixed width and
				 * cannot silently change size, whereas WriteUInt16 is only unpacked today
				 * because of a standing 'todo: should be using WritePackedWhole' in FishNet's
				 * Writer. A packed backfill would overrun the placeholder and corrupt the
				 * first field written after it. */
				writer.InsertUInt8Unpacked(flags, flagPos);
				return true;
			}

			/* Rewind Length as well as Position. Writer.Length only ever grows — every write
			 * does Length = Max(Length, Position) — and GetArraySegment sends 0..Length, so
			 * restoring Position alone left this placeholder's bytes inside the sent segment
			 * as trailing garbage whenever nothing was written after it. */
			writer.Position = flagPos;
			writer.Length = startLength;
			return false;
		}

		/// <summary>
		/// Delta reader for <see cref="CharacterTransientGroundingReport"/>.
		/// Reads the bitmask and reconstructs only the changed fields.
		/// </summary>
		internal static CharacterTransientGroundingReport ReadDelta(
			Reader reader,
			CharacterTransientGroundingReport prev)
		{
			byte flags = reader.ReadUInt8Unpacked();
			CharacterTransientGroundingReport result = prev;

			if ((flags & FOUND_GROUND_BIT) != 0)
				result.FoundAnyGround = reader.ReadBoolean();

			if ((flags & STABLE_BIT) != 0)
				result.IsStableOnGround = reader.ReadBoolean();

			if ((flags & SNAPPING_BIT) != 0)
				result.SnappingPrevented = reader.ReadBoolean();

			if ((flags & GROUND_NORMAL_BIT) != 0)
				result.GroundNormal = reader.ReadDeltaVector3(prev.GroundNormal);

			if ((flags & INNER_NORMAL_BIT) != 0)
				result.InnerGroundNormal = reader.ReadDeltaVector3(prev.InnerGroundNormal);

			if ((flags & OUTER_NORMAL_BIT) != 0)
				result.OuterGroundNormal = reader.ReadDeltaVector3(prev.OuterGroundNormal);

			return result;
		}
	}

	/// <summary>
	/// Custom delta serializers for <see cref="KinematicCharacterMotorState"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 2-byte bitmask (14 bits for 14 fields)
	/// followed by delta-encoded values for only the changed fields.
	/// On a typical grounded walking tick, only Position, Rotation, BaseVelocity, and
	/// GroundingStatus change — sending ~25 bytes instead of ~127.
	/// On an idle tick where the character stands still, this sends 2 bytes.
	/// </para>
	/// <para>
	/// The nested <see cref="CharacterTransientGroundingReport"/> uses its own
	/// delta serializer, so savings compound for unchanged grounding normals.
	/// </para>
	/// </summary>
	public static class KinematicCharacterMotorStateDeltaSerializer
	{
		/// <summary>Bit flag for Position changes.</summary>
		private const ushort POSITION_BIT = 1 << 0;
		/// <summary>Bit flag for Rotation changes.</summary>
		private const ushort ROTATION_BIT = 1 << 1;
		/// <summary>Bit flag for BaseVelocity changes.</summary>
		private const ushort VELOCITY_BIT = 1 << 2;
		/// <summary>Bit flag for CurrentPlatformID changes.</summary>
		private const ushort PLATFORM_ID_BIT = 1 << 3;
		/// <summary>Bit flag for LastPlatformPosition changes.</summary>
		private const ushort LAST_PLATFORM_POS_BIT = 1 << 4;
		/// <summary>Bit flag for MustUnground changes.</summary>
		private const ushort MUST_UNGROUND_BIT = 1 << 5;
		/// <summary>Bit flag for MustUngroundTime changes.</summary>
		private const ushort MUST_UNGROUND_TIME_BIT = 1 << 6;
		/// <summary>Bit flag for LastMovementIterationFoundAnyGround changes.</summary>
		private const ushort LAST_FOUND_GROUND_BIT = 1 << 7;
		/// <summary>Bit flag for GroundingStatus changes.</summary>
		private const ushort GROUNDING_BIT = 1 << 8;
		/// <summary>Bit flag for AttachedRigidbodyVelocity changes.</summary>
		private const ushort ATTACHED_RB_VEL_BIT = 1 << 9;
		/// <summary>Bit flag for IsCrouching changes.</summary>
		private const ushort IS_CROUCHING_BIT = 1 << 10;
		/// <summary>Bit flag for JumpRequested changes.</summary>
		private const ushort JUMP_REQUESTED_BIT = 1 << 11;
		/// <summary>Bit flag for TimeSinceLastAbleToJump changes.</summary>
		private const ushort TIME_SINCE_JUMP_BIT = 1 << 12;
		/// <summary>Bit flag for TimeSinceJumpRequested changes.</summary>
		private const ushort TIME_SINCE_JUMP_REQ_BIT = 1 << 13;

		/// <summary>
		/// Registers the custom delta serializers at runtime.
		/// </summary>
		/// <summary>
		/// Custom full serializer: writes all fields of <see cref="KinematicCharacterMotorState"/>.
		/// Nested <see cref="CharacterTransientGroundingReport"/> uses its full serializer.
		/// Extension method discovered by FishNet codegen via naming convention.
		/// </summary>
		public static void WriteKinematicCharacterMotorState(this Writer writer, KinematicCharacterMotorState value)
		{
			writer.WriteVector3(value.Position);
			writer.WriteQuaternion32(value.Rotation);
			writer.WriteVector3(value.BaseVelocity);
			writer.WriteInt64(value.CurrentPlatformID);
			writer.WriteVector3(value.LastPlatformPosition);
			writer.WriteBoolean(value.MustUnground);
			writer.WriteSingle(value.MustUngroundTime);
			writer.WriteBoolean(value.LastMovementIterationFoundAnyGround);
			CharacterTransientGroundingReportDeltaSerializer.WriteCharacterTransientGroundingReport(writer, value.GroundingStatus);
			writer.WriteVector3(value.AttachedRigidbodyVelocity);
			writer.WriteBoolean(value.IsCrouching);
			writer.WriteBoolean(value.JumpRequested);
			writer.WriteSingle(value.TimeSinceLastAbleToJump);
			writer.WriteSingle(value.TimeSinceJumpRequested);
		}

		/// <summary>
		/// Custom full deserializer: reads all fields of <see cref="KinematicCharacterMotorState"/>.
		/// </summary>
		public static KinematicCharacterMotorState ReadKinematicCharacterMotorState(this Reader reader)
		{
			return new KinematicCharacterMotorState
			{
				Position = reader.ReadVector3(),
				Rotation = reader.ReadQuaternion32(),
				BaseVelocity = reader.ReadVector3(),
				CurrentPlatformID = reader.ReadInt64(),
				LastPlatformPosition = reader.ReadVector3(),
				MustUnground = reader.ReadBoolean(),
				MustUngroundTime = reader.ReadSingle(),
				LastMovementIterationFoundAnyGround = reader.ReadBoolean(),
				GroundingStatus = CharacterTransientGroundingReportDeltaSerializer.ReadCharacterTransientGroundingReport(reader),
				AttachedRigidbodyVelocity = reader.ReadVector3(),
				IsCrouching = reader.ReadBoolean(),
				JumpRequested = reader.ReadBoolean(),
				TimeSinceLastAbleToJump = reader.ReadSingle(),
				TimeSinceJumpRequested = reader.ReadSingle(),
			};
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterSerializers()
		{
			GenericWriter<KinematicCharacterMotorState>.SetWrite(WriteKinematicCharacterMotorState);
			GenericReader<KinematicCharacterMotorState>.SetRead(ReadKinematicCharacterMotorState);
			GenericDeltaWriter<KinematicCharacterMotorState>.SetWrite(WriteDelta);
			GenericDeltaReader<KinematicCharacterMotorState>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer for <see cref="KinematicCharacterMotorState"/>.
		/// Writes a 2-byte bitmask indicating which of the 14 fields changed,
		/// followed by delta-encoded values for only those fields.
		/// </summary>
		/// <summary>
		/// Delta writer for <see cref="CharacterReplicateData"/>. Writes a 1-byte bitmask
		/// followed by delta-encoded values for only the changed fields.
		/// </summary>
		/// <param name="writer">Writer to serialize to.</param>
		/// <param name="prev">Previous replicate data.</param>
		/// <param name="next">Current replicate data.</param>
		/// <param name="option">Delta serializer option (Unset = auto).</param>
		/// <returns>True if any data was written.</returns>
		private static bool WriteDelta(
			Writer writer,
			KinematicCharacterMotorState prev,
			KinematicCharacterMotorState next,
			DeltaSerializerOption option)
		{
			ushort flags = 0;
			// See CharacterReplicateDataDeltaSerializer.WriteDelta for why these are three separate
			// values rather than one forceWrite.
			bool fullSerialize = option.FastContains(DeltaSerializerOption.FullSerialize);
			bool mustEmit = option != DeltaSerializerOption.Unset;
			DeltaSerializerOption fieldOption = fullSerialize ? option : DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			int startLength = writer.Length;
			writer.WriteUInt16(0);

			if (writer.WriteDeltaVector3(prev.Position, next.Position, fieldOption))
				flags |= POSITION_BIT;

			if (writer.WriteDeltaQuaternion(prev.Rotation, next.Rotation, option: fieldOption))
				flags |= ROTATION_BIT;

			if (writer.WriteDeltaVector3(prev.BaseVelocity, next.BaseVelocity, fieldOption))
				flags |= VELOCITY_BIT;

			if (writer.WriteDeltaInt64(prev.CurrentPlatformID, next.CurrentPlatformID, fieldOption))
				flags |= PLATFORM_ID_BIT;

			if (writer.WriteDeltaVector3(prev.LastPlatformPosition, next.LastPlatformPosition, fieldOption))
				flags |= LAST_PLATFORM_POS_BIT;

			// Booleans written explicitly — see the note in
			// CharacterTransientGroundingReportDeltaSerializer.WriteDelta.
			if (fullSerialize || prev.MustUnground != next.MustUnground)
			{
				writer.WriteBoolean(next.MustUnground);
				flags |= MUST_UNGROUND_BIT;
			}

			if (fullSerialize || prev.MustUngroundTime != next.MustUngroundTime)
			{
				writer.WriteSingle(next.MustUngroundTime);
				flags |= MUST_UNGROUND_TIME_BIT;
			}

			if (fullSerialize || prev.LastMovementIterationFoundAnyGround != next.LastMovementIterationFoundAnyGround)
			{
				writer.WriteBoolean(next.LastMovementIterationFoundAnyGround);
				flags |= LAST_FOUND_GROUND_BIT;
			}

			if (CharacterTransientGroundingReportDeltaSerializer.WriteDelta(writer, prev.GroundingStatus, next.GroundingStatus, fieldOption))
				flags |= GROUNDING_BIT;

			if (writer.WriteDeltaVector3(prev.AttachedRigidbodyVelocity, next.AttachedRigidbodyVelocity, fieldOption))
				flags |= ATTACHED_RB_VEL_BIT;

			if (fullSerialize || prev.IsCrouching != next.IsCrouching)
			{
				writer.WriteBoolean(next.IsCrouching);
				flags |= IS_CROUCHING_BIT;
			}

			if (fullSerialize || prev.JumpRequested != next.JumpRequested)
			{
				writer.WriteBoolean(next.JumpRequested);
				flags |= JUMP_REQUESTED_BIT;
			}

			if (fullSerialize || prev.TimeSinceLastAbleToJump != next.TimeSinceLastAbleToJump)
			{
				writer.WriteSingle(next.TimeSinceLastAbleToJump);
				flags |= TIME_SINCE_JUMP_BIT;
			}

			if (fullSerialize || prev.TimeSinceJumpRequested != next.TimeSinceJumpRequested)
			{
				writer.WriteSingle(next.TimeSinceJumpRequested);
				flags |= TIME_SINCE_JUMP_REQ_BIT;
			}

			if (flags != 0 || mustEmit)
			{
				/* Insert rather than seek-write-seek: the Insert* helpers are fixed width and
				 * cannot silently change size, whereas WriteUInt16 is only unpacked today
				 * because of a standing 'todo: should be using WritePackedWhole' in FishNet's
				 * Writer. A packed backfill would overrun the placeholder and corrupt the
				 * first field written after it. */
				writer.InsertUInt16Unpacked(flags, flagPos);
				return true;
			}

			/* Rewind Length as well as Position. Writer.Length only ever grows — every write
			 * does Length = Max(Length, Position) — and GetArraySegment sends 0..Length, so
			 * restoring Position alone left this placeholder's bytes inside the sent segment
			 * as trailing garbage whenever nothing was written after it. */
			writer.Position = flagPos;
			writer.Length = startLength;
			return false;
		}

		/// <summary>
		/// Delta reader for <see cref="KinematicCharacterMotorState"/>.
		/// Reads the bitmask and reconstructs only the changed fields from their deltas,
		/// carrying forward unchanged fields from the previous value.
		/// </summary>
		/// <summary>
		/// Delta reader for <see cref="KinematicCharacterMotorState"/>. Reads the bitmask and
		/// reconstructs only the changed fields from their deltas.
		/// </summary>
		/// <param name="reader">Reader to deserialize from.</param>
		/// <param name="prev">Previous motor state.</param>
		/// <returns>Reconstructed motor state.</returns>
		private static KinematicCharacterMotorState ReadDelta(
			Reader reader,
			KinematicCharacterMotorState prev)
		{
			ushort flags = reader.ReadUInt16();
			KinematicCharacterMotorState result = prev;

			if ((flags & POSITION_BIT) != 0)
				result.Position = reader.ReadDeltaVector3(prev.Position);

			if ((flags & ROTATION_BIT) != 0)
				result.Rotation = reader.ReadDeltaQuaternion(prev.Rotation);

			if ((flags & VELOCITY_BIT) != 0)
				result.BaseVelocity = reader.ReadDeltaVector3(prev.BaseVelocity);

			if ((flags & PLATFORM_ID_BIT) != 0)
				result.CurrentPlatformID = reader.ReadDeltaInt64(prev.CurrentPlatformID);

			if ((flags & LAST_PLATFORM_POS_BIT) != 0)
				result.LastPlatformPosition = reader.ReadDeltaVector3(prev.LastPlatformPosition);

			if ((flags & MUST_UNGROUND_BIT) != 0)
				result.MustUnground = reader.ReadBoolean();

			if ((flags & MUST_UNGROUND_TIME_BIT) != 0)
				result.MustUngroundTime = reader.ReadSingle();

			if ((flags & LAST_FOUND_GROUND_BIT) != 0)
				result.LastMovementIterationFoundAnyGround = reader.ReadBoolean();

			if ((flags & GROUNDING_BIT) != 0)
				result.GroundingStatus = CharacterTransientGroundingReportDeltaSerializer.ReadDelta(reader, prev.GroundingStatus);

			if ((flags & ATTACHED_RB_VEL_BIT) != 0)
				result.AttachedRigidbodyVelocity = reader.ReadDeltaVector3(prev.AttachedRigidbodyVelocity);

			if ((flags & IS_CROUCHING_BIT) != 0)
				result.IsCrouching = reader.ReadBoolean();

			if ((flags & JUMP_REQUESTED_BIT) != 0)
				result.JumpRequested = reader.ReadBoolean();

			if ((flags & TIME_SINCE_JUMP_BIT) != 0)
				result.TimeSinceLastAbleToJump = reader.ReadSingle();

			if ((flags & TIME_SINCE_JUMP_REQ_BIT) != 0)
				result.TimeSinceJumpRequested = reader.ReadSingle();

			return result;
		}
	}
}