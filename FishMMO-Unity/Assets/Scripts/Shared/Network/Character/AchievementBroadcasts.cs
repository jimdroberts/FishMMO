using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for updating a single achievement for a character.
	/// Contains the achievement template ID, value, and tier.
	/// </summary>
	public struct AchievementUpdateBroadcast : IBroadcast
	{
		/// <summary>Template ID of the achievement to update.</summary>
		public int TemplateID;
		/// <summary>Current value or progress for the achievement.</summary>
		public uint Value;
		/// <summary>Current tier or level of the achievement.</summary>
		public byte Tier;
	}

	/// <summary>
	/// Broadcast for updating multiple achievements for a character at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct AchievementUpdateMultipleBroadcast : IBroadcast
	{
		/// <summary>List of achievements to update.</summary>
		public List<AchievementUpdateBroadcast> Achievements;
	}
}