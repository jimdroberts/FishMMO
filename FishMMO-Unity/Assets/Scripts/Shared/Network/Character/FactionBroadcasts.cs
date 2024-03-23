using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct FactionUpdateBroadcast : IBroadcast
	{
		public int templateID;
		public int newValue;
	}

	public struct FactionUpdateMultipleBroadcast : IBroadcast
	{
		public List<FactionUpdateBroadcast> factions;
	}
}