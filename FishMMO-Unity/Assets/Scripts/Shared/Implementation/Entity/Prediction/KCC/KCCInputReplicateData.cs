using FishNet.Object.Prediction;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single frame of input data for KCC character replication and prediction.
	/// Used internally by <see cref="KCCController.SetInputs"/> and <see cref="PlayerInputController"/>.
	/// </summary>
	public struct KCCInputReplicateData : IReplicateData
	{
		/// <summary>
		/// Forward movement axis value (W/S or up/down).
		/// </summary>
		public float MoveAxisForward;

		/// <summary>
		/// Right movement axis value (A/D or left/right).
		/// </summary>
		public float MoveAxisRight;

		/// <summary>
		/// Bitmask of movement flags (jump, crouch, sprint, etc).
		/// Each flag is a bit position defined by <see cref="KCCMoveFlags"/>.
		/// </summary>
		public int MoveFlags;

		/// <summary>
		/// Camera position for this input frame.
		/// </summary>
		public Vector3 CameraPosition;

		/// <summary>
		/// Camera rotation for this input frame.
		/// </summary>
		public Quaternion CameraRotation;

		/// <summary>
		/// Constructs a new input replicate data frame with movement and camera info.
		/// </summary>
		/// <param name="moveAxisForward">Forward axis value.</param>
		/// <param name="moveAxisRight">Right axis value.</param>
		/// <param name="moveFlags">Movement flags bitmask.</param>
		/// <param name="cameraPosition">Camera position.</param>
		/// <param name="cameraRotation">Camera rotation.</param>
		public KCCInputReplicateData(float moveAxisForward, float moveAxisRight, int moveFlags, Vector3 cameraPosition, Quaternion cameraRotation) : this()
		{
			MoveAxisForward = moveAxisForward;
			MoveAxisRight = moveAxisRight;
			MoveFlags = moveFlags;
			CameraPosition = cameraPosition;
			CameraRotation = cameraRotation;
			tick = 0;
		}

		private uint tick;

		/// <inheritdoc/>
		public void Dispose() { }

		/// <inheritdoc/>
		public uint GetTick() => tick;

		/// <inheritdoc/>
		public void SetTick(uint value) => tick = value;
	}
}