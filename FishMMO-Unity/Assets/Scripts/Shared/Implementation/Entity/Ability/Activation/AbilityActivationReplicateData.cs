using FishNet.Object.Prediction;
using FishNet.CodeGenerating;

namespace FishMMO.Shared
{
	/// <summary>
	/// Replicate data for ability activation, used for network prediction.
	/// Camera data is not included here to save bandwidth; instead, AbilityController reads from
	/// KCCController.VirtualCameraPosition/VirtualCameraRotation which is guaranteed fresh via OnPostTick ordering.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct AbilityActivationReplicateData : IReplicateData
	{
		/// <summary>
		/// Flags representing the activation state.
		/// </summary>
		public int ActivationFlags;

		/// <summary>
		/// The ID of the queued ability or consumable template.
		/// Typed as long to match <see cref="Ability.ID"/> for ability activations.
		/// Consumable activations store template IDs which fit in int but use long for uniformity.
		/// </summary>
		public long QueuedAbilityID;

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityActivationReplicateData"/> struct.
		/// </summary>
		/// <param name="activationFlags">Activation flags (IsHeld, IsConsumable, etc. are encoded here).</param>
		/// <param name="queuedAbilityID">Queued ability or consumable template ID.</param>
		public AbilityActivationReplicateData(int activationFlags, long queuedAbilityID)
		{
			ActivationFlags = activationFlags;
			QueuedAbilityID = queuedAbilityID;

			tick = 0;
		}

		private uint tick;

		/// <summary>
		/// Disposes the replicate data (no-op).
		/// </summary>
		public void Dispose() { }

		/// <summary>
		/// Gets the network tick value.
		/// </summary>
		/// <returns>The tick value.</returns>
		public uint GetTick() => tick;

		/// <summary>
		/// Sets the network tick value.
		/// </summary>
		/// <param name="value">Tick value.</param>
		public void SetTick(uint value) => tick = value;
	}
}