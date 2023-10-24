using FishNet.Object.Prediction;

public struct AbilityReconcileData : IReconcileData
{
	public bool Interrupt;
	public int AbilityID;
	public float RemainingTime;

	public AbilityReconcileData(bool interrupt, int abilityID, float remainingTime)
	{
		Interrupt = interrupt;
		AbilityID = abilityID;
		RemainingTime = remainingTime;
		_tick = 0;
	}

	private uint _tick;
	public void Dispose() { }
	public uint GetTick() => _tick;
	public void SetTick(uint value) => _tick = value;
}