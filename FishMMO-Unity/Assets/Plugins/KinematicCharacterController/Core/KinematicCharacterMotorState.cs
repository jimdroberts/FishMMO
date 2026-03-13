using FishNet.CodeGenerating;
using FishNet.Object.Prediction;
using System;
using UnityEngine;

namespace KinematicCharacterController
{
	/// <summary>
	/// Represents the entire state of a character motor that is pertinent for simulation.
	/// Includes all fields read during Replicate so reconcile can fully restore state.
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
		/// Last known position of the current platform, used to compute platform velocity during replay.
		/// Without this field, reconcile replay computes incorrect platform delta and causes permanent desync.
		/// </summary>
		public Vector3 LastPlatformPosition;

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

		private uint tick;

		/// <inheritdoc/>
		public void Dispose() { }

		/// <inheritdoc/>
		public uint GetTick() => tick;

		/// <inheritdoc/>
		public void SetTick(uint value) => tick = value;
	}
}
