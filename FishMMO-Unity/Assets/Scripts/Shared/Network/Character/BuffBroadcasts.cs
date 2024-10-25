using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct BuffAddBroadcast : IBroadcast
	{
		public int TemplateID;
	}

	public struct BuffAddMultipleBroadcast : IBroadcast
	{
		public List<BuffAddBroadcast> Buffs;
	}

	public struct BuffRemoveBroadcast : IBroadcast
	{
		public int TemplateID;
	}

	public struct BuffRemoveMultipleBroadcast : IBroadcast
	{
		public List<BuffRemoveBroadcast> Buffs;
	}
}