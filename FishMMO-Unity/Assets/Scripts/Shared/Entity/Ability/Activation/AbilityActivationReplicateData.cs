using FishNet.Object.Prediction;
using UnityEngine;

namespace FishMMO.Shared
{
	public struct AbilityActivationReplicateData : IReplicateData
	{
		public int ActivationFlags;
		public long QueuedAbilityID;
		public KeyCode HeldKey;

		public AbilityActivationReplicateData(int activationFlags, long queuedAbilityID, KeyCode heldKey)
		{
			ActivationFlags = activationFlags;
			QueuedAbilityID = queuedAbilityID;
			HeldKey = heldKey;
			_tick = 0;
		}

		private uint _tick;
		public void Dispose() { }
		public uint GetTick() => _tick;
		public void SetTick(uint value) => _tick = value;
	}
}