using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CharacterAttributeUpdateBroadcast : IBroadcast
	{
		public int TemplateID;
		public int Value;
	}

	public struct CharacterResourceAttributeUpdateBroadcast : IBroadcast
	{
		public int TemplateID;
		public int CurrentValue;
		public int Value;
	}

	public struct CharacterAttributeUpdateMultipleBroadcast : IBroadcast
	{
		public List<CharacterAttributeUpdateBroadcast> Attributes;
	}

	public struct CharacterResourceAttributeUpdateMultipleBroadcast : IBroadcast
	{
		public List<CharacterResourceAttributeUpdateBroadcast> Attributes;
	}
}