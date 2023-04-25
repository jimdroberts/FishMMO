using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class AbilityController : NetworkBehaviour
{
	public Transform abilitySpawner;
	public Character character;
	public Dictionary<string, Ability> abilities = new Dictionary<string, Ability>();
	private AbilityActivationEvent currentAbilityEvent;

	public AbilityActivationEvent CurrentAbilityEvent { get { return currentAbilityEvent; } }

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (character == null || !base.IsOwner)
		{
			enabled = false;
			return;
		}
	}

	void Update()
	{
		if (currentAbilityEvent != null)
		{
			AbilityActivationEventResult result = currentAbilityEvent.OnUpdate(character);
			if (result == AbilityActivationEventResult.Finished)
			{
				currentAbilityEvent.Ability.Finish(character, character.TargetController.current);

				if (character.CooldownController != null && currentAbilityEvent.Ability.cooldown > 0.0f)
				{
					float cooldownReduction = CalculateSpeedReduction(currentAbilityEvent.Ability.Template.CooldownReductionAttribute);
					float cooldown = (currentAbilityEvent.Ability.cooldown) * cooldownReduction;

					character.CooldownController.AddCooldown(currentAbilityEvent.Ability.Name, new CooldownInstance(cooldown));
				}
				Cancel();
			}
			else if (result == AbilityActivationEventResult.Reset)
			{
				Cancel();
			}
		}
	}

	public void Activate(string referenceID, KeyCode heldKey)
	{
		if (!string.IsNullOrWhiteSpace(referenceID) && abilities.ContainsKey(referenceID) /*&& abilities[referenceID].isHotkey*/)
		{
			if (abilities[referenceID].Template.IsHoldToActivate)
			{
				if (heldKey != KeyCode.None)
				{
					Activate(new AbilityHoldEvent(abilities[referenceID], heldKey));
				}
			}
			else
			{
				Activate(new AbilityActivationEvent(abilities[referenceID]));
			}
		}
	}
	private void Activate(AbilityActivationEvent activationEvent)
	{
		if (CanActivate(activationEvent))
		{
			currentAbilityEvent = activationEvent;

			float activateSpeedReduction = CalculateSpeedReduction(currentAbilityEvent.Ability.Template.ActivationSpeedReductionAttribute);
			currentAbilityEvent.ApplySpeedReduction(activateSpeedReduction);

			currentAbilityEvent.Ability.ConsumeResource(character);
			currentAbilityEvent.Ability.Start(character, character.TargetController.current);
		}
	}

	private bool CanActivate(AbilityActivationEvent activationEvent)
	{
		if (currentAbilityEvent != null ||
			activationEvent.Ability == null ||
			character.CooldownController.IsOnCooldown(activationEvent.Ability.Name) ||
			!activationEvent.Ability.MeetsRequirements(character) ||
			!activationEvent.Ability.HasResource(character))
		{
			return false;
		}
		return true;
	}

	public void Interrupt(Character attacker)
	{
		if (currentAbilityEvent != null)
		{
			currentAbilityEvent.Interrupt(character, attacker);
			Cancel();
		}
	}

	private float CalculateSpeedReduction(CharacterAttributeTemplate attribute)
	{
		float result = 1.0f;
		if (attribute != null)
		{
			CharacterAttribute speedReduction;
			if (character.AttributeController.TryGetAttribute(attribute.Name, out speedReduction))
			{
				result = 1.0f - ((speedReduction.FinalValue * 0.01f).Clamp(0.0f, 1.0f));
			}
		}
		return result;
	}

	/// <summary>
	/// Cancels the current ability and sets it to null.
	/// </summary>
	private void Cancel()
	{
		currentAbilityEvent = null;
	}

	public void RemoveAbility(string referenceID)
	{
		abilities.Remove(referenceID);
	}
}