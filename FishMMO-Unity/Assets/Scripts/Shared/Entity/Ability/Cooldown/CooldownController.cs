using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	public class CooldownController : CharacterBehaviour, ICooldownController
	{
		private Dictionary<long, CooldownInstance> cooldowns = new Dictionary<long, CooldownInstance>();

		private List<long> keysToRemove = new List<long>();

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			cooldowns.Clear();
		}

		public void Read(Reader reader)
		{
			int cooldownCount = reader.ReadInt32();
			for (int i = 0; i < cooldownCount; ++i)
			{
				long abilityID = reader.ReadInt64();
				CooldownInstance cooldown = new CooldownInstance(reader.ReadSingle(), reader.ReadSingle());

				AddCooldown(abilityID, cooldown);
			}
		}

		public void Write(Writer writer)
		{
			writer.WriteInt32(cooldowns.Count);
			foreach (KeyValuePair<long, CooldownInstance> cooldown in cooldowns)
			{
				writer.WriteInt64(cooldown.Key);
				writer.WriteSingle(cooldown.Value.TotalTime);
				writer.WriteSingle(cooldown.Value.RemainingTime);
			}
		}

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
				//Log.Debug($"{key} is off cooldown.");
				RemoveCooldown(key);
			}
			keysToRemove.Clear();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOnCooldown(long id)
		{
			return cooldowns.ContainsKey(id);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetCooldown(long id, out float cooldown)
		{
			if (cooldowns.TryGetValue(id, out CooldownInstance cooldownInstance))
			{
				cooldown = cooldownInstance.RemainingTime;
				return true;
			}
			cooldown = 0.0f;
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddCooldown(long id, CooldownInstance cooldown)
		{
			if (!cooldowns.ContainsKey(id))
			{
				//Log.Debug($"{id} is on cooldown.");
				cooldowns.Add(id, cooldown);

				if (base.IsOwner)
				{
					ICooldownController.OnAddCooldown?.Invoke(id, cooldown);
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveCooldown(long id)
		{
			cooldowns.Remove(id);

			if (base.IsOwner)
			{
				ICooldownController.OnRemoveCooldown?.Invoke(id);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Clear()
		{
			cooldowns.Clear();
		}
	}
}