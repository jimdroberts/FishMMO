using UnityEngine;

[CreateAssetMenu(fileName = "New Character Attribute Database", menuName = "Character/Attribute/Database", order = 0)]
public class CharacterAttributeTemplateDatabase : ScriptableObject
{
	[SerializeField]
	private CharacterAttributeTemplateDictionary attributes = new CharacterAttributeTemplateDictionary();
	public CharacterAttributeTemplateDictionary Attributes { get { return attributes; } }

	public CharacterAttributeTemplate GetCharacterAttribute(string name)
	{
		CharacterAttributeTemplate attribute;
		attributes.TryGetValue(name, out attribute);
		return attribute;
	}
}