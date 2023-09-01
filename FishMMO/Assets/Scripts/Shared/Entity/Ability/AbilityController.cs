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
	public const int NO_ABILITY = 0;

	//public Random currentSeed = 12345;

	public Transform abilitySpawner;
	public Character character;
	public Dictionary<int, Ability> knownAbilities = new Dictionary<int, Ability>();
	public HashSet<int> knownTemplates = new HashSet<int>();
	public HashSet<int> knownSpawnEvents = new HashSet<int>();
	public HashSet<int> knownHitEvents = new HashSet<int>();
	public HashSet<int> knownMoveEvents = new HashSet<int>();

	public CharacterAttributeTemplate bloodResource = null;
	public AbilityEvent bloodResourceConversion = null;
	public AbilityEvent charged = null;
	public AbilityEvent channeled = null;

	public Action<float> OnUpdate;
	// Invoked when the current ability is Interrupted.
	public Action OnInterrupt;
	// Invoked when the current ability is Cancelled.
	public Action OnCancel;

	public bool InterruptQueued;
	public int QueuedAbilityID;
	public int CurrentAbilityID;
	public float RemainingTime;
	public KeyCode HeldKey;

	public bool IsActivating { get { return CurrentAbilityID != NO_ABILITY; } }
	public bool AbilityQueued { get { return QueuedAbilityID != NO_ABILITY; } }

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
			HandleCharacterInput(out AbilityActivationReplicateData activationData);
			Replicate(activationData, false);
		}
		if (base.IsServer)
		{
			Replicate(default, true);
			AbilityReconcileData state = new AbilityReconcileData(InterruptQueued,
																  CurrentAbilityID,
																  RemainingTime);
			Reconcile(state, true);
		}
	}

	[Replicate]
	private void Replicate(AbilityActivationReplicateData activationData, bool asServer, Channel channel = Channel.Unreliable, bool replaying = false)
	{
		if (activationData.InterruptQueued)
		{
			OnInterrupt?.Invoke();
			Cancel();
		}
		else if (IsActivating && knownAbilities.TryGetValue(CurrentAbilityID, out Ability ability))
		{
			if (RemainingTime > 0.0f)
			{
				float delta = (float)base.TimeManager.TickDelta;
				RemainingTime -= delta;

				// handle ability update here, display cast bar, display hitbox telegraphs, etc
				OnUpdate?.Invoke(delta);

				// handle held ability updates
				if (HeldKey != KeyCode.None)
				{
					// a held ability hotkey was released or the character can no longer activate the ability
					if (activationData.HeldKey == KeyCode.None || !CanActivate(ability))
					{
						// add ability to cooldowns
						AddCooldown(ability);

						Cancel();
					}
					// channeled abilities like beam effects or a charge rush that are continuously updating or spawning objects should be handled here
					else if (ability.HasAbilityEvent(channeled.ID))
					{
						// channeled abilities consume resources during activation
						ConsumeResources(ability);

						// get target info
						TargetInfo targetInfo = TargetController.GetTarget(character.TargetController, activationData.Ray, ability.range);

						// spawn the ability object
						AbilityObject.Spawn(ability, character, abilitySpawner, targetInfo);
					}
				}
				return;
			}

			// this will allow for charged abilities to remain held for aiming purposes
			if (ability.HasAbilityEvent(charged.ID) &&
				HeldKey != KeyCode.None &&
				activationData.HeldKey != KeyCode.None)
			{
				return;
			}

			// complete the final activation of the ability
			if (CanActivate(ability))
			{
				// consume resources
				ConsumeResources(ability);

				// add ability to cooldowns
				AddCooldown(ability);

				// get target info
				TargetInfo targetInfo = TargetController.GetTarget(character.TargetController, activationData.Ray, ability.range);

				// spawn the ability object
				AbilityObject.Spawn(ability, character, abilitySpawner, targetInfo);
			}
			// reset ability data
			Cancel();
		}
		else if (activationData.QueuedAbilityID != NO_ABILITY &&
				 knownAbilities.TryGetValue(activationData.QueuedAbilityID, out Ability validatedAbility) &&
				 CanActivate(validatedAbility))
		{
			InterruptQueued = false;
			CurrentAbilityID = activationData.QueuedAbilityID;
			RemainingTime = validatedAbility.activationTime * CalculateSpeedReduction(validatedAbility.Template.ActivationSpeedReductionAttribute);
			if (validatedAbility.HasAbilityEvent(channeled.ID) || validatedAbility.HasAbilityEvent(charged.ID))
			{
				HeldKey = activationData.HeldKey;
			}
		}
	}

	[Reconcile]
	private void Reconcile(AbilityReconcileData rd, bool asServer, Channel channel = Channel.Unreliable)
	{
		if (rd.Interrupt && rd.AbilityID == NO_ABILITY)
		{
			OnInterrupt?.Invoke();
			Cancel();
		}
		else
		{
			CurrentAbilityID = rd.AbilityID;
			RemainingTime = rd.RemainingTime;
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

	public void Interrupt(Character attacker)
	{
		InterruptQueued = true;
	}

	public void Activate(int referenceID, KeyCode heldKey)
	{
		// validate UI controls are focused so we aren't casting spells when hovering over interfaces.
		if (!UIManager.ControlHasFocus() && !UIManager.InputControlHasFocus() && !InputManager.MouseMode)
		{
			return;
		}

		if (!IsActivating && !InterruptQueued)
		{
			QueuedAbilityID = referenceID;
			HeldKey = heldKey;
		}
	}

	private void HandleCharacterInput(out AbilityActivationReplicateData activationEventData)
	{
		activationEventData = new AbilityActivationReplicateData(InterruptQueued,
																 QueuedAbilityID,
																 HeldKey,
#if UNITY_CLIENT
																 character.TargetController.currentRay);
#else
																 default);
#endif

		InterruptQueued = false;
		QueuedAbilityID = NO_ABILITY;
	}

	/// <summary>
	/// Validates that we can activate the ability and returns it if successful.
	/// </summary>
	private bool CanActivate(Ability ability)
	{
		return knownAbilities.TryGetValue(ability.abilityID, out Ability knownAbility) &&
				!character.CooldownController.IsOnCooldown(knownAbility.Template.Name) &&
				knownAbility.MeetsRequirements(character) &&
				knownAbility.HasResource(character, bloodResourceConversion, bloodResource);
	}

	internal void Cancel()
	{
		InterruptQueued = false;
		QueuedAbilityID = NO_ABILITY;
		CurrentAbilityID = NO_ABILITY;
		RemainingTime = 0.0f;
		HeldKey = KeyCode.None;

		OnCancel?.Invoke();
	}

	internal void ConsumeResources(Ability ability)
	{
		if (ability.AbilityEvents.ContainsKey(bloodResourceConversion.ID))
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
	}

	internal void AddCooldown(Ability ability)
	{
		AbilityTemplate currentAbilityTemplate = ability.Template;
		if (character.CooldownController != null && ability.cooldown > 0.0f)
		{
			float cooldownReduction = CalculateSpeedReduction(currentAbilityTemplate.CooldownReductionAttribute);
			float cooldown = ability.cooldown * cooldownReduction;

			character.CooldownController.AddCooldown(currentAbilityTemplate.Name, new CooldownInstance(cooldown));
		}
	}

	public void RemoveAbility(int referenceID)
	{
		knownAbilities.Remove(referenceID);
	}

	public bool CanManipulate()
	{
		if (character == null)
			return false;

		/*if (!character.IsSafeZone &&
			  (character.State == CharacterState.Idle ||
			  character.State == CharacterState.Moving) &&
			  character.State != CharacterState.UsingObject &&
			  character.State != CharacterState.IsFrozen &&
			  character.State != CharacterState.IsStunned &&
			  character.State != CharacterState.IsMesmerized) return true;
		*/
		return true;
	}

	public void LearnAbilityTypes(AbilityTemplate[] abilityTemplates, AbilityEvent[] abilityEvents)
	{
		if (abilityTemplates != null)
		{
			for (int i = 0; i < abilityTemplates.Length; ++i)
			{
				if (!knownTemplates.Contains(abilityTemplates[i].ID))
				{
					knownTemplates.Add(abilityTemplates[i].ID);
				}
			}
		}
		if (abilityEvents != null)
		{
			for (int i = 0; i < abilityEvents.Length; ++i)
			{
				AbilityEvent abilityEvent = abilityEvents[i];
				if (abilityEvent is HitEvent)
				{
					if (!knownHitEvents.Contains(abilityEvent.ID))
					{
						knownHitEvents.Add(abilityEvent.ID);
					}
				}
				else if (abilityEvent is MoveEvent)
				{
					if (!knownMoveEvents.Contains(abilityEvent.ID))
					{
						knownMoveEvents.Add(abilityEvent.ID);
					}
				}
				else if (abilityEvent is SpawnEvent)
				{
					if (!knownSpawnEvents.Contains(abilityEvent.ID))
					{
						knownSpawnEvents.Add(abilityEvent.ID);
					}
				}
			}
		}
	}

	public void LearnAbility(Ability ability)
	{

	}
}