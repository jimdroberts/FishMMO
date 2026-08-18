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
		/// <summary>
		/// True when this character's body is still in the world because its owner disconnected
		/// during combat. Selecting it resumes that body rather than starting a fresh session.
		/// </summary>
		/// <remarks>
		/// Surfaced so the player is told what happened instead of silently being dropped back
		/// into a character that may have taken damage — or died — while they were gone.
		/// </remarks>
		public bool IsCombatLogged;
		/// <summary>Template IDs of equipped items indexed by slot (see <see cref="ItemSlot"/>). Null when equipment data is unavailable.</summary>
		/// <remarks>FishNet requires a custom serializer for Dictionary{int,int}. If this type is sent over
		/// the network, register a custom serializer via <c>NetworkManager.Serializer.RegisterSerializerType</c>
		/// or change this field to an array of key-value pair structs for native FishNet serialization.</remarks>
		public Dictionary<int, int> EquippedItems;

	/// <summary>
	/// Serializable key-value pair for equipped items. Register as a FishNet custom
	/// serializer and migrate EquippedItems to EquippedItemEntry[] when ready.
	/// See FishNet.Serialize.DictionarySerializer for the registration pattern.
	/// </summary>
	[System.Serializable]
	public struct EquippedItemEntry { public int Key; public int Value; }
	}
}