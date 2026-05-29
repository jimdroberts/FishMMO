using FishNet.Object.Prediction;

namespace FishMMO.Shared
{
	/// <summary>
	/// Replicate data for ability activation, used internally by <see cref="AbilityController"/>.
	/// The unified <see cref="CharacterReplicateData"/> is what FishNet serializes for prediction.
	/// </summary>
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
		/// Returns the network tick for this replicate input as a <see cref="PredictionTick"/>.
		/// This is the only sanctioned way to produce a PredictionTick — use it as the
		/// currentTick argument from within the prediction pipeline.
		/// </summary>
		public PredictionTick GetPredictionTick() => new PredictionTick(tick);

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