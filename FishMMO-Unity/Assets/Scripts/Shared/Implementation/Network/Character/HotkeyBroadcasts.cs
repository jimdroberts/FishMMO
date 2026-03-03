using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Data structure representing a hotkey assignment for a character.
	/// </summary>
	public class HotkeyData
	{
		/// <summary>Type of the hotkey (e.g., ability, item).</summary>
		public byte Type;
		/// <summary>Slot index where the hotkey is assigned.</summary>
		public int Slot;
		/// <summary>Reference ID for the hotkey target (e.g., ability or item ID). Defaults to -1 if unset.</summary>
		public long ReferenceID = -1;
	}

	/// <summary>
	/// Broadcast for setting a single hotkey assignment for a character.
	/// Contains the hotkey data to be set.
	/// </summary>
	public struct HotkeySetBroadcast : IBroadcast
	{
		/// <summary>Hotkey data to assign.</summary>
		public HotkeyData HotkeyData;
	}

	/// <summary>
	/// Broadcast for setting multiple hotkey assignments at once.
	/// Used for bulk hotkey updates or synchronization.
	/// </summary>
	public struct HotkeySetMultipleBroadcast : IBroadcast
	{
		/// <summary>List of hotkey assignments to set.</summary>
		public List<HotkeySetBroadcast> Hotkeys;
	}
}