using FishNet.Object.Prediction;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Replicate data for ability activation, used for network prediction.
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
		/// The key held during activation.
		/// </summary>
		public KeyCode HeldKey;

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityActivationReplicateData"/> struct.
		/// </summary>
		/// <param name="activationFlags">Activation flags.</param>
		/// <param name="queuedAbilityID">Queued ability ID.</param>
		/// <param name="heldKey">Held key.</param>
		public AbilityActivationReplicateData(int activationFlags, long queuedAbilityID, KeyCode heldKey)
		{
			ActivationFlags = activationFlags;
			QueuedAbilityID = queuedAbilityID;
			HeldKey = heldKey;

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