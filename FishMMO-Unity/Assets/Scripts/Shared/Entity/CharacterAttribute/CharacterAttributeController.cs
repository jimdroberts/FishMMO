using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
						AddResourceAttribute(new CharacterResourceAttribute(attribute.ID, attribute.InitialValue, attribute.InitialValue, 0));
					}
					else
					{
						// Non-resource attributes (e.g., strength, agility) are initialized with base value.
						AddAttribute(new CharacterAttribute(attribute.ID, attribute.InitialValue, 0));
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
			}
		}

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
					Mana = mana.CurrentValue,
					Stamina = stamina.CurrentValue,
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
			}
		}

		/// <summary>
		/// Server sent an attribute update broadcast.
		/// </summary>
		private void OnClientCharacterAttributeUpdateBroadcastReceived(CharacterAttributeUpdateBroadcast msg, Channel channel)
		{
			CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(msg.TemplateID);
			if (template != null &&
				Attributes.TryGetValue(template.ID, out CharacterAttribute attribute))
			{
				attribute.SetValue(msg.Value);
			}
		}

		/// <summary>
		/// Server sent a multiple attribute update broadcast.
		/// </summary>
		private void OnClientCharacterAttributeUpdateMultipleBroadcastReceived(CharacterAttributeUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (CharacterAttributeUpdateBroadcast subMsg in msg.Attributes)
			{
				CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(subMsg.TemplateID);
				if (template != null &&
					Attributes.TryGetValue(template.ID, out CharacterAttribute attribute))
				{
					attribute.SetValue(subMsg.Value);
				}
			}
		}

		/// <summary>
		/// Server sent a resource attribute update broadcast.
		/// </summary>
		private void OnClientCharacterResourceAttributeUpdateBroadcastReceived(CharacterResourceAttributeUpdateBroadcast msg, Channel channel)
		{
			CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(msg.TemplateID);
			if (template != null &&
				ResourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
			{
				attribute.SetCurrentValue(msg.CurrentValue);
				attribute.SetValue(msg.Value);
			}
		}

		/// <summary>
		/// Server sent a multiple resource attribute update broadcast.
		/// </summary>
		private void OnClientCharacterResourceAttributeUpdateMultipleBroadcastReceived(CharacterResourceAttributeUpdateMultipleBroadcast msg, Channel channel)
		{
			foreach (CharacterResourceAttributeUpdateBroadcast subMsg in msg.Attributes)
			{
				CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(subMsg.TemplateID);
				if (template != null &&
					ResourceAttributes.TryGetValue(template.ID, out CharacterResourceAttribute attribute))
				{
					attribute.SetCurrentValue(subMsg.CurrentValue);
					attribute.SetValue(subMsg.Value);
				}
			}
		}
#endif
	}
}