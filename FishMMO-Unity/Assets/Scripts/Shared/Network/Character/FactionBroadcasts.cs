using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct FactionUpdateBroadcast : IBroadcast
	{
		public int TemplateID;
		public int NewValue;
	}

	public struct FactionUpdateMultipleBroadcast : IBroadcast
	{
		public List<FactionUpdateBroadcast> Factions;
	}
}