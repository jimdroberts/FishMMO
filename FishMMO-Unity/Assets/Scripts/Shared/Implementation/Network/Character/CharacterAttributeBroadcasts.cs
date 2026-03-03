using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for updating a single character attribute (e.g., strength, agility).
	/// Contains the template ID and the new value for the attribute.
	/// </summary>
	public struct CharacterAttributeUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the attribute to update.</summary>
		public int TemplateID;
		/// <summary>New value for the attribute.</summary>
		public int Value;
	}

	/// <summary>
	/// Broadcast for updating a character's resource attribute (e.g., health, mana).
	/// Contains the template ID, current value, and base attribute value.
	/// </summary>
	public struct CharacterResourceAttributeUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the resource attribute to update.</summary>
		public int TemplateID;
		/// <summary>Current value of the resource (e.g., current health).</summary>
		public float CurrentValue;
		/// <summary>Base value of the resource attribute used for local final-value recalculation.</summary>
		public int Value;
	}

	/// <summary>
	/// Broadcast for updating multiple character attributes at once.
	/// Used for bulk attribute updates or synchronization.
	/// </summary>
	public struct CharacterAttributeUpdateMultipleBroadcast : IBroadcast
	{
		/// <summary>List of attribute updates to apply.</summary>
		public List<CharacterAttributeUpdateBroadcast> Attributes;
	}

	/// <summary>
	/// Broadcast for updating multiple character resource attributes at once.
	/// Used for bulk resource attribute updates or synchronization.
	/// </summary>
	public struct CharacterResourceAttributeUpdateMultipleBroadcast : IBroadcast
	{
		/// <summary>List of resource attribute updates to apply.</summary>
		public List<CharacterResourceAttributeUpdateBroadcast> Attributes;
	}

	/// <summary>
	/// Observer-targeted broadcast for updating attributes of a specific character.
	/// Contains the target character ID and all attribute updates for that character.
	/// </summary>
	public struct CharacterObserverAttributeUpdateBroadcast : IBroadcast
	{
		/// <summary>Target character ID to apply updates to.</summary>
		public long CharacterID;
		/// <summary>List of attribute updates to apply to the target character.</summary>
		public List<CharacterAttributeUpdateBroadcast> Attributes;
	}

	/// <summary>
	/// Observer-targeted broadcast for updating resource attributes of a specific character.
	/// Contains the target character ID and all resource updates for that character.
	/// </summary>
	public struct CharacterObserverResourceAttributeUpdateBroadcast : IBroadcast
	{
		/// <summary>Target character ID to apply updates to.</summary>
		public long CharacterID;
		/// <summary>List of resource attribute updates to apply to the target character.</summary>
		public List<CharacterResourceAttributeUpdateBroadcast> Attributes;
	}
}