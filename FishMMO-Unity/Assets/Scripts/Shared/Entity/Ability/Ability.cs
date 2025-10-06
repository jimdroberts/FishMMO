using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an in-game ability instance, constructed from an <see cref="AbilityTemplate"/> and containing all runtime state, events, and resource requirements.
	/// </summary>
	public class Ability
	{
		/// <summary>
		/// Unique identifier for this ability instance.
		/// </summary>
		public long ID;

		/// <summary>
		/// Total activation time for this ability, including all modifiers.
		/// </summary>
		public float ActivationTime;

		/// <summary>
		/// Total lifetime of the ability effect, including all modifiers.
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// Total cooldown for this ability, including all modifiers.
		/// </summary>
		public float Cooldown;

		/// <summary>
		/// The effective range of the ability, calculated as <see cref="Speed"/> * <see cref="LifeTime"/>.
		/// </summary>
		public float Range { get { return Speed * LifeTime; } }

		/// <summary>
		/// Total speed of the ability effect, including all modifiers.
		/// </summary>
		public float Speed;

		/// <summary>
		/// The template from which this ability was constructed.
		/// </summary>
		public AbilityTemplate Template { get; private set; }

		/// <summary>
		/// The display name of the ability.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Cached tooltip string for this ability, for UI display.
		/// </summary>
		public string CachedTooltip { get; private set; }

		/// <summary>
		/// The total resources required to use this ability, including all modifiers.
		/// </summary>
		public AbilityResourceDictionary Resources { get; private set; }

		/// <summary>
		/// The total required attributes to use this ability, including all modifiers.
		/// </summary>
		public AbilityResourceDictionary RequiredAttributes { get; private set; }

		/// <summary>
		/// Optional override for the ability type, set by certain events.
		/// </summary>
		public AbilityTypeOverrideEventType TypeOverride { get; private set; }

		/// <summary>
		/// All ability events, indexed by event ID, for quick access.
		/// </summary>
		public Dictionary<int, AbilityEvent> AbilityEvents = new Dictionary<int, AbilityEvent>();

		/// <summary>
		/// All OnTick events, indexed by event ID.
		/// </summary>
		public Dictionary<int, AbilityOnTickEvent> OnTickEvents = new Dictionary<int, AbilityOnTickEvent>();

		/// <summary>
		/// All OnHit events, indexed by event ID.
		/// </summary>
		public Dictionary<int, AbilityOnHitEvent> OnHitEvents = new Dictionary<int, AbilityOnHitEvent>();

		/// <summary>
		/// All OnPreSpawn events, indexed by event ID.
		/// </summary>
		public Dictionary<int, AbilityOnPreSpawnEvent> OnPreSpawnEvents = new Dictionary<int, AbilityOnPreSpawnEvent>();

		/// <summary>
		/// All OnSpawn events, indexed by event ID.
		/// </summary>
		public Dictionary<int, AbilityOnSpawnEvent> OnSpawnEvents = new Dictionary<int, AbilityOnSpawnEvent>();

		/// <summary>
		/// All OnDestroy events, indexed by event ID.
		/// </summary>
		public Dictionary<int, AbilityOnDestroyEvent> OnDestroyEvents = new Dictionary<int, AbilityOnDestroyEvent>();

		/// <summary>
		/// Cache of all active ability objects, organized as a dictionary mapping container IDs to dictionaries of ability object IDs and their corresponding <see cref="AbilityObject"/> instances.
		/// </summary>
		public Dictionary<int, Dictionary<int, AbilityObject>> Objects { get; set; }

		/// <summary>
		/// The total resource cost for this ability, summing all resource values.
		/// </summary>
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

		/// <summary>
		/// Constructs an ability from a template and optional event list.
		/// </summary>
		/// <param name="template">The ability template to use.</param>
		/// <param name="abilityEvents">Optional list of event IDs to add to the ability.</param>
		public Ability(AbilityTemplate template, List<int> abilityEvents = null)
		{
			Initialize(-1, template, abilityEvents);
		}

		/// <summary>
		/// Constructs an ability from an ability ID, template ID, and optional event list.
		/// </summary>
		/// <param name="abilityID">The unique ability instance ID.</param>
		/// <param name="templateID">The template ID to look up.</param>
		/// <param name="abilityEvents">Optional list of event IDs to add to the ability.</param>
		public Ability(long abilityID, int templateID, List<int> abilityEvents = null)
		{
			Initialize(abilityID, AbilityTemplate.Get<AbilityTemplate>(templateID), abilityEvents);
		}

		/// <summary>
		/// Constructs an ability from an ability ID, template, and optional event list.
		/// </summary>
		/// <param name="abilityID">The unique ability instance ID.</param>
		/// <param name="template">The ability template to use.</param>
		/// <param name="abilityEvents">Optional list of event IDs to add to the ability.</param>
		public Ability(long abilityID, AbilityTemplate template, List<int> abilityEvents = null)
		{
			Initialize(abilityID, template, abilityEvents);
		}

		/// <summary>
		/// Initializes the ability instance from the given template and event list.
		/// </summary>
		/// <param name="abilityID">The unique ability instance ID.</param>
		/// <param name="template">The ability template to use.</param>
		/// <param name="abilityEvents">Optional list of event IDs to add to the ability.</param>
		private void Initialize(long abilityID, AbilityTemplate template, List<int> abilityEvents)
		{
			ID = abilityID;
			Template = template;
			Name = Template.Name;
			CachedTooltip = null;

			// Add all events from the template to the ability's event dictionaries.
			AddEvents(Template.OnTickEvents);
			AddEvents(Template.OnHitEvents);
			AddEvents(Template.OnPreSpawnEvents);
			AddEvents(Template.OnSpawnEvents);
			AddEvents(Template.OnDestroyEvents);

			// Add any additional events provided in the constructor.
			if (abilityEvents != null)
			{
				foreach (var eventId in abilityEvents)
				{
					var abilityEvent = AbilityEvent.Get<AbilityEvent>(eventId);
					if (abilityEvent != null && !AbilityEvents.ContainsKey(abilityEvent.ID))
						AbilityEvents.Add(abilityEvent.ID, abilityEvent);
				}
			}

			// Apply all stat/resource/attribute modifiers from the template.
			AddTemplateModifiers(Template);
		}

		/// <summary>
		/// Adds a list of ability events to the appropriate event dictionaries and applies their stat/resource/attribute modifiers.
		/// </summary>
		/// <typeparam name="T">The type of ability event.</typeparam>
		/// <param name="abilityEvents">The list of events to add.</param>
		public void AddEvents<T>(List<T> abilityEvents) where T : AbilityEvent
		{
			if (abilityEvents == null) return;

			foreach (var abilityEvent in abilityEvents)
			{
				if (abilityEvent == null) continue;

				// Always add to AbilityEvents
				if (!AbilityEvents.ContainsKey(abilityEvent.ID))
					AbilityEvents.Add(abilityEvent.ID, abilityEvent);

				// Add to the specific event dictionary and apply modifiers
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

		/// <summary>
		/// Adds the stat/resource/attribute modifiers from an ability event to this ability.
		/// </summary>
		/// <param name="abilityEvent">The event whose modifiers to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void AddEventModifiers(AbilityEvent abilityEvent)
		{
			AddStats(abilityEvent.ActivationTime, abilityEvent.LifeTime, abilityEvent.Cooldown, abilityEvent.Speed,
				abilityEvent.Resources, abilityEvent.RequiredAttributes);
		}

		/// <summary>
		/// Adds the stat/resource/attribute modifiers from a template to this ability.
		/// </summary>
		/// <param name="template">The template whose modifiers to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void AddTemplateModifiers(AbilityTemplate template)
		{
			AddStats(template.ActivationTime, template.LifeTime, template.Cooldown, template.Speed,
				template.Resources, template.RequiredAttributes);
		}

		/// <summary>
		/// Adds stat/resource/attribute modifiers to this ability.
		/// </summary>
		/// <param name="activationTime">Activation time to add.</param>
		/// <param name="lifeTime">Lifetime to add.</param>
		/// <param name="cooldown">Cooldown to add.</param>
		/// <param name="speed">Speed to add.</param>
		/// <param name="addResources">Resources to add.</param>
		/// <param name="addRequiredAttributes">Required attributes to add.</param>
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

		/// <summary>
		/// Checks if the given character meets all required attributes for this ability.
		/// </summary>
		/// <param name="character">The character to check.</param>
		/// <returns>True if requirements are met, false otherwise.</returns>
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

		/// <summary>
		/// Attempts to get an ability event by its event ID.
		/// </summary>
		/// <param name="eventID">The event ID to look up.</param>
		/// <param name="abilityEvent">The found ability event, or null if not found.</param>
		/// <returns>True if found, false otherwise.</returns>
		public bool TryGetAbilityEvent(int eventID, out AbilityEvent abilityEvent)
		{
			return AbilityEvents.TryGetValue(eventID, out abilityEvent);
		}

		/// <summary>
		/// Checks if this ability contains an event with the given event ID.
		/// </summary>
		/// <param name="eventID">The event ID to check.</param>
		/// <returns>True if the event exists, false otherwise.</returns>
		public bool HasAbilityEvent(int eventID)
		{
			return AbilityEvents.ContainsKey(eventID);
		}

		/// <summary>
		/// Removes an ability event by its event ID and updates all stat/resource/attribute modifiers accordingly.
		/// </summary>
		/// <param name="eventID">The event ID to remove.</param>
		/// <returns>True if the event was removed, false otherwise.</returns>
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

		/// <summary>
		/// Checks if the given character has enough resources to use this ability.
		/// </summary>
		/// <param name="character">The character to check.</param>
		/// <param name="resourceConversionTrigger">Optional event that allows resource conversion (e.g., health for mana).</param>
		/// <returns>True if the character has enough resources, false otherwise.</returns>
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

		/// <summary>
		/// Consumes the required resources from the given character to use this ability.
		/// </summary>
		/// <param name="character">The character using the ability.</param>
		/// <param name="resourceConversionTrigger">Optional event that allows resource conversion (e.g., health for mana).</param>
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

		/// <summary>
		/// Removes an ability object from the cache by container and object ID.
		/// </summary>
		/// <param name="containerID">The container ID.</param>
		/// <param name="objectID">The object ID to remove.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveAbilityObject(int containerID, int objectID)
		{
			if (Objects.TryGetValue(containerID, out Dictionary<int, AbilityObject> container))
			{
				container.Remove(objectID);
			}
		}

		/// <summary>
		/// Returns the tooltip string for this ability, using the template and type override if present.
		/// </summary>
		/// <returns>Formatted tooltip string for the ability.</returns>
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