using System;

[Serializable]
public class WorldServerDetails
{
	public string Name;
	public DateTime LastPulse;
	public string Address;
	public ushort Port;
	public int CharacterCount;
	public bool Locked;
}