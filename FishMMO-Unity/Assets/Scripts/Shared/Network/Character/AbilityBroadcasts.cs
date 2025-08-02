using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for adding a known ability to a character.
	/// Contains the template ID of the ability to add.
	/// </summary>
	public struct KnownAbilityAddBroadcast : IBroadcast
	{
		/// <summary>Template ID of the ability to add.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Broadcast for adding multiple known abilities to a character at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct KnownAbilityAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of known abilities to add.</summary>
		public List<KnownAbilityAddBroadcast> Abilities;
	}

	/// <summary>
	/// Broadcast for adding a known ability event to a character.
	/// Contains the template ID of the ability event to add.
	/// </summary>
	public struct KnownAbilityEventAddBroadcast : IBroadcast
	{
		/// <summary>Template ID of the ability event to add.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Broadcast for adding multiple known ability events to a character at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct KnownAbilityEventAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of known ability events to add.</summary>
		public List<KnownAbilityEventAddBroadcast> AbilityEvents;
	}

	/// <summary>
	/// Broadcast for adding an ability to a character, including its events.
	/// Contains the ability's instance ID, template ID, and associated event IDs.
	/// </summary>
	public struct AbilityAddBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID of the ability.</summary>
		public long ID;
		/// <summary>Template ID of the ability.</summary>
		public int TemplateID;
		/// <summary>List of event IDs associated with the ability.</summary>
		public List<int> Events;
	}

	/// <summary>
	/// Broadcast for adding multiple abilities to a character at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct AbilityAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of abilities to add.</summary>
		public List<AbilityAddBroadcast> Abilities;
	}
}