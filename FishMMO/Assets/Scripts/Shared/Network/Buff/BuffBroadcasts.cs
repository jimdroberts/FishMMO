using System.Collections.Generic;
using FishNet.Broadcast;

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