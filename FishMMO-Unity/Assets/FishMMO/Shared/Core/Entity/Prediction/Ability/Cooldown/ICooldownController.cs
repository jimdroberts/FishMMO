using System;
using FishNet.Serializing;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for a cooldown controller using immutable <see cref="CooldownInstance"/>
	/// based on StartTick + DurationTicks. No per-tick mutation is needed — cooldowns
	/// are evaluated via <c>(currentTick - StartTick) &lt; DurationTicks</c>.
	/// </summary>
	public interface ICooldownController : ICharacterBehaviour
	{
		/// <summary>
		/// Event invoked when a cooldown is added.
		/// </summary>
		static Action<long, CooldownInstance> OnAddCooldown;

		/// <summary>
		/// Event invoked when a cooldown is updated (e.g., remaining time changes due to reconcile).
		/// </summary>
		static Action<long, CooldownInstance> OnUpdateCooldown;

		/// <summary>
		/// Event invoked when a cooldown is removed (expired or explicitly cleared).
		/// </summary>
		static Action<long> OnRemoveCooldown;

		/// <summary>
		/// Reads cooldown data from a network reader.
		/// </summary>
		/// <param name="reader">The network reader.</param>
		/// <param name="currentTick">Current tick for computing UI-facing remaining time.</param>
		void Read(Reader reader, uint currentTick);

		/// <summary>
		/// Writes cooldown data to a network writer.
		/// </summary>
		/// <param name="writer">The network writer.</param>
		void Write(Writer writer);

		/// <summary>
		/// Writes cooldown data, or an empty framed block when <paramref name="includeEntries"/>
		/// is false (non-owner connections have no use for a peer's cooldowns).
		/// </summary>
		void Write(Writer writer, bool includeEntries);

		/// <summary>
		/// Removes all cooldowns that have expired as of <paramref name="currentTick"/>.
		/// Called from the Replicate loop. Safe during replay because the same tick
		/// value produces the same expiration decisions deterministically.
		/// </summary>
		/// <param name="currentTick">The current network tick.</param>
		void ExpireElapsed(uint currentTick);

		/// <summary>
		/// Checks if an ability is on cooldown at the given tick.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="currentTick">The current network tick.</param>
		/// <returns>True if on cooldown, otherwise false.</returns>
		bool IsOnCooldown(long id, uint currentTick);

		/// <summary>
		/// Maps a raw authoritative tick to the controller's current replicate-tick domain
		/// when a replicate tick is available.
		/// </summary>
		/// <param name="serverTick">Fallback authoritative tick used before any replicate tick exists.</param>
		/// <returns>The effective tick to use for cooldown comparisons.</returns>
		uint ResolveAuthoritativeTick(uint serverTick);

		/// <summary>
		/// Tries to get the remaining cooldown time in seconds for an ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="currentTick">The current network tick.</param>
		/// <param name="cooldown">Remaining cooldown time in seconds.</param>
		/// <returns>True if found and still on cooldown, otherwise false.</returns>
		bool TryGetCooldown(long id, uint currentTick, out float cooldown);

		/// <summary>
		/// Tries to get the full <see cref="CooldownInstance"/> for an ability or consumable,
		/// whether or not it is still running.
		/// </summary>
		/// <param name="id">Ability or consumable template ID.</param>
		/// <param name="instance">The cooldown instance.</param>
		/// <returns>True if a cooldown is tracked for the id.</returns>
		/// <remarks>
		/// <see cref="TryGetCooldown"/> reports only the REMAINING seconds, which is not enough to
		/// draw a sweep — a sweep needs the fraction, and a fraction needs the total the cooldown
		/// started from. The UI previously took its total from the instance handed to
		/// <see cref="OnAddCooldown"/> and then never updated, which is what made the hotkey bar's
		/// sweep binary rather than animated.
		/// </remarks>
		bool TryGetCooldownInstance(long id, out CooldownInstance instance);

		/// <summary>
		/// Adds a cooldown for the specified ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		/// <param name="cooldown">Cooldown instance.</param>
		void AddCooldown(long id, CooldownInstance cooldown);

		/// <summary>
		/// Removes the cooldown for the specified ability.
		/// </summary>
		/// <param name="id">Ability ID.</param>
		void RemoveCooldown(long id);

		/// <summary>
		/// Clears all cooldowns.
		/// </summary>
		void Clear();

		/// <summary>
		/// Creates a reconcile snapshot of all active cooldowns.
		/// Returns null when no cooldowns are active.
		/// </summary>
		CooldownReconcileEntry[] CreateReconcileSnapshot();

		/// <summary>
		/// Restores cooldown state from a reconcile snapshot.
		/// </summary>
		void RestoreFromReconcile(CooldownReconcileEntry[] entries);
	}
}