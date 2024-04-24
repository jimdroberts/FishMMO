using System;
using Cysharp.Text;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Character Attribute", menuName = "Character/Attribute/Character Attribute", order = 1)]
	public class CharacterAttributeTemplate : CachedScriptableObject<CharacterAttributeTemplate>, ICachedObject
	{
		[Serializable]
		public class CharacterAttributeFormulaDictionary : SerializableDictionary<CharacterAttributeTemplate, CharacterAttributeFormulaTemplate> { }

		[Serializable]
		public class CharacterAttributeSet : SerializableHashSet<CharacterAttributeTemplate> { }

		public string Description;
		public int InitialValue;
		public int MinValue;
		public int MaxValue;
		public bool IsPercentage;
		public bool IsResourceAttribute;
		public bool ClampFinalValue;
		public CharacterAttributeSet ParentTypes = new CharacterAttributeSet();
		public CharacterAttributeSet ChildTypes = new CharacterAttributeSet();
		public CharacterAttributeSet DependantTypes = new CharacterAttributeSet();
		public CharacterAttributeFormulaDictionary Formulas = new CharacterAttributeFormulaDictionary();

		public string Name { get { return this.name; } }
		/// <summary>
		/// Returns InitialValue * 0.01f
		/// </summary>
		public float InitialValueAsPct { get { return InitialValue * 0.01f; } }

		public string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				if (!string.IsNullOrWhiteSpace(Name))
				{
					sb.Append("<size=120%><color=#f5ad6e>");
					sb.Append(Name);
					sb.Append("</color></size>");
				}
				if (!string.IsNullOrWhiteSpace(Description))
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Description: ");
					sb.Append(Description);
					sb.Append("</color>");
				}
				if (InitialValue > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Initial Value: ");
					sb.Append(InitialValue);
					sb.Append("</color>");
				}
				if (MinValue > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Min Value: ");
					sb.Append(MinValue);
					sb.Append("</color>");
				}
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