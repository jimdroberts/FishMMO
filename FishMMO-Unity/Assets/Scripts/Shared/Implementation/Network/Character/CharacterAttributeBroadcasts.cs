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
	/// Broadcast for updating multiple character attributes at once.
	/// Used for bulk attribute updates or synchronization.
	/// </summary>
	public struct CharacterAttributeUpdateMultipleBroadcast : IBroadcast
	{
		/// <summary>List of attribute updates to apply.</summary>
		public List<CharacterAttributeUpdateBroadcast> Attributes;
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

	// Resource attribute (HP/MP/Stamina) broadcasts were removed: their values are
	// conveyed each prediction tick via CharacterReconcileData.ResourceState and reach
	// all observers automatically through FishNet Prediction V2 state forwarding.
}