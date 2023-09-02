using FishNet.Connection;
using FishMMO.Server;
using System;
using System.Collections.Generic;

public class SceneServerDetails
{
	public NetworkConnection Connection;
	public DateTime LastPulse;
	public string Address;
	public ushort Port;
	public Dictionary<int, SceneInstanceDetails> Scenes;
	public bool Locked;

	public int TotalClientCount
	{
		get
		{
			int count = 0;
			if (Scenes != null)
			{
				foreach (SceneInstanceDetails details in Scenes.Values)
				{
					count += details.ClientCount;
				}
			}
			return count;
		}
	}
}