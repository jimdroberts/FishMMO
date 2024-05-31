using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class CooldownController : CharacterBehaviour, ICooldownController
	{
		private Dictionary<int, CooldownInstance> cooldowns = new Dictionary<int, CooldownInstance>();

		private List<int> keysToRemove = new List<int>();

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
				//Debug.Log($"{key} is off cooldown.");
				cooldowns.Remove(key);
			}
			keysToRemove.Clear();
		}

		public bool IsOnCooldown(int id)
		{
			return cooldowns.ContainsKey(id);
		}

		public void AddCooldown(int id, CooldownInstance cooldown)
		{
			if (!cooldowns.ContainsKey(id))
			{
				//Debug.Log($"{id} is on cooldown.");
				cooldowns.Add(id, cooldown);
			}
		}

		public void RemoveCooldown(int id)
		{
			cooldowns.Remove(id);
		}
	}
}