using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject database for item attribute templates, providing lookup and storage by name.
	/// Used to manage and retrieve item attribute templates for items in the game.
	/// </summary>
	[CreateAssetMenu(fileName = "New Item Attribute Database", menuName = "FishMMO/Item/Item Attribute/Database", order = 0)]
	public class ItemAttributeTemplateDatabase : ScriptableObject
	{
		/// <summary>
		/// Serializable dictionary mapping attribute names to their templates.
		/// </summary>
		[Serializable]
		public class ItemAttributeDictionary : SerializableDictionary<string, ItemAttributeTemplate> { }

		/// <summary>
		/// Internal dictionary of item attribute templates, serialized for inspector access.
		/// </summary>
		[SerializeField]
		private ItemAttributeDictionary attributes = new ItemAttributeDictionary();

		/// <summary>
		/// Gets the dictionary of item attribute templates.
		/// </summary>
		public ItemAttributeDictionary Attributes { get { return this.attributes; } }

		/// <summary>
		/// Retrieves an item attribute template by name.
		/// Returns null if not found.
		/// </summary>
		/// <param name="name">The name of the attribute template to retrieve.</param>
		/// <returns>The item attribute template, or null if not found.</returns>
		public ItemAttributeTemplate GetItemAttribute(string name)
		{
			this.attributes.TryGetValue(name, out ItemAttributeTemplate attribute);
			return attribute;
		}
	}
}