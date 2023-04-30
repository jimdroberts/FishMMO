using System.Collections.Generic;
using FishNet.Broadcast;

public struct CharacterAttributeUpdateBroadcast : IBroadcast
{
	public int templateID;
	public int baseValue;
	public int modifier;
}

public struct CharacterAttributeUpdateMultipleBroadcast : IBroadcast
{
	public List<CharacterAttributeUpdateBroadcast> attributes;
}