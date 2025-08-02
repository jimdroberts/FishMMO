using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject database for item templates, providing lookup and storage by name.
	/// Used to manage and retrieve item templates for items in the game.
	/// </summary>
	[CreateAssetMenu(fileName = "New Item Database", menuName = "FishMMO/Item/Database", order = 0)]
	public class ItemTemplateDatabase : ScriptableObject
	{
		/// <summary>
		/// Serializable dictionary mapping item names to their templates.
		/// </summary>
		[Serializable]
		public class ItemDictionary : SerializableDictionary<string, BaseItemTemplate> { }

		/// <summary>
		/// Internal dictionary of item templates, serialized for inspector access.
		/// </summary>
		[SerializeField]
		private ItemDictionary items = new ItemDictionary();

		/// <summary>
		/// Gets the dictionary of item templates.
		/// </summary>
		public ItemDictionary Items { get { return items; } }

		/// <summary>
		/// Retrieves an item template by name.
		/// Returns null if not found.
		/// </summary>
		/// <param name="name">The name of the item template to retrieve.</param>
		/// <returns>The item template, or null if not found.</returns>
		public BaseItemTemplate GetItem(string name)
		{
			items.TryGetValue(name, out BaseItemTemplate item);
			return item;
		}
	}
}