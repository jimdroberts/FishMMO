using FishNet.CodeGenerating;
using FishNet.Object.Prediction;
using System;
using UnityEngine;

namespace KinematicCharacterController
{
	/// <summary>
	/// Represents the state of a character motor that is carried in the reconcile.
	/// <para>
	/// NOT everything the simulation reads. Known omissions that can affect a replay: the capsule
	/// dimensions (so a reconciled <c>IsCrouching</c> flip leaves the collider at the wrong height —
	/// <c>KCCController.ApplyState</c> does not resize it), the attached-rigidbody references that
	/// gate the mount/dismount velocity impulses, the platform velocity, and
	/// <c>KCCController.hasDoneInitialGroundProbe</c>, which <c>ApplyState</c> actively resets on
	/// every reconcile. <c>LastGroundingStatus</c> is omitted safely — the motor re-derives it at the
	/// top of its next update, before any consumer.
	/// </para>
	/// </summary>
	[Serializable]
	[UseGlobalCustomSerializer]
	public struct KinematicCharacterMotorState : IReconcileData
	{
		/// <summary>
		/// World position of the character.
		/// </summary>
		public Vector3 Position;

		/// <summary>
		/// World rotation of the character.
		/// </summary>
		public Quaternion Rotation;

		/// <summary>
		/// Current base velocity of the character.
		/// </summary>
		public Vector3 BaseVelocity;

		/// <summary>
		/// Scene object ID of the platform the character is standing on. Zero if none.
		/// </summary>
		public long CurrentPlatformID;

		/// <summary>
		/// Whether the motor must force-unground on the next update.
		/// </summary>
		public bool MustUnground;

		/// <summary>
		/// Time remaining for forced unground state.
		/// </summary>
		public float MustUngroundTime;

		/// <summary>
		/// Whether the last movement iteration found any ground beneath the character.
		/// </summary>
		public bool LastMovementIterationFoundAnyGround;

		/// <summary>
		/// Full grounding status report for the character.
		/// </summary>
		public CharacterTransientGroundingReport GroundingStatus;

		/// <summary>
		/// Velocity inherited from an attached rigidbody (e.g. moving platform).
		/// </summary>
		public Vector3 AttachedRigidbodyVelocity;

		/// <summary>
		/// Whether the character is currently crouching.
		/// </summary>
		public bool IsCrouching;

		/// <summary>
		/// Whether a jump has been requested and is pending execution.
		/// </summary>
		public bool JumpRequested;

		/// <summary>
		/// Time elapsed since the character was last able to jump (for grace period).
		/// </summary>
		public float TimeSinceLastAbleToJump;

		/// <summary>
		/// Time elapsed since a jump was last requested (for pre-grounding grace).
		/// </summary>
		public float TimeSinceJumpRequested;

		/// <summary>
		/// Whether the character is swimming: no ground solving, no gravity, floating at the surface or diving.
		/// </summary>
		public bool IsSwimming;

		/// <summary>
		/// Ticks left of a climb out of the water onto a ledge; zero when not climbing.
		/// </summary>
		public byte ClimbOutTicks;

		/// <summary>
		/// The ladder, rope ladder or rope being climbed: its index in the character's scene's climbables, plus one; zero
		/// when not climbing. An index, not a reference, so the server and the owner name the same one.
		/// </summary>
		public ushort Climbable;

		/// <summary>Where in the climb the character is (FishMMO.Shared.ClimbPhase as a byte; zero when not climbing).</summary>
		public byte ClimbPhase;

		/// <summary>
		/// While stepping on or off a climbable at its top: ticks left before the step is given up. While not climbing:
		/// ticks left before the character may take hold of one again.
		/// </summary>
		public byte ClimbTicks;

		private uint tick;

		/// <inheritdoc/>
		public void Dispose() { }

		/// <inheritdoc/>
		public uint GetTick() => tick;

		/// <inheritdoc/>
		public void SetTick(uint value) => tick = value;
	}
}
