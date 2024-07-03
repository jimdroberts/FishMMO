using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class CooldownController : CharacterBehaviour, ICooldownController
	{
		private Dictionary<long, CooldownInstance> cooldowns = new Dictionary<long, CooldownInstance>();

		private List<long> keysToRemove = new List<long>();

		public void OnTick(float deltaTime)
		{
			foreach (var pair in cooldowns)
			{
				pair.Value.SubtractTime(deltaTime);

				if (base.IsOwner)
				{
					ICooldownController.OnUpdateCooldown?.Invoke(pair.Key, pair.Value);
				}

				if (!pair.Value.IsOnCooldown)
				{
					keysToRemove.Add(pair.Key);
				}
			}

			foreach (var key in keysToRemove)
			{
				//Debug.Log($"{key} is off cooldown.");
				RemoveCooldown(key);
			}
			keysToRemove.Clear();
		}

		public bool IsOnCooldown(long id)
		{
			return cooldowns.ContainsKey(id);
		}

		public void AddCooldown(long id, CooldownInstance cooldown)
		{
			if (!cooldowns.ContainsKey(id))
			{
				//Debug.Log($"{id} is on cooldown.");
				cooldowns.Add(id, cooldown);

				if (base.IsOwner)
				{
					ICooldownController.OnAddCooldown?.Invoke(id, cooldown);
				}
			}
		}

		public void RemoveCooldown(long id)
		{
			cooldowns.Remove(id);

			if (base.IsOwner)
			{
				ICooldownController.OnRemoveCooldown?.Invoke(id);
			}
		}
	}
}