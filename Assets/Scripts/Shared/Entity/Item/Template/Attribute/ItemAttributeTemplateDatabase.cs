using System;
using UnityEngine;

[CreateAssetMenu(fileName = "New Item Attribute Database", menuName = "Item/Item Attribute/Database", order = 0)]
public class ItemAttributeTemplateDatabase : ScriptableObject
{
	[Serializable]
	public class ItemAttributeDictionary : SerializableDictionary<string, ItemAttributeTemplate> { }

	[SerializeField]
	private ItemAttributeDictionary attributes = new ItemAttributeDictionary();
	public ItemAttributeDictionary Attributes { get { return this.attributes; } }

	public ItemAttributeTemplate GetItemAttribute(string name)
	{
		ItemAttributeTemplate attribute;
		this.attributes.TryGetValue(name, out attribute);
		return attribute;
	}
}