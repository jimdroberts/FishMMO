using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for updating the owner's archetype.
	/// </summary>
	public struct ArchetypeUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the archetype to apply.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Observer-targeted broadcast for updating archetype of a specific character.
	/// </summary>
	public struct CharacterObserverArchetypeUpdateBroadcast : IBroadcast
	{
		/// <summary>Target character ID to apply updates to.</summary>
		public long CharacterID;
		/// <summary>Template ID of the archetype to apply.</summary>
		public int TemplateID;
	}
}