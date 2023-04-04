using System;
using UnityEngine;

public class AbilityActivationEvent
{
	protected Ability ability;
	protected float remainingTime;

	public Ability Ability { get { return ability; } }

	public AbilityActivationEvent(Ability ability)
	{
		if (ability == null)
		{
			throw new Exception("Ability instance cannot be null");
		}
		this.ability = ability;
		remainingTime = ability.activationTime;
	}

	public void ApplySpeedReduction(float speedReduction)
	{
		remainingTime *= speedReduction;
	}

	public virtual AbilityActivationEventResult OnUpdate(Character character)
	{
		if (remainingTime > 0.0f)
		{
			remainingTime -= Time.deltaTime;
			if (remainingTime < 0.0f)
			{
				remainingTime = 0.0f;
			}
			ability.Update(character, character.TargetController.current);

			return AbilityActivationEventResult.Update;
		}
		else
		{
			return AbilityActivationEventResult.Finished;
		}
	}

	public void Interrupt(Character activator, Character attacker)
	{
		ability.Interrupt(activator, new TargetInfo(attacker.transform, Vector3.zero));
	}
}