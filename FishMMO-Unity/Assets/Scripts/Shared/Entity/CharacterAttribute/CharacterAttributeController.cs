using System.Collections.Generic;
using System.Runtime.CompilerServices;
#if UNITY_SERVER
using System;
using FishNet.Broadcast;
#endif
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls all character attributes and resource attributes for an entity.
	/// Handles initialization from template databases, network payload serialization,
	/// parent/child/dependency relationship wiring, tick-based resource regeneration,
	/// and client-side broadcast synchronization via FishNet.
	/// </summary>
	public class CharacterAttributeController : CharacterBehaviour, ICharacterAttributeController
	{
#if UNITY_SERVER
		/// <summary>
		/// Dirty non-resource attribute template ids pending a network flush.
		/// </summary>
		private readonly HashSet<int> dirtyAttributeTemplateIDs = new HashSet<int>();

		/// <summary>
		/// Dirty resource attribute template ids pending a network flush.
		/// </summary>
		private readonly HashSet<int> dirtyResourceTemplateIDs = new HashSet<int>();

		/// <summary>
		/// Last base values sent to interested connections for non-resource attributes.
		/// </summary>
		private readonly Dictionary<int, int> lastSentAttributeValues = new Dictionary<int, int>();

		/// <summary>
		/// Last base values sent to interested connections for resource attributes.
		/// </summary>
		private readonly Dictionary<int, int> lastSentResourceValues = new Dictionary<int, int>();

		/// <summary>
		/// Last current values sent to interested connections for resource attributes.
		/// </summary>
		private readonly Dictionary<int, float> lastSentResourceCurrentValues = new Dictionary<int, float>();

		/// <summary>
		/// Epsilon used when comparing resource current values for network-dirty suppression.
		/// </summary>
		private const float ResourceCurrentValueEpsilon = 0.0001f;
#endif

		/// <summary>
		/// Reference to the ScriptableObject database containing all character attribute templates.
		/// Used to initialize and manage available attributes for this character.
		/// </summary>
		public CharacterAttributeTemplateDatabase CharacterAttributeDatabase;

		/// <summary>
		/// Template for the health resource attribute (e.g., HP).
		/// </summary>
		public CharacterAttributeTemplate HealthResourceTemplate;
		/// <summary>
		/// Template for the health regeneration attribute.
		/// </summary>
		public CharacterAttributeTemplate HealthRegenerationTemplate;
		/// <summary>
		/// Template for the mana resource attribute (e.g., MP).
		/// </summary>
		public CharacterAttributeTemplate ManaResourceTemplate;
		/// <summary>
		/// Template for the mana regeneration attribute.
		/// </summary>
		public CharacterAttributeTemplate ManaRegenerationTemplate;
		/// <summary>
		/// Template for the stamina resource attribute.
		/// </summary>
		public CharacterAttributeTemplate StaminaResourceTemplate;
		/// <summary>
		/// Template for the stamina regeneration attribute.
		/// </summary>
		public CharacterAttributeTemplate StaminaRegenerationTemplate;

		/// <summary>
		/// Dictionary of all non-resource character attributes, keyed by template ID.
		/// </summary>
		private readonly Dictionary<int, CharacterAttribute> attributes = new Dictionary<int, CharacterAttribute>();
		/// <summary>
		/// Dictionary of all resource character attributes (e.g., health, mana), keyed by template ID.
		/// </summary>
		private readonly Dictionary<int, CharacterResourceAttribute> resourceAttributes = new Dictionary<int, CharacterResourceAttribute>();

		/// <summary>
		/// Public accessor for all non-resource character attributes.
		/// </summary>
		public Dictionary<int, CharacterAttribute> Attributes { get { return attributes; } }
		/// <summary>
		/// Public accessor for all resource character attributes.
		/// </summary>
		public Dictionary<int, CharacterResourceAttribute> ResourceAttributes { get { return resourceAttributes; } }

		/// <summary>
		/// CharacterBehaviour function which is typically called once during initialization and typically before Awake or OnAwake.
		/// Initializes all attributes and resource attributes from the CharacterAttributeDatabase, and sets up dependencies.
		/// </summary>
		public override void InitializeOnce()
		{
			base.InitializeOnce();

			if (CharacterAttributeDatabase != null)
			{
				foreach (CharacterAttributeTemplate attribute in CharacterAttributeDatabase.Attributes)
				{
					if (attribute.IsResourceAttribute)
					{
						// Resource attributes (e.g., health, mana) are initialized with current and max values.
						AddResourceAttribute(new CharacterResourceAttribute(this,attribute.ID, attribute.InitialValue, attribute.InitialValue, 0));
					}
					else
					{
						// Non-resource attributes (e.g., strength, agility) are initialized with base value.
						AddAttribute(new CharacterAttribute(this, attribute.ID, attribute.InitialValue, 0));
					}
				}

				// Set up parent/child/dependant relationships for all attributes.
				InitializeAttributeDependents();
				InitializeResourceAttributeDependents();
			}
			else
			{
				Log.Error("CharacterAttributeController", "Character Attribute Database is missing!");
			}

#if UNITY_SERVER
			MarkAllAttributesDirty();
#endif
		}

		/// <summary>
		/// Reads attribute and resource attribute base values from the network payload and applies them.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader containing serialized attribute data.</param>
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			int attributeCount = reader.ReadInt32();
			if (attributeCount > 0)
			{
				for (int i = 0; i < attributeCount; ++i)
				{
					int templateID = reader.ReadInt32();
					int value = reader.ReadInt32();
					SetAttribute(templateID, value);
				}
			}

			int resourceAttributeCount = reader.ReadInt32();
			if (resourceAttributeCount > 0)
			{
				for (int i = 0; i < resourceAttributeCount; ++i)
				{
					int templateID = reader.ReadInt32();
					int value = reader.ReadInt32();
					float currentValue = reader.ReadSingle();
					SetResourceAttribute(templateID, value, currentValue);
				}
			}
		}

		/// <summary>
		/// Writes all attribute and resource attribute base values to the network payload for client synchronization.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to serialize attribute data into.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			writer.WriteInt32(Attributes.Count);
			foreach (CharacterAttribute attribute in Attributes.Values)
			{
				writer.WriteInt32(attribute.Template.ID);
				writer.WriteInt32(attribute.Value);
			}

			writer.WriteInt32(ResourceAttributes.Count);
			foreach (CharacterResourceAttribute resourceAttribute in ResourceAttributes.Values)
			{
				writer.WriteInt32(resourceAttribute.Template.ID);
				writer.WriteInt32(resourceAttribute.Value);
				writer.WriteSingle(resourceAttribute.CurrentValue);
			}
		}

		/// <summary>
		/// Resets the state of all resource attributes by restoring their current values to their final values.
		/// </summary>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			foreach (CharacterResourceAttribute characterResourceAttribute in ResourceAttributes.Values)
			{
				characterResourceAttribute.SetCurrentValue(characterResourceAttribute.FinalValue);
			}

#if UNITY_SERVER
			dirtyAttributeTemplateIDs.Clear();
			dirtyResourceTemplateIDs.Clear();
			lastSentAttributeValues.Clear();
			lastSentResourceValues.Clear();
			lastSentResourceCurrentValues.Clear();
#endif
		}

		/// <summary>
		/// Sets the base value and optional external modifier of a non-resource attribute by template ID.
		/// </summary>
		/// <param name="id">The template ID of the attribute.</param>
		/// <param name="value">The new base value.</param>
		/// <param name="modifier">Optional external modifier value to set.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetAttribute(int id, int value, int? modifier = null)
		{
			if (Attributes.TryGetValue(id, out CharacterAttribute attribute))
			{
				attribute.SetValue(value);
				if (modifier.HasValue)
				{
					attribute.SetModifier(modifier.Value);
				}
			}
		}

		/// <summary>
		/// Sets the base value, current value, and optional external modifier of a resource attribute by template ID.
		/// </summary>
		/// <param name="id">The template ID of the resource attribute.</param>
		/// <param name="value">The new base value.</param>
		/// <param name="currentValue">The new current depletable value.</param>
		/// <param name="modifier">Optional external modifier value to set.</param>
		/// <param name="clampFinalValue">If true, clamps the current value to the final value. If false, only clamps to zero.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetResourceAttribute(int id, int value, float currentValue, int? modifier = null)
		{
			if (ResourceAttributes.TryGetValue(id, out CharacterResourceAttribute attribute))
			{
				attribute.SetValue(value);
				attribute.SetCurrentValue(currentValue);
				if (modifier.HasValue)
				{
					attribute.SetModifier(modifier.Value);
				}
			}
		}

		/// <summary>
		/// Attempts to retrieve a non-resource attribute by its template reference.
		/// </summary>
		/// <param name="template">The attribute template to look up.</param>
		/// <param name="attribute">The found attribute instance, or null.</param>
		/// <returns>True if the attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute)
		{
			return Attributes.TryGetValue(template.ID, out attribute);
		}

		/// <summary>
		/// Attempts to retrieve a non-resource attribute by template ID.
		/// </summary>
		/// <param name="id">The template ID to look up.</param>
		/// <param name="attribute">The found attribute instance, or null.</param>
		/// <returns>True if the attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetAttribute(int id, out CharacterAttribute attribute)
		{
			return Attributes.TryGetValue(id, out attribute);
		}

		/// <summary>
		/// Attempts to retrieve a resource attribute by its template reference.
		/// </summary>
		/// <param name="template">The resource attribute template to look up.</param>
		/// <param name="attribute">The found resource attribute instance, or null.</param>
		/// <returns>True if the resource attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute)
		{
			return ResourceAttributes.TryGetValue(template.ID, out attribute);
		}

		/// <summary>
		/// Gets the current health percentage as a value in the range [0.0, 1.0].
		/// Returns 0.0 if the attribute is missing or FinalValue is zero.
		/// </summary>
		/// <returns>Current health percentage.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float GetHealthResourceAttributeCurrentPercentage()
		{
			if (ResourceAttributes.TryGetValue(HealthResourceTemplate.ID, out CharacterResourceAttribute attribute) &&
				attribute.FinalValue > 0)
			{
				return attribute.CurrentValue / attribute.FinalValue;
			}
			return 0.0f;
		}

		/// <summary>
		/// Gets the current mana percentage as a value in the range [0.0, 1.0].
		/// Returns 0.0 if the attribute is missing or FinalValue is zero.
		/// </summary>
		/// <returns>Current mana percentage.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float GetManaResourceAttributeCurrentPercentage()
		{
			if (ResourceAttributes.TryGetValue(ManaResourceTemplate.ID, out CharacterResourceAttribute attribute) &&
				attribute.FinalValue > 0)
			{
				return attribute.CurrentValue / attribute.FinalValue;
			}
			return 0.0f;
		}

		/// <summary>
		/// Gets the current stamina percentage as a value in the range [0.0, 1.0].
		/// Returns 0.0 if the attribute is missing or FinalValue is zero.
		/// </summary>
		/// <returns>Current stamina percentage.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float GetStaminaResourceAttributeCurrentPercentage()
		{
			if (ResourceAttributes.TryGetValue(StaminaResourceTemplate.ID, out CharacterResourceAttribute attribute) &&
				attribute.FinalValue > 0)
			{
				return attribute.CurrentValue / attribute.FinalValue;
			}
			return 0.0f;
		}

		/// <summary>
		/// Attempts to retrieve the health resource attribute.
		/// </summary>
		/// <param name="health">The found health resource attribute, or null.</param>
		/// <returns>True if the health attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetHealthAttribute(out CharacterResourceAttribute health)
		{
			return ResourceAttributes.TryGetValue(HealthResourceTemplate.ID, out health);
		}

		/// <summary>
		/// Attempts to retrieve the mana resource attribute.
		/// </summary>
		/// <param name="mana">The found mana resource attribute, or null.</param>
		/// <returns>True if the mana attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetManaAttribute(out CharacterResourceAttribute mana)
		{
			return ResourceAttributes.TryGetValue(ManaResourceTemplate.ID, out mana);
		}

		/// <summary>
		/// Attempts to retrieve the stamina resource attribute.
		/// </summary>
		/// <param name="stamina">The found stamina resource attribute, or null.</param>
		/// <returns>True if the stamina attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina)
		{
			return ResourceAttributes.TryGetValue(StaminaResourceTemplate.ID, out stamina);
		}

		/// <summary>
		/// Attempts to retrieve a resource attribute by template ID.
		/// </summary>
		/// <param name="id">The template ID to look up.</param>
		/// <param name="attribute">The found resource attribute instance, or null.</param>
		/// <returns>True if the resource attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute)
		{
			return ResourceAttributes.TryGetValue(id, out attribute);
		}

		/// <summary>
		/// Adds a non-resource attribute instance to the controller if not already present.
		/// </summary>
		/// <param name="instance">The attribute instance to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddAttribute(CharacterAttribute instance)
		{
			if (!Attributes.ContainsKey(instance.Template.ID))
			{
				Attributes.Add(instance.Template.ID, instance);
#if UNITY_SERVER
				instance.OnAttributeUpdated -= CharacterAttribute_OnAttributeUpdated;
				instance.OnAttributeUpdated += CharacterAttribute_OnAttributeUpdated;
#endif
			}
		}

		/// <summary>
		/// Wires parent, child, and dependency relationships for the given attribute instance
		/// based on its template configuration. Parents add this instance as their child (this attribute
		/// feeds into parent formulas). Children are added as this instance's children (they feed into
		/// this attribute's formulas). Dependencies are soft references for lookups with no propagation.
		/// </summary>
		/// <param name="instance">The attribute instance to wire relationships for.</param>
		private void AddDependents(CharacterAttribute instance)
		{
			foreach (CharacterAttributeTemplate parent in instance.Template.ParentTypes)
			{
				if (parent.IsResourceAttribute)
				{
					if (ResourceAttributes.TryGetValue(parent.ID, out CharacterResourceAttribute parentResourceInstance))
					{
						parentResourceInstance.AddChild(instance);
					}
				}
				else if (Attributes.TryGetValue(parent.ID, out CharacterAttribute parentInstance))
				{
					parentInstance.AddChild(instance);
				}
			}

			foreach (CharacterAttributeTemplate child in instance.Template.ChildTypes)
			{
				if (child.IsResourceAttribute)
				{
					if (ResourceAttributes.TryGetValue(child.ID, out CharacterResourceAttribute childResourceInstance))
					{
						instance.AddChild(childResourceInstance);
					}
				}
				else if (Attributes.TryGetValue(child.ID, out CharacterAttribute childInstance))
				{
					instance.AddChild(childInstance);
				}
			}

			foreach (CharacterAttributeTemplate dependant in instance.Template.DependantTypes)
			{
				if (dependant.IsResourceAttribute)
				{
					if (ResourceAttributes.TryGetValue(dependant.ID, out CharacterResourceAttribute dependantResourceInstance))
					{
						instance.AddDependant(dependantResourceInstance);
					}
				}
				else if (Attributes.TryGetValue(dependant.ID, out CharacterAttribute dependantInstance))
				{
					instance.AddDependant(dependantInstance);
				}
			}
		}

		/// <summary>
		/// Initializes parent/child/dependency relationships for all non-resource attributes.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void InitializeAttributeDependents()
		{
			foreach (CharacterAttribute instance in attributes.Values)
			{
				AddDependents(instance);
			}
		}

		/// <summary>
		/// Adds a resource attribute instance to the controller if not already present.
		/// </summary>
		/// <param name="instance">The resource attribute instance to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddResourceAttribute(CharacterResourceAttribute instance)
		{
			if (!ResourceAttributes.ContainsKey(instance.Template.ID))
			{
				ResourceAttributes.Add(instance.Template.ID, instance);
#if UNITY_SERVER
				instance.OnAttributeUpdated -= CharacterAttribute_OnAttributeUpdated;
				instance.OnAttributeUpdated += CharacterAttribute_OnAttributeUpdated;
#endif
			}
		}

#if UNITY_SERVER
		/// <summary>
		/// Subscribes to network tick updates used to batch and flush dirty attribute state.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Unsubscribes from network tick updates.
		/// </summary>
		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Called on network tick to flush dirty attribute updates in batches.
		/// </summary>
		private void TimeManager_OnTick()
		{
			FlushDirtyAttributeUpdates();
		}

		/// <summary>
		/// Marks all known attributes and resources as dirty so they are included in the next flush.
		/// </summary>
		private void MarkAllAttributesDirty()
		{
			foreach (CharacterAttribute attribute in Attributes.Values)
			{
				dirtyAttributeTemplateIDs.Add(attribute.Template.ID);
			}

			foreach (CharacterResourceAttribute resource in ResourceAttributes.Values)
			{
				dirtyResourceTemplateIDs.Add(resource.Template.ID);
			}
		}

		/// <summary>
		/// Handles local attribute update notifications and marks corresponding ids as dirty.
		/// </summary>
		/// <param name="attribute">The updated attribute.</param>
		private void CharacterAttribute_OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (attribute == null || attribute.Template == null)
			{
				return;
			}

			if (attribute is CharacterResourceAttribute)
			{
				dirtyResourceTemplateIDs.Add(attribute.Template.ID);
			}
			else
			{
				dirtyAttributeTemplateIDs.Add(attribute.Template.ID);
			}
		}

		/// <summary>
		/// Flushes dirty non-resource and resource attributes to the owning client and observers using single or batch broadcasts.
		/// </summary>
		private void FlushDirtyAttributeUpdates()
		{
			if (!base.IsServerStarted || !base.IsSpawned)
			{
				return;
			}

			FlushDirtyAttributes();
			FlushDirtyResourceAttributes();
		}

		/// <summary>
		/// Flushes dirty non-resource attributes and broadcasts either a single update or a packed batch.
		/// </summary>
		private void FlushDirtyAttributes()
		{
			if (dirtyAttributeTemplateIDs.Count == 0)
			{
				return;
			}

			List<CharacterAttributeUpdateBroadcast> updates = new List<CharacterAttributeUpdateBroadcast>(dirtyAttributeTemplateIDs.Count);
			foreach (int templateID in dirtyAttributeTemplateIDs)
			{
				if (!attributes.TryGetValue(templateID, out CharacterAttribute attribute) || attribute?.Template == null)
				{
					continue;
				}

				int current = attribute.Value;
				if (lastSentAttributeValues.TryGetValue(templateID, out int last) && last == current)
				{
					continue;
				}

				updates.Add(new CharacterAttributeUpdateBroadcast()
				{
					TemplateID = templateID,
					Value = current,
				});
				lastSentAttributeValues[templateID] = current;
			}

			dirtyAttributeTemplateIDs.Clear();
			SendAttributeUpdates(updates);
		}

		/// <summary>
		/// Flushes dirty resource attributes and broadcasts either a single update or a packed batch.
		/// </summary>
		private void FlushDirtyResourceAttributes()
		{
			if (dirtyResourceTemplateIDs.Count == 0)
			{
				return;
			}

			List<CharacterResourceAttributeUpdateBroadcast> updates = new List<CharacterResourceAttributeUpdateBroadcast>(dirtyResourceTemplateIDs.Count);
			foreach (int templateID in dirtyResourceTemplateIDs)
			{
				if (!resourceAttributes.TryGetValue(templateID, out CharacterResourceAttribute resource) || resource?.Template == null)
				{
					continue;
				}

				float currentValue = resource.CurrentValue;
				int value = resource.Value;

				bool currentUnchanged = lastSentResourceCurrentValues.TryGetValue(templateID, out float lastCurrent) && Math.Abs(lastCurrent - currentValue) <= ResourceCurrentValueEpsilon;
				bool valueUnchanged = lastSentResourceValues.TryGetValue(templateID, out int lastValue) && lastValue == value;
				if (currentUnchanged && valueUnchanged)
				{
					continue;
				}

				updates.Add(new CharacterResourceAttributeUpdateBroadcast()
				{
					TemplateID = templateID,
					CurrentValue = currentValue,
					Value = value,
				});

				lastSentResourceCurrentValues[templateID] = currentValue;
				lastSentResourceValues[templateID] = value;
			}

			dirtyResourceTemplateIDs.Clear();
			SendResourceUpdates(updates);
		}

		/// <summary>
		/// Sends non-resource attribute updates to owner and observers using separate payload types.
		/// </summary>
		/// <param name="updates">Collected updates to send.</param>
		private void SendAttributeUpdates(List<CharacterAttributeUpdateBroadcast> updates)
		{
			if (updates == null || updates.Count == 0)
			{
				return;
			}

			SendOwnerAttributeUpdates(updates);
			SendObserverAttributeUpdates(updates);
		}

		/// <summary>
		/// Sends non-resource attribute updates to the owning connection.
		/// </summary>
		/// <param name="updates">Collected updates to send.</param>
		private void SendOwnerAttributeUpdates(List<CharacterAttributeUpdateBroadcast> updates)
		{
			if (updates.Count == 1)
			{
				BroadcastToOwnerOnly(Character, updates[0], Channel.Reliable);
			}
			else
			{
				CharacterAttributeUpdateMultipleBroadcast batchBroadcast = new CharacterAttributeUpdateMultipleBroadcast
				{
					Attributes = updates,
				};
				BroadcastToOwnerOnly(Character, batchBroadcast, Channel.Reliable);
			}
		}

		/// <summary>
		/// Sends non-resource attribute updates to observer connections with character routing context.
		/// </summary>
		/// <param name="updates">Collected updates to send.</param>
		private void SendObserverAttributeUpdates(List<CharacterAttributeUpdateBroadcast> updates)
		{
			if (Character == null)
			{
				return;
			}

			CharacterObserverAttributeUpdateBroadcast observerBroadcast = new CharacterObserverAttributeUpdateBroadcast
			{
				CharacterID = Character.ID,
				Attributes = updates,
			};
			BroadcastToObserversOnly(Character, observerBroadcast, Channel.Reliable);
		}

		/// <summary>
		/// Sends resource attribute updates as either a single broadcast or a packed batch.
		/// Owner is excluded because owner resources are reconciled through prediction state.
		/// </summary>
		/// <param name="updates">Collected updates to send.</param>
		private void SendResourceUpdates(List<CharacterResourceAttributeUpdateBroadcast> updates)
		{
			if (updates == null || updates.Count == 0 || Character == null)
			{
				return;
			}

			CharacterObserverResourceAttributeUpdateBroadcast observerBroadcast = new CharacterObserverResourceAttributeUpdateBroadcast
			{
				CharacterID = Character.ID,
				Attributes = updates,
			};
			BroadcastToObserversOnly(Character, observerBroadcast, Channel.Reliable);
		}

		/// <summary>
		/// Broadcasts the payload to only the owner of the character.
		/// </summary>
		/// <typeparam name="T">Broadcast payload type.</typeparam>
		/// <param name="character">Character whose owner should receive the payload.</param>
		/// <param name="broadcast">Broadcast payload.</param>
		/// <param name="channel">Transport channel.</param>
		private static void BroadcastToOwnerOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character == null)
			{
				return;
			}

			NetworkConnection owner = character.Owner;
			if (owner != null && owner.IsActive)
			{
				owner.Broadcast(broadcast, true, channel);
			}
		}

		/// <summary>
		/// Broadcasts the payload to all current observers of the character, excluding the owner.
		/// </summary>
		/// <typeparam name="T">Broadcast payload type.</typeparam>
		/// <param name="character">Character whose observer connections should receive the payload.</param>
		/// <param name="broadcast">Broadcast payload.</param>
		/// <param name="channel">Transport channel.</param>
		private static void BroadcastToObserversOnly<T>(ICharacter character, T broadcast, Channel channel)
			where T : struct, IBroadcast
		{
			if (character == null || character.Observers == null)
			{
				return;
			}

			NetworkConnection owner = character.Owner;
			foreach (NetworkConnection observer in character.Observers)
			{
				if (observer == null || observer == owner || !observer.IsActive)
				{
					continue;
				}

				observer.Broadcast(broadcast, true, channel);
			}
		}

#endif

		/// <summary>
		/// Initializes parent/child/dependency relationships for all resource attributes.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void InitializeResourceAttributeDependents()
		{
			foreach (CharacterResourceAttribute instance in resourceAttributes.Values)
			{
				AddDependents(instance);
			}
		}

		/// <summary>
		/// Accumulated time since the last regeneration tick, in seconds.
		/// </summary>
		private float accumulatedRegenDelta = 0.0f;

		/// <summary>
		/// Processes resource regeneration for health, mana, and stamina using a 5-second tick rate.
		/// Accumulates delta time and applies regeneration in discrete intervals.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since the last call, in seconds.</param>
		public void Regenerate(float deltaTime)
		{
			const float REGEN_TICK_RATE = 5.0f;

			accumulatedRegenDelta += deltaTime;

			// Check if accumulatedDelta has reached or exceeded REGEN_TICK_RATE seconds
			if (accumulatedRegenDelta >= REGEN_TICK_RATE)
			{
				// Calculate how many 5-second intervals have passed
				int intervals = (int)(accumulatedRegenDelta / REGEN_TICK_RATE);

				// Reduce accumulatedDelta by the total duration of processed intervals
				accumulatedRegenDelta -= intervals * REGEN_TICK_RATE;

				// Regenerate health, mana, and stamina
				RegenerateResource(HealthResourceTemplate, HealthRegenerationTemplate, intervals);
				RegenerateResource(ManaResourceTemplate, ManaRegenerationTemplate, intervals);
				RegenerateResource(StaminaResourceTemplate, StaminaRegenerationTemplate, intervals);
			}
		}

		/// <summary>
		/// Regenerates a single resource attribute by looking up its regeneration dependency and applying the gain.
		/// </summary>
		/// <param name="resourceTemplate">The template of the resource to regenerate.</param>
		/// <param name="regenerationTemplate">The template of the regeneration rate attribute.</param>
		/// <param name="intervals">The number of 5-second intervals to process.</param>
		private void RegenerateResource(CharacterAttributeTemplate resourceTemplate, CharacterAttributeTemplate regenerationTemplate, int intervals)
		{
			if (resourceTemplate != null &&
				regenerationTemplate != null &&
				resourceAttributes.TryGetValue(resourceTemplate.ID, out CharacterResourceAttribute resource))
			{
				int regenAmountPerInterval = resource.GetDependantFinalValue(regenerationTemplate.Name);
				int totalRegenAmount = regenAmountPerInterval * intervals;
				resource.Gain(totalRegenAmount);
			}
		}

		/// <summary>
		/// Applies a resource state snapshot to restore health, mana, stamina, and regeneration delta.
		/// Used by FishNet's Replicate/Reconcile prediction system.
		/// </summary>
		/// <param name="resourceState">The resource state snapshot to apply.</param>
		public void ApplyResourceState(CharacterAttributeResourceState resourceState)
		{
			if (resourceAttributes.TryGetValue(HealthResourceTemplate.ID, out CharacterResourceAttribute health) &&
				resourceAttributes.TryGetValue(ManaResourceTemplate.ID, out CharacterResourceAttribute mana) &&
				resourceAttributes.TryGetValue(StaminaResourceTemplate.ID, out CharacterResourceAttribute stamina))
			{
				accumulatedRegenDelta = resourceState.RegenDelta;
				health.SetFinal(resourceState.MaxHealth);
				mana.SetFinal(resourceState.MaxMana);
				stamina.SetFinal(resourceState.MaxStamina);
				health.SetCurrentValue(resourceState.Health);
				// Skipping internal UI update here fixes an issue with Replicate/Reconcile fighting over UI updates.
				mana.SetCurrentValue(resourceState.Mana, false);
				stamina.SetCurrentValue(resourceState.Stamina);
			}
		}

		/// <summary>
		/// Captures the current resource state as a snapshot for FishNet's Replicate/Reconcile prediction system.
		/// </summary>
		/// <returns>A snapshot containing the current health, mana, stamina, and regeneration delta.</returns>
		public CharacterAttributeResourceState GetResourceState()
		{
			if (resourceAttributes.TryGetValue(HealthResourceTemplate.ID, out CharacterResourceAttribute health) &&
				resourceAttributes.TryGetValue(ManaResourceTemplate.ID, out CharacterResourceAttribute mana) &&
				resourceAttributes.TryGetValue(StaminaResourceTemplate.ID, out CharacterResourceAttribute stamina))
			{
				return new CharacterAttributeResourceState()
				{
					RegenDelta = accumulatedRegenDelta,
					Health = health.CurrentValue,
					MaxHealth = health.FinalValue,
					Mana = mana.CurrentValue,
					MaxMana = mana.FinalValue,
					Stamina = stamina.CurrentValue,
					MaxStamina = stamina.FinalValue,
				};
			}
			return default;
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts on the client. Registers broadcast handlers for attribute synchronization.
		/// Disables the controller for non-owners.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<CharacterAttributeUpdateBroadcast>(OnClientCharacterAttributeUpdateBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterAttributeUpdateMultipleBroadcast>(OnClientCharacterAttributeUpdateMultipleBroadcastReceived);

			ClientManager.RegisterBroadcast<CharacterResourceAttributeUpdateBroadcast>(OnClientCharacterResourceAttributeUpdateBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterResourceAttributeUpdateMultipleBroadcast>(OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived);

			ClientManager.RegisterBroadcast<CharacterObserverAttributeUpdateBroadcast>(OnClientCharacterObserverAttributeUpdateBroadcastReceived);
			ClientManager.RegisterBroadcast<CharacterObserverResourceAttributeUpdateBroadcast>(OnClientCharacterObserverResourceAttributeUpdateBroadcastReceived);
		}

		/// <summary>
		/// Called when the character stops on the client. Unregisters broadcast handlers for attribute synchronization.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<CharacterAttributeUpdateBroadcast>(OnClientCharacterAttributeUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterAttributeUpdateMultipleBroadcast>(OnClientCharacterAttributeUpdateMultipleBroadcastReceived);

				ClientManager.UnregisterBroadcast<CharacterResourceAttributeUpdateBroadcast>(OnClientCharacterResourceAttributeUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterResourceAttributeUpdateMultipleBroadcast>(OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived);

				ClientManager.UnregisterBroadcast<CharacterObserverAttributeUpdateBroadcast>(OnClientCharacterObserverAttributeUpdateBroadcastReceived);
				ClientManager.UnregisterBroadcast<CharacterObserverResourceAttributeUpdateBroadcast>(OnClientCharacterObserverResourceAttributeUpdateBroadcastReceived);
			}
		}

		/// <summary>
		/// Resolves a target character attribute controller from the client character cache.
		/// </summary>
		/// <param name="characterID">Target character ID.</param>
		/// <param name="attributeController">Resolved attribute controller if available.</param>
		/// <returns>True if a target controller was resolved.</returns>
		private static bool TryGetCachedCharacterAttributeController(long characterID, out ICharacterAttributeController attributeController)
		{
			attributeController = null;
			if (characterID <= 0)
			{
				return false;
			}

			if (!BaseCharacter.ClientCharacters.TryGetValue(characterID, out ICharacter character) ||
				character == null)
			{
				return false;
			}

			return character.TryGet(out attributeController);
		}

		/// <summary>
		/// Server sent an attribute update broadcast.
		/// </summary>
		private void OnClientCharacterAttributeUpdateBroadcastReceived(CharacterAttributeUpdateBroadcast msg, Channel channel)
		{
			SetAttribute(msg.TemplateID, msg.Value);
		}

		/// <summary>
		/// Server sent a multiple attribute update broadcast.
		/// </summary>
		private void OnClientCharacterAttributeUpdateMultipleBroadcastReceived(CharacterAttributeUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (CharacterAttributeUpdateBroadcast subMsg in msg.Attributes)
			{
				SetAttribute(subMsg.TemplateID, subMsg.Value);
			}
		}

		/// <summary>
		/// Server sent a resource attribute update broadcast.
		/// </summary>
		private void OnClientCharacterResourceAttributeUpdateBroadcastReceived(CharacterResourceAttributeUpdateBroadcast msg, Channel channel)
		{
			if (base.IsOwner)
			{
				return;
			}

			SetResourceAttribute(msg.TemplateID, msg.Value, msg.CurrentValue);
		}

		/// <summary>
		/// Server sent a multiple resource attribute update broadcast.
		/// </summary>
		private void OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived(CharacterResourceAttributeUpdateMultipleBroadcast msg, Channel channel)
		{
			if (base.IsOwner)
			{
				return;
			}

			foreach (CharacterResourceAttributeUpdateBroadcast subMsg in msg.Attributes)
			{
				SetResourceAttribute(subMsg.TemplateID, subMsg.Value, subMsg.CurrentValue);
			}
		}

		/// <summary>
		/// Server sent observer-targeted non-resource attribute updates for a specific character.
		/// </summary>
		private void OnClientCharacterObserverAttributeUpdateBroadcastReceived(CharacterObserverAttributeUpdateBroadcast msg, Channel channel)
		{
			if (!TryGetCachedCharacterAttributeController(msg.CharacterID, out ICharacterAttributeController attributeController))
			{
				return;
			}

			foreach (CharacterAttributeUpdateBroadcast subMsg in msg.Attributes)
			{
				attributeController.SetAttribute(subMsg.TemplateID, subMsg.Value);
			}
		}

		/// <summary>
		/// Server sent observer-targeted resource attribute updates for a specific character.
		/// </summary>
		private void OnClientCharacterObserverResourceAttributeUpdateBroadcastReceived(CharacterObserverResourceAttributeUpdateBroadcast msg, Channel channel)
		{
			if (!TryGetCachedCharacterAttributeController(msg.CharacterID, out ICharacterAttributeController attributeController))
			{
				return;
			}

			foreach (CharacterResourceAttributeUpdateBroadcast subMsg in msg.Attributes)
			{
				attributeController.SetResourceAttribute(subMsg.TemplateID, subMsg.Value, subMsg.CurrentValue);
			}
		}
#endif
	}
}