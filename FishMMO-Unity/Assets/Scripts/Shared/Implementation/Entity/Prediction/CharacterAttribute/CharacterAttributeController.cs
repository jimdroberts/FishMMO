using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Connection;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls all character attributes and resource attributes for an entity.
	/// Handles initialization from template databases, network payload serialization,
	/// parent/child/dependency relationship wiring, tick-based resource regeneration,
	/// and reconcile-driven synchronization of both base and resource attributes
	/// via the unified <see cref="CharacterReconcileData"/>. There is no longer a
	/// separate broadcast path for non-resource attributes.
	/// </summary>
	public class CharacterAttributeController : CharacterBehaviour, ICharacterAttributeController, IPredictableController
	{
		/// <summary>
		/// Execution order in the unified prediction pipeline.
		/// Runs after buff reconcile/tick and before ability processing so
		/// regenerated resources are available for same-tick activation checks.
		/// </summary>
		public int Order => 95;

		/// <summary>
		/// Cached non-resource attribute snapshot, rebuilt lazily by
		/// <see cref="CreateAttributeSnapshot"/>. Held by reference and re-emitted
		/// across consecutive ticks when no attribute mutated, so the delta
		/// serializer's <c>ReferenceEquals</c> fast-path produces zero network bytes.
		/// </summary>
		private AttributeReconcileEntry[] cachedAttributeSnapshot;

		/// <summary>
		/// When true, <see cref="cachedAttributeSnapshot"/> is stale and must be rebuilt
		/// on the next <see cref="CreateAttributeSnapshot"/> call.
		/// </summary>
		private bool attributeSnapshotDirty = true;

		/// <summary>
		/// While true, attribute-update notifications (raised during reconcile restore)
		/// MUST NOT mark <see cref="cachedAttributeSnapshot"/> dirty. Reconcile restores
		/// the canonical state from the server snapshot — invalidating the snapshot here
		/// would force a needless rebuild and break <c>ReferenceEquals</c> identity on
		/// the next tick when nothing has actually changed.
		/// </summary>
		private bool suppressAttributeDirty;

		/// <summary>
		/// Reference to the ScriptableObject database containing all character attribute templates.
		/// Used to initialize and manage available attributes for this character.
		/// </summary>
		public CharacterAttributeTemplateDatabase CharacterAttributeDatabase;

		/// <summary>
		/// Template ID for the health resource attribute (e.g., HP).
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int HealthResourceTemplateID;
		/// <summary>
		/// Template ID for the health regeneration attribute.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int HealthRegenerationTemplateID;
		/// <summary>
		/// Template ID for the mana resource attribute (e.g., MP).
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int ManaResourceTemplateID;
		/// <summary>
		/// Template ID for the mana regeneration attribute.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int ManaRegenerationTemplateID;
		/// <summary>
		/// Template ID for the stamina resource attribute.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int StaminaResourceTemplateID;
		/// <summary>
		/// Template ID for the stamina regeneration attribute.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int StaminaRegenerationTemplateID;

		/// <summary>
		/// Time in seconds between resource regeneration ticks.
		/// Configurable per character type (e.g., NPCs may use a different rate than players).
		/// </summary>
		[SerializeField]
		[Tooltip("Time in seconds between resource regeneration ticks.")]
		private float regenTickRate = 5.0f;

		/// <summary>
		/// Propagation depth counter for batched attribute graph updates.
		/// While > 0, attribute change notifications are deferred.
		/// </summary>
		private int propagationDepth;

		/// <summary>
		/// Attributes whose <see cref="CharacterAttribute.OnAttributeUpdated"/> event
		/// has been deferred during the current propagation batch.
		/// </summary>
		private readonly HashSet<CharacterAttribute> pendingNotifications = new HashSet<CharacterAttribute>();

		/// <summary>
		/// Reusable buffer for draining <see cref="pendingNotifications"/> without
		/// iterator-invalidation issues if a listener triggers a new propagation.
		/// </summary>
		private readonly List<CharacterAttribute> notificationDrainBuffer = new List<CharacterAttribute>();

		/// <summary>
		/// Guard flag preventing re-entrant notification draining.
		/// </summary>
		private bool isDrainingNotifications;

		/// <inheritdoc/>
		public bool IsPropagating => propagationDepth > 0;

		/// <inheritdoc/>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void BeginPropagation()
		{
			propagationDepth++;
		}

		/// <inheritdoc/>
		public void EndPropagation()
		{
			if (--propagationDepth > 0)
			{
				return;
			}
			propagationDepth = 0;

			if (isDrainingNotifications)
			{
				// Re-entrant — the outer EndPropagation will drain new entries.
				return;
			}

			isDrainingNotifications = true;
			while (pendingNotifications.Count > 0)
			{
				notificationDrainBuffer.Clear();
				foreach (CharacterAttribute attr in pendingNotifications)
				{
					notificationDrainBuffer.Add(attr);
				}
				pendingNotifications.Clear();
				for (int i = 0; i < notificationDrainBuffer.Count; i++)
				{
					notificationDrainBuffer[i].OnAttributeUpdated?.Invoke(notificationDrainBuffer[i]);
				}
			}
			isDrainingNotifications = false;
		}

		/// <inheritdoc/>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void EnqueueNotification(CharacterAttribute attribute)
		{
			pendingNotifications.Add(attribute);
		}

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
						AddResourceAttribute(new CharacterResourceAttribute(this, attribute.ID, attribute.InitialValue, attribute.InitialValue, 0));
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

				// Cache regen dependency references now that the graph is fully wired.
				CacheRegenReferences();
			}
			else
			{
				Log.Error("CharacterAttributeController", "Character Attribute Database is missing!");
			}

			// Force the next reconcile snapshot to include every initial attribute value.
			attributeSnapshotDirty = true;
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

			// Reset the regen tick counter so that a reconnect or scene transfer
			// does not carry over a stale partial-interval. Without this, the
			// first Regenerate() call after reset would fire a tick early or late,
			// causing a regen desync between client and server until the next
			// reconcile corrects it.
			regenTickAccum = 0;

			// Defensively clear the cached interval as well. OnStartNetwork recomputes
			// it from TimeManager.TickDelta on the next spawn, so we must not let a
			// stale value from a previous session leak into the gap between ResetState
			// and OnStartNetwork (Regenerate guards on regenTickInterval == 0 → no-op).
			regenTickInterval = 0u;

			// Reset propagation state to prevent stale notifications.
			propagationDepth = 0;
			pendingNotifications.Clear();
			isDrainingNotifications = false;

			foreach (CharacterResourceAttribute characterResourceAttribute in ResourceAttributes.Values)
			{
				characterResourceAttribute.SetCurrentValue(characterResourceAttribute.FinalValue);
			}

			// Force the next reconcile snapshot to rebuild from scratch.
			cachedAttributeSnapshot = null;
			attributeSnapshotDirty = true;
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
		/// Attempts to retrieve the health resource attribute.
		/// </summary>
		/// <param name="health">The found health resource attribute, or null.</param>
		/// <returns>True if the health attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetHealthAttribute(out CharacterResourceAttribute health)
		{
			return ResourceAttributes.TryGetValue(HealthResourceTemplateID, out health);
		}

		/// <summary>
		/// Attempts to retrieve the mana resource attribute.
		/// </summary>
		/// <param name="mana">The found mana resource attribute, or null.</param>
		/// <returns>True if the mana attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetManaAttribute(out CharacterResourceAttribute mana)
		{
			return ResourceAttributes.TryGetValue(ManaResourceTemplateID, out mana);
		}

		/// <summary>
		/// Attempts to retrieve the stamina resource attribute.
		/// </summary>
		/// <param name="stamina">The found stamina resource attribute, or null.</param>
		/// <returns>True if the stamina attribute was found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina)
		{
			return ResourceAttributes.TryGetValue(StaminaResourceTemplateID, out stamina);
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
				// Both client and server need a fresh reconcile snapshot when the
				// attribute set changes (length-changed path produces a full-array
				// write that replaces any cached index-delta state).
				instance.OnAttributeUpdated -= CharacterAttribute_OnAttributeUpdated;
				instance.OnAttributeUpdated += CharacterAttribute_OnAttributeUpdated;
				attributeSnapshotDirty = true;
			}
		}

		/// <summary>
		/// Invalidates the cached reconcile snapshot when any tracked attribute mutates,
		/// unless we are currently inside an authoritative reconcile restore (in which
		/// case the snapshot already matches the server and should not be marked dirty).
		/// Resource attributes are excluded — they ride <see cref="CharacterReconcileData.ResourceState"/>.
		/// </summary>
		private void CharacterAttribute_OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (suppressAttributeDirty || attribute == null || attribute is CharacterResourceAttribute)
			{
				return;
			}
			attributeSnapshotDirty = true;
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
				// No dirty-tracking subscription for resource attributes: their values
				// are reconciled each tick via CharacterReconcileData.ResourceState and
				// forwarded to observers by FishNet Prediction V2 state forwarding.
			}
		}

		/// <summary>
		/// Initializes the tick-based regen interval. Attribute state replication is handled
		/// entirely through the unified reconcile pipeline — there is no per-tick flush subscription.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			// Compute the integer regen interval once so both client and server
			// agree on exactly which ticks produce a regen pulse. This eliminates
			// the float-drift desync the old float accumulator caused.
			if (base.TimeManager != null)
			{
				double td = base.TimeManager.TickDelta;
				regenTickInterval = td > 0.0 ? (uint)Mathf.Max(1, Mathf.CeilToInt((float)(regenTickRate / td))) : 1u;
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
		/// Number of prediction ticks between resource regeneration pulses.
		/// Computed once from <see cref="regenTickRate"/> / TickDelta in
		/// <see cref="OnStartNetwork"/> (or first OnReplicate if not yet set).
		/// Using integer ticks eliminates the float-drift desync that the old
		/// float accumulator caused over ~300+ ticks.
		/// </summary>
		private uint regenTickInterval;

		/// <summary>
		/// Current tick counter toward the next regeneration pulse.
		/// When this reaches <see cref="regenTickInterval"/>, a regen fires and the counter resets.
		/// Reconciled via <see cref="CharacterAttributeResourceState.RegenTickAccum"/>.
		/// </summary>
		private uint regenTickAccum;

		/// <summary>
		/// Cached regeneration dependency attribute references, resolved once during init
		/// to avoid per-tick string-keyed dictionary lookups in <see cref="RegenerateResource"/>.
		/// </summary>
		private CharacterAttribute cachedHealthRegen;
		private CharacterAttribute cachedManaRegen;
		private CharacterAttribute cachedStaminaRegen;

		/// <summary>
		/// Resolves and caches regeneration dependency attribute references for health, mana, and stamina.
		/// Must be called after <see cref="InitializeResourceAttributeDependents"/> so the dependency graph is fully wired.
		/// </summary>
		private void CacheRegenReferences()
		{
			if (HealthResourceTemplateID != 0 && HealthRegenerationTemplateID != 0 &&
				resourceAttributes.TryGetValue(HealthResourceTemplateID, out var health))
			{
				CharacterAttributeTemplate healthRegenTemplate = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(HealthRegenerationTemplateID);
				if (healthRegenTemplate != null)
				{
					cachedHealthRegen = health.GetDependant(healthRegenTemplate.Name);
				}
			}
			if (ManaResourceTemplateID != 0 && ManaRegenerationTemplateID != 0 &&
				resourceAttributes.TryGetValue(ManaResourceTemplateID, out var mana))
			{
				CharacterAttributeTemplate manaRegenTemplate = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(ManaRegenerationTemplateID);
				if (manaRegenTemplate != null)
				{
					cachedManaRegen = mana.GetDependant(manaRegenTemplate.Name);
				}
			}
			if (StaminaResourceTemplateID != 0 && StaminaRegenerationTemplateID != 0 &&
				resourceAttributes.TryGetValue(StaminaResourceTemplateID, out var stamina))
			{
				CharacterAttributeTemplate staminaRegenTemplate = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(StaminaRegenerationTemplateID);
				if (staminaRegenTemplate != null)
				{
					cachedStaminaRegen = stamina.GetDependant(staminaRegenTemplate.Name);
				}
			}
		}

		/// <summary>
		/// Processes resource regeneration for health, mana, and stamina.
		/// Counts prediction ticks and fires a regen pulse every
		/// <see cref="regenTickInterval"/> ticks. Uses integer counting to
		/// guarantee deterministic client/server agreement.
		/// </summary>
		public void Regenerate()
		{
			if (regenTickInterval == 0) return;

			regenTickAccum++;
			if (regenTickAccum >= regenTickInterval)
			{
				regenTickAccum = 0;
				RegenerateResource(HealthResourceTemplateID, cachedHealthRegen, 1);
				RegenerateResource(ManaResourceTemplateID, cachedManaRegen, 1);
				RegenerateResource(StaminaResourceTemplateID, cachedStaminaRegen, 1);
			}
		}

		/// <summary>
		/// Regenerates a single resource attribute using the cached regen dependency reference.
		/// </summary>
		/// <param name="resourceTemplateID">The template ID of the resource to regenerate.</param>
		/// <param name="cachedRegen">The cached regeneration dependency attribute (resolved at init).</param>
		/// <param name="intervals">The number of regen-tick intervals to process.</param>
		private void RegenerateResource(int resourceTemplateID, CharacterAttribute cachedRegen, int intervals)
		{
			if (resourceTemplateID != 0 &&
				cachedRegen != null &&
				resourceAttributes.TryGetValue(resourceTemplateID, out CharacterResourceAttribute resource))
			{
				int totalRegenAmount = cachedRegen.FinalValue * intervals;
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
			if (HealthResourceTemplateID == 0 || ManaResourceTemplateID == 0 || StaminaResourceTemplateID == 0)
			{
				return;
			}

			if (resourceAttributes.TryGetValue(HealthResourceTemplateID, out CharacterResourceAttribute health) &&
				resourceAttributes.TryGetValue(ManaResourceTemplateID, out CharacterResourceAttribute mana) &&
				resourceAttributes.TryGetValue(StaminaResourceTemplateID, out CharacterResourceAttribute stamina))
			{
				regenTickAccum = resourceState.RegenTickAccum;
				float previousHealth = health.CurrentValue;
				int previousMaxHealth = health.FinalValue;
				float previousMana = mana.CurrentValue;
				int previousMaxMana = mana.FinalValue;
				float previousStamina = stamina.CurrentValue;
				int previousMaxStamina = stamina.FinalValue;

				// Batch all notifications so listeners see fully-settled values.
				BeginPropagation();

				health.SetFinal(resourceState.MaxHealth);
				mana.SetFinal(resourceState.MaxMana);
				stamina.SetFinal(resourceState.MaxStamina);

				// Re-propagate to parents so any attribute whose formula depends on
				// these max values recalculates. SetFinal intentionally does not call
				// UpdateValues — doing so would overwrite the reconciled snapshot with
				// a formula-computed result. Only parents need the re-calculation.
				PropagateToParents(health);
				PropagateToParents(mana);
				PropagateToParents(stamina);

				// Apply all current values without immediate callbacks so reconcile does not
				// spam intermediate UI updates. Notifications are re-enqueued once below.
				health.SetCurrentValue(resourceState.Health, false);
				mana.SetCurrentValue(resourceState.Mana, false);
				stamina.SetCurrentValue(resourceState.Stamina, false);

				if (previousMaxHealth != health.FinalValue || previousHealth != health.CurrentValue)
				{
					EnqueueNotification(health);
				}
				if (previousMaxMana != mana.FinalValue || previousMana != mana.CurrentValue)
				{
					EnqueueNotification(mana);
				}
				if (previousMaxStamina != stamina.FinalValue || previousStamina != stamina.CurrentValue)
				{
					EnqueueNotification(stamina);
				}

				EndPropagation();
			}
		}

		/// <summary>
		/// Re-propagates value changes to all parent attributes.
		/// Used after <see cref="CharacterAttribute.SetFinal"/> to ensure
		/// dependent formulas reflect the new FinalValue without recalculating the attribute itself.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void PropagateToParents(CharacterAttribute attribute)
		{
			foreach (CharacterAttribute parent in attribute.Parents.Values)
			{
				parent.UpdateValues();
			}
		}

		/// <summary>
		/// Captures the current resource state as a snapshot for FishNet's Replicate/Reconcile prediction system.
		/// </summary>
		/// <returns>A snapshot containing the current health, mana, stamina, and regeneration delta.</returns>
		public CharacterAttributeResourceState GetResourceState()
		{
			if (HealthResourceTemplateID == 0 || ManaResourceTemplateID == 0 || StaminaResourceTemplateID == 0)
			{
				return default;
			}

			if (resourceAttributes.TryGetValue(HealthResourceTemplateID, out CharacterResourceAttribute health) &&
				resourceAttributes.TryGetValue(ManaResourceTemplateID, out CharacterResourceAttribute mana) &&
				resourceAttributes.TryGetValue(StaminaResourceTemplateID, out CharacterResourceAttribute stamina))
			{
				return new CharacterAttributeResourceState()
				{
					RegenTickAccum = regenTickAccum,
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

		/// <summary>
		/// Attributes do not contribute owner input into <see cref="CharacterReplicateData"/>.
		/// </summary>
		/// <param name="input">Unified replicate input for this tick.</param>
		public void PopulateInput(ref CharacterReplicateData input)
		{
		}

		/// <summary>
		/// Advances deterministic resource regeneration for the current prediction tick.
		/// </summary>
		/// <param name="input">Unified replicate input for this tick.</param>
		/// <param name="state">Current replicate execution state.</param>
		/// <param name="channel">Transport channel.</param>
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			if (base.TimeManager == null)
			{
				return;
			}

			// Regen state mutation (regenTickAccum advance, resource value Gain) must
			// run every replay tick to stay in lock-step with the authoritative server, but
			// the OnAttributeUpdated notifications that resource.Gain raises must NOT fire
			// once per replay tick (UI flicker / repeated ECA). Wrap Regenerate() in a
			// propagation scope and discard pendingNotifications when replaying so the
			// authoritative reconcile (ApplyResourceState) remains the sole source of
			// client-visible resource update events.
			if (state.ContainsReplayed())
			{
				BeginPropagation();
				try
				{
					Regenerate();
				}
				finally
				{
					pendingNotifications.Clear();
					EndPropagation();
				}
			}
			else
			{
				Regenerate();
			}
		}

		/// <summary>
		/// Writes the full reconcile snapshot for this tick: resource state plus
		/// the sorted-by-template-ID non-resource attribute snapshot.
		/// </summary>
		/// <param name="reconcileData">Mutable unified reconcile payload.</param>
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			reconcileData.ResourceState = GetResourceState();
			reconcileData.Attributes = CreateAttributeSnapshot();
		}

		/// <summary>
		/// Restores resource state and the full non-resource attribute snapshot
		/// from authoritative reconcile data. Notifications are batched through
		/// the propagation system; dirty-tracking is suppressed during the restore
		/// so the rebuilt snapshot retains <c>ReferenceEquals</c> identity.
		/// </summary>
		/// <param name="rd">Unified reconcile payload.</param>
		/// <param name="channel">Transport channel.</param>
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			ApplyResourceState(rd.ResourceState);
			ApplyAttributeSnapshot(rd.Attributes);
		}

		/// <summary>
		/// Builds (or returns cached) a sorted-by-TemplateID array of non-resource
		/// attribute entries for the reconcile snapshot.
		/// <para>
		/// The cache is invalidated by <see cref="CharacterAttribute_OnAttributeUpdated"/>
		/// whenever any tracked non-resource attribute mutates. When unchanged, the
		/// same array reference is returned across consecutive ticks, allowing
		/// <see cref="AttributeReconcileEntry.WriteArrayDelta"/> to skip the array
		/// entirely via <c>ReferenceEquals</c>.
		/// </para>
		/// </summary>
		private AttributeReconcileEntry[] CreateAttributeSnapshot()
		{
			if (!attributeSnapshotDirty && cachedAttributeSnapshot != null)
			{
				return cachedAttributeSnapshot;
			}

			int count = attributes.Count;
			if (count == 0)
			{
				cachedAttributeSnapshot = null;
				attributeSnapshotDirty = false;
				return null;
			}

			// Collect into a flat array, then sort by TemplateID so index-delta
			// comparisons in subsequent ticks remain meaningful.
			AttributeReconcileEntry[] snapshot = new AttributeReconcileEntry[count];
			int i = 0;
			foreach (CharacterAttribute attribute in attributes.Values)
			{
				snapshot[i++] = new AttributeReconcileEntry
				{
					TemplateID = attribute.Template.ID,
					Value = attribute.Value,
					ExternalModifier = attribute.ExternalModifier,
				};
			}
			System.Array.Sort(snapshot, (a, b) => a.TemplateID.CompareTo(b.TemplateID));

			cachedAttributeSnapshot = snapshot;
			attributeSnapshotDirty = false;
			return snapshot;
		}

		/// <summary>
		/// Applies an authoritative non-resource attribute snapshot from the server.
		/// Iterates the entries and writes <c>Value</c> + <c>ExternalModifier</c> into the
		/// matching <see cref="CharacterAttribute"/>. <c>FormulaModifier</c> is recomputed
		/// locally via the dependency graph (intentionally not replicated).
		/// </summary>
		/// <param name="snapshot">The reconciled snapshot. May be null when the controller has no attributes.</param>
		private void ApplyAttributeSnapshot(AttributeReconcileEntry[] snapshot)
		{
			if (snapshot == null || snapshot.Length == 0 || attributes.Count == 0)
			{
				return;
			}

			// Suppress dirty-tracking: the values we are writing match the
			// server, so the cached snapshot already represents canonical state.
			// Batch notifications so derived attributes settle before listeners fire.
			suppressAttributeDirty = true;
			BeginPropagation();
			try
			{
				for (int i = 0; i < snapshot.Length; i++)
				{
					AttributeReconcileEntry entry = snapshot[i];
					if (attributes.TryGetValue(entry.TemplateID, out CharacterAttribute attribute))
					{
						attribute.SetValue(entry.Value);
						attribute.SetModifier(entry.ExternalModifier);
					}
				}
			}
			finally
			{
				EndPropagation();
				suppressAttributeDirty = false;
			}
		}
	}
}