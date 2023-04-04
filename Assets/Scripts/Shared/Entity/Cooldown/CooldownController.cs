using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class CooldownController : NetworkBehaviour
{
	private Dictionary<string, CooldownInstance> cooldowns = new Dictionary<string, CooldownInstance>();

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (!base.IsOwner)
		{
			enabled = false;
			return;
		}
	}

	void Update()
	{
		foreach (KeyValuePair<string, CooldownInstance> pair in new Dictionary<string, CooldownInstance>(cooldowns))
		{
			pair.Value.SubtractTime(Time.deltaTime);
			if (!pair.Value.IsOnCooldown)
			{
				cooldowns.Remove(pair.Key);
			}
		}
	}

	public bool IsOnCooldown(string name)
	{
		return cooldowns.ContainsKey(name);
	}

	public void AddCooldown(string name, CooldownInstance cooldown)
	{
		if (!cooldowns.ContainsKey(name))
		{
			cooldowns.Add(name, cooldown);
		}
	}

	public void RemoveCooldown(string name)
	{
		cooldowns.Remove(name);
	}
}