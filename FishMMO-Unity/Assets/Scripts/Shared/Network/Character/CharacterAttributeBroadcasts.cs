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
	/// Contains the template ID, current value, and maximum value.
	/// </summary>
	public struct CharacterResourceAttributeUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the resource attribute to update.</summary>
		public int TemplateID;
		/// <summary>Current value of the resource (e.g., current health).</summary>
		public int CurrentValue;
		/// <summary>Maximum value of the resource (e.g., max health).</summary>
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
}