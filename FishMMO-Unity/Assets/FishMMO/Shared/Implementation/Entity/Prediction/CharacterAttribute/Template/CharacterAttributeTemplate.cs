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
		/// The character sheet heading this attribute is listed under, e.g. "Arena". Empty: the heading for its kind
		/// (Resource, Damage, Resistance, Core).
		/// </summary>
		[Tooltip("The character sheet heading this attribute is listed under, e.g. \"Arena\". Empty: the heading for its kind (Resource, Damage, Resistance, Core).")]
		public string SheetCategory;

		/// <summary>
		/// The name the character sheet shows for this attribute. Empty: the template's name.
		/// </summary>
		/// <remarks>
		/// A label only. The template's name is its identity (the ID is a hash of it), so renaming the asset would orphan
		/// every saved value; this changes only what the sheet reads.
		/// </remarks>
		[Tooltip("The name the character sheet shows. Empty: the template's name. A label only: renaming the asset changes its ID and orphans every saved value.")]
		public string SheetLabel;

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

		/// <summary>What the character sheet calls this attribute: <see cref="SheetLabel"/>, or the name when it is empty.</summary>
		public string SheetName { get { return string.IsNullOrWhiteSpace(SheetLabel) ? Name : SheetLabel; } }

		/// <summary>
		/// Returns the initial value as a percentage (InitialValue * 0.01f).
		/// </summary>
		public float InitialValueAsPct { get { return InitialValue * 0.01f; } }

		/// <summary>
		/// Formats a value in this attribute's own unit, signed.
		/// </summary>
		/// <remarks>
		/// Every tooltip that writes one of these values — an attribute's own sheet numbers, the
		/// bonus an <see cref="AttributeBuffTemplate"/> grants, a modifier a
		/// <see cref="CompositeBuffTemplate"/> carries — goes through here, because the value alone
		/// is ambiguous in a way that is easy to author and impossible to notice. A percentage
		/// attribute's value is in percentage POINTS (<see cref="InitialValueAsPct"/> divides by
		/// 100), so a movement-speed buff authored as 30 means "+30%"; printed raw it reads as
		/// "30", which is a plausible-looking number and wrong by two orders of magnitude.
		/// <para>
		/// The sign is always written, so a modifier is legible as a modifier rather than as an
		/// absolute value that happens to sit in a "Bonus Attributes" block.
		/// </para>
		/// </remarks>
		/// <param name="value">The value to format, in this attribute's own unit.</param>
		/// <returns>The value with its sign and its unit suffix.</returns>
		public string FormatValue(int value)
		{
			return IsPercentage ? $"{value:+#;-#;0}%" : $"{value:+#;-#;0}";
		}

		/// <summary>A value in this attribute's own unit, unsigned: a level, not a change.</summary>
		private string FormatAbsolute(int value)
		{
			return IsPercentage ? $"{value}%" : value.ToString();
		}

		/// <summary>
		/// Describes the attribute: what it is and the range it lives in.
		/// </summary>
		/// <param name="content">The content being assembled.</param>
		public void BuildTooltip(TooltipContent content)
		{
			// The sheet's own words: "Arena Rating" for a row labelled Rating under the Arena heading.
			string title = !string.IsNullOrWhiteSpace(SheetCategory) && !string.IsNullOrWhiteSpace(SheetLabel)
				? SheetCategory.Trim() + " " + SheetLabel.Trim()
				: SheetName;
			if (!string.IsNullOrWhiteSpace(title))
			{
				content.AddTitle(title);
			}
			if (!string.IsNullOrWhiteSpace(Description))
			{
				content.AddBody(Description);
			}
			// Absolute values, not modifiers: no sign. An unbounded maximum is left out rather than printed as 2147483647.
			if (InitialValue > 0)
			{
				content.AddStat("Initial", FormatAbsolute(InitialValue), TooltipPriority.Stats);
			}
			if (MinValue > 0)
			{
				content.AddStat("Minimum", FormatAbsolute(MinValue), TooltipPriority.Stats + 1);
			}
			if (MaxValue > 0 && MaxValue < int.MaxValue)
			{
				content.AddStat("Maximum", FormatAbsolute(MaxValue), TooltipPriority.Stats + 2);
			}
		}
	}
}