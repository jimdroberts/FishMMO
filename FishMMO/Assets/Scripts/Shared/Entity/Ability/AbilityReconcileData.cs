using FishNet.Object.Prediction;

public struct AbilityReconcileData : IReconcileData
{
	public bool Interrupt;
	public int AbilityID;
	public float RemainingTime;

	private uint _tick;
	public void Dispose() { }
	public uint GetTick() => _tick;
	public void SetTick(uint value) => _tick = value;
}