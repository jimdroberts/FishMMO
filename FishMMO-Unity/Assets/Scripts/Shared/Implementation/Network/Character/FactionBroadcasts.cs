using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for updating a single faction value for a character.
	/// Contains the faction template ID and the new value.
	/// </summary>
	public struct FactionUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the faction to update.</summary>
		public int TemplateID;
		/// <summary>New value for the faction (e.g., reputation or standing).</summary>
		public int NewValue;
	}

	/// <summary>
	/// Broadcast for updating multiple faction values for a character at once.
	/// Used for bulk faction updates or synchronization.
	/// </summary>
	public struct FactionUpdateMultipleBroadcast : IBroadcast
	{
		/// <summary>List of faction updates to apply.</summary>
		public List<FactionUpdateBroadcast> Factions;
	}

	/// <summary>
	/// Observer-targeted broadcast for updating faction values of a specific character.
	/// </summary>
	public struct CharacterObserverFactionUpdateBroadcast : IBroadcast
	{
		/// <summary>Target character ID to apply updates to.</summary>
		public long CharacterID;
		/// <summary>List of faction updates to apply to the target character.</summary>
		public List<FactionUpdateBroadcast> Factions;
	}
}