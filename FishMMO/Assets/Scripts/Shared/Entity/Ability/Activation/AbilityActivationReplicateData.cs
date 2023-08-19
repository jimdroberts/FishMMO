using FishNet.Object.Prediction;

public class AbilityActivationReplicateData : IReplicateData
{
	public bool InterruptQueued;
	public int AbilityID;
	public float RemainingTime;

	public void ApplySpeedReduction(float speedReduction)
	{
		RemainingTime *= speedReduction;
	}

	public virtual AbilityActivationEventResult OnUpdate(Character character, float deltaTime)
	{
		if (InterruptQueued)
		{
			return AbilityActivationEventResult.Interrupted;
		}

		if (RemainingTime > 0.0f)
		{
			RemainingTime -= deltaTime;

			return AbilityActivationEventResult.Updated;
		}

		return AbilityActivationEventResult.Finished;
	}

	public virtual void Interrupt(Character attacker)
	{
		InterruptQueued = true;
	}

	private uint _tick;
	public void Dispose() { }
	public uint GetTick() => _tick;
	public void SetTick(uint value) => _tick = value;
}