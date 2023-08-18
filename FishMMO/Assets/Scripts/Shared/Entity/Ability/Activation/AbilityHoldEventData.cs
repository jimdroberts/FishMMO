using UnityEngine;

public class AbilityHoldEventData : AbilityActivationEventData
{
	public KeyCode HeldKey;

	public override AbilityActivationEventResult OnUpdate(Character character, float deltaTime)
	{
		if (HeldKey != KeyCode.None)
		{
			if (InterruptQueued)
			{
				return AbilityActivationEventResult.Interrupted;
			}

			if (Input.GetKey(HeldKey))
			{
				RemainingTime -= deltaTime;

				return AbilityActivationEventResult.Updated;
			}

			if (Input.GetKeyUp(HeldKey) && RemainingTime < 0.0f)
			{
				return AbilityActivationEventResult.Finished;
			}

			return AbilityActivationEventResult.Interrupted;
		}
		else
		{
			return AbilityActivationEventResult.Interrupted;
		}
	}
}