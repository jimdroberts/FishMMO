using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for adding a single buff to a character.
	/// Contains the template ID of the buff to be added.
	/// </summary>
	public struct BuffAddBroadcast : IBroadcast
	{
		/// <summary>Template ID of the buff to add.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Broadcast for adding multiple buffs to a character at once.
	/// Used for bulk buff addition or synchronization.
	/// </summary>
	public struct BuffAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of buffs to add.</summary>
		public List<BuffAddBroadcast> Buffs;
	}

	/// <summary>
	/// Broadcast for removing a single buff from a character.
	/// Contains the template ID of the buff to be removed.
	/// </summary>
	public struct BuffRemoveBroadcast : IBroadcast
	{
		/// <summary>Template ID of the buff to remove.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Broadcast for removing multiple buffs from a character at once.
	/// Used for bulk buff removal or synchronization.
	/// </summary>
	public struct BuffRemoveMultipleBroadcast : IBroadcast
	{
		/// <summary>List of buffs to remove.</summary>
		public List<BuffRemoveBroadcast> Buffs;
	}
}