using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public class Ability
	{
		public long ID;
		public float ActivationTime;
		public float LifeTime;
		public float Cooldown;
		public float Range { get { return Speed * LifeTime; } }
		public float Speed;

		public AbilityTemplate Template { get; private set; }
		public string Name { get; set; }
		public string CachedTooltip { get; private set; }
		public AbilityResourceDictionary Resources { get; private set; }
		public AbilityResourceDictionary RequiredAttributes { get; private set; }

		public AbilityTypeOverrideEventType TypeOverride { get; private set; }

		// All triggers by ID for quick access
		public Dictionary<int, AbilityEvent> AbilityEvents = new Dictionary<int, AbilityEvent>();
		public Dictionary<int, AbilityOnTickEvent> OnTickEvents = new Dictionary<int, AbilityOnTickEvent>();
		public Dictionary<int, AbilityOnHitEvent> OnHitEvents = new Dictionary<int, AbilityOnHitEvent>();
		public Dictionary<int, AbilityOnPreSpawnEvent> OnPreSpawnEvents = new Dictionary<int, AbilityOnPreSpawnEvent>();
		public Dictionary<int, AbilityOnSpawnEvent> OnSpawnEvents = new Dictionary<int, AbilityOnSpawnEvent>();
		public Dictionary<int, AbilityOnDestroyEvent> OnDestroyEvents = new Dictionary<int, AbilityOnDestroyEvent>();

		/// <summary>
		/// Cache of all active ability Objects. <ContainerID, <AbilityObjectID, AbilityObject>>
		/// </summary>
		public Dictionary<int, Dictionary<int, AbilityObject>> Objects { get; set; }

		public int TotalResourceCost
		{
			get
			{
				int totalCost = 0;
				foreach (int cost in Resources.Values)
				{
					totalCost += cost;
				}
				return totalCost;
			}
		}

		public Ability(AbilityTemplate template, List<int> abilityEvents = null)
		{
			Initialize(-1, template, abilityEvents);
		}

		public Ability(long abilityID, int templateID, List<int> abilityEvents = null)
		{
			Initialize(abilityID, AbilityTemplate.Get<AbilityTemplate>(templateID), abilityEvents);
		}

		public Ability(long abilityID, AbilityTemplate template, List<int> abilityEvents = null)
		{
			Initialize(abilityID, template, abilityEvents);
		}

		private void Initialize(long abilityID, AbilityTemplate template, List<int> abilityEvents)
		{
			ID = abilityID;
			Template = template;
			Name = Template.Name;
			CachedTooltip = null;

			AddEvents(Template.OnTickEvents);
			AddEvents(Template.OnHitEvents);
			AddEvents(Template.OnPreSpawnEvents);
			AddEvents(Template.OnSpawnEvents);
			AddEvents(Template.OnDestroyEvents);

			if (abilityEvents != null)
			{
				foreach (var eventId in abilityEvents)
				{
					var abilityEvent = AbilityEvent.Get<AbilityEvent>(eventId);
					if (abilityEvent != null && !AbilityEvents.ContainsKey(abilityEvent.ID))
						AbilityEvents.Add(abilityEvent.ID, abilityEvent);
				}
			}

			AddTemplateModifiers(Template);
		}


		public void AddEvents<T>(List<T> abilityEvents) where T : AbilityEvent
		{
			if (abilityEvents == null) return;

			foreach (var abilityEvent in abilityEvents)
			{
				if (abilityEvent == null) continue;

				// Always add to AbilityEvents
				if (!AbilityEvents.ContainsKey(abilityEvent.ID))
					AbilityEvents.Add(abilityEvent.ID, abilityEvent);

				switch (abilityEvent)
				{
					case AbilityOnTickEvent tickEvent:
						if (!OnTickEvents.ContainsKey(tickEvent.ID))
						{
							OnTickEvents.Add(tickEvent.ID, tickEvent);
							AddEventModifiers(tickEvent);
						}
						break;
					case AbilityOnHitEvent hitEvent:
						if (!OnHitEvents.ContainsKey(hitEvent.ID))
						{
							OnHitEvents.Add(hitEvent.ID, hitEvent);
							AddEventModifiers(hitEvent);
						}
						break;
					case AbilityOnPreSpawnEvent preSpawnEvent:
						if (!OnPreSpawnEvents.ContainsKey(preSpawnEvent.ID))
						{
							OnPreSpawnEvents.Add(preSpawnEvent.ID, preSpawnEvent);
							AddEventModifiers(preSpawnEvent);
						}
						break;
					case AbilityOnSpawnEvent spawnEvent:
						if (!OnSpawnEvents.ContainsKey(spawnEvent.ID))
						{
							OnSpawnEvents.Add(spawnEvent.ID, spawnEvent);
							AddEventModifiers(spawnEvent);
						}
						break;
					case AbilityOnDestroyEvent destroyEvent:
						if (!OnDestroyEvents.ContainsKey(destroyEvent.ID))
						{
							OnDestroyEvents.Add(destroyEvent.ID, destroyEvent);
							AddEventModifiers(destroyEvent);
						}
						break;
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void AddEventModifiers(AbilityEvent abilityEvent)
		{
			AddStats(abilityEvent.ActivationTime, abilityEvent.LifeTime, abilityEvent.Cooldown, abilityEvent.Speed,
				abilityEvent.Resources, abilityEvent.RequiredAttributes);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void AddTemplateModifiers(AbilityTemplate template)
		{
			AddStats(template.ActivationTime, template.LifeTime, template.Cooldown, template.Speed,
				template.Resources, template.RequiredAttributes);
		}

		private void AddStats(float activationTime, float lifeTime, float cooldown, float speed,
			IDictionary<CharacterAttributeTemplate, int> addResources,
			IDictionary<CharacterAttributeTemplate, int> addRequiredAttributes)
		{
			ActivationTime += activationTime;
			LifeTime += lifeTime;
			Cooldown += cooldown;
			Speed += speed;

			if (Resources == null)
			{
				Resources = new AbilityResourceDictionary();
			}

			if (RequiredAttributes == null)
			{
				RequiredAttributes = new AbilityResourceDictionary();
			}

			if (addResources != null)
			{
				foreach (var pair in addResources)
				{
					if (!Resources.ContainsKey(pair.Key))
					{
						Resources[pair.Key] = pair.Value;
					}
					else
					{
						Resources[pair.Key] += pair.Value;
					}
				}
			}

			if (addRequiredAttributes != null)
			{
				foreach (var pair in addRequiredAttributes)
				{
					if (!RequiredAttributes.ContainsKey(pair.Key))
					{
						RequiredAttributes[pair.Key] = pair.Value;
					}
					else
					{
						RequiredAttributes[pair.Key] += pair.Value;
					}
				}
			}
		}

		public bool MeetsRequirements(ICharacter character)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return false;
			}
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in RequiredAttributes)
			{
				if (!attributeController.TryGetResourceAttribute(pair.Key.ID, out CharacterResourceAttribute requirement) ||
					requirement.CurrentValue < pair.Value)
				{
					return false;
				}
			}
			return true;
		}

		public bool TryGetAbilityEvent(int eventID, out AbilityEvent abilityEvent)
		{
			return AbilityEvents.TryGetValue(eventID, out abilityEvent);
		}

		public bool HasAbilityEvent(int eventID)
		{
			return AbilityEvents.ContainsKey(eventID);
		}

		public bool RemoveAbilityEvent(int eventID)
		{
			if (AbilityEvents.TryGetValue(eventID, out var abilityEvent))
			{
				AbilityEvents.Remove(eventID);
				OnTickEvents.Remove(eventID);
				OnHitEvents.Remove(eventID);
				OnPreSpawnEvents.Remove(eventID);
				OnSpawnEvents.Remove(eventID);
				OnDestroyEvents.Remove(eventID);

				ActivationTime -= abilityEvent.ActivationTime;
				LifeTime -= abilityEvent.LifeTime;
				Cooldown -= abilityEvent.Cooldown;
				Speed -= abilityEvent.Speed;

				if (Resources != null)
				{
					foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.Resources)
					{
						if (Resources.ContainsKey(pair.Key))
						{
							Resources[pair.Key] -= pair.Value;
						}
					}
				}

				if (RequiredAttributes != null)
				{
					foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in abilityEvent.RequiredAttributes)
					{
						if (RequiredAttributes.ContainsKey(pair.Key))
						{
							RequiredAttributes[pair.Key] -= pair.Value;
						}
					}
				}
				return true;
			}
			return false;
		}

		public bool HasResource(ICharacter character, AbilityEvent resourceConversionTrigger = null)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return false;
			}
			if (resourceConversionTrigger != null && AbilityEvents.ContainsKey(resourceConversionTrigger.ID))
			{
				int totalCost = TotalResourceCost;
				CharacterResourceAttribute resource;
				if (!attributeController.TryGetHealthAttribute(out resource) ||
					resource.CurrentValue < totalCost)
				{
					return false;
				}
				return true;
			}
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in Resources)
			{
				CharacterResourceAttribute resource;
				if (!attributeController.TryGetResourceAttribute(pair.Key.ID, out resource) ||
					resource.CurrentValue < pair.Value)
				{
					return false;
				}
			}
			return true;
		}

		public void ConsumeResources(ICharacter character, AbilityEvent resourceConversionTrigger = null)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}
			if (resourceConversionTrigger != null && AbilityEvents.ContainsKey(resourceConversionTrigger.ID))
			{
				int totalCost = TotalResourceCost;
				CharacterResourceAttribute resource;
				if (attributeController.TryGetHealthAttribute(out resource) &&
					resource.CurrentValue >= totalCost)
				{
					resource.Consume(totalCost);
				}
				return;
			}
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in Resources)
			{
				CharacterResourceAttribute resource;
				if (attributeController.TryGetResourceAttribute(pair.Key.ID, out resource) &&
					resource.CurrentValue >= pair.Value)
				{
					resource.Consume(pair.Value);
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveAbilityObject(int containerID, int objectID)
		{
			if (Objects.TryGetValue(containerID, out Dictionary<int, AbilityObject> container))
			{
				container.Remove(objectID);
			}
		}

		public string Tooltip()
		{
			if (!string.IsNullOrWhiteSpace(CachedTooltip))
			{
				return CachedTooltip;
			}

			CachedTooltip = Template.Tooltip(null);

			if (TypeOverride != null)
			{
				CachedTooltip += RichText.Format($"\r\nType: {TypeOverride.OverrideAbilityType}", true, "f5ad6eFF", "120%");
			}
			else
			{
				CachedTooltip += RichText.Format($"\r\nType: {Template.Type}", true, "f5ad6eFF", "120%");
			}

			return CachedTooltip;
		}
	}
}