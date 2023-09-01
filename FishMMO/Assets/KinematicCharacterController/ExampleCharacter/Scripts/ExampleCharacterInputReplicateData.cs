using FishNet.Object.Prediction;
using UnityEngine;

namespace KinematicCharacterController.Examples
{
	public struct ExampleCharacterInputReplicateData : IReplicateData
	{
		public float MoveAxisForward;
		public float MoveAxisRight;
		public Quaternion CameraRotation;
		public int MoveFlags;

		public ExampleCharacterInputReplicateData(float moveAxisForward, float moveAxisRight, Quaternion cameraRotation, int moveFlags)
		{
			MoveAxisForward = moveAxisForward;
			MoveAxisRight = moveAxisRight;
			CameraRotation = cameraRotation;
			MoveFlags = moveFlags;

			_tick = 0;
		}

		private uint _tick;
		public void Dispose() { }
		public uint GetTick() => _tick;
		public void SetTick(uint value) => _tick = value;
	}
}