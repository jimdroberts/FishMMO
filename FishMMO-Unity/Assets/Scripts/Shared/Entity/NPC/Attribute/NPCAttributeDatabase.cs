using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New NPC Attribute Database", menuName = "FishMMO/Character/NPC/Attribute/Database", order = 0)]
	public class NPCAttributeDatabase : ScriptableObject
	{
		public List<NPCAttribute> Attributes = new List<NPCAttribute>();

		public bool TryGetNPCAttribute(string name, out NPCAttribute npcAttribute)
		{
			foreach (NPCAttribute attribute in Attributes)
			{
				if (attribute == null)
				{
					continue;
				}
				if (attribute.Template == null)
				{
					continue;
				}
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