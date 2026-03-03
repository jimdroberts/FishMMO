using System;
using Cysharp.Text;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Character Attribute", menuName = "FishMMO/Character/Attribute/Character Attribute", order = 1)]
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
		/// </summary>
		/// <returns>Formatted tooltip string for UI display.</returns>
		public string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				// Add the attribute name in styled text if present
				if (!string.IsNullOrWhiteSpace(Name))
				{
					sb.Append("<size=120%><color=#f5ad6e>");
					sb.Append(Name);
					sb.Append("</color></size>");
				}
				// Add the description if present
				if (!string.IsNullOrWhiteSpace(Description))
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Description: ");
					sb.Append(Description);
					sb.Append("</color>");
				}
				// Show initial value if greater than 0
				if (InitialValue > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Initial Value: ");
					sb.Append(InitialValue);
					sb.Append("</color>");
				}
				// Show min value if greater than 0
				if (MinValue > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Min Value: ");
					sb.Append(MinValue);
					sb.Append("</color>");
				}
				// Show max value if greater than 0
				if (MaxValue > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Max Value: ");
					sb.Append(MaxValue);
					sb.Append("</color>");
				}
				return sb.ToString();
			}
		}
	}
}