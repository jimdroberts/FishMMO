using FishNet.Object.Prediction;

namespace FishMMO.Shared
{
	public struct AbilityReconcileData : IReconcileData
	{
		public long AbilityID;
		public float RemainingTime;
		public CharacterAttributeResourceState ResourceState;

		public AbilityReconcileData(long abilityID, float remainingTime, CharacterAttributeResourceState resourceState) : this()
		{
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