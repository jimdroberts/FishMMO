using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls and manages ability cooldowns using immutable <see cref="CooldownInstance"/>
	/// based on StartTick + DurationTicks. No per-tick mutation — cooldowns expire
	/// automatically via integer comparison: <c>(currentTick - StartTick) &gt;= DurationTicks</c>.
	/// </summary>
	public class CooldownController : CharacterBehaviour, ICooldownController, IPredictableController
	{
		/// <summary>
		/// Execution order in the unified prediction pipeline.
		/// Runs before <see cref="AbilityController"/> so newly elapsed cooldowns
		/// are removed before ability start checks in the same tick.
		/// </summary>
		public int Order => 90;

		/// <summary>
		/// Dictionary of active cooldowns, keyed by ability ID.
		/// Entries are immutable <see cref="CooldownInstance"/> values — they are never
		/// mutated after insertion, only removed when expired.
		/// </summary>
		private SortedDictionary<long, CooldownInstance> cooldowns = new SortedDictionary<long, CooldownInstance>();

		/// <summary>
		/// Reusable buffer for collecting expired cooldown keys during <see cref="ExpireElapsed"/>.
		/// </summary>
		private List<long> keysToRemove = new List<long>();

		/// <summary>
		/// Cached reconcile snapshot, reused across ticks when cooldowns haven't changed.
		/// Invalidated by <see cref="AddCooldown"/>, <see cref="RemoveCooldown"/>,
		/// <see cref="Clear"/>, and <see cref="RestoreFromReconcile"/>.
		/// </summary>
		private CooldownReconcileEntry[] cachedSnapshot;

		/// <summary>
		/// When true, <see cref="cachedSnapshot"/> is stale and must be rebuilt.
		/// </summary>
		private bool snapshotDirty = true;

		/// <summary>
		/// Cached tick delta for converting between seconds and ticks.
		/// Lazily initialized from <see cref="TimeManager.TickDelta"/>.
		/// </summary>
		private float cachedTickDelta;

		/// <summary>
		/// Returns the fixed time step per tick. Caches on first access.
		/// Falls back to <see cref="Time.fixedDeltaTime"/> if TimeManager is unavailable.
		/// </summary>
		internal float TickDelta
		{
			get
			{
				if (cachedTickDelta <= 0f && base.TimeManager != null)
				{
					cachedTickDelta = (float)base.TimeManager.TickDelta;
				}
				return cachedTickDelta > 0f ? cachedTickDelta : Time.fixedDeltaTime;
			}
		}

		/// <summary>
		/// Returns the internal cooldown dictionary. Used by reconcile serialization.
		/// </summary>
		internal SortedDictionary<long, CooldownInstance> Cooldowns => cooldowns;

		/// <summary>
		/// Cooldowns do not contribute owner input into <see cref="CharacterReplicateData"/>.
		/// </summary>
		/// <param name="input">Unified replicate data for this tick.</param>
		public void PopulateInput(ref CharacterReplicateData input)
		{
		}

		/// <summary>
		/// Expires elapsed cooldowns deterministically for the current prediction tick.
		/// </summary>
		/// <param name="input">Unified replicate input containing the network tick.</param>
		/// <param name="state">Current replicate execution state.</param>
		/// <param name="channel">Transport channel.</param>
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			ExpireElapsed(input.GetTick());
		}

		/// <summary>
		/// Writes cooldown reconcile state for this tick.
		/// </summary>
		/// <param name="reconcileData">Mutable unified reconcile payload.</param>
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			reconcileData.Cooldowns = CreateReconcileSnapshot();
		}

		/// <summary>
		/// Restores cooldowns from authoritative reconcile state.
		/// </summary>
		/// <param name="rd">Unified reconcile payload.</param>
		/// <param name="channel">Transport channel.</param>
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			RestoreFromReconcile(rd.Cooldowns);
		}

		/// <inheritdoc/>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			cooldowns.Clear();
			keysToRemove.Clear();
		}

		/// <summary>
		/// Reads cooldown data from a network reader, discarding any entries that have
		/// already expired relative to <paramref name="currentTick"/>.
		/// Wire format: int count, then per cooldown: long abilityID, uint startTick, uint durationTicks.
		/// </summary>
		/// <param name="reader">The network reader.</param>
		/// <param name="currentTick">Current network tick used to discard expired entries.</param>
		public void Read(Reader reader, uint currentTick)
		{
			const int maxPayloadCooldowns = 4096;

			cooldowns.Clear();
			keysToRemove.Clear();

			int cooldownCount = reader.ReadInt32();
			if (cooldownCount < 0 || cooldownCount > maxPayloadCooldowns)
			{
				return;
			}

			float td = TickDelta;
			for (int i = 0; i < cooldownCount; ++i)
			{
				long abilityID = reader.ReadInt64();
				uint startTick = reader.ReadUInt32();
				uint durationTicks = reader.ReadUInt32();

				// Skip cooldowns that have already expired by the time we receive them.
				if (currentTick - startTick >= durationTicks)
				{
					continue;
				}

				CooldownInstance cooldown = new CooldownInstance(startTick, durationTicks, td);

				if (i < maxPayloadCooldowns)
				{
					cooldowns[abilityID] = cooldown;
				}
			}
		}

		/// <summary>
		/// Writes cooldown data to a network writer.
		/// Wire format: int count, then per cooldown: long abilityID, uint startTick, uint durationTicks.
		/// </summary>
		/// <param name="writer">The network writer.</param>
		public void Write(Writer writer)
		{
			writer.WriteInt32(cooldowns.Count);
			foreach (KeyValuePair<long, CooldownInstance> cooldown in cooldowns)
			{
				writer.WriteInt64(cooldown.Key);
				writer.WriteUInt32(cooldown.Value.StartTick);
				writer.WriteUInt32(cooldown.Value.DurationTicks);
			}
		}

		/// <summary>
		/// Removes all cooldowns that have expired as of <paramref name="currentTick"/>.
		/// Because <see cref="CooldownInstance"/> is immutable, this only removes entries —
		/// it never mutates them. Safe to call during replay since the same tick always
		/// produces the same expiration result.
		/// <para>
		/// Performance note: SortedDictionary iteration is O(n log n) per full traversal
		/// (each MoveNext is O(log n)) and provides no early-exit benefit since it is
		/// ordered by ability ID, not expiry tick. Acceptable because typical cooldown
		/// counts per character are small (< 20). If profiling shows this as a hotspot,
		/// consider a secondary structure keyed by expiry tick.
		/// </para>
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		public void ExpireElapsed(uint currentTick)
		{
			if (cooldowns.Count == 0)
			{
				return;
			}

			keysToRemove.Clear();

			foreach (KeyValuePair<long, CooldownInstance> pair in cooldowns)
			{
				if (!pair.Value.IsOnCooldown(currentTick))
				{
					keysToRemove.Add(pair.Key);
				}
			}

			for (int i = 0; i < keysToRemove.Count; i++)
			{
				RemoveCooldown(keysToRemove[i]);
			}
		}

		/// <summary>
		/// Checks if an ability is on cooldown at the given tick.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="currentTick">The current network tick.</param>
		/// <returns>True if on cooldown, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOnCooldown(long id, uint currentTick)
		{
			return cooldowns.TryGetValue(id, out CooldownInstance cd) && cd.IsOnCooldown(currentTick);
		}

		/// <summary>
		/// Tries to get the remaining cooldown time in seconds for an ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="currentTick">The current network tick.</param>
		/// <param name="cooldown">Remaining cooldown time in seconds.</param>
		/// <returns>True if found and still on cooldown, otherwise false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetCooldown(long id, uint currentTick, out float cooldown)
		{
			if (cooldowns.TryGetValue(id, out CooldownInstance cooldownInstance) &&
				cooldownInstance.IsOnCooldown(currentTick))
			{
				cooldown = cooldownInstance.RemainingTime(currentTick);
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
			bool existed = cooldowns.ContainsKey(id);
			cooldowns[id] = cooldown;
			snapshotDirty = true;

			if (!base.IsOwner)
			{
				return;
			}

			if (existed)
			{
				ICooldownController.OnUpdateCooldown?.Invoke(id, cooldown);
			}
			else
			{
				ICooldownController.OnAddCooldown?.Invoke(id, cooldown);
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
			snapshotDirty = true;

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
			snapshotDirty = true;
		}

		/// <summary>
		/// Creates a reconcile snapshot of all active cooldowns.
		/// Returns the cached array when cooldowns haven't changed since the last call.
		/// Returns null when no cooldowns are active.
		/// </summary>
		public CooldownReconcileEntry[] CreateReconcileSnapshot()
		{
			if (cooldowns.Count == 0)
			{
				cachedSnapshot = null;
				snapshotDirty = false;
				return null;
			}

			if (!snapshotDirty && cachedSnapshot != null)
			{
				return cachedSnapshot;
			}

			if (cachedSnapshot == null || cachedSnapshot.Length != cooldowns.Count)
			{
				cachedSnapshot = new CooldownReconcileEntry[cooldowns.Count];
			}

			int i = 0;
			foreach (KeyValuePair<long, CooldownInstance> kvp in cooldowns)
			{
				cachedSnapshot[i++] = new CooldownReconcileEntry
				{
					AbilityID = kvp.Key,
					StartTick = kvp.Value.StartTick,
					DurationTicks = kvp.Value.DurationTicks,
				};
			}
			snapshotDirty = false;
			return cachedSnapshot;
		}

		/// <summary>
		/// Restores cooldown state from a reconcile snapshot, replacing all current entries.
		/// </summary>
		public void RestoreFromReconcile(CooldownReconcileEntry[] entries)
		{
			cooldowns.Clear();
			snapshotDirty = true;
			if (entries == null)
			{
				return;
			}

			float td = TickDelta;
			for (int i = 0; i < entries.Length; i++)
			{
				cooldowns[entries[i].AbilityID] = new CooldownInstance(
					entries[i].StartTick,
					entries[i].DurationTicks,
					td);
			}
		}
	}
}