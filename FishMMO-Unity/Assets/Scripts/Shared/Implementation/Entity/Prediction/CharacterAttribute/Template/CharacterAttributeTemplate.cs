using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Character Attribute", menuName = "FishMMO/Character/Attribute/Character Attribute", order = 1)]
	/// <summary>
	/// Template that defines a character attribute's configuration, including base values, clamping rules,
	/// parent/child/dependency relationships, and formula mappings for derived calculations.
	/// </summary>
	public class CharacterAttributeTemplate : CachedScriptableObject<CharacterAttributeTemplate>, ICachedObject
	{
		/// <summary>
		/// Serializable dictionary mapping attribute templates to their formula templates.
		/// Used to define how child attributes affect this attribute.
		/// </summary>
		[Serializable]
		public class CharacterAttributeFormulaDictionary : SerializableDictionary<CharacterAttributeTemplate, CharacterAttributeFormulaTemplate> { }

		/// <summary>
		/// Serializable set of attribute templates. Used for parent, child, and dependant relationships.
		/// </summary>
		[Serializable]
		public class CharacterAttributeSet : SerializableHashSet<CharacterAttributeTemplate> { }

		/// <summary>
		/// A description of the attribute, used for tooltips and UI.
		/// </summary>
		public string Description;

		/// <summary>
		/// The initial (base) value for this attribute when a character is created.
		/// </summary>
		public int InitialValue;

		/// <summary>
		/// The minimum value this attribute can have (used for clamping).
		/// </summary>
		public int MinValue;

		/// <summary>
		/// The maximum value this attribute can have (used for clamping).
		/// </summary>
		public int MaxValue;

		/// <summary>
		/// If true, this attribute is treated as a percentage (e.g., 0-100%).
		/// </summary>
		public bool IsPercentage;

		/// <summary>
		/// If true, this attribute is a resource (e.g., health, mana) that can be consumed or regenerated.
		/// </summary>
		public bool IsResourceAttribute;

		/// <summary>
		/// If true, the final value of this attribute is clamped between MinValue and MaxValue.
		/// </summary>
		public bool ClampFinalValue;

		/// <summary>
		/// Set of parent attribute types (attributes that depend on this one).
		/// </summary>
		public CharacterAttributeSet ParentTypes = new CharacterAttributeSet();

		/// <summary>
		/// Set of child attribute types (attributes this one depends on for formulas).
		/// </summary>
		public CharacterAttributeSet ChildTypes = new CharacterAttributeSet();

		/// <summary>
		/// Set of dependant attribute types (additional dependencies for complex relationships).
		/// </summary>
		public CharacterAttributeSet DependantTypes = new CharacterAttributeSet();

		/// <summary>
		/// Dictionary of formulas defining how each child attribute affects this attribute.
		/// </summary>
		public CharacterAttributeFormulaDictionary Formulas = new CharacterAttributeFormulaDictionary();

		/// <summary>
		/// The display name of the attribute (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// Returns the initial value as a percentage (InitialValue * 0.01f).
		/// </summary>
		public float InitialValueAsPct { get { return InitialValue * 0.01f; } }

		/// <summary>
		/// Builds a rich text tooltip string describing this attribute, including name, description, and value ranges.
		/// Uses <see cref="TooltipColors"/> for consistent formatting.
		/// </summary>
		/// <returns>Formatted tooltip string for UI display.</returns>
		public string Tooltip()
		{
			using (var builder = new TooltipBuilder())
			{
				if (!string.IsNullOrWhiteSpace(Name))
				{
					builder.AddLine(Name, 0, TooltipColors.Title, false, "120%");
				}
				if (!string.IsNullOrWhiteSpace(Description))
				{
					builder.AddLine($"Description: {Description}", 10, TooltipColors.Label);
				}
				if (InitialValue > 0)
				{
					builder.AddLine($"Initial Value: {InitialValue}", 20, TooltipColors.Label);
				}
				if (MinValue > 0)
				{
					builder.AddLine($"Min Value: {MinValue}", 30, TooltipColors.Label);
				}
				if (MaxValue > 0)
				{
					builder.AddLine($"Max Value: {MaxValue}", 40, TooltipColors.Label);
				}
				return builder.Build();
			}
		}
	}
}