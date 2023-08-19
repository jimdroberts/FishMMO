using FishMMO.Client;
using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class AbilityController : NetworkBehaviour
{
	private AbilityActivationReplicateData queuedAbilityEvent = null;
	private AbilityActivationReplicateData currentAbilityEvent = null;
	//public Random currentSeed = 12345;

	public Transform abilitySpawner;
	public Character character;
	public Dictionary<int, Ability> knownAbilities = new Dictionary<int, Ability>();
	public Dictionary<int, SpawnEvent> knownSpawnEvents = new Dictionary<int, SpawnEvent>();
	public Dictionary<int, HitEvent> knownHitEvents = new Dictionary<int, HitEvent>();
	public Dictionary<int, MoveEvent> knownMoveEvents = new Dictionary<int, MoveEvent>();

	public CharacterAttributeTemplate bloodResource = null;
	public AbilityEvent bloodResourceConversion = null;
	public AbilityEvent holdToActivate = null;

	// Invoked when the current ability is Interrupted.
	public Action OnInterrupt;
	// Invoked when the current ability is cancelled.
	public Action OnCancelled;

	public AbilityActivationReplicateData CurrentAbilityEvent { get { return currentAbilityEvent; } }

	private void Awake()
	{
		InstanceFinder.TimeManager.OnTick += TimeManager_OnTick;
	}

	void OnDestroy()
	{
		if (InstanceFinder.TimeManager != null)
		{
			InstanceFinder.TimeManager.OnTick -= TimeManager_OnTick;
		}
	}

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (character == null || !base.IsOwner)
		{
			enabled = false;
			return;
		}
	}

	private void TimeManager_OnTick()
	{
		if (base.IsOwner)
		{
			Reconcile(default, false);
			HandleActivation(out AbilityActivationReplicateData activationData);
			Replicate(activationData, false);
		}
		if (base.IsServer)
		{
			Replicate(default, true);
			AbilityReconcileData state = new AbilityReconcileData()
			{
				Interrupt = currentAbilityEvent.InterruptQueued,
				AbilityID = currentAbilityEvent.AbilityID,
				RemainingTime = currentAbilityEvent.RemainingTime,
			};
			Reconcile(state, true);
		}
	}

	[Replicate]
	private void Replicate(AbilityActivationReplicateData activationData, bool asServer, Channel channel = Channel.Unreliable, bool replaying = false)
	{
		// Simulate the Ability Activation
		if (activationData != null)
		{
			AbilityActivationEventResult result = activationData.OnUpdate(character, (float)base.TimeManager.TickDelta);
			switch (result)
			{
				case AbilityActivationEventResult.Updated:
					break;
				case AbilityActivationEventResult.Interrupted:
					Cancel();
					break;
				case AbilityActivationEventResult.Finished:
					if (CanActivate(activationData, out Ability ability))
					{
						// consume resources
						if (ability.AbilityEvents.ContainsKey(bloodResourceConversion.Name))
						{
							int totalCost = ability.TotalResourceCost;

							CharacterResourceAttribute resource;
							if (character.AttributeController.TryGetResourceAttribute(bloodResource.Name, out resource) &&
								resource.CurrentValue >= totalCost)
							{
								resource.Consume(totalCost);
							}
						}
						else
						{
							foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in ability.resources)
							{
								CharacterResourceAttribute resource;
								if (character.AttributeController.TryGetResourceAttribute(pair.Key.Name, out resource) &&
									resource.CurrentValue < pair.Value)
								{
									resource.Consume(pair.Value);
								}
							}
						}

						// add ability to cooldowns
						AbilityTemplate currentAbilityTemplate = ability.Template;
						if (character.CooldownController != null && ability.cooldown > 0.0f)
						{
							float cooldownReduction = CalculateSpeedReduction(currentAbilityTemplate.CooldownReductionAttribute);
							float cooldown = ability.cooldown * cooldownReduction;

							character.CooldownController.AddCooldown(currentAbilityTemplate.Name, new CooldownInstance(cooldown));
						}

						// spawn the ability object
						AbilityObject.Spawn(ability, character, character.TargetController.current);
					}
					Cancel();
					break;
				default:
					break;
			}
		}
	}

	[Reconcile]
	private void Reconcile(AbilityReconcileData rd, bool asServer, Channel channel = Channel.Unreliable)
	{
		if (rd.Interrupt)
		{
			OnInterrupt?.Invoke();
			Cancel();
		}
		else
		{
			if (knownAbilities.ContainsKey(rd.AbilityID))
			{
				currentAbilityEvent.AbilityID = rd.AbilityID;
			}
			currentAbilityEvent.RemainingTime = rd.RemainingTime;
		}
	}

	public void QueueActivation(int referenceID, KeyCode heldKey)
	{
		if (knownAbilities.TryGetValue(referenceID, out Ability ability))
		{
			if (ability.AbilityEvents.ContainsKey(holdToActivate.Name))
			{
				if (heldKey != KeyCode.None)
				{
					queuedAbilityEvent = new AbilityHoldActivationReplicateData()
					{
						AbilityID = ability.abilityID,
						RemainingTime = ability.activationTime,
						HeldKey = heldKey,
					};

					float activateSpeedReduction = CalculateSpeedReduction(ability.Template.ActivationSpeedReductionAttribute);
					queuedAbilityEvent.ApplySpeedReduction(activateSpeedReduction);
				}
			}
			else
			{
				queuedAbilityEvent = new AbilityActivationReplicateData()
				{
					AbilityID = ability.abilityID,
					RemainingTime = ability.activationTime,
				};

				float activateSpeedReduction = CalculateSpeedReduction(ability.Template.ActivationSpeedReductionAttribute);
				queuedAbilityEvent.ApplySpeedReduction(activateSpeedReduction);
			}
		}
	}

	private void HandleActivation(out AbilityActivationReplicateData activationEventData)
	{
		activationEventData = null;

		if (currentAbilityEvent != null)
		{
			activationEventData = currentAbilityEvent;
		}
		else if (queuedAbilityEvent != null && CanActivate(queuedAbilityEvent, out Ability ability))
		{
			currentAbilityEvent = queuedAbilityEvent;
			activationEventData = currentAbilityEvent;
		}

		queuedAbilityEvent = null;
	}

	/// <summary>
	/// Validates that we can activate the ability and returns it if successful.
	/// </summary>
	private bool CanActivate(AbilityActivationReplicateData activationEvent, out Ability ability)
	{
		// validate UI controls are focused so we aren't casting spells when hovering over interfaces.
		if (!UIManager.ControlHasFocus() && !UIManager.InputControlHasFocus() && !InputManager.MouseMode)
		{
			ability = null;
			return false;
		}

		// we require an ability event to proceed
		if (currentAbilityEvent != null)
		{
			ability = null;
			return false;
		}

		// check that the ability is known, not on cooldown, and we meet the requirements to activate it
		return knownAbilities.TryGetValue(activationEvent.AbilityID, out ability) &&
				!character.CooldownController.IsOnCooldown(ability.Template.Name) &&
				ability.MeetsRequirements(character) &&
				ability.HasResource(character, bloodResourceConversion, bloodResource);
	}

	internal void Cancel()
	{
		currentAbilityEvent = null;
		queuedAbilityEvent = null;

		OnCancelled?.Invoke();
	}

	public void Interrupt(Character attacker)
	{
		if (currentAbilityEvent != null)
		{
			currentAbilityEvent.Interrupt(attacker);
		}
		if (queuedAbilityEvent != null)
		{
			queuedAbilityEvent = null;
		}
	}

	private float CalculateSpeedReduction(CharacterAttributeTemplate attribute)
	{
		if (attribute != null)
		{
			CharacterAttribute speedReduction;
			if (character.AttributeController.TryGetAttribute(attribute.Name, out speedReduction))
			{
				return 1.0f - ((speedReduction.FinalValue * 0.01f).Clamp(0.0f, 1.0f));
			}
		}
		return 1.0f;
	}

	public void RemoveAbility(int referenceID)
	{
		knownAbilities.Remove(referenceID);
	}
}