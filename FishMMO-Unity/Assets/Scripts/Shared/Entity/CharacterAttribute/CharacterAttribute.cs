using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a character attribute, including its value, modifier, dependencies, and hierarchical relationships.
	/// Supports parent/child/dependency relationships and value propagation for complex attribute systems.
	/// </summary>
	public class CharacterAttribute
	{
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
		private Dictionary<string, CharacterAttribute> parents = new Dictionary<string, CharacterAttribute>();

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
		/// </summary>
		/// <param name="item">The attribute that was changed.</param>
		protected virtual void Internal_OnAttributeChanged(CharacterAttribute item)
		{
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
		/// Sets the final value directly. Use with caution; normally final value is calculated.
		/// </summary>
		/// <param name="newValue">The new final value.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetFinal(int newValue)
		{
			finalValue = newValue;
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
		/// Gets the parent attributes (attributes that depend on this attribute).
		/// </summary>
		public Dictionary<string, CharacterAttribute> Parents { get { return parents; } }

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
		public CharacterAttribute(int templateID, int initialValue, int initialModifier)
		{
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
			if (!parents.ContainsKey(parent.Template.Name))
			{
				parents.Add(parent.Template.Name, parent);
			}
		}

		/// <summary>
		/// Removes a parent attribute.
		/// </summary>
		/// <param name="parent">The parent attribute to remove.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveParent(CharacterAttribute parent)
		{
			parents.Remove(parent.Template.Name);
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
		/// Updates the attribute's values and propagates changes to parent attributes if needed.
		/// </summary>
		public void UpdateValues()
		{
			UpdateValues(false);
		}

		/// <summary>
		/// Updates the attribute's values, propagates changes to parent attributes if needed,
		/// and notifies listeners after propagation completes.
		/// </summary>
		/// <param name="forceUpdate">If true, forces update even if value is unchanged.</param>
		public void UpdateValues(bool forceUpdate)
		{
			int oldFinalValue = finalValue;

			ApplyChildren();

			// If the final value changed, propagate the update to all parents
			if (forceUpdate || finalValue != oldFinalValue)
			{
				foreach (CharacterAttribute parent in parents.Values)
				{
					parent.UpdateValues();
				}
			}

			Internal_OnAttributeChanged(this);
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
						formulaModifier += pair.Value.CalculateBonus(this, child);
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