using UnityEngine;

public class AbilityHoldEvent : AbilityActivationEvent
{
	private KeyCode heldKey;

	public AbilityHoldEvent(Ability abilityInstance, KeyCode heldKey) : base(abilityInstance)
	{
		this.heldKey = heldKey;
	}

	public override AbilityActivationEventResult OnUpdate(Character character)
	{
		if (heldKey != KeyCode.None)
		{
			if (Input.GetKey(heldKey))
			{
				remainingTime -= Time.deltaTime;
				if (remainingTime < 0.0f)
				{
					remainingTime = 0.0f;
				}
				ability.Update(character, character.TargetController.current);

				return AbilityActivationEventResult.Update;
			}
			else if (Input.GetKeyUp(heldKey) && remainingTime <= ability.activationTime)
			{
				return AbilityActivationEventResult.Finished;
			}
			else
			{
				return AbilityActivationEventResult.Reset;
			}
		}
		else
		{
			return AbilityActivationEventResult.Reset;
		}
	}
}