using FishNet.Object.Prediction;
using UnityEngine;

public struct AbilityActivationReplicateData : IReplicateData
{
	public bool InterruptQueued;
	public int QueuedAbilityID;
	public KeyCode HeldKey;
	public Ray Ray;

	public AbilityActivationReplicateData(bool interruptQueued, int queuedAbilityID, KeyCode heldKey, Ray ray)
	{
		InterruptQueued = interruptQueued;
		QueuedAbilityID = queuedAbilityID;
		HeldKey = heldKey;
		Ray = ray;
		_tick = 0;
	}

	private uint _tick;
	public void Dispose() { }
	public uint GetTick() => _tick;
	public void SetTick(uint value) => _tick = value;
}