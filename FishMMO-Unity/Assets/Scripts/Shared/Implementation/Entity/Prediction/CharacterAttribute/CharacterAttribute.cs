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
		/// The modifier accumulated from external sources such as equipped items, buffs, and region effects.
		/// Persistent across formula recalculations. Managed via <see cref="AddModifier"/> and <see cref="SetModifier"/>.
		/// </summary>
		private int externalModifier;

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
				value = newValue;
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
				value = tmp;
				UpdateValues(forceUpdate);
			}
		}
		/// <summary>
		/// Sets the external modifier value and propagates changes through the attribute hierarchy.
		/// Used by systems such as NPC initialization and network synchronization.
		/// </summary>
		/// <param name="newValue">The new external modifier value.</param>
		public void SetModifier(int newValue)
		{
			if (externalModifier != newValue)
			{
				externalModifier = newValue;
				UpdateValues();
			}
		}

		/// <summary>
		/// Adds or subtracts an amount from the external modifier and propagates changes through the attribute hierarchy.
		/// Used by items, buffs, and region effects. Addition: AddModifier(10) | Subtraction: AddModifier(-10)
		/// </summary>
		/// <param name="amount">The amount to add (can be negative).</param>
		public void AddModifier(int amount)
		{
			int tmp = externalModifier + amount;
			if (externalModifier != tmp)
			{
				externalModifier = tmp;
				UpdateValues();
			}
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
			value = newValue;
		}

		/// <summary>
		/// Sets the external modifier directly without recomputing derived values or notifying listeners.
		/// Used exclusively for two-phase reconcile alongside <see cref="SetValueDirect"/>.
		/// </summary>
		/// <param name="newValue">The new external modifier value.</param>
		public void SetModifierDirect(int newValue)
		{
			externalModifier = newValue;
		}

		/// <summary>
		/// Sets the final value directly. Use with caution; normally final value is calculated.
		/// </summary>
		/// <param name="newValue">The new final value.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetFinal(int newValue)
		{
			finalValue = newValue;
		}

		/// <summary>
		/// Installs an authoritative final value AND back-solves <see cref="ExternalModifier"/> so a
		/// later recompute reproduces it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="SetFinal"/> writes <c>finalValue</c> directly, which is what the resource
		/// reconcile wants — the server's number must not be overwritten by a local formula pass.
		/// But it leaves <c>value</c> and <c>externalModifier</c> untouched, and those are what
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
			externalModifier = newFinal - value - formulaModifier;
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
			externalModifier = initialModifier;
			formulaModifier = 0;
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