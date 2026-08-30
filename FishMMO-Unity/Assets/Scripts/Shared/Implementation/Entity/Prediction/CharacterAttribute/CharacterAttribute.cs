using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a character attribute, including its value, modifier, dependencies, and hierarchical relationships.
	/// Supports parent/child/dependency relationships and value propagation for complex attribute systems.
	/// </summary>
	public class CharacterAttribute
	{
		/// <summary>
		/// Reference to the controller that manages this attribute, allowing for callbacks and interactions with the owning character or system.
		/// </summary>
		protected ICharacterAttributeController characterAttributeController;

		/// <summary>
		/// Version number for this attribute instance, used for client synchronization and updates.
		/// Incremented whenever the attribute's state changes in a way that requires client updates (
		/// e.g., value or modifier changes that affect the final value).
		/// Not incremented for changes that do not affect client state (e.g., internal
		/// tracking of dependencies that doesn't meet the next update threshold).
		/// </summary>
		public long Version;

		/// <summary>
		/// Counts every change to a persisted field. Advances on mutation, never on a save.
		/// </summary>
		/// <remarks>
		/// <see cref="Version"/> cannot do this job. It is bumped by the save snapshot and by
		/// nothing else, so two snapshots of an attribute that changed between them differ, but an
		/// attribute that changes WHILE a save is in flight still carries the version that save
		/// wrote. A guard built on it would clear the mark on a change the write never contained.
		/// This counter moves when the value moves, which is the question being asked.
		/// </remarks>
		private long changeCount;

		/// <summary>The <see cref="changeCount"/> observed when the in-flight save snapshotted.</summary>
		private long snapshotChangeCount;

		/// <summary>The <see cref="Version"/> stamped on the in-flight save's snapshot.</summary>
		private long snapshotVersion;

		/// <summary>
		/// Whether this attribute has changed since the database last confirmed it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The periodic save used to write every attribute of every resident character on every
		/// pass, because it had no way to tell which had moved. Most have not: strength does not
		/// drift while a player stands in a bank, and a character out of combat at full health
		/// changes nothing at all between one save and the next.
		/// </para>
		/// <para>
		/// <b>Marked by the writers of the PERSISTED fields, and by nothing else.</b> Those fields
		/// are <see cref="Value"/> for every attribute and <c>CurrentValue</c> for a resource —
		/// <see cref="ExternalModifier"/> and <see cref="FinalValue"/> are not written to the
		/// database, because they are rebuilt from the ledger and the formula graph on load. Every
		/// writer compares before it assigns, so setting a field to the value it already holds marks
		/// nothing.
		/// </para>
		/// <para>
		/// <b>It used to be marked from <see cref="Internal_OnAttributeChanged"/> instead</b>, which
		/// is the funnel every change passes through — including changes that move no persisted
		/// field at all. Equipping an item, a buff ticking, walking into a region: each marked its
		/// attribute dirty, and the periodic save then rewrote a row whose contents were identical.
		/// In combat that was most of the sheet, most of the time, which is precisely the case the
		/// flag was introduced to avoid.
		/// </para>
		/// </remarks>
		public bool PersistenceDirty { get; private set; }

		/// <summary>
		/// Records that this attribute has changed in a way the database does not have yet.
		/// </summary>
		protected void MarkPersistenceDirty()
		{
			unchecked { ++changeCount; }
			PersistenceDirty = true;
		}

		/// <summary>
		/// Records the snapshot a save is about to write, so its confirmation can be checked
		/// against what the attribute has done since.
		/// </summary>
		/// <remarks>
		/// Called on the main thread as the batch is built, immediately after <see cref="Version"/>
		/// is stamped. The mark deliberately stays set until the write is confirmed: an attribute
		/// with a save in flight is still one the database does not have, so a second save path
		/// — a logout, a despawn — that runs in the meantime must still pick it up.
		/// </remarks>
		/// <param name="stampedVersion">The version written onto the snapshot row.</param>
		public void MarkPersistPending(long stampedVersion)
		{
			snapshotVersion = stampedVersion;
			snapshotChangeCount = changeCount;
		}

		/// <summary>
		/// Clears <see cref="PersistenceDirty"/> if the confirmed write is still the newest thing
		/// this attribute has to say.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two guards, and both are load-bearing. The version must match the snapshot that is being
		/// confirmed, so a stale confirmation — an earlier save landing after a later one already
		/// snapshotted — cannot clear a mark it knows nothing about. The change count must match
		/// what the snapshot saw, so a value that moved while the write was in flight stays dirty
		/// and is carried to the next pass.
		/// </para>
		/// <para>
		/// A save that fails never calls this at all, so nothing is lost — the attribute is simply
		/// written again on the following pass. That matters more than it looks: the periodic save
		/// has no retry of its own, because writing everything every time WAS the retry.
		/// </para>
		/// </remarks>
		/// <param name="persistedVersion">The version that was successfully written.</param>
		public void MarkPersisted(long persistedVersion)
		{
			if (persistedVersion == snapshotVersion &&
				changeCount == snapshotChangeCount)
			{
				PersistenceDirty = false;
			}
		}

		/// <summary>
		/// The template that defines this attribute's configuration and formulas.
		/// </summary>
		public CharacterAttributeTemplate Template { get; private set; }

		/// <summary>
		/// The base value of the attribute before any modifiers are applied.
		/// </summary>
		private int value;

		/// <summary>
		/// The modifier derived from child attribute formulas. Reset and recalculated each time
		/// <see cref="ApplyChildren"/> runs. This value is entirely managed by the formula system.
		/// </summary>
		private int formulaModifier;

		/// <summary>
		/// The modifier contributed by external sources such as equipped items, buffs, region effects
		/// and NPC scaling. Persistent across formula recalculations.
		/// </summary>
		/// <remarks>
		/// <b>A cached sum, not the storage.</b> The storage is <see cref="modifierSources"/>; this is
		/// kept in step with it so <see cref="CalculateFinalValue"/> and every reader of
		/// <see cref="ExternalModifier"/> stay a single field read. Never assign to it outside
		/// <see cref="RecomputeExternalModifier"/>.
		/// </remarks>
		private int externalModifier;

		/// <summary>
		/// Who contributed what. Lazily allocated: an attribute nobody has modified holds nothing.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A short list rather than a dictionary. An attribute carries a handful of sources at most —
		/// a couple of items, a couple of buffs — so a linear walk with a struct compare beats hashing
		/// and allocates nothing until the first source arrives. See <see cref="ModifierSource"/> for
		/// why attribution is needed at all.
		/// </para>
		/// <para>
		/// An entry whose value reaches zero is REMOVED rather than kept at zero, so the list length
		/// tracks live contributors and a test can assert that a source was genuinely released rather
		/// than merely zeroed.
		/// </para>
		/// </remarks>
		private List<ModifierEntry> modifierSources;

		/// <summary>One contributor's entry in the ledger.</summary>
		private struct ModifierEntry
		{
			public ModifierSource Source;
			public int Value;
		}

		/// <summary>
		/// The final value of the attribute after applying all modifiers and clamping (if enabled by the template).
		/// Calculated as <c>value + formulaModifier + externalModifier</c>.
		/// </summary>
		private int finalValue;

		/// <summary>
		/// Attributes that depend on this attribute (parents in the attribute hierarchy).
		/// When this attribute changes, these parent attributes may need to update as well.
		/// </summary>
		private SortedDictionary<int, CharacterAttribute> parents = new SortedDictionary<int, CharacterAttribute>();

		/// <summary>
		/// Attributes that this attribute depends on (children in the attribute hierarchy).
		/// These are used in formulas to calculate this attribute's value.
		/// </summary>
		private Dictionary<string, CharacterAttribute> children = new Dictionary<string, CharacterAttribute>();

		/// <summary>
		/// Additional dependency attributes that may influence this attribute's value or logic.
		/// Used for more complex relationships beyond parent/child.
		/// </summary>
		private Dictionary<string, CharacterAttribute> dependencies = new Dictionary<string, CharacterAttribute>();

		/// <summary>
		/// Event invoked when this attribute is updated (value, modifier, or final value changes).
		/// </summary>
		public Action<CharacterAttribute> OnAttributeUpdated;

		/// <summary>
		/// Invokes the <see cref="OnAttributeUpdated"/> event for the given attribute.
		/// During graph propagation, the notification is deferred until all values stabilize.
		/// </summary>
		/// <param name="item">The attribute that was changed.</param>
		protected virtual void Internal_OnAttributeChanged(CharacterAttribute item)
		{
			/* Deliberately does NOT mark persistence dirty. This fires for every change, and most
			 * changes move nothing the database stores: an external modifier arriving, a formula
			 * recomputing because a child moved, a propagation pass reaching a parent. The writers
			 * of the persisted fields mark themselves — see PersistenceDirty. */

			if (characterAttributeController != null && characterAttributeController.IsPropagating)
			{
				characterAttributeController.EnqueueNotification(item);
				return;
			}
			OnAttributeUpdated?.Invoke(item);
		}

		/// <summary>
		/// Gets the base value of the attribute (before modifiers).
		/// </summary>
		public int Value { get { return value; } }

		/// <summary>
		/// Sets the base value of the attribute and updates dependent values if changed.
		/// </summary>
		/// <param name="newValue">The new base value.</param>
		/// <param name="forceUpdate">If true, forces update even if value is unchanged.</param>
		public void SetValue(int newValue, bool forceUpdate = false)
		{
			if (forceUpdate || value != newValue)
			{
				bool moved = value != newValue;
				value = newValue;
				// Only when the number actually moved. forceUpdate asks for a graph pass, not for a
				// database write, and marking on it would rewrite the row on every forced refresh.
				if (moved)
				{
					MarkPersistenceDirty();
				}
				UpdateValues(forceUpdate);
			}
		}

		/// <summary>
		/// Adds or subtracts an amount from the base value of the attribute. Addition: AddValue(123) | Subtraction: AddValue(-123)
		/// </summary>
		/// <param name="amount">The amount to add (can be negative).</param>
		/// <param name="forceUpdate">If true, forces update even if value is unchanged.</param>
		public void AddValue(int amount, bool forceUpdate = false)
		{
			int tmp = value + amount;
			if (forceUpdate || value != tmp)
			{
				bool moved = value != tmp;
				value = tmp;
				if (moved)
				{
					MarkPersistenceDirty();
				}
				UpdateValues(forceUpdate);
			}
		}
		/// <summary>
		/// Installs or replaces one named source's contribution.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Idempotent, which is the whole point.</b> Calling this twice with the same source and
		/// value leaves one entry worth that value — so applying an item or a buff a second time, from
		/// a database load, a payload restore or a reconcile replay, cannot double it. That property
		/// is what replaced a set of carefully ordered suppressions.
		/// </para>
		/// <para>
		/// A value of zero removes the entry. A source contributing nothing is not a contributor, and
		/// keeping it would make "is this source applied?" un-answerable.
		/// </para>
		/// </remarks>
		/// <param name="source">Who is contributing.</param>
		/// <param name="value">Their whole contribution, not a delta.</param>
		public void SetSource(ModifierSource source, int value)
		{
			if (SetSourceSilent(source, value))
			{
				UpdateValues();
			}
		}

		/// <summary>
		/// Removes one named source's contribution.
		/// </summary>
		/// <remarks>
		/// A source that is not present is a no-op, and that is deliberate: it is the correct answer
		/// for a peer that never applied it. The old shape subtracted a stored value unconditionally,
		/// so a client whose add had been suppressed still ran the subtraction and drove the sheet
		/// negative until the next authoritative push corrected it.
		/// </remarks>
		/// <param name="source">The contributor to release.</param>
		public void ClearSource(ModifierSource source)
		{
			if (SetSourceSilent(source, 0))
			{
				UpdateValues();
			}
		}

		/// <summary>The contribution currently recorded for one source, or zero when it has none.</summary>
		public int GetSourceValue(ModifierSource source)
		{
			if (modifierSources == null)
			{
				return 0;
			}
			for (int i = 0; i < modifierSources.Count; ++i)
			{
				if (modifierSources[i].Source == source)
				{
					return modifierSources[i].Value;
				}
			}
			return 0;
		}

		/// <summary>Number of live contributors. For diagnostics and tests.</summary>
		public int ModifierSourceCount => modifierSources?.Count ?? 0;

		/// <summary>
		/// Drops every contribution, attributed or not.
		/// </summary>
		/// <remarks>
		/// For a character being recycled, where the whole sheet belongs to the previous occupant.
		/// Distinct from <c>SetModifierDirect(0)</c>, which would install a residual of minus the
		/// attributed sum and leave those sources in place — a total of zero today and the previous
		/// occupant's contributors still in the ledger tomorrow.
		/// </remarks>
		public void ClearAllModifierSources()
		{
			if (modifierSources == null || modifierSources.Count == 0)
			{
				externalModifier = 0;
				return;
			}
			modifierSources.Clear();
			externalModifier = 0;
		}

		/// <summary>
		/// Writes a source and refreshes the cached sum, without touching the attribute graph.
		/// </summary>
		/// <returns>True when the total actually moved.</returns>
		private bool SetSourceSilent(ModifierSource source, int value)
		{
			int previous = externalModifier;

			if (modifierSources == null)
			{
				if (value == 0)
				{
					return false;
				}
				modifierSources = new List<ModifierEntry>(2);
			}

			int index = -1;
			for (int i = 0; i < modifierSources.Count; ++i)
			{
				if (modifierSources[i].Source == source)
				{
					index = i;
					break;
				}
			}

			if (value == 0)
			{
				if (index < 0)
				{
					return false;
				}
				modifierSources.RemoveAt(index);
			}
			else if (index < 0)
			{
				modifierSources.Add(new ModifierEntry { Source = source, Value = value });
			}
			else
			{
				if (modifierSources[index].Value == value)
				{
					return false;
				}
				modifierSources[index] = new ModifierEntry { Source = source, Value = value };
			}

			RecomputeExternalModifier();
			return externalModifier != previous;
		}

		/// <summary>Refreshes the cached sum from the ledger.</summary>
		private void RecomputeExternalModifier()
		{
			int total = 0;
			if (modifierSources != null)
			{
				for (int i = 0; i < modifierSources.Count; ++i)
				{
					total += modifierSources[i].Value;
				}
			}
			externalModifier = total;
		}

		/// <summary>
		/// Installs an authoritative TOTAL, preserving what this peer has attributed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The total is the server's answer and must be reproduced exactly, but it is not a
		/// contributor — so it lands in the <see cref="ModifierSourceKind.Authoritative"/> entry as
		/// the RESIDUAL between the server's number and the sum of everything this peer attributed.
		/// The observable total is identical to the old wholesale overwrite; what changes is that the
		/// attributed sources survive it.
		/// </para>
		/// <para>
		/// That survival is the point. Collapsing the ledger to a single entry every reconcile would
		/// leave the owner unable to release an item or a buff between reconciles — the release would
		/// find nothing to remove and silently keep the bonus until the next authoritative push.
		/// </para>
		/// </remarks>
		/// <param name="newValue">The server's total external modifier.</param>
		public void SetModifier(int newValue)
		{
			if (SetAuthoritativeTotalSilent(newValue))
			{
				UpdateValues();
			}
		}

		/// <summary>
		/// Adds an unattributed amount to the external modifier.
		/// </summary>
		/// <remarks>
		/// <b>Prefer <see cref="SetSource"/>.</b> This writes into the
		/// <see cref="ModifierSourceKind.Unattributed"/> bucket, which nothing can release except by
		/// adding the negation — the exact failure the ledger exists to end. Nothing in the shipped
		/// call graph uses it; it survives as a visible escape hatch rather than an absent one, and as
		/// the shape the notification-suppression tests exercise.
		/// </remarks>
		/// <param name="amount">The amount to add (can be negative).</param>
		public void AddModifier(int amount)
		{
			if (amount == 0)
			{
				return;
			}
			SetSource(ModifierSource.Unattributed, GetSourceValue(ModifierSource.Unattributed) + amount);
		}

		/// <summary>
		/// Sets the authoritative residual so the ledger sums to <paramref name="newValue"/>.
		/// </summary>
		/// <returns>True when the total actually moved.</returns>
		private bool SetAuthoritativeTotalSilent(int newValue)
		{
			int attributed = externalModifier - GetSourceValue(ModifierSource.Authoritative);
			return SetSourceSilent(ModifierSource.Authoritative, newValue - attributed);
		}

		/// <summary>
		/// Sets the base value directly without recomputing derived values or notifying listeners.
		/// Used exclusively for two-phase reconcile in
		/// <see cref="CharacterAttributeController.ApplyAttributeSnapshot"/>; the caller is
		/// responsible for calling <see cref="UpdateValues(bool)"/> after all values have been
		/// applied to guarantee a single correct graph evaluation pass with no intermediate states.
		/// </summary>
		/// <param name="newValue">The new base value.</param>
		public void SetValueDirect(int newValue)
		{
			if (value == newValue)
			{
				return;
			}

			value = newValue;

			/* Marked here as well as in Internal_OnAttributeChanged. This setter exists precisely to
			 * write the value without raising the change event, so the funnel that normally records
			 * the change never runs — and an attribute the database is behind on that does not say
			 * so is one the periodic save silently stops writing. */
			MarkPersistenceDirty();
		}

		/// <summary>
		/// Sets the external modifier total without recomputing derived values or notifying listeners.
		/// Used exclusively for two-phase reconcile alongside <see cref="SetValueDirect"/>.
		/// </summary>
		/// <remarks>
		/// The silent twin of <see cref="SetModifier"/>, and it installs the same residual — see there
		/// for why the attributed sources are preserved rather than collapsed. Silence is what the
		/// two-phase reconcile needs: phase one writes every raw value, phase two runs one graph pass.
		/// </remarks>
		/// <param name="newValue">The server's total external modifier.</param>
		public void SetModifierDirect(int newValue)
		{
			SetAuthoritativeTotalSilent(newValue);
		}

		/* SetFinal was REMOVED rather than left as an unused public setter.
		 *
		 * It wrote finalValue and nothing else, which is exactly half of what an authoritative
		 * install needs: value and externalModifier are what CalculateFinalValue reads, so the next
		 * recompute — any AddModifier from a buff, an equip or an unequip — threw the server's
		 * number away. SetFinalDerivingModifier below is the whole operation and is what the
		 * resource reconcile calls; leaving the half-version available under the shorter name is how
		 * the bug comes back. */

		/// <summary>
		/// Installs an authoritative final value AND back-solves <see cref="ExternalModifier"/> so a
		/// later recompute reproduces it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Writing <c>finalValue</c> directly is what the resource reconcile wants — the server's
		/// number must not be overwritten by a local formula pass. But writing it ALONE (which is
		/// all the removed <c>SetFinal</c> did) leaves <c>value</c> and <c>externalModifier</c>
		/// untouched, and those are what
		/// <see cref="CalculateFinalValue"/> reads. Resource attributes carry neither of them in the
		/// reconcile, so the very next thing that called <c>UpdateValues</c> on the resource — any
		/// <see cref="AddModifier"/> from a buff, an equip or an unequip — recomputed the final from
		/// state the reconcile had never corrected and threw the authoritative maximum away.
		/// </para>
		/// <para>
		/// Choosing the modifier that closes the gap makes the two agree: the value is right now, and
		/// it is still right after the next recompute. The clamp is applied deliberately rather than
		/// bypassed, so this peer lands on exactly the number the server's own clamped
		/// <c>CalculateFinalValue</c> produced for the same template.
		/// </para>
		/// </remarks>
		/// <param name="newFinal">The authoritative final value.</param>
		public void SetFinalDerivingModifier(int newFinal)
		{
			// The total the graph must arrive at, then the residual that gets it there without
			// discarding what this peer has attributed. See SetModifier.
			SetAuthoritativeTotalSilent(newFinal - value - formulaModifier);
			finalValue = CalculateFinalValue();
		}

		/// <summary>
		/// Gets the total modifier value (formula-derived + external).
		/// </summary>
		public int Modifier { get { return formulaModifier + externalModifier; } }

		/// <summary>
		/// Gets the modifier derived from child attribute formulas.
		/// </summary>
		public int FormulaModifier { get { return formulaModifier; } }

		/// <summary>
		/// Gets the modifier accumulated from external sources (items, buffs, regions).
		/// </summary>
		public int ExternalModifier { get { return externalModifier; } }

		/// <summary>
		/// Gets the final value of the attribute after applying modifiers and clamping.
		/// </summary>
		public int FinalValue { get { return finalValue; } }

		/// <summary>
		/// Returns the final value as a float.
		/// </summary>
		public float FinalValueAsFloat { get { return (float)finalValue; } }

		/// <summary>
		/// Returns the final value as a percentage (FinalValue * 0.01f).
		/// </summary>
		public float FinalValueAsPct { get { return finalValue * 0.01f; } }

		/// <summary>
		/// Parents of this attribute (the attributes that depend on it), keyed by Template.ID.
		/// </summary>
		/// <remarks>
		/// <see cref="SortedDictionary{TKey,TValue}"/> with the default <c>int</c> comparer
		/// guarantees ascending-ID iteration across all platforms, runtimes and rehash events, so
		/// listeners observe the cascade in a stable order. It is NOT what makes the arithmetic
		/// deterministic — <c>ApplyChildren</c> accumulates <c>int</c>s, and integer addition is
		/// associative, so no iteration order can change the value it produces. Do not unsort it on
		/// the strength of that; the notification order is the reason it is a SortedDictionary.
		/// Keying by ID rather than name also survives template renames without affecting sort order.
		/// </remarks>
		public SortedDictionary<int, CharacterAttribute> Parents { get { return parents; } }

		/// <summary>
		/// Gets the child attributes (attributes this attribute depends on).
		/// </summary>
		public Dictionary<string, CharacterAttribute> Children { get { return children; } }

		/// <summary>
		/// Gets the dependency attributes (additional dependencies for this attribute).
		/// </summary>
		public Dictionary<string, CharacterAttribute> Dependencies { get { return dependencies; } }

		/// <summary>
		/// Returns a string representation of the attribute (name and final value).
		/// </summary>
		public override string ToString()
		{
			return Template.Name + ": " + FinalValue;
		}

		/// <summary>
		/// Constructs a new CharacterAttribute from a template ID, initial value, and initial modifier.
		/// </summary>
		/// <param name="templateID">The template ID to use.</param>
		/// <param name="initialValue">The initial base value.</param>
		/// <param name="initialModifier">The initial modifier value.</param>
		public CharacterAttribute(ICharacterAttributeController characterAttributeController, int templateID, int initialValue, int initialModifier)
		{
			this.characterAttributeController = characterAttributeController;
			Template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(templateID);
			value = initialValue;
			formulaModifier = 0;
			// Through the ledger, so a freshly constructed attribute is already attributed rather
			// than carrying a number no source owns.
			SetSourceSilent(ModifierSource.Authoritative, initialModifier);
			finalValue = CalculateFinalValue();
		}

		/// <summary>
		/// Adds a parent attribute (an attribute that depends on this one).
		/// </summary>
		/// <param name="parent">The parent attribute to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddParent(CharacterAttribute parent)
		{
			if (!parents.ContainsKey(parent.Template.ID))
			{
				parents.Add(parent.Template.ID, parent);
			}
		}

		/// <summary>
		/// Removes a parent attribute.
		/// </summary>
		/// <param name="parent">The parent attribute to remove.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveParent(CharacterAttribute parent)
		{
			parents.Remove(parent.Template.ID);
		}

		/// <summary>
		/// Adds a child attribute (an attribute this one depends on).
		/// </summary>
		/// <param name="child">The child attribute to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddChild(CharacterAttribute child)
		{
			if (!children.ContainsKey(child.Template.Name))
			{
				children.Add(child.Template.Name, child);
				child.AddParent(this);
				UpdateValues();
			}
		}

		/// <summary>
		/// Removes a child attribute.
		/// </summary>
		/// <param name="child">The child attribute to remove.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveChild(CharacterAttribute child)
		{
			children.Remove(child.Template.Name);
			child.RemoveParent(this);
			UpdateValues();
		}

		/// <summary>
		/// Adds a dependency attribute.
		/// </summary>
		/// <param name="dependency">The dependency attribute to add.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddDependant(CharacterAttribute dependency)
		{
			if (!dependencies.ContainsKey(dependency.Template.Name))
			{
				dependencies.Add(dependency.Template.Name, dependency);
			}
		}

		/// <summary>
		/// Removes a dependency attribute.
		/// </summary>
		/// <param name="dependency">The dependency attribute to remove.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveDependant(CharacterAttribute dependency)
		{
			dependencies.Remove(dependency.Template.Name);
		}

		/// <summary>
		/// Gets a dependency attribute by name.
		/// </summary>
		/// <param name="name">The name of the dependency attribute.</param>
		/// <returns>The dependency attribute, or null if not found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public CharacterAttribute GetDependant(string name)
		{
			dependencies.TryGetValue(name, out CharacterAttribute result);
			return result;
		}

		/// <summary>
		/// Gets the value of a dependency attribute by name.
		/// </summary>
		/// <param name="name">The name of the dependency attribute.</param>
		/// <returns>The value of the dependency attribute, or 0 if not found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int GetDependantValue(string name)
		{
			return !dependencies.TryGetValue(name, out CharacterAttribute attribute) ? 0 : attribute.Value;
		}

		/// <summary>
		/// Gets the minimum value of a dependency attribute by name.
		/// </summary>
		/// <param name="name">The name of the dependency attribute.</param>
		/// <returns>The minimum value of the dependency attribute, or 0 if not found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int GetDependantMinValue(string name)
		{
			return !dependencies.TryGetValue(name, out CharacterAttribute attribute) ? 0 : attribute.Template.MinValue;
		}

		/// <summary>
		/// Gets the maximum value of a dependency attribute by name.
		/// </summary>
		/// <param name="name">The name of the dependency attribute.</param>
		/// <returns>The maximum value of the dependency attribute, or 0 if not found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int GetDependantMaxValue(string name)
		{
			return !dependencies.TryGetValue(name, out CharacterAttribute attribute) ? 0 : attribute.Template.MaxValue;
		}

		/// <summary>
		/// Gets the modifier of a dependency attribute by name.
		/// </summary>
		/// <param name="name">The name of the dependency attribute.</param>
		/// <returns>The modifier of the dependency attribute, or 0 if not found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int GetDependantModifier(string name)
		{
			return !dependencies.TryGetValue(name, out CharacterAttribute attribute) ? 0 : attribute.Modifier;
		}

		/// <summary>
		/// Gets the final value of a dependency attribute by name.
		/// </summary>
		/// <param name="name">The name of the dependency attribute.</param>
		/// <returns>The final value of the dependency attribute, or 0 if not found.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int GetDependantFinalValue(string name)
		{
			return !dependencies.TryGetValue(name, out CharacterAttribute attribute) ? 0 : attribute.FinalValue;
		}

		/// <summary>
		/// Maximum recursion depth for the attribute propagation chain inside
		/// <see cref="UpdateValues(bool)"/>. The graph is validated to be acyclic at
		/// startup by <c>CharacterAttributeController.ValidateGraphAcyclic</c>, making
		/// this depth unreachable under normal operation. The guard exists for runtime
		/// graph-mutation bugs (dynamic rewiring, malformed template injection at runtime)
		/// that could otherwise produce a stack overflow without a clear error message.
		/// </summary>
		private const int MaxPropagationDepth = 256;

		/// <summary>
		/// Updates the attribute's values and propagates changes to parent attributes if needed.
		/// </summary>
		public void UpdateValues()
		{
			UpdateValues(false, 0);
		}

		/// <summary>
		/// Updates the attribute's values, propagates changes to parent attributes if needed,
		/// and notifies listeners after propagation completes.
		/// <para>
		/// The outermost call brackets the entire graph walk with
		/// <see cref="ICharacterAttributeController.BeginPropagation"/> /
		/// <see cref="ICharacterAttributeController.EndPropagation"/>.
		/// Intermediate nodes enqueue notifications instead of firing them,
		/// so listeners only see fully-stabilized values.
		/// </para>
		/// </summary>
		/// <param name="forceUpdate">If true, forces update even if value is unchanged.</param>
		public void UpdateValues(bool forceUpdate)
		{
			UpdateValues(forceUpdate, 0);
		}

		/// <summary>
		/// Internal depth-tracked implementation of <see cref="UpdateValues(bool)"/>.
		/// The <paramref name="depth"/> parameter is incremented on each recursive parent
		/// call; exceeding <see cref="MaxPropagationDepth"/> logs a Critical error and
		/// halts propagation to prevent a stack overflow from a runtime graph mutation bug.
		/// </summary>
		private void UpdateValues(bool forceUpdate, int depth)
		{
			if (depth > MaxPropagationDepth)
			{
				Log.Error("CharacterAttribute",
					$"UpdateValues exceeded MaxPropagationDepth ({MaxPropagationDepth}) on attribute " +
					$"TemplateID={Template.ID} ({Template.name}). The attribute graph may have been " +
					"mutated at runtime to create excessive chain depth or an undiscovered cycle. " +
					"Halting propagation to prevent a stack overflow.");
				return;
			}

			bool isRoot = characterAttributeController != null && !characterAttributeController.IsPropagating;
			if (isRoot)
			{
				characterAttributeController.BeginPropagation();
			}

			int oldFinalValue = finalValue;

			ApplyChildren();

			// If the final value changed, propagate the update to all parents.
			if (forceUpdate || finalValue != oldFinalValue)
			{
				foreach (CharacterAttribute parent in parents.Values)
				{
					parent.UpdateValues(false, depth + 1);
				}
			}

			Internal_OnAttributeChanged(this);

			if (isRoot)
			{
				characterAttributeController.EndPropagation();
			}
		}

		/// <summary>
		/// Recalculates the formula modifier from child attribute formulas, then updates the final value.
		/// Only resets <see cref="formulaModifier"/>; <see cref="externalModifier"/> is preserved.
		/// Event notification is performed by <see cref="UpdateValues(bool)"/> after parent propagation.
		/// </summary>
		private void ApplyChildren()
		{
			formulaModifier = 0;
			if (Template.Formulas != null)
			{
				foreach (KeyValuePair<CharacterAttributeTemplate, CharacterAttributeFormulaTemplate> pair in Template.Formulas)
				{
					if (children.TryGetValue(pair.Key.Name, out CharacterAttribute child))
					{
						formulaModifier += pair.Value.CalculateBonus(characterAttributeController, this, child);
					}
				}
			}
			finalValue = CalculateFinalValue();
		}

		/// <summary>
		/// Calculates the final value by adding base value and modifier, and clamps if required by the template.
		/// </summary>
		/// <returns>The calculated final value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private int CalculateFinalValue()
		{
			int total = value + formulaModifier + externalModifier;
			if (Template.ClampFinalValue)
			{
				return total.Clamp(Template.MinValue, Template.MaxValue);
			}
			return total;
		}
	}
}