using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an in-game ability instance, constructed from an <see cref="AbilityTemplate"/> and containing all runtime state and events.
	/// Resource costs and requirements are determined by ECA conditions on the template's <see cref="BaseAbilityTemplate.ActivationConditions"/>
	/// and each event's <see cref="Trigger.Conditions"/> via the <see cref="IResourceCost"/> interface.
	/// Implements <see cref="ITooltip"/> for consistent UI tooltip display.
	/// </summary>
	public class Ability : ITooltip
	{
		/// <summary>
		/// Unique identifier for this ability instance.
		/// </summary>
		public long ID;

		/// <summary>
		/// Version number for this ability instance, used for client synchronization and updates.
		/// </summary>
		public long Version;

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
		/// Gets the icon sprite from the ability template.
		/// </summary>
		public Sprite Icon { get { return Template?.Icon; } }

		/// <summary>
		/// The display name of the ability.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Cached tooltip string for this ability, for UI display.
		/// </summary>
		public string CachedTooltip { get; private set; }

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
		/// Cached resource costs dictionary, invalidated when events are added or removed.
		/// </summary>
		private Dictionary<CharacterAttributeTemplate, int> cachedResourceCosts;

		/// <summary>
		/// Whether the cached resource costs need to be recalculated.
		/// </summary>
		private bool resourceCostsDirty = true;

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

			// Add any additional events provided in the constructor (e.g., from crafting).
			if (abilityEvents != null)
			{
				foreach (int eventId in abilityEvents)
				{
					AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(eventId);
					AddEvent(abilityEvent);
				}
			}

			// Apply stat modifiers from the template.
			AddTemplateModifiers(Template);
		}

		/// <summary>
		/// Adds a single ability event to the appropriate event dictionaries and applies its stat modifiers.
		/// </summary>
		/// <param name="abilityEvent">The ability event to add.</param>
		public void AddEvent(AbilityEvent abilityEvent)
		{
			if (abilityEvent == null || AbilityEvents.ContainsKey(abilityEvent.ID)) return;

			AbilityEvents.Add(abilityEvent.ID, abilityEvent);
			AddEventModifiers(abilityEvent);
			resourceCostsDirty = true;

			switch (abilityEvent)
			{
				case AbilityOnTickEvent tickEvent:
					OnTickEvents[tickEvent.ID] = tickEvent;
					break;
				case AbilityOnHitEvent hitEvent:
					OnHitEvents[hitEvent.ID] = hitEvent;
					break;
				case AbilityOnPreSpawnEvent preSpawnEvent:
					OnPreSpawnEvents[preSpawnEvent.ID] = preSpawnEvent;
					break;
				case AbilityOnSpawnEvent spawnEvent:
					OnSpawnEvents[spawnEvent.ID] = spawnEvent;
					break;
				case AbilityOnDestroyEvent destroyEvent:
					OnDestroyEvents[destroyEvent.ID] = destroyEvent;
					break;
			}
		}

		/// <summary>
		/// Adds a list of ability events to the appropriate event dictionaries and applies their stat modifiers.
		/// </summary>
		/// <typeparam name="T">The type of ability event.</typeparam>
		/// <param name="abilityEvents">The list of events to add.</param>
		public void AddEvents<T>(List<T> abilityEvents) where T : AbilityEvent
		{
			if (abilityEvents == null) return;

			foreach (T abilityEvent in abilityEvents)
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
		/// Adds the stat modifiers from an ability event to this ability.
		/// </summary>
		/// <param name="abilityEvent">The event whose modifiers to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void AddEventModifiers(AbilityEvent abilityEvent)
		{
			ActivationTime += abilityEvent.ActivationTime;
			LifeTime += abilityEvent.LifeTime;
			Cooldown += abilityEvent.Cooldown;
			Speed += abilityEvent.Speed;
		}

		/// <summary>
		/// Adds the stat modifiers from a template to this ability.
		/// </summary>
		/// <param name="template">The template whose modifiers to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void AddTemplateModifiers(AbilityTemplate template)
		{
			ActivationTime += template.ActivationTime;
			LifeTime += template.LifeTime;
			Cooldown += template.Cooldown;
			Speed += template.Speed;
		}

		/// <summary>
		/// Aggregates all resource costs from the template's <see cref="BaseAbilityTemplate.ActivationConditions"/>
		/// and each event's <see cref="Trigger.Conditions"/> by scanning for <see cref="IResourceCost"/> implementations.
		/// </summary>
		/// <returns>A dictionary mapping resource attribute templates to their total required amounts.</returns>
		public Dictionary<CharacterAttributeTemplate, int> GetResourceCosts()
		{
			if (!resourceCostsDirty && cachedResourceCosts != null)
			{
				return cachedResourceCosts;
			}

			if (cachedResourceCosts == null)
			{
				cachedResourceCosts = new Dictionary<CharacterAttributeTemplate, int>();
			}
			else
			{
				cachedResourceCosts.Clear();
			}

			// Aggregate from template activation conditions.
			CollectResourceCosts(Template?.ActivationConditions, cachedResourceCosts);

			// Aggregate from each event's conditions.
			foreach (AbilityEvent evt in AbilityEvents.Values)
			{
				CollectResourceCosts(evt?.Conditions, cachedResourceCosts);
			}

			resourceCostsDirty = false;
			return cachedResourceCosts;
		}

		/// <summary>
		/// Scans a list of conditions for <see cref="IResourceCost"/> implementations and adds their costs to the dictionary.
		/// </summary>
		/// <param name="conditions">The conditions to scan.</param>
		/// <param name="costs">The dictionary to accumulate costs into.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void CollectResourceCosts(List<BaseCondition> conditions, Dictionary<CharacterAttributeTemplate, int> costs)
		{
			if (conditions == null) return;

			foreach (BaseCondition condition in conditions)
			{
				if (condition is IResourceCost resourceCost &&
					resourceCost.ResourceTemplate != null &&
					resourceCost.ResourceAmount > 0)
				{
					if (costs.ContainsKey(resourceCost.ResourceTemplate))
					{
						costs[resourceCost.ResourceTemplate] += resourceCost.ResourceAmount;
					}
					else
					{
						costs[resourceCost.ResourceTemplate] = resourceCost.ResourceAmount;
					}
				}
			}
		}

		/// <summary>
		/// The total resource cost for this ability, summing all <see cref="IResourceCost"/> amounts.
		/// </summary>
		public int TotalResourceCost
		{
			get
			{
				int totalCost = 0;
				Dictionary<CharacterAttributeTemplate, int> costs = GetResourceCosts();
				foreach (int cost in costs.Values)
				{
					totalCost += cost;
				}
				return totalCost;
			}
		}

		/// <summary>
		/// Evaluates the template's activation conditions (excluding <see cref="IResourceCost"/> conditions, which are handled by <see cref="HasResource"/>).
		/// Checks requirements such as faction, archetype, and attribute conditions.
		/// </summary>
		/// <param name="character">The character to check.</param>
		/// <returns>True if all non-resource activation conditions are met, false otherwise.</returns>
		public bool MeetsActivationConditions(ICharacter character)
		{
			if (Template?.ActivationConditions == null || Template.ActivationConditions.Count == 0) return true;

			EventData checkData = new EventData(character);
			foreach (BaseCondition condition in Template.ActivationConditions)
			{
				if (condition == null) continue;

				// Skip resource cost conditions; those are handled by HasResource via aggregation.
				if (condition is IResourceCost) continue;

				if (!condition.Evaluate(character, checkData))
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
		/// Removes an ability event by its event ID and updates stat modifiers accordingly.
		/// </summary>
		/// <param name="eventID">The event ID to remove.</param>
		/// <returns>True if the event was removed, false otherwise.</returns>
		public bool RemoveAbilityEvent(int eventID)
		{
			if (AbilityEvents.TryGetValue(eventID, out AbilityEvent abilityEvent))
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

				resourceCostsDirty = true;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Checks if the given character has enough resources to use this ability.
		/// Aggregates all <see cref="IResourceCost"/> conditions from the template and events.
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

			Dictionary<CharacterAttributeTemplate, int> costs = GetResourceCosts();
			if (costs.Count == 0) return true;

			// Blood resource conversion: all costs are summed and checked against health.
			if (resourceConversionTrigger != null && AbilityEvents.ContainsKey(resourceConversionTrigger.ID))
			{
				int totalCost = 0;
				foreach (int cost in costs.Values)
				{
					totalCost += cost;
				}

				if (!attributeController.TryGetHealthAttribute(out CharacterResourceAttribute resource) ||
					resource.CurrentValue < totalCost)
				{
					return false;
				}
				return true;
			}

			// Normal check: each resource is checked individually against its aggregated cost.
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in costs)
			{
				if (!attributeController.TryGetResourceAttribute(pair.Key.ID, out CharacterResourceAttribute resource) ||
					resource.CurrentValue < pair.Value)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Consumes the required resources from the given character to use this ability.
		/// Aggregates all <see cref="IResourceCost"/> conditions from the template and events.
		/// </summary>
		/// <param name="character">The character using the ability.</param>
		/// <param name="resourceConversionTrigger">Optional event that allows resource conversion (e.g., health for mana).</param>
		public void ConsumeResources(ICharacter character, AbilityEvent resourceConversionTrigger = null)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			Dictionary<CharacterAttributeTemplate, int> costs = GetResourceCosts();
			if (costs.Count == 0) return;

			// Blood resource conversion: all costs are summed and consumed from health.
			if (resourceConversionTrigger != null && AbilityEvents.ContainsKey(resourceConversionTrigger.ID))
			{
				int totalCost = 0;
				foreach (int cost in costs.Values)
				{
					totalCost += cost;
				}

				if (attributeController.TryGetHealthAttribute(out CharacterResourceAttribute resource) &&
					resource.CurrentValue >= totalCost)
				{
					resource.Consume(totalCost);
				}
				return;
			}

			// Normal consumption: each resource is consumed individually.
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in costs)
			{
				if (attributeController.TryGetResourceAttribute(pair.Key.ID, out CharacterResourceAttribute resource) &&
					resource.CurrentValue >= pair.Value)
				{
					resource.Consume(pair.Value);
				}
			}
		}

		/// <summary>
		/// Removes an ability object from the cache by container and object ID.
		/// If the container becomes empty after removal, it is also removed.
		/// </summary>
		/// <param name="containerID">The container ID.</param>
		/// <param name="objectID">The object ID to remove.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveAbilityObject(int containerID, int objectID)
		{
			if (Objects != null &&
				Objects.TryGetValue(containerID, out Dictionary<int, AbilityObject> container))
			{
				container.Remove(objectID);

				if (container.Count == 0)
				{
					Objects.Remove(containerID);
				}
			}
		}

		/// <summary>
		/// Destroys all spawned ability objects for this ability and clears the Objects dictionary.
		/// Call this when the owning character disconnects, dies, or is otherwise cleaned up.
		/// </summary>
		public void DestroyAllAbilityObjects()
		{
			if (Objects == null)
			{
				return;
			}

			foreach (Dictionary<int, AbilityObject> container in Objects.Values)
			{
				foreach (AbilityObject obj in container.Values)
				{
					if (obj != null && obj.GameObject != null)
					{
						obj.Ability = null;
						obj.Caster = null;
						obj.GameObject.SetActive(false);
						UnityEngine.Object.Destroy(obj.GameObject);
					}
				}
			}
			Objects.Clear();
		}

		/// <summary>
		/// Detaches all spawned ability objects from this ability without destroying them.
		/// The objects continue to exist in the world using their <see cref="AbilityObjectSnapshot"/>
		/// for data-driven behavior. The <see cref="AbilityObject.Ability"/> and <see cref="AbilityObject.Caster"/>
		/// references are nulled out, and the Objects dictionary is cleared.
		/// Call this when the owning character disconnects or dies to allow in-flight
		/// projectiles to persist visually while gracefully degrading ECA events.
		/// </summary>
		public void DetachAllAbilityObjects()
		{
			if (Objects == null)
			{
				return;
			}

			// Create a single phantom caster to replace the live caster reference in all detached objects,
			// preserving identity and attribute data for stat-scaled calculations while gracefully degrading other behaviour lookups.
			SnapshotCharacter phantomCaster = null;

			foreach (Dictionary<int, AbilityObject> container in Objects.Values)
			{
				foreach (AbilityObject obj in container.Values)
				{
					if (obj != null)
					{
						// Create the phantom caster on demand when we encounter the first object that needs it.
						if (phantomCaster == null)
						{
							phantomCaster = AbilityObjectSnapshot.CreatePhantomCaster(obj.Caster, obj.Transform);
						}

						// Replace the live caster with a phantom that preserves identity
						// and attribute data for stat-scaled calculations.
						obj.Caster = phantomCaster;
						obj.Ability = null;
					}
				}
			}
			Objects.Clear();
		}

		/// <summary>
		/// Destroys all spawned ability objects whose <see cref="AbilityObject.SpawnTick"/> is greater than
		/// the specified tick. Used during reconcile rollback to remove client-predicted objects
		/// that the server has not confirmed.
		/// </summary>
		/// <param name="tick">The reconcile tick. Objects spawned after this tick are destroyed.</param>
		public void DestroyAbilityObjectsAfterTick(uint tick)
		{
			if (Objects == null)
			{
				return;
			}

			List<int> emptyContainers = null;

			foreach (KeyValuePair<int, Dictionary<int, AbilityObject>> containerEntry in Objects)
			{
				List<int> toRemove = null;

				foreach (KeyValuePair<int, AbilityObject> objEntry in containerEntry.Value)
				{
					if (objEntry.Value != null && objEntry.Value.SpawnTick > tick)
					{
						if (objEntry.Value.GameObject != null)
						{
							objEntry.Value.Ability = null;
							objEntry.Value.Caster = null;
							objEntry.Value.GameObject.SetActive(false);
							UnityEngine.Object.Destroy(objEntry.Value.GameObject);
						}

						if (toRemove == null)
						{
							toRemove = new List<int>();
						}
						toRemove.Add(objEntry.Key);
					}
				}

				if (toRemove != null)
				{
					foreach (int key in toRemove)
					{
						containerEntry.Value.Remove(key);
					}
				}

				if (containerEntry.Value.Count == 0)
				{
					if (emptyContainers == null)
					{
						emptyContainers = new List<int>();
					}
					emptyContainers.Add(containerEntry.Key);
				}
			}

			if (emptyContainers != null)
			{
				foreach (int key in emptyContainers)
				{
					Objects.Remove(key);
				}
			}
		}

		/// <summary>
		/// Returns the tooltip string for this ability, using the template and type override if present.
		/// Caches the result to avoid repeated string allocations.
		/// </summary>
		/// <returns>Formatted tooltip string for the ability.</returns>
		public string Tooltip()
		{
			if (!string.IsNullOrWhiteSpace(CachedTooltip))
			{
				return CachedTooltip;
			}

			using (var builder = new TooltipBuilder())
			{
				Template.BuildTooltip(builder);
				AbilityType abilityType = TypeOverride != null ? TypeOverride.OverrideAbilityType : Template.Type;
				if (abilityType != AbilityType.None)
				{
					builder.AddLine($"Type: {abilityType}", 90, TooltipColors.Title, false, "120%");
				}
				CachedTooltip = builder.Build();
			}

			return CachedTooltip;
		}
	}
}