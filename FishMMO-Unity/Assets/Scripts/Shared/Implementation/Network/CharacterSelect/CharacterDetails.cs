using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing details about a character for selection and display.
	/// </summary>
	[Serializable]
	public class CharacterDetails
	{
		/// <summary>Name of the character.</summary>
		public string CharacterName;
		/// <summary>Name of the scene where the character is currently located.</summary>
		public string SceneName;
		/// <summary>Template ID representing the character's race.</summary>
		public int RaceTemplateID;
		/// <summary>Template IDs of equipped items indexed by slot (see <see cref="ItemSlot"/>). Null when equipment data is unavailable.</summary>
		/// <remarks>FishNet requires a custom serializer for Dictionary{int,int}. If this type is sent over
		/// the network, register a custom serializer via <c>NetworkManager.Serializer.RegisterSerializerType</c>
		/// or change this field to an array of key-value pair structs for native FishNet serialization.</remarks>
		public Dictionary<int, int> EquippedItems;
	}
}