using FishNet.Object.Prediction;

namespace FishMMO.Shared
{
	public struct AbilityReconcileData : IReconcileData
	{
		public bool Interrupt;
		public long AbilityID;
		public float RemainingTime;
		public CharacterAttributeResourceState ResourceState;

		public AbilityReconcileData(bool interrupt, long abilityID, float remainingTime, CharacterAttributeResourceState resourceState)
		{
			Interrupt = interrupt;
			AbilityID = abilityID;
			RemainingTime = remainingTime;
			ResourceState = resourceState;

			_tick = 0;
		}

		private uint _tick;
		public void Dispose() { }
		public uint GetTick() => _tick;
		public void SetTick(uint value) => _tick = value;
	}
}