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
		private const byte FORWARD_BIT = 1 << 0;
		private const byte RIGHT_BIT = 1 << 1;
		private const byte MOVE_FLAGS_BIT = 1 << 2;
		private const byte POSITION_BIT = 1 << 3;
		private const byte ROTATION_BIT = 1 << 4;
		private const byte ACTIVATION_FLAGS_BIT = 1 << 5;
		private const byte QUEUED_ABILITY_BIT = 1 << 6;

		/// <summary>
		/// Registers the custom delta serializers at runtime, before any scene loads.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterDeltaSerializers()
		{
			GenericDeltaWriter<CharacterReplicateData>.SetWrite(WriteDelta);
			GenericDeltaReader<CharacterReplicateData>.SetRead(ReadDelta);
		}

		private static bool WriteDelta(
			Writer writer,
			CharacterReplicateData prev,
			CharacterReplicateData next,
			DeltaSerializerOption option)
		{
			byte flags = 0;
			bool forceWrite = option != DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			if (forceWrite || prev.MoveAxisForward != next.MoveAxisForward)
			{
				writer.WriteSingle(next.MoveAxisForward);
				flags |= FORWARD_BIT;
			}

			if (forceWrite || prev.MoveAxisRight != next.MoveAxisRight)
			{
				writer.WriteSingle(next.MoveAxisRight);
				flags |= RIGHT_BIT;
			}

			if (writer.WriteDeltaInt32(prev.MoveFlags, next.MoveFlags, option))
				flags |= MOVE_FLAGS_BIT;

			if (writer.WriteDeltaVector3(prev.CameraPosition, next.CameraPosition, option))
				flags |= POSITION_BIT;

			if (writer.WriteDeltaQuaternion(prev.CameraRotation, next.CameraRotation, option: option))
				flags |= ROTATION_BIT;

			if (writer.WriteDeltaInt32(prev.ActivationFlags, next.ActivationFlags, option))
				flags |= ACTIVATION_FLAGS_BIT;

			if (writer.WriteDeltaInt64(prev.QueuedAbilityID, next.QueuedAbilityID, option))
				flags |= QUEUED_ABILITY_BIT;

			if (flags != 0 || forceWrite)
			{
				int endPos = writer.Position;
				writer.Position = flagPos;
				writer.WriteUInt8Unpacked(flags);
				writer.Position = endPos;
				return true;
			}

			writer.Position = flagPos;
			return false;
		}

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
		private const byte FOUND_GROUND_BIT = 1 << 0;
		private const byte STABLE_BIT = 1 << 1;
		private const byte SNAPPING_BIT = 1 << 2;
		private const byte GROUND_NORMAL_BIT = 1 << 3;
		private const byte INNER_NORMAL_BIT = 1 << 4;
		private const byte OUTER_NORMAL_BIT = 1 << 5;

		/// <summary>
		/// Registers the custom delta serializers at runtime.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterDeltaSerializers()
		{
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
			bool forceWrite = option != DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			writer.WriteUInt8Unpacked(0);

			if (writer.WriteDeltaBoolean(prev.FoundAnyGround, next.FoundAnyGround, option))
				flags |= FOUND_GROUND_BIT;

			if (writer.WriteDeltaBoolean(prev.IsStableOnGround, next.IsStableOnGround, option))
				flags |= STABLE_BIT;

			if (writer.WriteDeltaBoolean(prev.SnappingPrevented, next.SnappingPrevented, option))
				flags |= SNAPPING_BIT;

			if (writer.WriteDeltaVector3(prev.GroundNormal, next.GroundNormal, option))
				flags |= GROUND_NORMAL_BIT;

			if (writer.WriteDeltaVector3(prev.InnerGroundNormal, next.InnerGroundNormal, option))
				flags |= INNER_NORMAL_BIT;

			if (writer.WriteDeltaVector3(prev.OuterGroundNormal, next.OuterGroundNormal, option))
				flags |= OUTER_NORMAL_BIT;

			if (flags != 0 || forceWrite)
			{
				int endPos = writer.Position;
				writer.Position = flagPos;
				writer.WriteUInt8Unpacked(flags);
				writer.Position = endPos;
				return true;
			}

			writer.Position = flagPos;
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
				result.FoundAnyGround = reader.ReadDeltaBoolean(prev.FoundAnyGround);

			if ((flags & STABLE_BIT) != 0)
				result.IsStableOnGround = reader.ReadDeltaBoolean(prev.IsStableOnGround);

			if ((flags & SNAPPING_BIT) != 0)
				result.SnappingPrevented = reader.ReadDeltaBoolean(prev.SnappingPrevented);

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
		private const ushort POSITION_BIT = 1 << 0;
		private const ushort ROTATION_BIT = 1 << 1;
		private const ushort VELOCITY_BIT = 1 << 2;
		private const ushort PLATFORM_ID_BIT = 1 << 3;
		private const ushort LAST_PLATFORM_POS_BIT = 1 << 4;
		private const ushort MUST_UNGROUND_BIT = 1 << 5;
		private const ushort MUST_UNGROUND_TIME_BIT = 1 << 6;
		private const ushort LAST_FOUND_GROUND_BIT = 1 << 7;
		private const ushort GROUNDING_BIT = 1 << 8;
		private const ushort ATTACHED_RB_VEL_BIT = 1 << 9;
		private const ushort IS_CROUCHING_BIT = 1 << 10;
		private const ushort JUMP_REQUESTED_BIT = 1 << 11;
		private const ushort TIME_SINCE_JUMP_BIT = 1 << 12;
		private const ushort TIME_SINCE_JUMP_REQ_BIT = 1 << 13;

		/// <summary>
		/// Registers the custom delta serializers at runtime.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void RegisterDeltaSerializers()
		{
			GenericDeltaWriter<KinematicCharacterMotorState>.SetWrite(WriteDelta);
			GenericDeltaReader<KinematicCharacterMotorState>.SetRead(ReadDelta);
		}

		/// <summary>
		/// Delta writer for <see cref="KinematicCharacterMotorState"/>.
		/// Writes a 2-byte bitmask indicating which of the 14 fields changed,
		/// followed by delta-encoded values for only those fields.
		/// </summary>
		private static bool WriteDelta(
			Writer writer,
			KinematicCharacterMotorState prev,
			KinematicCharacterMotorState next,
			DeltaSerializerOption option)
		{
			ushort flags = 0;
			bool forceWrite = option != DeltaSerializerOption.Unset;

			int flagPos = writer.Position;
			writer.WriteUInt16(0);

			if (writer.WriteDeltaVector3(prev.Position, next.Position, option))
				flags |= POSITION_BIT;

			if (writer.WriteDeltaQuaternion(prev.Rotation, next.Rotation, option: option))
				flags |= ROTATION_BIT;

			if (writer.WriteDeltaVector3(prev.BaseVelocity, next.BaseVelocity, option))
				flags |= VELOCITY_BIT;

			if (writer.WriteDeltaInt64(prev.CurrentPlatformID, next.CurrentPlatformID, option))
				flags |= PLATFORM_ID_BIT;

			if (writer.WriteDeltaVector3(prev.LastPlatformPosition, next.LastPlatformPosition, option))
				flags |= LAST_PLATFORM_POS_BIT;

			if (writer.WriteDeltaBoolean(prev.MustUnground, next.MustUnground, option))
				flags |= MUST_UNGROUND_BIT;

			if (forceWrite || prev.MustUngroundTime != next.MustUngroundTime)
			{
				writer.WriteSingle(next.MustUngroundTime);
				flags |= MUST_UNGROUND_TIME_BIT;
			}

			if (writer.WriteDeltaBoolean(prev.LastMovementIterationFoundAnyGround, next.LastMovementIterationFoundAnyGround, option))
				flags |= LAST_FOUND_GROUND_BIT;

			if (CharacterTransientGroundingReportDeltaSerializer.WriteDelta(writer, prev.GroundingStatus, next.GroundingStatus, option))
				flags |= GROUNDING_BIT;

			if (writer.WriteDeltaVector3(prev.AttachedRigidbodyVelocity, next.AttachedRigidbodyVelocity, option))
				flags |= ATTACHED_RB_VEL_BIT;

			if (writer.WriteDeltaBoolean(prev.IsCrouching, next.IsCrouching, option))
				flags |= IS_CROUCHING_BIT;

			if (writer.WriteDeltaBoolean(prev.JumpRequested, next.JumpRequested, option))
				flags |= JUMP_REQUESTED_BIT;

			if (forceWrite || prev.TimeSinceLastAbleToJump != next.TimeSinceLastAbleToJump)
			{
				writer.WriteSingle(next.TimeSinceLastAbleToJump);
				flags |= TIME_SINCE_JUMP_BIT;
			}

			if (forceWrite || prev.TimeSinceJumpRequested != next.TimeSinceJumpRequested)
			{
				writer.WriteSingle(next.TimeSinceJumpRequested);
				flags |= TIME_SINCE_JUMP_REQ_BIT;
			}

			if (flags != 0 || forceWrite)
			{
				int endPos = writer.Position;
				writer.Position = flagPos;
				writer.WriteUInt16(flags);
				writer.Position = endPos;
				return true;
			}

			writer.Position = flagPos;
			return false;
		}

		/// <summary>
		/// Delta reader for <see cref="KinematicCharacterMotorState"/>.
		/// Reads the bitmask and reconstructs only the changed fields from their deltas,
		/// carrying forward unchanged fields from the previous value.
		/// </summary>
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
				result.MustUnground = reader.ReadDeltaBoolean(prev.MustUnground);

			if ((flags & MUST_UNGROUND_TIME_BIT) != 0)
				result.MustUngroundTime = reader.ReadSingle();

			if ((flags & LAST_FOUND_GROUND_BIT) != 0)
				result.LastMovementIterationFoundAnyGround = reader.ReadDeltaBoolean(prev.LastMovementIterationFoundAnyGround);

			if ((flags & GROUNDING_BIT) != 0)
				result.GroundingStatus = CharacterTransientGroundingReportDeltaSerializer.ReadDelta(reader, prev.GroundingStatus);

			if ((flags & ATTACHED_RB_VEL_BIT) != 0)
				result.AttachedRigidbodyVelocity = reader.ReadDeltaVector3(prev.AttachedRigidbodyVelocity);

			if ((flags & IS_CROUCHING_BIT) != 0)
				result.IsCrouching = reader.ReadDeltaBoolean(prev.IsCrouching);

			if ((flags & JUMP_REQUESTED_BIT) != 0)
				result.JumpRequested = reader.ReadDeltaBoolean(prev.JumpRequested);

			if ((flags & TIME_SINCE_JUMP_BIT) != 0)
				result.TimeSinceLastAbleToJump = reader.ReadSingle();

			if ((flags & TIME_SINCE_JUMP_REQ_BIT) != 0)
				result.TimeSinceJumpRequested = reader.ReadSingle();

			return result;
		}
	}
}
