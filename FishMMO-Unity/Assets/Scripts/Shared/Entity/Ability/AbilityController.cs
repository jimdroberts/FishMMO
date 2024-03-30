using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class AbilityController : CharacterBehaviour, IAbilityController
	{
		public const long NO_ABILITY = 0;

		private long currentAbilityID;
		private bool interruptQueued;
		private long queuedAbilityID;
		private float remainingTime;
		private KeyCode heldKey;
		//private Random currentSeed = 12345;

		public Transform AbilitySpawner;
		public CharacterAttributeTemplate BloodResourceTemplate;
		public CharacterAttributeTemplate AttackSpeedReductionTemplate;
		public AbilityEvent BloodResourceConversionTemplate;
		public AbilityEvent ChargedTemplate;
		public AbilityEvent ChanneledTemplate;

		public event Func<bool> OnCanManipulate;

		public event Action<string, float, float> OnUpdate;
		// Invoked when the current ability is Interrupted.
		public event Action OnInterrupt;
		// Invoked when the current ability is Cancelled.
		public event Action OnCancel;

		// UI
		public event Action OnReset;
		public event Action<long, Ability> OnAddAbility;
		public event Action<long, BaseAbilityTemplate> OnAddKnownAbility;

		public Dictionary<long, Ability> KnownAbilities { get; private set; }
		public HashSet<int> KnownBaseAbilities { get; private set; }
		public HashSet<int> KnownEvents { get; private set; }
		public HashSet<int> KnownSpawnEvents { get; private set; }
		public HashSet<int> KnownHitEvents { get; private set; }
		public HashSet<int> KnownMoveEvents { get; private set; }
		public bool IsActivating { get { return currentAbilityID != NO_ABILITY; } }
		public bool AbilityQueued { get { return queuedAbilityID != NO_ABILITY; } }

		public override void OnAwake()
		{
			base.OnAwake();

			KnownAbilities = new Dictionary<long, Ability>();
			KnownBaseAbilities = new HashSet<int>();
			KnownEvents = new HashSet<int>();
			KnownSpawnEvents = new HashSet<int>();
			KnownHitEvents = new HashSet<int>();
			KnownMoveEvents = new HashSet<int>();
		}

		public override void OnStartNetwork()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		public override void OnStopNetwork()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				this.enabled = false;
			}
			else
			{
				ClientManager.RegisterBroadcast<KnownAbilityAddBroadcast>(OnClientKnownAbilityAddBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityAddMultipleBroadcast>(OnClientKnownAbilityAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<AbilityAddBroadcast>(OnClientAbilityAddBroadcastReceived);
				ClientManager.RegisterBroadcast<AbilityAddMultipleBroadcast>(OnClientAbilityAddMultipleBroadcastReceived);
			}
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<KnownAbilityAddBroadcast>(OnClientKnownAbilityAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityAddMultipleBroadcast>(OnClientKnownAbilityAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<AbilityAddBroadcast>(OnClientAbilityAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<AbilityAddMultipleBroadcast>(OnClientAbilityAddMultipleBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityAddBroadcastReceived(KnownAbilityAddBroadcast msg, Channel channel)
		{
			BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(msg.templateID);
			if (baseAbilityTemplate != null)
			{
				LearnBaseAbilities(new List<BaseAbilityTemplate>() { baseAbilityTemplate });

				OnAddKnownAbility?.Invoke(baseAbilityTemplate.ID, baseAbilityTemplate);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityAddMultipleBroadcastReceived(KnownAbilityAddMultipleBroadcast msg, Channel channel)
		{
			List<BaseAbilityTemplate> templates = new List<BaseAbilityTemplate>();
			foreach (KnownAbilityAddBroadcast knownAbility in msg.abilities)
			{
				BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(knownAbility.templateID);
				if (baseAbilityTemplate != null)
				{
					templates.Add(baseAbilityTemplate);

					OnAddKnownAbility?.Invoke(baseAbilityTemplate.ID, baseAbilityTemplate);
				}
			}
			LearnBaseAbilities(templates);
		}

		/// <summary>
		/// Server sent an add ability broadcast.
		/// </summary>
		private void OnClientAbilityAddBroadcastReceived(AbilityAddBroadcast msg, Channel channel)
		{
			AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(msg.templateID);
			if (abilityTemplate != null)
			{
				Ability newAbility = new Ability(msg.id, abilityTemplate, msg.events);
				LearnAbility(newAbility);

				OnAddAbility?.Invoke(newAbility.ID, newAbility);
			}
		}

		/// <summary>
		/// Server sent an add multiple ability broadcast.
		/// </summary>
		private void OnClientAbilityAddMultipleBroadcastReceived(AbilityAddMultipleBroadcast msg, Channel channel)
		{
			foreach (AbilityAddBroadcast ability in msg.abilities)
			{
				AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(ability.templateID);
				if (abilityTemplate != null)
				{
					Ability newAbility = new Ability(ability.id, abilityTemplate, ability.events);
					LearnAbility(newAbility);

					OnAddAbility?.Invoke(newAbility.ID, newAbility);
				}
			}
		}
#endif

		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			int abilityCount = reader.ReadInt32();
			if (abilityCount < 1)
			{
				return;
			}
			KnownAbilities.Clear();

			for (int i = 0; i < abilityCount; ++i)
			{
				long abilityID = reader.ReadInt64();
				int abilityTemplateID = reader.ReadInt32();

				List<int> abilityEvents = new List<int>();
				int abilityEventsCount = reader.ReadInt32();
				for (int j = 0; j < abilityEventsCount; ++j)
				{
					abilityEvents.Add(reader.ReadInt32());
				}
				Ability ability = new Ability(abilityID, abilityTemplateID, abilityEvents);

				LearnAbility(ability);
			}

			// invoke client reset event
			if (base.Owner.IsLocalClient)
			{
				OnReset?.Invoke();

				foreach (Ability ability in KnownAbilities.Values)
				{
					// update our client with abilities
					OnAddAbility?.Invoke(ability.ID, ability);
				}
			}
		}

		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			writer.WriteInt32(KnownAbilities.Count);
			foreach (Ability ability in KnownAbilities.Values)
			{
				writer.WriteInt64(ability.ID);
				writer.WriteInt32(ability.Template.ID);

				writer.WriteInt32(ability.AbilityEvents.Count);
				foreach (int abilityEvent in ability.AbilityEvents.Keys)
				{
					writer.WriteInt32(abilityEvent);
				}
			}
		}

		private void TimeManager_OnTick()
		{
			Replicate(HandleCharacterInput());
			if (base.IsServerStarted)
			{
				AbilityReconcileData state = new AbilityReconcileData(interruptQueued,
																	  currentAbilityID,
																	  remainingTime);
				Reconcile(state);
			}
		}

		private AbilityActivationReplicateData HandleCharacterInput()
		{
			if (!base.IsOwner)
			{
				return default;
			}

			AbilityActivationReplicateData activationEventData = new AbilityActivationReplicateData(interruptQueued,
																									queuedAbilityID,
																									heldKey);
			// clear the locally queued data
			interruptQueued = false;
			queuedAbilityID = NO_ABILITY;
			heldKey = KeyCode.None;

			return activationEventData;
		}

		[Replicate]
		private void Replicate(AbilityActivationReplicateData activationData, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			if (activationData.InterruptQueued)
			{
				OnInterrupt?.Invoke();
				Cancel();
			}
			else if (IsActivating &&
					 KnownAbilities.TryGetValue(currentAbilityID, out Ability currentAbility))
			{
				Debug.Log("Activating " + currentAbility.ID);
				remainingTime -= (float)base.TimeManager.TickDelta;

				if (remainingTime > 0.0f)
				{
					// handle ability update here, display cast bar, display hitbox telegraphs, etc
					OnUpdate?.Invoke(currentAbility.Name, remainingTime, currentAbility.ActivationTime * CalculateSpeedReduction(currentAbility.Template.ActivationSpeedReductionAttribute));

					// handle held ability updates
					if (heldKey != KeyCode.None)
					{
						// a held ability hotkey was released or the character can no longer activate the ability
						if (activationData.HeldKey == KeyCode.None || !CanActivate(currentAbility))
						{
							// add ability to cooldowns
							AddCooldown(currentAbility);

							Cancel();
						}
						// channeled abilities like beam effects or a charge rush that are continuously updating or spawning objects should be handled here
						else if (ChanneledTemplate != null &&
								 currentAbility.HasAbilityEvent(ChanneledTemplate.ID) &&
								 Character.TryGet(out ITargetController t))
						{
							// get target info
							TargetInfo targetInfo = t.UpdateTarget(Character.CharacterController.VirtualCameraPosition,
																   Character.CharacterController.VirtualCameraRotation * Vector3.forward,
																   currentAbility.Range);

							// spawn the ability object
							if (AbilityObject.TrySpawn(currentAbility, Character, this, AbilitySpawner, targetInfo))
							{
								// channeled abilities consume resources during activation
								currentAbility.ConsumeResources(Character, BloodResourceConversionTemplate, BloodResourceTemplate);
							}
						}
					}
					return;
				}

				// this will allow for charged abilities to remain held
				if (ChargedTemplate != null &&
					currentAbility.HasAbilityEvent(ChargedTemplate.ID) &&
					heldKey != KeyCode.None &&
					activationData.HeldKey != KeyCode.None)
				{
					return;
				}

				// complete the final activation of the ability
				if (CanActivate(currentAbility) &&
					Character.TryGet(out ITargetController tc))
				{
					// get target info
					TargetInfo targetInfo = tc.UpdateTarget(Character.CharacterController.VirtualCameraPosition,
															Character.CharacterController.VirtualCameraRotation * Vector3.forward,
															currentAbility.Range);

					// spawn the ability object
					if (AbilityObject.TrySpawn(currentAbility, Character, this, AbilitySpawner, targetInfo))
					{
						// consume resources
						currentAbility.ConsumeResources(Character, BloodResourceConversionTemplate, BloodResourceTemplate);

						// add ability to cooldowns
						AddCooldown(currentAbility);
					}
				}
				// reset ability data
				Cancel();
			}
			else if (activationData.QueuedAbilityID != NO_ABILITY &&
					 KnownAbilities.TryGetValue(activationData.QueuedAbilityID, out Ability validatedAbility) &&
					 CanActivate(validatedAbility))
			{
				Debug.Log("New Ability Activation " + validatedAbility.ID);

				interruptQueued = false;
				currentAbilityID = activationData.QueuedAbilityID;
				remainingTime = validatedAbility.ActivationTime * CalculateSpeedReduction(validatedAbility.Template.ActivationSpeedReductionAttribute);
				heldKey = activationData.HeldKey;
			}
		}

		[Reconcile]
		private void Reconcile(AbilityReconcileData rd, Channel channel = Channel.Unreliable)
		{
			if (rd.Interrupt ||
				rd.AbilityID == NO_ABILITY ||
				!KnownAbilities.TryGetValue(rd.AbilityID, out Ability ability))
			{
				if (currentAbilityID != NO_ABILITY)
				{
					OnInterrupt?.Invoke();
					Cancel();
				}
			}
			else
			{
				currentAbilityID = rd.AbilityID;
				remainingTime = rd.RemainingTime;
			}
		}

		public float CalculateSpeedReduction(CharacterAttributeTemplate attribute)
		{
			if (attribute != null &&
				Character.TryGet(out ICharacterAttributeController attributeController))
			{
				CharacterAttribute speedReduction;
				if (attributeController.TryGetAttribute(attribute.ID, out speedReduction))
				{
					return 1.0f - ((speedReduction.FinalValueAsPct).Clamp(0.0f, 1.0f));
				}
			}
			return 1.0f;
		}

		public void Interrupt(Character attacker)
		{
			interruptQueued = true;
		}


		public void Activate(long referenceID, KeyCode heldKey)
		{
#if !UNITY_SERVER
			// validate UI controls are focused so we aren't casting spells when hovering over interfaces.
			bool canManipulate = OnCanManipulate == null ? true : (bool)OnCanManipulate?.Invoke();

			if (!canManipulate ||
				!CanManipulate())
			{
				//Debug.Log("Cannot activate");
				return;
			}

			if (!IsActivating && !interruptQueued)
			{
				//Debug.Log("Activating " + referenceID);
				queuedAbilityID = referenceID;
				this.heldKey = heldKey;
			}
#endif
		}

		/// <summary>
		/// Validates that we can activate the ability and returns it if successful.
		/// </summary>
		private bool CanActivate(Ability ability)
		{
			if (ability == null)
			{
				return false;
			}
			if (!CanManipulate())
			{
				return false;
			}
			if (!KnownAbilities.TryGetValue(ability.ID, out Ability knownAbility))
			{
				return false;
			}
			if (!Character.TryGet(out ICooldownController cooldownController) ||
				cooldownController.IsOnCooldown(knownAbility.Template.Name))
			{
				return false;
			}
			if (!knownAbility.MeetsRequirements(Character) ||
				!knownAbility.HasResource(Character, BloodResourceConversionTemplate, BloodResourceTemplate))
			{
				return false;
			}
			return true;
		}

		internal void Cancel()
		{
			//Debug.Log("Cancel");

			interruptQueued = false;
			queuedAbilityID = NO_ABILITY;
			currentAbilityID = NO_ABILITY;
			remainingTime = 0.0f;
			heldKey = KeyCode.None;

			OnCancel?.Invoke();
		}

		internal void AddCooldown(Ability ability)
		{
			AbilityTemplate currentAbilityTemplate = ability.Template;
			if (ability.Cooldown > 0.0f &&
				Character.TryGet(out ICooldownController cooldownController))
			{
				float cooldownReduction = CalculateSpeedReduction(currentAbilityTemplate.CooldownReductionAttribute);
				float cooldown = ability.Cooldown * cooldownReduction;

				cooldownController.AddCooldown(currentAbilityTemplate.Name, new CooldownInstance(cooldown));
			}
		}

		public void RemoveAbility(int referenceID)
		{
			KnownAbilities.Remove(referenceID);
		}

		public bool CanManipulate()
		{
			if (Character == null ||
				Character.IsTeleporting ||
				!Character.IsSpawned)
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

		public bool KnowsAbility(int abilityID)
		{
			if ((KnownBaseAbilities != null && KnownBaseAbilities.Contains(abilityID)) ||
				(KnownEvents != null && KnownEvents.Contains(abilityID)))
			{
				return true;
			}
			return false;
		}

		public bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null)
		{
			if (abilityTemplates == null)
			{
				return false;
			}

			for (int i = 0; i < abilityTemplates.Count; ++i)
			{
				// if the template is an ability event we add them to their mapped containers
				AbilityEvent abilityEvent = abilityTemplates[i] as AbilityEvent;
				if (abilityEvent != null)
				{
					// add the event to the global events map
					if (!KnownEvents.Contains(abilityEvent.ID))
					{
						KnownEvents.Add(abilityEvent.ID);
					}

					// figure out what kind of event it is and add to the respective category
					if (abilityEvent is HitEvent)
					{
						if (!KnownHitEvents.Contains(abilityEvent.ID))
						{
							KnownHitEvents.Add(abilityEvent.ID);
						}
					}
					else if (abilityEvent is MoveEvent)
					{
						if (!KnownMoveEvents.Contains(abilityEvent.ID))
						{
							KnownMoveEvents.Add(abilityEvent.ID);
						}
					}
					else if (abilityEvent is SpawnEvent)
					{
						if (!KnownSpawnEvents.Contains(abilityEvent.ID))
						{
							KnownSpawnEvents.Add(abilityEvent.ID);
						}
					}
				}
				else
				{
					AbilityTemplate abilityTemplate = abilityTemplates[i] as AbilityTemplate;
					if (abilityTemplate != null)
					{
						if (!KnownBaseAbilities.Contains(abilityTemplate.ID))
						{
							KnownBaseAbilities.Add(abilityTemplate.ID);
						}
					}
				}
			}
			return true;
		}

		public void LearnAbility(Ability ability)
		{
			if (ability == null)
			{
				return;
			}
			KnownAbilities[ability.ID] = ability;
		}
	}
}