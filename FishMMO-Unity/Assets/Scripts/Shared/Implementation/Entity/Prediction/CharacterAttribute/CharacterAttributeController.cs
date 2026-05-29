using System;
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
		/// Cached sorted template IDs for attributes. Rebuilt only when the attribute
		/// key set changes so we can avoid sorting on every value mutation.
		/// </summary>
		private int[] cachedSortedTemplateIDs;

		/// <summary>
		/// Cached sorted attribute references matching <see cref="cachedSortedTemplateIDs"/>.
		/// This removes dictionary lookups from the hot snapshot creation path.
		/// </summary>
		private CharacterAttribute[] cachedSortedAttributes;

		/// <summary>
		/// Monotonic version counter incremented whenever the attribute keyset changes
		/// (add/remove). Used to invalidate <see cref="cachedSortedTemplateIDs"/> /
		/// <see cref="cachedSortedAttributes"/> even when an add+remove leaves Count
		/// unchanged (which the length-only check would miss).
		/// Using <c>ulong</c> instead of <c>uint</c> to avoid wrap-around in very
		/// long-running servers (uint wraps in ~4 billion increments).
		/// </summary>
		private ulong attributeStructureVersion;

		/// <summary>
		/// The <see cref="attributeStructureVersion"/> at which the cached sorted
		/// ordering was last built. When this drifts from the current version the
		/// cache is rebuilt.
		/// </summary>
		private ulong cachedSortedStructureVersion;

		/// <summary>
		/// While true, attribute-update notifications (raised during reconcile restore)
		/// MUST NOT mark <see cref="cachedAttributeSnapshot"/> dirty. Reconcile restores
		/// the canonical state from the server snapshot — invalidating the snapshot here
		/// would force a needless rebuild and break <c>ReferenceEquals</c> identity on
		/// the next tick when nothing has actually changed.
		/// </summary>
		private bool suppressAttributeDirty;

		/// <summary>
		/// When true, <see cref="EnqueueNotification"/> is a no-op and all attribute-change
		/// events are fully discarded. Set during prediction replay so listeners (UI callbacks,
		/// ECA triggers, passive proc systems) cannot observe intermediate resimulation states.
		/// <para>
		/// Without this guard a listener triggered transitively by <see cref="Regenerate"/>
		/// (e.g. a resource.Gain callback that mutates another attribute) can enqueue a second
		/// wave of notifications that <see cref="EndPropagation"/> then drains, leaking
		/// replay side-effects into the committed state even after
		/// <see cref="DiscardPendingNotifications"/> has cleared the first wave.
		/// </para>
		/// Must be cleared in a <c>finally</c> block to guarantee it cannot get stuck <c>true</c>
		/// after an exception.
		/// </summary>
		private bool suppressNotifications;

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
		/// <para>
		/// <b>Deduplication semantics:</b> because this is a <c>HashSet</c>, an attribute
		/// that mutates multiple times within a single propagation batch produces only
		/// <em>one</em> notification to listeners — the "changed at least once" model.
		/// Systems that need per-mutation events (delta accumulation, threshold crossings,
		/// or before/after state comparisons) must not rely on this queue for that purpose.
		/// </para>
		/// </summary>
		private readonly HashSet<CharacterAttribute> pendingNotifications = new HashSet<CharacterAttribute>();

		/// <summary>
		/// Reusable set buffer for tracking which template IDs appear in the current
		/// reconcile snapshot. Allocated once and cleared before each use in
		/// <see cref="ApplyAttributeSnapshot"/> to avoid a per-reconcile
		/// <c>HashSet&lt;int&gt;</c> allocation on the reconcile path.
		/// </summary>
		private readonly HashSet<int> reconcileSeenIDs = new HashSet<int>();

		/// <summary>
		/// Reusable buffer for draining <see cref="pendingNotifications"/> without
		/// iterator-invalidation issues if a listener triggers a new propagation.
		/// </summary>
		private readonly List<CharacterAttribute> notificationDrainBuffer = new List<CharacterAttribute>();

		/// <summary>
		/// Stable comparer used to sort the notification drain buffer by Template.ID so
		/// listener invocation order is deterministic across platforms and rehash boundaries
		/// (HashSet enumeration order is not specified). Required for prediction determinism
		/// when multiple attributes mutate within the same propagation batch.
		/// <para>
		/// Implemented as a sealed singleton <see cref="IComparer{T}"/> rather than a
		/// <c>Comparison&lt;T&gt;</c> delegate so that <see cref="List{T}.Sort(IComparer{T})"/>
		/// can be called without allocating an intermediate <c>FunctorComparer&lt;T&gt;</c>
		/// wrapper on the heap. <c>List&lt;T&gt;.Sort(Comparison&lt;T&gt;)</c> boxes the
		/// delegate into a heap-allocated wrapper internally in Unity Mono — a per-drain GC
		/// hit that compounds for every character with attribute changes in the same tick.
		/// </para>
		/// </summary>
		private sealed class CharacterAttributeIDComparer : IComparer<CharacterAttribute>
		{
			/// <summary>Singleton — avoids any allocation at the call site.</summary>
			internal static readonly CharacterAttributeIDComparer Instance = new CharacterAttributeIDComparer();
			private CharacterAttributeIDComparer() { }
			/// <inheritdoc/>
			public int Compare(CharacterAttribute x, CharacterAttribute y) =>
				x.Template.ID.CompareTo(y.Template.ID);
		}

		/// <summary>
		/// Maximum number of consecutive notification drain passes inside a single
		/// <see cref="EndPropagation"/> call. A well-behaved listener should never
		/// re-enqueue the attribute it just received in a way that causes unbounded
		/// re-notification, but without graph cycles the cycle-detection in
		/// <see cref="ValidateGraphAcyclic"/> does not cover listener-level re-entrancy.
		/// This cap converts a potential tick-time hang into a detectable error.
		/// Set to 1 000 (not 10 000) so a runaway listener cannot freeze an entire
		/// server tick before the guard fires.
		/// </summary>
		private const int MaxNotificationDrainIterations = 1000;

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
			// Guard against underflow: each EndPropagation must pair with a BeginPropagation.
			// An underflow corrupts IsPropagating reporting and may cascade into silent
			// notification loss; log and return rather than going negative.
			if (propagationDepth <= 0)
			{
				propagationDepth = 0;
				Log.Error("CharacterAttributeController",
					"EndPropagation called without a matching BeginPropagation (propagationDepth <= 0). " +
					"Begin/End mismatch detected; call ignored to prevent depth underflow.");
				return;
			}

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
			try
			{
				int drainIterations = 0;
				while (pendingNotifications.Count > 0)
				{
					// Safety valve: a listener that re-enqueues the attribute it just received
					// can cause unbounded re-notification even without a dependency-graph cycle
					// (ValidateGraphAcyclic only covers graph topology, not listener behaviour).
					// Cap iterations and hard-fail to prevent a tick-time hang.
					if (++drainIterations > MaxNotificationDrainIterations)
					{
						Log.Error("CharacterAttributeController",
							$"EndPropagation exceeded {MaxNotificationDrainIterations} drain iterations. " +
							"A notification listener is likely re-enqueueing attributes; discarding queue to prevent an infinite loop.");
						pendingNotifications.Clear();
						break;
					}

					notificationDrainBuffer.Clear();
					foreach (CharacterAttribute attr in pendingNotifications)
					{
						notificationDrainBuffer.Add(attr);
					}
					pendingNotifications.Clear();

					// Sort by stable Template.ID so listener invocation order matches
					// across server and client during reconcile replay. HashSet enumeration
					// order is unspecified and can drift across .NET runtime versions.
					notificationDrainBuffer.Sort(CharacterAttributeIDComparer.Instance);

					for (int i = 0; i < notificationDrainBuffer.Count; i++)
					{
						CharacterAttribute drainAttr = notificationDrainBuffer[i];
						// Per-listener isolation: one misbehaving listener must not abort the
						// entire propagation pass and leave subsequent listeners without
						// their notifications. Log and continue.
						try
						{
							drainAttr.OnAttributeUpdated?.Invoke(drainAttr);
						}
						catch (Exception ex)
						{
							// Null-conditional: Template should never be null on a live attribute, but
							// guarding prevents a secondary NRE inside the catch masking the original.
							Log.Error("CharacterAttributeController",
								$"EndPropagation: listener on attribute TemplateID={drainAttr?.Template?.ID.ToString() ?? "unknown"} threw an exception; " +
								$"skipping to preserve remaining notifications. Exception: {ex}");
						}
					}
				}
			}
			finally
			{
				isDrainingNotifications = false;
			}
		}

		/// <summary>
		/// Discards any queued notifications without touching propagation stack state.
		/// CRITICAL: must NOT reset <see cref="propagationDepth"/> or
		/// <see cref="isDrainingNotifications"/> — doing so corrupts the propagation
		/// stack if called from within a nested BeginPropagation/EndPropagation pair
		/// (e.g. during replay nested inside another prediction system), causing
		/// orphaned notifications and EndPropagation to decrement below zero.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void DiscardPendingNotifications()
		{
			pendingNotifications.Clear();
		}

		/// <inheritdoc/>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void EnqueueNotification(CharacterAttribute attribute)
		{
			// suppressNotifications is set during prediction replay so that listener
			// side-effects (UI updates, ECA triggers) do not fire during resimulation,
			// and so that a listener which mutates an attribute cannot enqueue a second
			// wave of notifications that escapes DiscardPendingNotifications.
			if (suppressNotifications)
			{
				return;
			}
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

				// Validate no cycles exist in the attribute dependency graph.
				// Cycles cause infinite recursion inside UpdateValues() during prediction
				// reconcile — a hard fail at startup is far safer than a tick-time crash.
				ValidateGraphAcyclic();
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
		/// Attributes are written in ascending Template.ID order for deterministic serialization
		/// across platforms regardless of Dictionary enumeration order.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to serialize attribute data into.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			// Sort keys for deterministic wire order. WritePayload is a one-shot
			// initialisation call, so the allocation is acceptable.
			var sortedAttrIDs = new List<int>(Attributes.Keys);
			sortedAttrIDs.Sort();

			writer.WriteInt32(sortedAttrIDs.Count);
			for (int i = 0; i < sortedAttrIDs.Count; i++)
			{
				CharacterAttribute attribute = Attributes[sortedAttrIDs[i]];
				writer.WriteInt32(attribute.Template.ID);
				writer.WriteInt32(attribute.Value);
			}

			var sortedResIDs = new List<int>(ResourceAttributes.Keys);
			sortedResIDs.Sort();

			writer.WriteInt32(sortedResIDs.Count);
			for (int i = 0; i < sortedResIDs.Count; i++)
			{
				CharacterResourceAttribute resourceAttribute = ResourceAttributes[sortedResIDs[i]];
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

			// Reset regen state.
			regenTickInterval = 0u;
			regenIntervalSourceTickDelta = 0.0;
			nextRegenTick = 0u;
			hasNextRegenTick = false;
			lastProcessedRegenTick = 0u;
			hasLastProcessedRegenTick = false;

			// Reset propagation state.
			propagationDepth = 0;
			pendingNotifications.Clear();
			isDrainingNotifications = false;
			// Clear suppression flags unconditionally. An exception that exits a
			// BeginPropagation/EndPropagation or replay block with one of these stuck
			// true would silently break notification dispatch or snapshot invalidation
			// for the entire remaining lifetime of the entity.
			suppressNotifications = false;
			suppressAttributeDirty = false;

			foreach (CharacterResourceAttribute characterResourceAttribute in ResourceAttributes.Values)
			{
				characterResourceAttribute.SetCurrentValue(characterResourceAttribute.FinalValue);
			}

			// Force full snapshot rebuild.
			cachedAttributeSnapshot = null;
			attributeSnapshotDirty = true;

			cachedSortedTemplateIDs = null;
			cachedSortedAttributes = null;
			// Bump structure version so any cached ordering is invalidated.
			unchecked { attributeStructureVersion++; }
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
			if (Attributes.TryAdd(instance.Template.ID, instance))
			{
				// Both client and server need a fresh reconcile snapshot when the
				// attribute set changes (length-changed path produces a full-array
				// write that replaces any cached index-delta state).
				instance.OnAttributeUpdated -= CharacterAttribute_OnAttributeUpdated;
				instance.OnAttributeUpdated += CharacterAttribute_OnAttributeUpdated;

				// Invalidate cached snapshot state.
				attributeSnapshotDirty = true;

				// Bump the structure version so the sorted ordering is rebuilt even
				// when a paired add+remove leaves Count unchanged.
				unchecked { attributeStructureVersion++; }
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
		/// <para>
		/// Template lists are sorted by ID before wiring so graph construction order is deterministic
		/// across ScriptableObject serialization orderings and platform/editor differences.
		/// The copies are intentional — do NOT sort the template's own lists in-place as those are
		/// shared ScriptableObject data.
		/// </para>
		/// </summary>
		/// <param name="instance">The attribute instance to wire relationships for.</param>
		private void AddDependents(CharacterAttribute instance)
		{
			// Defensive copies sorted by ID — InitializeOnce is a one-shot call so allocation is fine.
			var sortedParents = new List<CharacterAttributeTemplate>(instance.Template.ParentTypes);
			var sortedChildren = new List<CharacterAttributeTemplate>(instance.Template.ChildTypes);
			var sortedDependants = new List<CharacterAttributeTemplate>(instance.Template.DependantTypes);
			sortedParents.Sort((a, b) => a.ID.CompareTo(b.ID));
			sortedChildren.Sort((a, b) => a.ID.CompareTo(b.ID));
			sortedDependants.Sort((a, b) => a.ID.CompareTo(b.ID));

			foreach (CharacterAttributeTemplate parent in sortedParents)
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

			foreach (CharacterAttributeTemplate child in sortedChildren)
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

			foreach (CharacterAttributeTemplate dependant in sortedDependants)
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
		/// Validates the attribute dependency graph has no cycles after wiring.
		/// A cycle causes infinite recursion inside <c>UpdateValues()</c> during
		/// prediction reconcile, which is an unrecoverable tick-time crash. A hard
		/// fail at startup (InitializeOnce) is far safer.
		/// </summary>
		private void ValidateGraphAcyclic()
		{
			// visited  = fully explored (no cycle through this node)
			// inStack  = on the current DFS path (cycle if re-entered)
			var visited = new HashSet<int>();
			var inStack = new HashSet<int>();

			// NOTE: dictionary enumeration order is non-deterministic across .NET runtimes
			// and hash seeds. That is intentional here — ValidateGraphAcyclic is startup-only
			// and the DFS explores every reachable node regardless of root iteration order.
			// Do NOT use raw dictionary iteration for any runtime propagation path that
			// feeds into prediction reconcile; use sorted collections there.
			foreach (CharacterAttribute root in attributes.Values)
			{
				if (!visited.Contains(root.Template.ID))
				{
					DFS_CheckCycle(root, visited, inStack);
				}
			}

			foreach (CharacterResourceAttribute root in ResourceAttributes.Values)
			{
				if (!visited.Contains(root.Template.ID))
				{
					DFS_CheckCycle(root, visited, inStack);
				}
			}
		}

		/// <summary>
		/// DFS visitor used by <see cref="ValidateGraphAcyclic"/>.
		/// Traverses <c>Parents</c> (the propagation direction: child value changes
		/// propagate upward to parents). Logs a Critical error and throws if a
		/// back-edge is found.
		/// </summary>
		private void DFS_CheckCycle(CharacterAttribute node, HashSet<int> visited, HashSet<int> inStack)
		{
			int id = node.Template.ID;
			inStack.Add(id);

			foreach (CharacterAttribute parent in node.Parents.Values)
			{
				int parentID = parent.Template.ID;
				if (inStack.Contains(parentID))
				{
					// Critical: cycle detected — would cause infinite recursion at reconcile.
					string msg = $"ValidateGraphAcyclic: Cycle detected in attribute graph! " +
								 $"Node {id} ({node.Template.name}) -> Parent {parentID} ({parent.Template.name}) creates a back-edge. " +
								 $"Fix the CharacterAttributeTemplate ScriptableObject data before entering play mode.";
					Log.Error("CharacterAttributeController", msg);
					throw new InvalidOperationException(msg);
				}

				if (!visited.Contains(parentID))
				{
					DFS_CheckCycle(parent, visited, inStack);
				}
			}

			inStack.Remove(id);
			visited.Add(id);
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
				// NOTE: resource attributes are intentionally excluded from the non-resource
				// attribute snapshot cache (cachedAttributeSnapshot / attributeStructureVersion).
				// Resources ride CharacterAttributeResourceState, so no version bump here.
				// If resources are ever folded into the generic snapshot, add the equivalent
				// unchecked { attributeStructureVersion++; } and OnAttributeUpdated subscription.
			}
		}

		/// <summary>
		/// Initializes the tick-based regen interval. Attribute state replication is handled
		/// entirely through the unified reconcile pipeline — there is no per-tick flush subscription.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			// Compute the integer regen interval so both client and server agree on
			// exactly which ticks produce a regen pulse. Recomputed on demand by
			// EnsureRegenIntervalCurrent() if TickDelta changes at runtime.
			EnsureRegenIntervalCurrent();
		}

		/// <summary>
		/// Recomputes <see cref="regenTickInterval"/> if the active TimeManager's
		/// TickDelta has changed since the last computation. Called at start and at
		/// the top of every replicate so a runtime tick-rate change cannot strand
		/// the regen interval at a stale value (#3 — TickDelta-change resilience).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void EnsureRegenIntervalCurrent()
		{
			if (base.TimeManager == null)
			{
				return;
			}

			double td = base.TimeManager.TickDelta;
			if (td <= 0.0)
			{
				return;
			}

			// Recompute only when TickDelta has meaningfully changed. An epsilon
			// comparison is used instead of exact bit equality because TickDelta may
			// originate from a float or be subject to serialization rounding: a tiny
			// representational drift with exact equality would suppress the rebuild
			// indefinitely, silently leaving the interval stale.
			if (regenTickInterval == 0u || Math.Abs(td - regenIntervalSourceTickDelta) > 1e-10)
			{
				regenTickInterval = (uint)Mathf.Max(1, Mathf.CeilToInt((float)(regenTickRate / td)));
				regenIntervalSourceTickDelta = td;
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
		/// Computed lazily from <see cref="regenTickRate"/> / TickDelta and recomputed
		/// whenever <see cref="FishNet.Managing.Timing.TimeManager.TickDelta"/> changes
		/// at runtime (e.g. host migration, reconnect, or rate change). Using integer
		/// ticks eliminates float-drift desync over long sessions.
		/// </summary>
		private uint regenTickInterval;

		/// <summary>
		/// Last TickDelta value used to compute <see cref="regenTickInterval"/>.
		/// Stored so we can detect runtime TickDelta changes and rebuild the
		/// interval without forcing a manual restart.
		/// </summary>
		private double regenIntervalSourceTickDelta;

		/// <summary>
		/// Absolute simulation tick at which the next resource-regeneration pulse should fire.
		/// Reconciled via <see cref="CharacterAttributeResourceState.NextRegenTick"/> so
		/// client and server fire at exactly the same tick after a reconcile.
		/// Using an absolute schedule (rather than <c>tick % interval</c>) means a runtime
		/// TickDelta change cannot cause an immediate spurious regen or a long gap:
		/// the existing scheduled tick fires naturally and the new interval takes effect
		/// only for subsequent pulses.
		/// Zero means the first pulse has not yet been scheduled.
		/// </summary>
		private uint nextRegenTick;

		/// <summary>
		/// False until <see cref="Regenerate(uint)"/> has scheduled the first pulse
		/// (or until <see cref="ApplyResourceState"/> restores a non-zero
		/// <see cref="nextRegenTick"/> from the server). Distinguishes "unstarted" from
		/// "scheduled at tick 0" without a sentinel value.
		/// </summary>
		private bool hasNextRegenTick;

		/// <summary>
		/// The last simulation tick at which <see cref="Regenerate(uint)"/> processed
		/// regen. Used as a monotonic guard so re-execution of the same tick during
		/// replay/resimulation, and execution of out-of-order future ticks ahead of
		/// the server, cannot double-fire a regen pulse. Local-only — not reconciled;
		/// authoritative resource values themselves come via <see cref="ApplyResourceState"/>.
		/// </summary>
		private uint lastProcessedRegenTick;

		/// <summary>
		/// False until <see cref="Regenerate(uint)"/> has accepted at least one tick;
		/// distinguishes "never processed" from "processed tick 0" without using a
		/// sentinel value that could collide with the wrap-safe tick space.
		/// </summary>
		private bool hasLastProcessedRegenTick;

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
				if (healthRegenTemplate == null)
				{
					Log.Warning("CharacterAttributeController",
						$"CacheRegenReferences: HealthRegenerationTemplateID={HealthRegenerationTemplateID} did not resolve to a template. Health regeneration will be disabled.");
				}
				else
				{
					cachedHealthRegen = health.GetDependant(healthRegenTemplate.Name);
					if (cachedHealthRegen == null)
					{
						Log.Warning("CharacterAttributeController",
							$"CacheRegenReferences: HealthRegenerationTemplate '{healthRegenTemplate.Name}' was not found as a dependant of the health resource attribute. " +
							"Check DependantTypes on the health resource template. Health regeneration will be disabled.");
					}
				}
			}
			if (ManaResourceTemplateID != 0 && ManaRegenerationTemplateID != 0 &&
				resourceAttributes.TryGetValue(ManaResourceTemplateID, out var mana))
			{
				CharacterAttributeTemplate manaRegenTemplate = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(ManaRegenerationTemplateID);
				if (manaRegenTemplate == null)
				{
					Log.Warning("CharacterAttributeController",
						$"CacheRegenReferences: ManaRegenerationTemplateID={ManaRegenerationTemplateID} did not resolve to a template. Mana regeneration will be disabled.");
				}
				else
				{
					cachedManaRegen = mana.GetDependant(manaRegenTemplate.Name);
					if (cachedManaRegen == null)
					{
						Log.Warning("CharacterAttributeController",
							$"CacheRegenReferences: ManaRegenerationTemplate '{manaRegenTemplate.Name}' was not found as a dependant of the mana resource attribute. " +
							"Check DependantTypes on the mana resource template. Mana regeneration will be disabled.");
					}
				}
			}
			if (StaminaResourceTemplateID != 0 && StaminaRegenerationTemplateID != 0 &&
				resourceAttributes.TryGetValue(StaminaResourceTemplateID, out var stamina))
			{
				CharacterAttributeTemplate staminaRegenTemplate = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(StaminaRegenerationTemplateID);
				if (staminaRegenTemplate == null)
				{
					Log.Warning("CharacterAttributeController",
						$"CacheRegenReferences: StaminaRegenerationTemplateID={StaminaRegenerationTemplateID} did not resolve to a template. Stamina regeneration will be disabled.");
				}
				else
				{
					cachedStaminaRegen = stamina.GetDependant(staminaRegenTemplate.Name);
					if (cachedStaminaRegen == null)
					{
						Log.Warning("CharacterAttributeController",
							$"CacheRegenReferences: StaminaRegenerationTemplate '{staminaRegenTemplate.Name}' was not found as a dependant of the stamina resource attribute. " +
							"Check DependantTypes on the stamina resource template. Stamina regeneration will be disabled.");
					}
				}
			}
		}

		/// <summary>
		/// Processes resource regeneration for health, mana, and stamina using the
		/// authoritative simulation tick. Fires when the tick reaches the absolute
		/// <see cref="nextRegenTick"/> schedule point, then advances that point by
		/// <see cref="regenTickInterval"/>.
		/// <para>
		/// Absolute scheduling (vs modulo) prevents a runtime TickDelta change from
		/// immediately firing a spurious pulse: the existing scheduled tick fires
		/// naturally, and the new interval applies only for subsequent pulses.
		/// </para>
		/// <para>
		/// <b>IMPORTANT — Replay non-determinism:</b> Regen is intentionally NOT
		/// replayed during rollback. The monotonic guard (<see cref="lastProcessedRegenTick"/>)
		/// causes all replayed ticks within the rollback window to be skipped. Authoritative
		/// resource values are restored by <see cref="ApplyResourceState"/> via reconcile.
		/// Future maintainers must NOT assume replay-deterministic regen — gameplay systems
		/// that depend on intermediate regen events during replay will not observe them.
		/// </para>
		/// </summary>
		/// <param name="tick">Authoritative simulation tick for this replicate step.</param>
		public void Regenerate(uint tick)
		{
			if (regenTickInterval == 0) return;

			// Monotonic guard: never process the same or an earlier tick twice.
			// Wrap-safe signed comparison handles tick rollover (~828 days at 60 Hz).
			if (hasLastProcessedRegenTick && (int)(tick - lastProcessedRegenTick) <= 0)
			{
				return;
			}

			lastProcessedRegenTick = tick;
			hasLastProcessedRegenTick = true;

			// Schedule the first pulse one full interval ahead of the first processed tick.
			// This is also overwritten by ApplyResourceState on reconcile so the client
			// fires at exactly the same absolute tick as the server.
			if (!hasNextRegenTick)
			{
				nextRegenTick = tick + regenTickInterval;
				hasNextRegenTick = true;
			}

			// Wrap-safe check: fire when tick has reached or passed the scheduled point.
			// Advance by += interval (not = tick + interval) to maintain cadence even
			// if a tick is skipped under high RTT or resimulation.
			if ((int)(tick - nextRegenTick) >= 0)
			{
				nextRegenTick += regenTickInterval;
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
				// WARNING — replay side-effect contract:
				// The monotonic guard in Regenerate() prevents this path from being reached
				// during replay: replayed ticks satisfy (tick ≤ lastProcessedRegenTick) and
				// return before RegenerateResource is ever called.
				// If that guard is removed or weakened, this Gain() call WILL mutate resource
				// state during replay. Reconcile overwrites the resource values afterward, but
				// any non-notification side effects inside Gain() (VFX, sounds, analytics,
				// direct gameplay callbacks) will still fire and leak as replay artefacts.
				// Before weakening the guard: audit Gain() for replay-unsafe side effects and
				// consider a suppressSideEffects parameter or a PredictionContext.IsReplaying flag.
				resource.Gain(totalRegenAmount);
			}
		}

		/// <summary>
		/// Applies a resource state snapshot to restore health, mana, stamina, and the regen schedule.
		/// Used by FishNet's Replicate/Reconcile prediction system.
		/// <para>
		/// Each resource is processed independently via <see cref="ApplyIndividualResourceState"/>
		/// so entities that legitimately lack one of the standard resources (NPCs without mana,
		/// monsters without stamina) still have their present resources correctly reconciled.
		/// A template ID of 0 means the resource is not present on this entity and is silently skipped.
		/// </para>
		/// </summary>
		/// <param name="resourceState">The resource state snapshot to apply.</param>
		public void ApplyResourceState(CharacterAttributeResourceState resourceState)
		{
			// Restore the absolute next-regen tick from server state so the client fires
			// at exactly the same tick as the server after reconcile replay.
			// nextRegenTick == 0 means the server has not yet scheduled a pulse;
			// hasNextRegenTick stays false until the first Regenerate() call.
			nextRegenTick = resourceState.NextRegenTick;
			hasNextRegenTick = nextRegenTick != 0u;

			// Batch all notifications so listeners see fully-settled cross-resource values.
			BeginPropagation();
			ApplyIndividualResourceState(HealthResourceTemplateID, resourceState.MaxHealth, resourceState.Health);
			ApplyIndividualResourceState(ManaResourceTemplateID, resourceState.MaxMana, resourceState.Mana);
			ApplyIndividualResourceState(StaminaResourceTemplateID, resourceState.MaxStamina, resourceState.Stamina);
			EndPropagation();
		}

		/// <summary>
		/// Applies a single resource attribute's reconciled max and current values.
		/// Silently skips when <paramref name="templateID"/> is 0 (resource not present on
		/// this entity) or when the ID does not resolve to a live resource attribute.
		/// <para>
		/// <see cref="CharacterAttribute.SetFinal"/> is used in place of
		/// <see cref="CharacterAttribute.SetValue"/> because the reconciled max value is
		/// authoritative from the server — invoking <c>UpdateValues</c> here would
		/// overwrite it with a locally-computed formula result.
		/// <see cref="PropagateToParents"/> propagates the new max upward so any parent
		/// whose formula reads this resource's max value recomputes correctly.
		/// Callers must bracket calls with <see cref="BeginPropagation"/>/<see cref="EndPropagation"/>.
		/// </para>
		/// </summary>
		private void ApplyIndividualResourceState(int templateID, int maxValue, float currentValue)
		{
			if (templateID == 0) return;
			if (!resourceAttributes.TryGetValue(templateID, out CharacterResourceAttribute resource)) return;

			float previousCurrent = resource.CurrentValue;
			int previousMax = resource.FinalValue;

			resource.SetFinal(maxValue);
			PropagateToParents(resource);
			resource.SetCurrentValue(currentValue, false);

			if (previousMax != resource.FinalValue || previousCurrent != resource.CurrentValue)
			{
				EnqueueNotification(resource);
			}
		}

		/// <summary>
		/// Re-propagates a resource attribute's new <c>FinalValue</c> to all parent attributes
		/// whose formulas depend on it.
		/// <para>
		/// <b>SetFinal invariant:</b> <see cref="CharacterAttribute.SetFinal"/> writes
		/// <c>finalValue</c> directly, bypassing <c>CalculateFinalValue</c> and
		/// <c>UpdateValues</c>. This is intentional for reconcile: the authoritative
		/// server value must not be overwritten by a local formula recalculation on the
		/// attribute itself. However, any <em>parent</em> attribute whose formula reads
		/// this attribute's <c>FinalValue</c> must still recompute — that is the sole
		/// responsibility of this helper.
		/// </para>
		/// <para>
		/// <b>Rule:</b> every call to <c>SetFinal</c> on a resource attribute during
		/// reconcile MUST be followed immediately by a call to this helper. Omitting
		/// the call silently breaks formula-derived attributes that read the resource's max.
		/// </para>
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
		/// Captures the current resource state as a snapshot for FishNet's Replicate/Reconcile
		/// prediction system. Each resource is sampled independently; a template ID of 0 means
		/// the resource is not present on this entity and its fields remain at the default (0),
		/// which is harmless for the delta serializer.
		/// </summary>
		/// <returns>A snapshot of present resource values plus the regen schedule.</returns>
		public CharacterAttributeResourceState GetResourceState()
		{
			var state = new CharacterAttributeResourceState
			{
				NextRegenTick = hasNextRegenTick ? nextRegenTick : 0u,
			};

			if (HealthResourceTemplateID != 0 &&
				resourceAttributes.TryGetValue(HealthResourceTemplateID, out CharacterResourceAttribute health))
			{
				state.Health = health.CurrentValue;
				state.MaxHealth = health.FinalValue;
			}
			if (ManaResourceTemplateID != 0 &&
				resourceAttributes.TryGetValue(ManaResourceTemplateID, out CharacterResourceAttribute mana))
			{
				state.Mana = mana.CurrentValue;
				state.MaxMana = mana.FinalValue;
			}
			if (StaminaResourceTemplateID != 0 &&
				resourceAttributes.TryGetValue(StaminaResourceTemplateID, out CharacterResourceAttribute stamina))
			{
				state.Stamina = stamina.CurrentValue;
				state.MaxStamina = stamina.FinalValue;
			}

			return state;
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

			// Pick up any runtime TickDelta change before processing this tick so
			// client and server cannot diverge after a tick-rate swap.
			EnsureRegenIntervalCurrent();

			// Regen is now tick-driven via the authoritative simulation tick from the
			// replicate input, with a per-tick monotonic guard inside Regenerate(tick)
			// to prevent double-firing under replay/resimulation or future-state
			// execution. The OnAttributeUpdated notifications that resource.Gain raises
			// must still NOT fire during replay (UI flicker / repeated ECA), so we wrap
			// the call in a propagation scope and drop queued notifications.
			uint tick = input.GetTick();
			if (state.ContainsReplayed())
			{
				// suppressNotifications ensures that EnqueueNotification is a no-op for the
				// entire replay block, including any second-wave notifications triggered by
				// listeners. Without this, a listener invoked by resource.Gain can mutate
				// another attribute and re-enqueue, bypassing DiscardPendingNotifications.
				suppressNotifications = true;
				BeginPropagation();
				try
				{
					Regenerate(tick);
				}
				finally
				{
					suppressNotifications = false;
					// Drop any residual queued notifications (Belt-and-suspenders:
					// suppressNotifications should have prevented all enqueues above).
					DiscardPendingNotifications();
					// Isolate EndPropagation exceptions: if a listener throws and somehow
					// escapes EndPropagation's own try/finally guard (e.g. re-entrant
					// exceptions), reset propagation state manually rather than leaving
					// propagationDepth > 0 or isDrainingNotifications stuck true permanently.
					try
					{
						EndPropagation();
					}
					catch (Exception ex)
					{
						propagationDepth = 0;
						isDrainingNotifications = false;
						pendingNotifications.Clear();
						Log.Error("CharacterAttributeController",
							$"OnReplicate: EndPropagation threw during replay finally block; " +
							$"propagation state forcibly reset to prevent permanent corruption. Exception: {ex}");
					}
				}
			}
			else
			{
				Regenerate(tick);
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

				// Clear cached ordering/reference state when there are no attributes.
				cachedSortedTemplateIDs = null;
				cachedSortedAttributes = null;
				return null;
			}

			// Rebuild stable sorted ordering + direct references only when keyset changes.
			// Use a version counter so a paired add+remove (same Count) still triggers rebuild.
			if (cachedSortedTemplateIDs == null ||
				cachedSortedTemplateIDs.Length != count ||
				cachedSortedStructureVersion != attributeStructureVersion)
			{
				// Avoid LINQ ToArray() — Dictionary.KeyCollection.CopyTo is a direct
				// array fill with no intermediate IEnumerable allocation.
				cachedSortedTemplateIDs = new int[count];
				attributes.Keys.CopyTo(cachedSortedTemplateIDs, 0);
				System.Array.Sort(cachedSortedTemplateIDs);

				cachedSortedAttributes = new CharacterAttribute[count];

				for (int i = 0; i < count; i++)
				{
					cachedSortedAttributes[i] = attributes[cachedSortedTemplateIDs[i]];
				}

				cachedSortedStructureVersion = attributeStructureVersion;
			}

			// Allocate a fresh snapshot array on each rebuild. This avoids fragile
			// buffer reuse semantics where external systems may hold references
			// longer than anticipated (late reconciles, resimulations, RTT jitter).
			AttributeReconcileEntry[] snapshot = new AttributeReconcileEntry[count];
			for (int i = 0; i < count; i++)
			{
				CharacterAttribute attribute = cachedSortedAttributes[i];
				snapshot[i].TemplateID = attribute.Template.ID;
				snapshot[i].Value = attribute.Value;
				snapshot[i].ExternalModifier = attribute.ExternalModifier;
			}

			cachedAttributeSnapshot = snapshot;
			attributeSnapshotDirty = false;

			return snapshot;
		}

		/// <summary>
		/// Applies an authoritative non-resource attribute snapshot from the server using
		/// a two-phase reconcile to eliminate order-dependent propagation:
		/// <list type="number">
		/// <item><description>
		/// <b>Phase 1 — silent assignment:</b> all raw <c>Value</c> and <c>ExternalModifier</c>
		/// fields are written via <see cref="CharacterAttribute.SetValueDirect"/> /
		/// <see cref="CharacterAttribute.SetModifierDirect"/> without triggering formula
		/// propagation. This prevents an earlier entry's assignment from evaluating a
		/// formula that reads a later entry's value before it has been applied.
		/// </description></item>
		/// <item><description>
		/// <b>Phase 2 — graph rebuild:</b> <see cref="CharacterAttribute.UpdateValues(bool)"/>
		/// is called on every attribute. Because all raw values are correct at this point
		/// the graph converges on the first pass regardless of iteration order, eliminating
		/// redundant intermediate recalculations.
		/// </description></item>
		/// </list>
		/// <c>FormulaModifier</c> is recomputed locally (intentionally not reconciled).
		/// </summary>
		/// <param name="snapshot">The reconciled snapshot. May be null when the controller has no attributes.</param>
		private void ApplyAttributeSnapshot(AttributeReconcileEntry[] snapshot)
		{
			if (snapshot == null || snapshot.Length == 0 || attributes.Count == 0)
			{
				return;
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			// Validate snapshot integrity: IDs must be in strictly ascending order (no duplicates).
			// A violation indicates a bug in CreateAttributeSnapshot or WritePayload.
			for (int i = 1; i < snapshot.Length; i++)
			{
				if (snapshot[i].TemplateID <= snapshot[i - 1].TemplateID)
				{
					Log.Error("CharacterAttributeController",
						$"ApplyAttributeSnapshot: snapshot is not in strictly ascending TemplateID order at index {i} " +
						$"(got {snapshot[i].TemplateID} after {snapshot[i - 1].TemplateID}). Possible serialization bug.");
				}
			}
			// In dev/editor builds, a keyset mismatch is a hard fail: stale client-only
			// attributes silently participate in formula evaluation with wrong values, and
			// the production recovery path (zero-out below) is not exercised during
			// development. Throw early so version skew or hotload bugs surface immediately.
			if (snapshot.Length != attributes.Count)
			{
				throw new InvalidOperationException(
					$"ApplyAttributeSnapshot: snapshot has {snapshot.Length} entries but client has {attributes.Count} attributes. " +
					"Server/client keyset mismatch — resolve before production release.");
			}
#endif

			// suppressAttributeDirty blocks CharacterAttribute_OnAttributeUpdated from marking
			// the snapshot dirty while we write authoritative values. We invalidate explicitly
			// in the finally block once all values are settled, which is both correct and
			// precise: the snapshot is rebuilt on the next CreateAttributeSnapshot call and the
			// ReferenceEquals fast-path remains valid for ticks where nothing changes afterward.
			suppressAttributeDirty = true;
			BeginPropagation();
			try
			{
				// Reuse the controller-level buffer to avoid a per-reconcile HashSet allocation.
				// Tracks which template IDs appear in the authoritative snapshot so we can
				// zero out any client-only stale attributes absent from the server's view.
				reconcileSeenIDs.Clear();

				// Phase 1: write all raw values without triggering formula evaluation so
				// no intermediate graph state is visible during the assignment pass.
				for (int i = 0; i < snapshot.Length; i++)
				{
					AttributeReconcileEntry entry = snapshot[i];
					reconcileSeenIDs.Add(entry.TemplateID);
					if (attributes.TryGetValue(entry.TemplateID, out CharacterAttribute attribute))
					{
						attribute.SetValueDirect(entry.Value);
						attribute.SetModifierDirect(entry.ExternalModifier);
					}
					else
					{
						// A reconcile entry with no matching live attribute is a desync.
						// Log and continue — skipping the missing entry is safer than crashing,
						// but the operator must investigate why server and client keyset differ.
						Log.Error("CharacterAttributeController",
							$"ApplyAttributeSnapshot: reconcile entry TemplateID={entry.TemplateID} has no matching attribute on this client. Possible server/client version mismatch.");
					}
				}

				// Production recovery: zero out client-only stale attributes absent from the
				// authoritative snapshot. Without this, a stale attribute participates in
				// formula evaluation indefinitely — reconcile never resets it and no other
				// system removes it. Parent attributes reading a stale FinalValue silently
				// compute wrong results for the remainder of the entity's lifetime.
				// (In dev/editor builds the count-mismatch throw above fires first.)
				if (reconcileSeenIDs.Count < attributes.Count)
				{
					foreach (KeyValuePair<int, CharacterAttribute> kvp in attributes)
					{
						if (!reconcileSeenIDs.Contains(kvp.Key))
						{
							kvp.Value.SetValueDirect(0);
							kvp.Value.SetModifierDirect(0);
						}
					}
				}

				// Phase 2: rebuild derived values now that all raw values are settled.
				// UpdateValues propagates to parents; since all inputs are correct the
				// graph converges on the first pass regardless of iteration order.
				// Optimization opportunity: track changed IDs during Phase 1 and only
				// rebuild affected subgraphs — currently O(all attributes + all edges)
				// per reconcile, acceptable at MMO attribute-graph sizes.
				for (int i = 0; i < snapshot.Length; i++)
				{
					if (attributes.TryGetValue(snapshot[i].TemplateID, out CharacterAttribute attribute))
					{
						attribute.UpdateValues(false);
					}
				}

				// Also rebuild zeroed stale attributes so their cleared values propagate
				// upward through the formula graph. Without this, parent attributes whose
				// formulas read a stale child's FinalValue compute wrong results even
				// though the stale child's raw values have already been zeroed.
				if (reconcileSeenIDs.Count < attributes.Count)
				{
					foreach (KeyValuePair<int, CharacterAttribute> kvp in attributes)
					{
						if (!reconcileSeenIDs.Contains(kvp.Key))
						{
							kvp.Value.UpdateValues(false);
						}
					}
				}
			}
			finally
			{
				// Isolate EndPropagation exceptions: same pattern as OnReplicate replay
				// block — an escaped listener exception must not leave propagationDepth
				// or isDrainingNotifications permanently corrupted.
				try
				{
					EndPropagation();
				}
				catch (Exception ex)
				{
					propagationDepth = 0;
					isDrainingNotifications = false;
					pendingNotifications.Clear();
					Log.Error("CharacterAttributeController",
						$"ApplyAttributeSnapshot: EndPropagation threw during finally block; " +
						$"propagation state forcibly reset. Exception: {ex}");
				}
				// Belt-and-suspenders: ensure no partial notification queue survives a
				// catastrophic exception path that EndPropagation's own drain-cap guard
				// may not cover (e.g. a StackOverflowException in a listener mid-drain).
				pendingNotifications.Clear();
				suppressAttributeDirty = false;
				// Explicit snapshot invalidation: suppressAttributeDirty blocked
				// CharacterAttribute_OnAttributeUpdated from marking dirty during the write
				// pass, so cachedAttributeSnapshot may still contain pre-reconcile values.
				// Invalidating here guarantees CreateAttributeSnapshot rebuilds on the next
				// call and the outgoing snapshot always reflects post-reconcile state.
				attributeSnapshotDirty = true;
				cachedAttributeSnapshot = null;
			}
		}
	}
}