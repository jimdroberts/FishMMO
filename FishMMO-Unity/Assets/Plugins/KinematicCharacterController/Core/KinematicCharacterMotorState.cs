﻿using FishNet.Object.Prediction;
using System;
using UnityEngine;

namespace KinematicCharacterController
{
	/// <summary>
	/// Represents the entire state of a character motor that is pertinent for simulation.
	/// Use this to save state or revert to past state
	/// </summary>
	[Serializable]
	public struct KinematicCharacterMotorState : IReconcileData
	{
		public Vector3 Position;
		public Quaternion Rotation;
		public Vector3 BaseVelocity;

		public bool MustUnground;
		public float MustUngroundTime;
		public bool LastMovementIterationFoundAnyGround;
		public CharacterTransientGroundingReport GroundingStatus;

		[NonSerialized]
		public Rigidbody AttachedRigidbody;
		public Vector3 AttachedRigidbodyVelocity;
		public float TimeSinceLastAbleToJump;
		public bool IsCrouching;
		public float TimeSinceJumpRequested;

		private uint _tick;
		public void Dispose() { }
		public uint GetTick() => _tick;
		public void SetTick(uint value) => _tick = value;
	}
}
