using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class CooldownController : CharacterBehaviour, ICooldownController
	{
		private Dictionary<string, CooldownInstance> cooldowns = new Dictionary<string, CooldownInstance>();

		private List<string> keysToRemove = new List<string>();

		public void OnTick(float deltaTime)
		{
			foreach (var pair in cooldowns)
			{
				pair.Value.SubtractTime(deltaTime);

				if (!pair.Value.IsOnCooldown)
				{
					keysToRemove.Add(pair.Key);
				}
			}

			foreach (var key in keysToRemove)
			{
				Debug.Log($"{key} is off cooldown.");
				cooldowns.Remove(key);
			}
			keysToRemove.Clear();
		}

		public bool IsOnCooldown(string name)
		{
			return cooldowns.ContainsKey(name);
		}

		public void AddCooldown(string name, CooldownInstance cooldown)
		{
			if (!cooldowns.ContainsKey(name))
			{
				Debug.Log($"{name} is on cooldown.");
				cooldowns.Add(name, cooldown);
			}
		}

		public void RemoveCooldown(string name)
		{
			cooldowns.Remove(name);
		}
	}
}