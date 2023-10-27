using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct BuffAddBroadcast : IBroadcast
	{
		public int templateID;
	}

	public struct BuffAddMultipleBroadcast : IBroadcast
	{
		public List<BuffAddBroadcast> buffs;
	}

	public struct BuffRemoveBroadcast : IBroadcast
	{
		public int templateID;
	}

	public struct BuffRemoveMultipleBroadcast : IBroadcast
	{
		public List<BuffRemoveBroadcast> buffs;
	}
}