using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct HotkeyData
	{
		public byte Type;
		public int Slot;
		public long ReferenceID;
	}

	public struct HotkeySetBroadcast : IBroadcast
	{
		public HotkeyData HotkeyData;
	}

	public struct HotkeySetMultipleBroadcast : IBroadcast
	{
		public List<HotkeySetBroadcast> Hotkeys;
	}
}