using System;

[Serializable]
public class WorldServerDetails
{
	public string name;
	public DateTime lastPulse;
	public string address;
	public ushort port;
	public int characterCount;
	public bool locked;
}