using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Immutable snapshot of ability data, captured lazily at the moment an object is
	/// <i>detached</i> from its ability (<see cref="Ability.DetachAllAbilityObjects"/>), not at
	/// spawn. If events were removed from the ability between spawn and detach the snapshot
	/// reflects the ability as it was at detach.
	/// Allows an <see cref="AbilityObject"/> to persist and function independently
	/// after the owning character disconnects, dies, or is otherwise cleaned up.
	/// When the live <see cref="Ability"/> reference becomes null (e.g., after detach),
	/// the AbilityObject falls back to this snapshot for lifetime checks,
	/// event dispatch, and collision handling.
	/// </summary>
	public sealed class AbilityObjectSnapshot
	{
		/// <summary>
		/// Shared empty dictionary returned when no OnTick events exist, avoiding per-instance allocation.
		/// </summary>
		private static readonly IReadOnlyDictionary<int, AbilityOnTickEvent> emptyOnTickEvents = new Dictionary<int, AbilityOnTickEvent>(0);
		/// <summary>
		/// Shared empty dictionary returned when no OnHit events exist.
		/// </summary>
		private static readonly IReadOnlyDictionary<int, AbilityOnHitEvent> emptyOnHitEvents = new Dictionary<int, AbilityOnHitEvent>(0);
		/// <summary>
		/// Shared empty dictionary returned when no OnDestroy events exist.
		/// </summary>
		private static readonly IReadOnlyDictionary<int, AbilityOnDestroyEvent> emptyOnDestroyEvents = new Dictionary<int, AbilityOnDestroyEvent>(0);

		/// <summary>
		/// Speed of the ability, used for movement actions.
		/// </summary>
		public readonly float Speed;

		/// <summary>
		/// Total configured lifetime of the ability.
		/// Used for the lifetime countdown check when the live Ability is no longer available.
		/// </summary>
		public readonly float LifeTime;

		/// <summary>
		/// Snapshot of OnTick event triggers for continued ticking.
		/// Stored as <see cref="IReadOnlyDictionary{TKey,TValue}"/> backed by a shallow, KEY-ORDERED
		/// copy of the live <see cref="Ability"/> map — see <see cref="CopyOrEmpty"/> for why the
		/// ordering has to survive the copy. Exposing it read-only prevents new entries without the
		/// overhead of a wrapper, so the snapshot stays effectively immutable even if the live
		/// ability's event maps are later modified by <see cref="Ability.RemoveAbilityEvent"/>.
		///
		/// <para>
		/// Do NOT cast this field to its concrete dictionary type. Casting bypasses the readonly
		/// contract and may allow accidental mutation of the snapshot's internal state.
		/// </para>
		/// </summary>
		public readonly IReadOnlyDictionary<int, AbilityOnTickEvent> OnTickEvents;

		/// <summary>
		/// Snapshot of OnHit event triggers for collision hit dispatching.
		/// See <see cref="OnTickEvents"/> for immutability rationale.
		/// </summary>
		/// <para>
		/// Do NOT cast this field to its concrete dictionary type. Casting bypasses the readonly
		/// contract and may allow accidental mutation of the snapshot's internal state.
		/// </para>
		public readonly IReadOnlyDictionary<int, AbilityOnHitEvent> OnHitEvents;

		/// <summary>
		/// Snapshot of OnDestroy event triggers for cleanup dispatching.
		/// See <see cref="OnTickEvents"/> for immutability rationale.
		/// </summary>
		/// <para>
		/// Do NOT cast this field to its concrete dictionary type. Casting bypasses the readonly
		/// contract and may allow accidental mutation of the snapshot's internal state.
		/// </para>
		public readonly IReadOnlyDictionary<int, AbilityOnDestroyEvent> OnDestroyEvents;

		/// <summary>
		/// Creates a snapshot from a live <see cref="Ability"/> instance.
		/// Each event map is copied into a new key-ordered dictionary and assigned to an
		/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> field. This isolates the snapshot from later
		/// mutations via <see cref="Ability.RemoveAbilityEvent"/> while still sharing the immutable
		/// <see cref="ScriptableObject"/> event template values.
		/// </summary>
		/// <param name="ability">The ability to snapshot. Must not be null.</param>
		public AbilityObjectSnapshot(Ability ability)
		{
			Speed = ability.Speed;
			LifeTime = ability.LifeTime;
			// Copy each dictionary structure so the snapshot is isolated from live mutations.
			// The dictionary keys/values (ScriptableObject event template references) are shared,
			// not deep-copied — they are immutable and safe to share.
			OnTickEvents = CopyOrEmpty(ability.OnTickEvents, emptyOnTickEvents);
			OnHitEvents = CopyOrEmpty(ability.OnHitEvents, emptyOnHitEvents);
			OnDestroyEvents = CopyOrEmpty(ability.OnDestroyEvents, emptyOnDestroyEvents);
		}

		/// <summary>
		/// Copies a source event map into an isolated, KEY-ORDERED dictionary, or returns a shared
		/// empty instance when the source is null or empty.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Sorted, because the order these are dispatched in is a correctness property.</b> The
		/// live maps on <see cref="Ability"/> are <see cref="SortedDictionary{TKey,TValue}"/> so every
		/// peer runs an object's OnTick / OnHit / OnDestroy events in the same order — they all share
		/// that object's <see cref="AbilityObject.RNG"/>, and an OnHit event is allowed to end the
		/// object, so the order decides both what each event rolls and which ones run at all.
		/// </para>
		/// <para>
		/// This used to copy into a plain <see cref="Dictionary{TKey,TValue}"/>, which dropped that
		/// guarantee from the type at exactly the moment it stops being checkable — an orphaned object
		/// outlives the ability it could be compared against. It happened to preserve the order (the
		/// copy appends in source order and a dictionary enumerates its entries array), but "happens
		/// to" is not what a determinism-critical path should rest on, and nothing would have caught a
		/// regression.
		/// </para>
		/// </remarks>
		private static IReadOnlyDictionary<int, TEvent> CopyOrEmpty<TEvent>(
			IReadOnlyDictionary<int, TEvent> source,
			IReadOnlyDictionary<int, TEvent> empty)
		{
			if (source == null || source.Count == 0)
			{
				return empty;
			}

			SortedDictionary<int, TEvent> copy = new SortedDictionary<int, TEvent>();
			foreach (KeyValuePair<int, TEvent> entry in source)
			{
				copy[entry.Key] = entry.Value;
			}
			return copy;
		}

	}
}