using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct KnownAbilityAddBroadcast : IBroadcast
	{
		public int templateID;
	}

	public struct KnownAbilityAddMultipleBroadcast : IBroadcast
	{
		public List<KnownAbilityAddBroadcast> abilities;
	}

	public struct AbilityAddBroadcast : IBroadcast
	{
		public long id;
		public int templateID;
		public List<int> events;
	}

	public struct AbilityAddMultipleBroadcast : IBroadcast
	{
		public List<AbilityAddBroadcast> abilities;
	}
}