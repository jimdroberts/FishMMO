using System;
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
		/// Whether this ability has changed since the database last confirmed it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The periodic save wrote every known ability of every resident character on every pass,
		/// because it had no way to tell which had changed. Almost none ever do: what is stored is
		/// the template and the set of event IDs, and those move only when a player crafts, learns
		/// or forgets something. Cooldowns are deliberately not persisted, so an ability in constant
		/// use produces exactly the same row as one that has sat in a hotbar untouched for a week.
		/// </para>
		/// <para>
		/// Set true on construction. That over-writes each ability once after a character loads,
		/// which is deliberate: it costs one redundant write per ability per login instead of one
		/// every save, and it does not depend on knowing which constructor the load path uses. An
		/// ability is never missed, only occasionally written when it need not have been.
		/// </para>
		/// <para>
		/// Set again by <see cref="MarkChanged"/>, which every mutation of the event set goes
		/// through, and cleared only by <see cref="MarkPersisted"/> once a write has landed.
		/// </para>
		/// </remarks>
		public bool PersistenceDirty { get; private set; } = true;

		/// <summary>
		/// Clears <see cref="PersistenceDirty"/> if this ability has not changed since the version
		/// that was written.
		/// </summary>
		/// <remarks>
		/// The version check is what makes the save safe to run in the background: an ability that
		/// changed while the write was in flight has moved past the version written and stays dirty.
		/// A write that fails never calls this, so the next pass carries it.
		/// </remarks>
		/// <param name="persistedVersion">The version that was successfully written.</param>
		public void MarkPersisted(long persistedVersion)
		{
			if (Version == persistedVersion)
			{
				PersistenceDirty = false;
			}
		}

		/// <summary>
		/// Records a change to what is persisted: the event set.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The mark alone is not enough. A save snapshots on the main thread and confirms later,
		/// and <see cref="MarkPersisted"/> tells a change made in that window from one already
		/// stored purely by <see cref="Version"/> — so a change that leaves the version alone is
		/// indistinguishable from no change at all, and clearing on the confirmation would drop
		/// it until the ability happened to change again. Advancing the version here is what makes
		/// that comparison mean something.
		/// </para>
		/// <para>
		/// The version only has to move, not to match the database's: the upsert admits any row
		/// strictly newer than the one it holds, and the save advances it again before writing.
		/// </para>
		/// </remarks>
		private void MarkChanged()
		{
			PersistenceDirty = true;
			++Version;
		}

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
		/// Returns the effective ability type, accounting for any <see cref="TypeOverride"/>.
		/// </summary>
		public AbilityType EffectiveType => TypeOverride != null ? TypeOverride.OverrideAbilityType : Template.Type;

		/// <summary>
		/// All ability events, indexed by event ID, for quick access.
		/// </summary>
		/// <remarks>
		/// <see cref="SortedDictionary{TKey,TValue}"/> is used intentionally for all event
		/// dictionaries so that iteration order is deterministic (ascending event ID).
		/// This guarantees identical execution order on client and server during CSP
		/// prediction/reconciliation, which <see cref="Dictionary{TKey,TValue}"/> cannot ensure.
		/// The O(log n) cost per lookup is acceptable because event counts per ability are small.
		/// </remarks>
		public SortedDictionary<int, AbilityEvent> AbilityEvents = new SortedDictionary<int, AbilityEvent>();

		/// <summary>
		/// All OnTick events, indexed by event ID.
		/// </summary>
		public SortedDictionary<int, AbilityOnTickEvent> OnTickEvents = new SortedDictionary<int, AbilityOnTickEvent>();

		/// <summary>
		/// All OnHit events, indexed by event ID.
		/// </summary>
		public SortedDictionary<int, AbilityOnHitEvent> OnHitEvents = new SortedDictionary<int, AbilityOnHitEvent>();

		/// <summary>
		/// All OnPreSpawn events, indexed by event ID.
		/// </summary>
		public SortedDictionary<int, AbilityOnPreSpawnEvent> OnPreSpawnEvents = new SortedDictionary<int, AbilityOnPreSpawnEvent>();

		/// <summary>
		/// All OnSpawn events, indexed by event ID.
		/// </summary>
		public SortedDictionary<int, AbilityOnSpawnEvent> OnSpawnEvents = new SortedDictionary<int, AbilityOnSpawnEvent>();

		/// <summary>
		/// All OnDestroy events, indexed by event ID.
		/// </summary>
		public SortedDictionary<int, AbilityOnDestroyEvent> OnDestroyEvents = new SortedDictionary<int, AbilityOnDestroyEvent>();

		/// <summary>
		/// Cache of all active ability objects, organized as a dictionary mapping container IDs to dictionaries of ability object IDs and their corresponding <see cref="AbilityObject"/> instances.
		/// </summary>
		public Dictionary<int, Dictionary<int, AbilityObject>> Objects { get; internal set; }

		/// <summary>
		/// Cached resource costs dictionary, invalidated when events are added or removed.
		/// </summary>
		private Dictionary<CharacterAttributeTemplate, int> cachedResourceCosts;

		/// <summary>
		/// Whether the cached resource costs need to be recalculated.
		/// </summary>
		private bool resourceCostsDirty = true;

		/// <summary>
		/// Cached total resource cost, invalidated alongside <see cref="cachedResourceCosts"/>.
		/// </summary>
		private int cachedTotalResourceCost;

		/// <summary>
		/// Whether <see cref="cachedTotalResourceCost"/> needs to be recalculated.
		/// </summary>
		private bool totalResourceCostDirty = true;

		/// <summary>
		/// Reusable buffer for container IDs to remove during <see cref="DestroyAbilityObjectsAfterTick"/>.
		/// Static because all usage is synchronous single-threaded (Unity main thread).
		/// </summary>
		private static readonly List<int> emptyContainerBuffer = new List<int>();

		/// <summary>
		/// Reusable buffer for object IDs to remove during <see cref="DestroyAbilityObjectsAfterTick"/>.
		/// Static because all usage is synchronous single-threaded (Unity main thread).
		/// </summary>
		private static readonly List<int> objectRemoveBuffer = new List<int>();

		/// <summary>
		/// Constructs an ability from a template and optional event list.
		/// </summary>
		/// <param name="template">The ability template to use.</param>
		/// <param name="abilityEvents">Optional list of event IDs to add to the ability.</param>
		/// <remarks>
		/// <b>Server-only crafting path.</b> ID is set to <c>-1</c> until the database assigns
		/// a persistent ID via the crafting persistence call. Never use this constructor for
		/// network-replicated ability instances — use <see cref="Ability(long, AbilityTemplate, List{int})"/>
		/// with the DB-assigned ID instead.
		/// </remarks>
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
			if (template == null)
			{
				throw new ArgumentNullException(nameof(template),
					$"Ability {abilityID} requires a non-null template.");
			}

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
					if (abilityEvent != null)
					{
						AddEvent(abilityEvent);
					}
					else
					{
						// Check if the ID corresponds to a type override template
						// (extends BaseAbilityTemplate, not AbilityEvent).
						BaseAbilityTemplate baseTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(eventId);
						if (baseTemplate is AbilityTypeOverrideEventType typeOverride)
						{
							TypeOverride = typeOverride;
						}
					}
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
			MarkChanged();
			AddEventModifiers(abilityEvent);
			resourceCostsDirty = true;
			totalResourceCostDirty = true;
			CachedTooltip = null;

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
		/// Delegates to <see cref="AddEvent"/> for each element, ensuring consistent modifier application
		/// and resource cost invalidation.
		/// </summary>
		/// <typeparam name="T">The type of ability event.</typeparam>
		/// <param name="abilityEvents">The list of events to add.</param>
		public void AddEvents<T>(List<T> abilityEvents) where T : AbilityEvent
		{
			if (abilityEvents == null) return;

			foreach (T abilityEvent in abilityEvents)
			{
				AddEvent(abilityEvent);
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
					costs.TryGetValue(resourceCost.ResourceTemplate, out int existing);
					costs[resourceCost.ResourceTemplate] = existing + resourceCost.ResourceAmount;
				}
			}
		}

		/// <summary>
		/// The total resource cost for this ability, summing all <see cref="IResourceCost"/> amounts.
		/// Uses the cached cost dictionary from <see cref="GetResourceCosts"/>.
		/// </summary>
		public int TotalResourceCost
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				if (!totalResourceCostDirty)
				{
					return cachedTotalResourceCost;
				}
				int totalCost = 0;
				foreach (int cost in GetResourceCosts().Values)
				{
					totalCost += cost;
				}
				cachedTotalResourceCost = totalCost;
				totalResourceCostDirty = false;
				return totalCost;
			}
		}

		/// <summary>
		/// Evaluates the template's activation conditions using a cached <see cref="EventData"/>.
		/// The <paramref name="checkData"/> reference is created or updated if the initiator
		/// changes, avoiding a per-call allocation on hot paths (e.g., every tick in Replicate).
		/// </summary>
		/// <param name="character">The character to check.</param>
		/// <param name="checkData">Reusable event data; created if null, recreated if initiator changed.</param>
		/// <returns>True if all non-resource activation conditions are met, false otherwise.</returns>
		public bool MeetsActivationConditions(ICharacter character, ref EventData checkData)
		{
			if (Template?.ActivationConditions == null || Template.ActivationConditions.Count == 0) return true;

			if (checkData == null || checkData.Initiator != character)
			{
				checkData = new EventData(character);
			}
			foreach (BaseCondition condition in Template.ActivationConditions)
			{
				if (condition == null) continue;

				// Skip resource cost conditions; those are handled by HasResource via aggregation.
				if (condition is IResourceCost) continue;

				if (!condition.Check(character, checkData))
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
				MarkChanged();
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
				totalResourceCostDirty = true;
				CachedTooltip = null;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Processes resource costs for this ability, either checking availability or consuming them.
		/// Aggregates all <see cref="IResourceCost"/> conditions from the template and events.
		/// Blood resource conversion redirects all costs to the health attribute.
		/// Uses a two-pass approach: validates all resources first, then consumes atomically.
		/// This prevents partial consumption when a later resource check fails.
		/// </summary>
		/// <param name="character">The character to check or consume resources from.</param>
		/// <param name="resourceConversionTrigger">Optional event that enables resource conversion (e.g., health for mana).</param>
		/// <param name="consume">If true, consumes resources after validation. If false, only checks availability.</param>
		/// <returns>True if all required resources are available (and consumed when <paramref name="consume"/> is true), false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ProcessResources(ICharacter character, AbilityEvent resourceConversionTrigger, bool consume)
		{
			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return false;
			}

			Dictionary<CharacterAttributeTemplate, int> costs = GetResourceCosts();
			if (costs.Count == 0) return true;

			// Blood resource conversion: all costs are summed and checked/consumed from health.
			if (resourceConversionTrigger != null && AbilityEvents.ContainsKey(resourceConversionTrigger.ID))
			{
				int totalCost = TotalResourceCost;

				if (!attributeController.TryGetHealthAttribute(out CharacterResourceAttribute resource) ||
					resource.CurrentValue < totalCost)
				{
					return false;
				}

				if (consume)
				{
					resource.Consume(totalCost);
				}
				return true;
			}

			// Normal path: two-pass to ensure atomic consumption.
			// Pass 1: validate all resources are sufficient.
			foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in costs)
			{
				if (!attributeController.TryGetResourceAttribute(pair.Key.ID, out CharacterResourceAttribute resource) ||
					resource.CurrentValue < pair.Value)
				{
					return false;
				}
			}

			// Pass 2: consume all resources (only reached when all checks passed).
			if (consume)
			{
				foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in costs)
				{
					if (attributeController.TryGetResourceAttribute(pair.Key.ID, out CharacterResourceAttribute resource))
					{
						resource.Consume(pair.Value);
					}
				}
			}
			return true;
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
			return ProcessResources(character, resourceConversionTrigger, false);
		}

		/// <summary>
		/// Consumes the required resources from the given character to use this ability.
		/// Aggregates all <see cref="IResourceCost"/> conditions from the template and events.
		/// </summary>
		/// <param name="character">The character using the ability.</param>
		/// <param name="resourceConversionTrigger">Optional event that allows resource conversion (e.g., health for mana).</param>
		public void ConsumeResources(ICharacter character, AbilityEvent resourceConversionTrigger = null)
		{
			ProcessResources(character, resourceConversionTrigger, true);
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
		/// Routes each object through <see cref="AbilityObject.DestroyAbilityObjectInternal"/> so that
		/// the destroyed-flag, OnTick unsubscription, and OnDestroy events are executed.
		/// <see cref="AbilityObject.Ability"/> is nulled before the internal call to prevent
		/// <see cref="RemoveAbilityObject"/> from modifying the <see cref="Objects"/> dictionary
		/// while we are iterating it; the dictionary is cleared in bulk after the loop.
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
					if (obj != null)
					{
						// Null the Ability back-reference so DestroyAbilityObjectInternal
						// skips RemoveAbilityObject (we Clear() below).
						obj.Ability = null;
						obj.DestroyAbilityObjectInternal();
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
			AbilityObjectSnapshot sharedSnapshot = null;

			foreach (KeyValuePair<int, Dictionary<int, AbilityObject>> containerEntry in Objects)
			{
				foreach (AbilityObject obj in containerEntry.Value.Values)
				{
					if (obj != null)
					{
						// Create the phantom caster on demand when we encounter the first object that needs it.
						if (phantomCaster == null)
						{
							phantomCaster = SnapshotCharacter.FromLive(obj.Caster, obj.Transform);
						}

						// Replace the live caster with a phantom that preserves identity
						// and attribute data for stat-scaled calculations.
						obj.Caster = phantomCaster;

						// All detached objects from the same ability share the same immutable
						// ability data, so create one snapshot and reuse it across the detach pass.
						sharedSnapshot ??= new AbilityObjectSnapshot(this);
						obj.Snapshot ??= sharedSnapshot;
						obj.Ability = null;

						/* Filed so a RE-observation can reclaim it. A caster culled from this
						 * client's observer set despawns through here, detaching its projectiles
						 * as visual phantoms — and when the caster comes back into range, the
						 * spawn payload rematerialises the same in-flight objects as fresh copies.
						 * Without the registry the observer rendered both: the phantom (which no
						 * hit or destroy broadcast can resolve any more) flew to lifetime expiry
						 * regardless of what the server's copy did. See
						 * AbilityObject.ReclaimDetached. */
						obj.RegisterDetached(ID, containerEntry.Key);
					}
				}
			}
			Objects.Clear();
		}

		/// <summary>
		/// Destroys all spawned ability objects whose <see cref="AbilityObject.SpawnTick"/> is greater than
		/// the specified tick. Used during reconcile rollback to remove client-predicted objects
		/// that the server has not confirmed.
		/// Routes each object through <see cref="AbilityObject.DestroyAbilityObjectInternal"/> so that
		/// the destroyed-flag, OnTick unsubscription, and OnDestroy events are executed.
		/// <see cref="AbilityObject.Ability"/> is nulled before the internal call to prevent
		/// <see cref="RemoveAbilityObject"/> from modifying the <see cref="Objects"/> dictionary
		/// while we are iterating it; the outer loop handles dictionary cleanup instead.
		/// </summary>
		/// <param name="tick">The reconcile tick. Objects spawned after this tick are destroyed.</param>
		/// <param name="includeTick">
		/// Also destroy objects spawned exactly ON <paramref name="tick"/>. Off by default, and it
		/// must stay that way for the ordinary mismatch path: FishNet replays from
		/// <c>tick + 1</c>, so an object spawned at <c>tick</c> is one the replay cannot recreate,
		/// and removing a spawn the server actually performed would delete it permanently. Only
		/// the caller that has established the server did NOT spawn at this tick may pass true —
		/// see <c>AbilityController.ShouldDestroySpawnsAtReconcileTick</c>.
		/// </param>
		public void DestroyAbilityObjectsAfterTick(uint tick, bool includeTick = false)
		{
			DestroyAbilityObjectsInternal(tick, includeTick, onlyTick: false);
		}

		/// <summary>
		/// Destroys only the ability objects spawned exactly ON the given tick, leaving every
		/// other tick's objects alone.
		/// </summary>
		/// <remarks>
		/// For the NoSpawn correction when the seeds AGREE at the reconcile tick: the server ran
		/// the activation and produced no object, so the object this client spawned at that tick is
		/// unconfirmed — but agreeing seeds mean nothing else diverged, so an object the client
		/// spawned on a LATER tick is exactly as confirmed as it was before this reconcile arrived.
		/// The <c>&gt;=</c> sweep used here previously deleted those later, confirmed objects too,
		/// and the replay cannot restore them (it skips spawns), so a self-buff cast one tick before
		/// a projectile erased the projectile permanently.
		/// </remarks>
		/// <param name="tick">The reconcile tick whose spawns must be removed.</param>
		public void DestroyAbilityObjectsAtTick(uint tick)
		{
			DestroyAbilityObjectsInternal(tick, includeTick: true, onlyTick: true);
		}

		/// <summary>Shared body of the two reconcile-rollback destroy scopes.</summary>
		private void DestroyAbilityObjectsInternal(uint tick, bool includeTick, bool onlyTick)
		{
			if (Objects == null)
			{
				return;
			}

			emptyContainerBuffer.Clear();

			foreach (KeyValuePair<int, Dictionary<int, AbilityObject>> containerEntry in Objects)
			{
				objectRemoveBuffer.Clear();

				foreach (KeyValuePair<int, AbilityObject> objEntry in containerEntry.Value)
				{
					if (objEntry.Value == null)
					{
						continue;
					}
					bool matches = onlyTick
						? objEntry.Value.SpawnTick.Value == tick
						: (IsSpawnTickAfter(objEntry.Value.SpawnTick, tick) ||
						   (includeTick && objEntry.Value.SpawnTick.Value == tick));
					if (matches)
					{
						// Null the Ability back-reference so DestroyAbilityObjectInternal
						// skips RemoveAbilityObject (we handle dict removal below).
						objEntry.Value.Ability = null;
						objEntry.Value.DestroyAbilityObjectInternal();
						objectRemoveBuffer.Add(objEntry.Key);
					}
				}

				for (int i = 0; i < objectRemoveBuffer.Count; i++)
				{
					containerEntry.Value.Remove(objectRemoveBuffer[i]);
				}

				if (containerEntry.Value.Count == 0)
				{
					emptyContainerBuffer.Add(containerEntry.Key);
				}
			}

			for (int i = 0; i < emptyContainerBuffer.Count; i++)
			{
				Objects.Remove(emptyContainerBuffer[i]);
			}
		}

		/// <summary>
		/// Returns true when the spawn tick is strictly after the given tick.
		/// Used during reconcile rollback to determine which objects to destroy.
		/// </summary>
		private static bool IsSpawnTickAfter(PredictionTick spawnTick, uint tick)
		{
			return (int)(spawnTick.Value - tick) > 0;
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
				AbilityType abilityType = EffectiveType;
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