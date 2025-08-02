using FishNet.Object.Prediction;

namespace FishMMO.Shared
{
	/// <summary>
	/// Reconcile data for ability state, used for network prediction reconciliation.
	/// </summary>
	public struct AbilityReconcileData : IReconcileData
	{
		/// <summary>
		/// The ID of the ability.
		/// </summary>
		public long AbilityID;

		/// <summary>
		/// The remaining cooldown or active time for the ability.
		/// </summary>
		public float RemainingTime;

		/// <summary>
		/// The resource state associated with the ability.
		/// </summary>
		public CharacterAttributeResourceState ResourceState;

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityReconcileData"/> struct.
		/// </summary>
		/// <param name="abilityID">Ability ID.</param>
		/// <param name="remainingTime">Remaining time.</param>
		/// <param name="resourceState">Resource state.</param>
		public AbilityReconcileData(long abilityID, float remainingTime, CharacterAttributeResourceState resourceState)
		{
			AbilityID = abilityID;
			RemainingTime = remainingTime;
			ResourceState = resourceState;

			_tick = 0;
		}

		private uint _tick;

		/// <summary>
		/// Disposes the reconcile data (no-op).
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