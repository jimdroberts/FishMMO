using FishNet.Connection;
using FishMMO.Server;
using System;
using System.Collections.Generic;

public class SceneServerDetails
{
	public NetworkConnection connection;
	public DateTime lastPulse;
	public string address;
	public ushort port;
	public Dictionary<int, SceneInstanceDetails> scenes;
	public bool locked;

	public int TotalClientCount
	{
		get
		{
			int count = 0;
			if (scenes != null)
			{
				foreach (SceneInstanceDetails details in scenes.Values)
				{
					count += details.clientCount;
				}
			}
			return count;
		}
	}
}