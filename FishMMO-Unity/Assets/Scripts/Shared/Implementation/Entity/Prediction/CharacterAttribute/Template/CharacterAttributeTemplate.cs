using System;
using UnityEngine;

using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Character Attribute", menuName = "FishMMO/Character/Attribute/Character Attribute", order = 1)]
	/// <summary>
	/// Template that defines a character attribute's configuration, including base values, clamping rules,
	/// parent/child/dependency relationships, and formula mappings for derived calculations.
	/// </summary>
	public class CharacterAttributeTemplate : CachedScriptableObject<CharacterAttributeTemplate>, ICachedObject, ITooltip
	{
		/// <summary>
		/// Attributes carry no icon of their own; the bars and rows that show them supply theirs.
		/// </summary>
		/// <remarks>
		/// Declared because <see cref="ITooltip"/> asks for one. An attribute is describable — the
		/// resource bars and the character sheet hover it — and it could not satisfy the interface
		/// without this, which is why it was the one producer left outside the tooltip system.
		/// </remarks>
		public UnityEngine.Sprite Icon => null;

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
		/// Describes the attribute: what it is and the range it lives in.
		/// </summary>
		/// <param name="content">The content being assembled.</param>
		public void BuildTooltip(TooltipContent content)
		{
			if (!string.IsNullOrWhiteSpace(Name))
			{
				content.AddTitle(Name);
			}
			if (!string.IsNullOrWhiteSpace(Description))
			{
				content.AddBody(Description);
			}
			if (InitialValue > 0)
			{
				content.AddStat("Initial", InitialValue.ToString(), TooltipPriority.Stats);
			}
			if (MinValue > 0)
			{
				content.AddStat("Minimum", MinValue.ToString(), TooltipPriority.Stats + 1);
			}
			if (MaxValue > 0)
			{
				content.AddStat("Maximum", MaxValue.ToString(), TooltipPriority.Stats + 2);
			}
		}
	}
}