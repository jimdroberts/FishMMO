using System.Collections.Generic;
using UnityEngine;

public class CooldownController : MonoBehaviour
{
	private Dictionary<string, CooldownInstance> cooldowns = new Dictionary<string, CooldownInstance>();

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