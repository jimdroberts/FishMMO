using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject database for storing and retrieving NPC attributes by name.
	/// </summary>
	[CreateAssetMenu(fileName = "New NPC Attribute Database", menuName = "FishMMO/Character/NPC/Attribute/Database", order = 0)]
	public class NPCAttributeDatabase : ScriptableObject
	{
		/// <summary>
		/// List of all NPC attributes in the database.
		/// </summary>
		public List<NPCAttribute> Attributes = new List<NPCAttribute>();

		/// <summary>
		/// Attempts to find an NPC attribute by its template name.
		/// </summary>
		/// <param name="name">The name of the attribute template to search for.</param>
		/// <param name="npcAttribute">The found NPCAttribute, or null if not found.</param>
		/// <returns>True if the attribute is found, false otherwise.</returns>
		public bool TryGetNPCAttribute(string name, out NPCAttribute npcAttribute)
		{
			foreach (NPCAttribute attribute in Attributes)
			{
				// Skip null attributes or attributes with null templates.
				if (attribute == null)
				{
					continue;
				}
				if (attribute.Template == null)
				{
					continue;
				}
				// Compare template name to requested name.
				if (attribute.Template.Name.Equals(name))
				{
					npcAttribute = attribute;
					return true;
				}
			}
			npcAttribute = null;
			return false;
		}
	}
}