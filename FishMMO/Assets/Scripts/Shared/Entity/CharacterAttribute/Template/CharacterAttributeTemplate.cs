using System;
using UnityEngine;

[CreateAssetMenu(fileName = "New Character Attribute", menuName = "Character/Attribute/Character Attribute", order = 1)]
public class CharacterAttributeTemplate : CachedScriptableObject<CharacterAttributeTemplate>
{
	[Serializable]
	public class CharacterAttributeFormulaDictionary : SerializableDictionary<CharacterAttributeTemplate, CharacterAttributeFormulaTemplate> { }

	[Serializable]
	public class CharacterAttributeSet : SerializableHashSet<CharacterAttributeTemplate> { }

	public string Description;
	public int InitialValue;
	public int MinValue;
	public int MaxValue;
	public bool IsResourceAttribute;
	public bool ClampFinalValue;
	public CharacterAttributeSet ParentTypes = new CharacterAttributeSet();
	public CharacterAttributeSet ChildTypes = new CharacterAttributeSet();
	public CharacterAttributeSet DependantTypes = new CharacterAttributeSet();
	public CharacterAttributeFormulaDictionary Formulas = new CharacterAttributeFormulaDictionary();

	public string Name { get { return this.name; } }
}