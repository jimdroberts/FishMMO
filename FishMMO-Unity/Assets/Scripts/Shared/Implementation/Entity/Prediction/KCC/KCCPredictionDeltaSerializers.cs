using FishNet.Serializing;
using KinematicCharacterController;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom delta serializers for <see cref="CharacterReplicateData"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 1-byte bitmask followed by delta-encoded values for only
	/// the changed fields. All eight bits are in use — bit 3, once a retired gap, now carries the
	/// equipment request pair, see the constants.
	/// <para>
	/// An idle tick costs the bitmask alone. For real figures rather than an estimate, run
	/// <c>PredictionBandwidthBenchmarkTests</c>, which measures this type against the production
	/// serializers; byte counts written into a comment go stale the first time a field is added.
	/// </para>
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
		/// <summary>Bit flag for the equipment request pair (packed request + source index).</summary>
		/* Bit 3 carried CameraPosition until the aim origin was derived from the motor (see
		 * CharacterAimOrigin) and sat as a gap for a while. It is live again: the byte was full, and
		 * widening the bitmask to a ushort would have cost every replicate a byte to carry a field
		 * that is non-zero on perhaps one tick in a thousand. Both peers ship together, so there is
		 * no build in the field that could read this bit as the old position. */
		private const byte EQUIPMENT_BIT = 1 << 3;
		/// <summary>Bit flag for aim direction changes.</summary>
		private const byte AIM_DIRECTION_BIT = 1 << 4;
		/// <summary>Bit flag for activation flags changes.</summary>
		private const byte ACTIVATION_FLAGS_BIT = 1 << 5;
		/// <summary>Bit flag for queued ability ID changes.</summary>
		private const byte QUEUED_ABILITY_BIT = 1 << 6;
		/// <summary>Bit flag for the client's view-offset changes.</summary>
		private const byte VIEW_OFFSET_BIT = 1 << 7;

		/// <summary>
		/// Custom full serializer: writes all fields of <see cref="CharacterReplicateData"/>.
		/// Extension method discovered by FishNet codegen via naming convention.
		/// </summary>
		public static void WriteCharacterReplicateData(this Writer writer, CharacterReplicateData value)
		{
			// One byte each — see MoveAxisCompression. These are input axes in [-1,1], and the pair
			// is ClampMagnitude'd on read, so 1/127 is finer than anything downstream can use.
			writer.WriteInt8Unpacked(MoveAxisCompression.Encode(value.MoveAxisForward));
			writer.WriteInt8Unpacked(MoveAxisCompression.Encode(value.MoveAxisRight));
			writer.WriteInt32(value.MoveFlags);
			// Two bytes: how far behind present this client renders its peers, whole ticks plus a
			// 1/256 remainder, for lag compensation.
			writer.WriteUInt8Unpacked(value.ViewOffsetTicks);
			writer.WriteUInt8Unpacked(value.ViewOffsetFraction);
			// Packed yaw/pitch rather than a quaternion — see AimDirectionCompression for why the
			// wire format has to be one the producer can commit to exactly.
			writer.WriteUInt32Unpacked(AimDirectionCompression.Encode(value.AimDirection));
			writer.WriteInt32(value.ActivationFlags);
			writer.WriteInt64(value.QueuedAbilityID);
			// The equipment request: one packed byte, plus the source index for an equip. Written
			// whole; both are small fixed-width values and a difference could only ever match.
			writer.WriteUInt8Unpacked(value.EquipmentRequest);
			writer.WriteInt16(value.EquipmentIndex);
		}

		/// <summary>
		/// Custom full deserializer: reads all fields of <see cref="CharacterReplicateData"/>.
		/// </summary>
		public static CharacterReplicateData ReadCharacterReplicateData(this Reader reader)
		{
			return new CharacterReplicateData
			{
				MoveAxisForward = MoveAxisCompression.Decode(reader.ReadInt8Unpacked()),
				MoveAxisRight = MoveAxisCompression.Decode(reader.ReadInt8Unpacked()),
				MoveFlags = reader.ReadInt32(),
				ViewOffsetTicks = reader.ReadUInt8Unpacked(),
				ViewOffsetFraction = reader.ReadUInt8Unpacked(),
				AimDirection = AimDirectionCompression.Decode(reader.ReadUInt32Unpacked()),
				ActivationFlags = reader.ReadInt32(),
				QueuedAbilityID = reader.ReadInt64(),
				EquipmentRequest = reader.ReadUInt8Unpacked(),
				EquipmentIndex = reader.ReadInt16(),
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

			/* Written whole rather than delta'd: the packed axis is already a single byte, so a
			 * varint difference could only ever match it and would cost a branch to decide. */
			if (fullSerialize || prev.MoveAxisForward != next.MoveAxisForward)
			{
				writer.WriteInt8Unpacked(MoveAxisCompression.Encode(next.MoveAxisForward));
				flags |= FORWARD_BIT;
			}

			if (fullSerialize || prev.MoveAxisRight != next.MoveAxisRight)
			{
				writer.WriteInt8Unpacked(MoveAxisCompression.Encode(next.MoveAxisRight));
				flags |= RIGHT_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MoveFlags, next.MoveFlags, fieldOption))
				flags |= MOVE_FLAGS_BIT;

			/* Written explicitly rather than through a delta primitive: it is one byte, and the
			 * broken WriteDeltaBoolean/ReadDeltaBoolean pair is a standing reminder that a
			 * primitive whose reader consumes a different number of bytes than its writer emits is
			 * indistinguishable from a working one until the stream is already misaligned. */
			if (fullSerialize ||
				prev.ViewOffsetTicks != next.ViewOffsetTicks ||
				prev.ViewOffsetFraction != next.ViewOffsetFraction)
			{
				writer.WriteUInt8Unpacked(next.ViewOffsetTicks);
				writer.WriteUInt8Unpacked(next.ViewOffsetFraction);
				flags |= VIEW_OFFSET_BIT;
			}

			/* Delta the PACKED aim rather than the decoded vector. Yaw and pitch move in small
			 * steps while a player turns, so the packed difference varint-packs to a byte or two
			 * where three floats could not. Decoding is exact on both sides because the producer
			 * already quantised the value. */
			if (writer.WriteDeltaUInt32(AimDirectionCompression.Encode(prev.AimDirection),
					AimDirectionCompression.Encode(next.AimDirection), fieldOption))
				flags |= AIM_DIRECTION_BIT;

			if (writer.WriteDeltaInt32(prev.ActivationFlags, next.ActivationFlags, fieldOption))
				flags |= ACTIVATION_FLAGS_BIT;

			if (writer.WriteDeltaInt64(prev.QueuedAbilityID, next.QueuedAbilityID, fieldOption))
				flags |= QUEUED_ABILITY_BIT;

			/* Written explicitly, as a pair, for the same reason the view offset is: three fixed
			 * bytes on the rare tick that carries a request, nothing on every other tick. */
			if (fullSerialize ||
				prev.EquipmentRequest != next.EquipmentRequest ||
				prev.EquipmentIndex != next.EquipmentIndex)
			{
				writer.WriteUInt8Unpacked(next.EquipmentRequest);
				writer.WriteInt16(next.EquipmentIndex);
				flags |= EQUIPMENT_BIT;
			}

			if (flags != 0 || mustEmit)
			{
				/* Insert rather than seek-write-seek: the Insert* helpers are fixed width and
				 * cannot silently change size. The placeholder here is a single byte written by
				 * WriteUInt8Unpacked and backfilled by InsertUInt8Unpacked; a packed backfill of a
				 * wider placeholder could overrun it and corrupt the first field written after it,
				 * which is why the fixed-width Insert helpers are used throughout. */
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
				result.MoveAxisForward = MoveAxisCompression.Decode(reader.ReadInt8Unpacked());

			if ((flags & RIGHT_BIT) != 0)
				result.MoveAxisRight = MoveAxisCompression.Decode(reader.ReadInt8Unpacked());

			if ((flags & MOVE_FLAGS_BIT) != 0)
				result.MoveFlags = reader.ReadDeltaInt32(prev.MoveFlags);

			if ((flags & VIEW_OFFSET_BIT) != 0)
			{
				result.ViewOffsetTicks = reader.ReadUInt8Unpacked();
				result.ViewOffsetFraction = reader.ReadUInt8Unpacked();
			}

			if ((flags & AIM_DIRECTION_BIT) != 0)
				result.AimDirection = AimDirectionCompression.Decode(
					reader.ReadDeltaUInt32(AimDirectionCompression.Encode(prev.AimDirection)));

			if ((flags & ACTIVATION_FLAGS_BIT) != 0)
				result.ActivationFlags = reader.ReadDeltaInt32(prev.ActivationFlags);

			if ((flags & QUEUED_ABILITY_BIT) != 0)
				result.QueuedAbilityID = reader.ReadDeltaInt64(prev.QueuedAbilityID);

			if ((flags & EQUIPMENT_BIT) != 0)
			{
				result.EquipmentRequest = reader.ReadUInt8Unpacked();
				result.EquipmentIndex = reader.ReadInt16();
			}

			return result;
		}
	}

	/// <summary>
	/// Custom delta serializers for <see cref="CharacterTransientGroundingReport"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 1-byte bitmask (6 bits for 6 fields)
	/// followed by delta-encoded values for only the changed fields.
	/// When the grounding status is stable and unchanged this writer declines entirely and sends
	/// NOTHING — the parent's flags word records its absence. It only emits a bitmask when something
	/// changed, or when a caller forces it, and nothing forces it today: the root reconcile routes
	/// FullSerialize through its own absolute path and passes Unset down here.
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
		/// Custom full serializer: writes all fields of <see cref="CharacterTransientGroundingReport"/>.
		/// Called from <see cref="WriteKinematicCharacterMotorState"/> for its nested GroundingStatus.
		/// </summary>
		internal static void WriteCharacterTransientGroundingReport(this Writer writer, CharacterTransientGroundingReport value)
		{
			writer.WriteBoolean(value.FoundAnyGround);
			writer.WriteBoolean(value.IsStableOnGround);
			writer.WriteBoolean(value.SnappingPrevented);
			/* Normals are only written while grounded.
			 *
			 * An ungrounded status carries three zero normals — but NOT by construction. The motor
			 * assigns a fresh report on every ground probe and immediately seeds its GroundNormal
			 * with the character's up vector, so an airborne motor holds (0,1,0) there; it is
			 * KCCController.GetState that zeroes all three when FoundAnyGround is false, precisely
			 * so the value this writer diffs against is the value the reader reconstructs. Do not
			 * remove that without changing this. Skipping them also removes the one case where delta-encoding
			 * this struct cost more than writing it whole: leaving the ground used to change all
			 * three normals at once, which is the worst input a per-field delta can be handed.
			 *
			 * Packed as directions, not Vector3s. All three are unit normals, so two thirds of a
			 * Vector3's twelve bytes encode a magnitude that is always one. The packed form is
			 * four bytes at ~0.0055 degrees, which is finer than the Vector3 DELTA path managed
			 * anyway — FishNet quantises those components to 0.001, about 0.1 degrees on a unit
			 * vector — so this is more precise than what it replaces, not less.
			 *
			 * Precision here feeds slope classification against MaxStableSlopeAngle (60 degrees on
			 * the player prefab); a hundredth of a degree cannot move a character across that line.
			 * These normals are also re-derived by the motor on its next ground probe, so any error
			 * is transient rather than accumulating. */
			if (value.FoundAnyGround)
			{
				writer.WriteUInt32Unpacked(AimDirectionCompression.Encode(value.GroundNormal));
				writer.WriteUInt32Unpacked(AimDirectionCompression.Encode(value.InnerGroundNormal));
				writer.WriteUInt32Unpacked(AimDirectionCompression.Encode(value.OuterGroundNormal));
			}
		}

		/// <summary>
		/// Custom full deserializer: reads all fields of <see cref="CharacterTransientGroundingReport"/>.
		/// </summary>
		internal static CharacterTransientGroundingReport ReadCharacterTransientGroundingReport(this Reader reader)
		{
			CharacterTransientGroundingReport result = new CharacterTransientGroundingReport
			{
				FoundAnyGround = reader.ReadBoolean(),
				IsStableOnGround = reader.ReadBoolean(),
				SnappingPrevented = reader.ReadBoolean(),
			};

			// Mirrors the writer: normals are on the wire only while grounded, and an ungrounded
			// snapshot is all-zero normals — see the note in the writer, and KCCController.GetState,
			// which is what makes that true.
			if (result.FoundAnyGround)
			{
				result.GroundNormal = AimDirectionCompression.Decode(reader.ReadUInt32Unpacked());
				result.InnerGroundNormal = AimDirectionCompression.Decode(reader.ReadUInt32Unpacked());
				result.OuterGroundNormal = AimDirectionCompression.Decode(reader.ReadUInt32Unpacked());
			}

			return result;
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

			/* Delta the PACKED normals, and only while grounded.
			 *
			 * Yaw and pitch creep in small steps as a character walks across a slope, so the packed
			 * difference varint-packs to a byte or two where three float components could not. An
			 * ungrounded snapshot has three zero normals because KCCController.GetState zeroes them
			 * (the motor itself seeds GroundNormal with the character's up vector), so skipping them
			 * there costs nothing and removes the worst input a per-field delta can be handed — all
			 * three changing at once as the character leaves the ground. The reader zeroes them from
			 * the FoundAnyGround flag instead, which matches the baseline the writer diffed against
			 * only because the producer normalised it. */
			if (next.FoundAnyGround)
			{
				if (writer.WriteDeltaUInt32(AimDirectionCompression.Encode(prev.GroundNormal),
						AimDirectionCompression.Encode(next.GroundNormal), fieldOption))
					flags |= GROUND_NORMAL_BIT;

				if (writer.WriteDeltaUInt32(AimDirectionCompression.Encode(prev.InnerGroundNormal),
						AimDirectionCompression.Encode(next.InnerGroundNormal), fieldOption))
					flags |= INNER_NORMAL_BIT;

				if (writer.WriteDeltaUInt32(AimDirectionCompression.Encode(prev.OuterGroundNormal),
						AimDirectionCompression.Encode(next.OuterGroundNormal), fieldOption))
					flags |= OUTER_NORMAL_BIT;
			}

			if (flags != 0 || mustEmit)
			{
				/* Insert rather than seek-write-seek: the Insert* helpers are fixed width and
				 * cannot silently change size. The placeholder here is a single byte written by
				 * WriteUInt8Unpacked and backfilled by InsertUInt8Unpacked; a packed backfill of a
				 * wider placeholder could overrun it and corrupt the first field written after it,
				 * which is why the fixed-width Insert helpers are used throughout. */
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
				result.GroundNormal = AimDirectionCompression.Decode(
					reader.ReadDeltaUInt32(AimDirectionCompression.Encode(prev.GroundNormal)));

			if ((flags & INNER_NORMAL_BIT) != 0)
				result.InnerGroundNormal = AimDirectionCompression.Decode(
					reader.ReadDeltaUInt32(AimDirectionCompression.Encode(prev.InnerGroundNormal)));

			if ((flags & OUTER_NORMAL_BIT) != 0)
				result.OuterGroundNormal = AimDirectionCompression.Decode(
					reader.ReadDeltaUInt32(AimDirectionCompression.Encode(prev.OuterGroundNormal)));

			/* Zero rather than carry forward. `result` starts as `prev`, so a character that was
			 * grounded last tick and is airborne now would otherwise keep the previous tick's
			 * normals — the writer deliberately sent none. This matches the writer's own baseline
			 * because KCCController.GetState zeroes an ungrounded snapshot's normals; the motor
			 * does not (it seeds GroundNormal with the character's up vector). */
			if (!result.FoundAnyGround)
			{
				result.GroundNormal = Vector3.zero;
				result.InnerGroundNormal = Vector3.zero;
				result.OuterGroundNormal = Vector3.zero;
			}

			return result;
		}
	}

	/// <summary>
	/// Custom delta serializers for <see cref="KinematicCharacterMotorState"/>.
	/// <para>
	/// <b>Delta serializer</b>: Writes a 2-byte bitmask over 14 bit positions carrying 13 fields
	/// (bit 4 is a retired gap — see the constants below)
	/// followed by delta-encoded values for only the changed fields.
	/// On a typical grounded walking tick, only Position, Rotation, BaseVelocity, and
	/// GroundingStatus change. On an idle tick where the character stands still this writer declines
	/// and sends nothing at all. Measured figures live in <c>PredictionBandwidthBenchmarkTests</c>
	/// rather than here, where they cannot go stale silently.
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
		/* Bit 4 is retired. It carried LastPlatformPosition, a field nothing ever read: platform
		 * velocity comes from KCCPlatform.LastCompletedTickVelocity, so the Vector3 in every full
		 * snapshot and the delta in every reconcile bought nothing. The bit is left as a gap rather
		 * than renumbered so a stale build cannot silently reinterpret a later field. */
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
		/// Custom full serializer: writes all fields of <see cref="KinematicCharacterMotorState"/>.
		/// Nested <see cref="CharacterTransientGroundingReport"/> uses its full serializer.
		/// Extension method discovered by FishNet codegen via naming convention.
		/// </summary>
		public static void WriteKinematicCharacterMotorState(this Writer writer, KinematicCharacterMotorState value)
		{
			writer.WriteVector3(value.Position);
			/* 64-bit packing. This is the once-per-second full snapshot the delta chain resets to,
			 * and the owner REPLAYS from it: KCCController.UpdateRotation slerps from
			 * Motor.TransientRotation, so an error injected here decays over several ticks rather
			 * than being overwritten, and Motor.CharacterUp — derived from this rotation — is the
			 * basis the movement input is projected onto. The 32-bit form measured 0.43 degrees
			 * mean and 1.24 degrees worst, which is the same class of owner-versus-server drift the
			 * aim quantisation was introduced to remove. Four bytes once a second per player. */
			writer.WriteQuaternion64(value.Rotation);
			writer.WriteVector3(value.BaseVelocity);
			writer.WriteInt64(value.CurrentPlatformID);
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
				Rotation = reader.ReadQuaternion64(),
				BaseVelocity = reader.ReadVector3(),
				CurrentPlatformID = reader.ReadInt64(),
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
		/// Writes a 2-byte bitmask indicating which of the 13 fields changed,
		/// followed by delta-encoded values for only those fields.
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

			/* Leading mode byte, and an absolute snapshot on FullSerialize.
			 *
			 * This type declares IReconcileData, so it advertises that it can be a ROOT reconcile —
			 * and the project rule for a root is that FullSerialize must produce a payload a peer
			 * holding no baseline can decode. FishNet's scalar deltas are difference-based, so
			 * "every field present" is not that. Today it is only ever nested inside
			 * CharacterReconcileData, whose serializer routes FullSerialize through its own absolute
			 * path and never passes anything but Unset down here, so the branch below is unreachable
			 * in production. It exists so that promoting this type to a root cannot silently ship the
			 * exact bug the mode byte was invented to prevent. One byte per reconcile, to the owner
			 * only. */
			int modePos = writer.Position;
			int modeLength = writer.Length;
			if (fullSerialize)
			{
				writer.WriteUInt8Unpacked(MODE_FULL_SNAPSHOT);
				WriteKinematicCharacterMotorState(writer, next);
				return true;
			}
			writer.WriteUInt8Unpacked(MODE_DELTA);

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
			// Back past the mode byte too, so "bytes iff true" still holds.
			writer.Position = modePos;
			writer.Length = modeLength;
			return false;
		}

		/// <summary>Leading byte: the payload is a delta against the reader's previous snapshot.</summary>
		private const byte MODE_DELTA = 0;

		/// <summary>Leading byte: the payload is an absolute snapshot. See <see cref="WriteDelta"/>.</summary>
		private const byte MODE_FULL_SNAPSHOT = 1;

		/// <summary>
		/// Delta reader for <see cref="KinematicCharacterMotorState"/>.
		/// Reads the bitmask and reconstructs only the changed fields from their deltas,
		/// carrying forward unchanged fields from the previous value.
		/// </summary>
		/// <param name="reader">Reader to deserialize from.</param>
		/// <param name="prev">Previous motor state.</param>
		/// <returns>Reconstructed motor state.</returns>
		private static KinematicCharacterMotorState ReadDelta(
			Reader reader,
			KinematicCharacterMotorState prev)
		{
			// Mode first — see WriteDelta. An absolute snapshot ignores prev entirely.
			byte mode = reader.ReadUInt8Unpacked();
			if (mode == MODE_FULL_SNAPSHOT)
			{
				return ReadKinematicCharacterMotorState(reader);
			}

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