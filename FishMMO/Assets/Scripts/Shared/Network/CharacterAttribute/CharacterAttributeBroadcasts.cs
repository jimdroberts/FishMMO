using System.Collections.Generic;
using FishNet.Broadcast;

public struct CharacterAttributeUpdateBroadcast : IBroadcast
{
	public int templateID;
	public int value;
}

public struct CharacterResourceAttributeUpdateBroadcast : IBroadcast
{
	public int templateID;
	public int value;
	public int max;
}

public struct CharacterAttributeUpdateMultipleBroadcast : IBroadcast
{
	public List<CharacterAttributeUpdateBroadcast> attributes;
}

public struct CharacterResourceAttributeUpdateMultipleBroadcast : IBroadcast
{
	public List<CharacterResourceAttributeUpdateBroadcast> attributes;
}