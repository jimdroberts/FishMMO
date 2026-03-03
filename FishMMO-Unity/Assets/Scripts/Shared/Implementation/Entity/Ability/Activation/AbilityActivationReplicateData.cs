using FishNet.Object.Prediction;

namespace FishMMO.Shared
{
	/// <summary>
	/// Replicate data for ability activation, used for network prediction.
	/// Camera data is not included here to save bandwidth; instead, AbilityController reads from
	/// KCCController.VirtualCameraPosition/VirtualCameraRotation which is guaranteed fresh via OnPostTick ordering.
	/// </summary>
	public struct AbilityActivationReplicateData : IReplicateData
	{
		/// <summary>
		/// Flags representing the activation state.
		/// </summary>
		public int ActivationFlags;

		/// <summary>
		/// The ID of the queued ability.
		/// </summary>
		public long QueuedAbilityID;

		/// <summary>
		/// Whether a key is held during activation.
		/// </summary>
		public bool IsHeld;

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityActivationReplicateData"/> struct.
		/// </summary>
		/// <param name="activationFlags">Activation flags.</param>
		/// <param name="queuedAbilityID">Queued ability ID.</param>
		/// <param name="isHeld">Whether a key is held.</param>
		public AbilityActivationReplicateData(int activationFlags, long queuedAbilityID, bool isHeld)
		{
			ActivationFlags = activationFlags;
			QueuedAbilityID = queuedAbilityID;
			IsHeld = isHeld;

			_tick = 0;
		}

		private uint _tick;

		/// <summary>
		/// Disposes the replicate data (no-op).
		/// </summary>
		public void Dispose() { }

		/// <summary>
		/// Gets the network tick value.
		/// </summary>
		/// <returns>The tick value.</returns>
		public uint GetTick() => _tick;

		/// <summary>
		/// Sets the network tick value.
		/// </summary>
		/// <param name="value">Tick value.</param>
		public void SetTick(uint value) => _tick = value;
	}
}