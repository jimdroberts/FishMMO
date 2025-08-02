using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls and manages ability cooldowns for a character.
	/// </summary>
	public class CooldownController : CharacterBehaviour, ICooldownController
	{
		/// <summary>
		/// Dictionary of active cooldowns, keyed by ability ID.
		/// </summary>
		private Dictionary<long, CooldownInstance> cooldowns = new Dictionary<long, CooldownInstance>();

		/// <summary>
		/// List of keys to remove after cooldowns expire.
		/// </summary>
		private List<long> keysToRemove = new List<long>();

		/// <inheritdoc/>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			cooldowns.Clear();
		}

		/// <summary>
		/// Reads cooldown data from a network reader.
		/// </summary>
		/// <param name="reader">The network reader.</param>
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

		/// <summary>
		/// Writes cooldown data to a network writer.
		/// </summary>
		/// <param name="writer">The network writer.</param>
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

		/// <summary>
		/// Updates all cooldowns by subtracting deltaTime and removes expired cooldowns.
		/// </summary>
		/// <param name="deltaTime">Time to subtract from each cooldown.</param>
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

		/// <summary>
		/// Checks if an ability is currently on cooldown.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <returns>True if on cooldown, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOnCooldown(long id)
		{
			return cooldowns.ContainsKey(id);
		}

		/// <summary>
		/// Tries to get the remaining cooldown time for an ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="cooldown">Remaining cooldown time.</param>
		/// <returns>True if found, otherwise false.</returns>
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

		/// <summary>
		/// Adds a cooldown for the specified ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="cooldown">Cooldown instance.</param>
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

		/// <summary>
		/// Removes the cooldown for the specified ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveCooldown(long id)
		{
			cooldowns.Remove(id);

			if (base.IsOwner)
			{
				ICooldownController.OnRemoveCooldown?.Invoke(id);
			}
		}

		/// <summary>
		/// Clears all cooldowns.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Clear()
		{
			cooldowns.Clear();
		}
	}
}