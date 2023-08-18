using FishNet.Object.Prediction;
using UnityEngine;

namespace KinematicCharacterController.Examples
{
	public struct PlayerCharacterInputs : IReplicateData
	{
		public float MoveAxisForward;
		public float MoveAxisRight;
		public Quaternion CameraRotation;
		public bool JumpDown;
		public bool CrouchActive;
		public bool SprintActive;

		private uint _tick;
		public void Dispose() { }
		public uint GetTick() => _tick;
		public void SetTick(uint value) => _tick = value;
	}
}