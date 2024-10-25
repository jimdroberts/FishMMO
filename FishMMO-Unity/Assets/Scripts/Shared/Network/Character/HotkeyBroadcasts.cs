using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public class HotkeyData
	{
		public byte Type;
		public int Slot;
		public long ReferenceID = -1;
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