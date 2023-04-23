using FishNet.Broadcast;

public struct CooldownAddBroadcast : IBroadcast
{
	public string name;
	public float value;
}

public struct CooldownRemoveBroadcast : IBroadcast
{
	public string name;
}
