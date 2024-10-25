using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct KnownAbilityAddBroadcast : IBroadcast
	{
		public int TemplateID;
	}

	public struct KnownAbilityAddMultipleBroadcast : IBroadcast
	{
		public List<KnownAbilityAddBroadcast> Abilities;
	}

	public struct AbilityAddBroadcast : IBroadcast
	{
		public long ID;
		public int TemplateID;
		public List<int> Events;
	}

	public struct AbilityAddMultipleBroadcast : IBroadcast
	{
		public List<AbilityAddBroadcast> Abilities;
	}
}