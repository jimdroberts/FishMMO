using System;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "New Item Database", menuName = "Item/Database", order = 0)]
public class ItemTemplateDatabase : ScriptableObject
{
	[Serializable]
	public class ItemDictionary : SerializableDictionary<string, BaseItemTemplate> { }

	[SerializeField]
	private ItemDictionary items = new ItemDictionary();
	public ItemDictionary Items { get { return items; } }

	public BaseItemTemplate GetItem(string name)
	{
		BaseItemTemplate item;
		items.TryGetValue(name, out item);
		return item;
	}
}