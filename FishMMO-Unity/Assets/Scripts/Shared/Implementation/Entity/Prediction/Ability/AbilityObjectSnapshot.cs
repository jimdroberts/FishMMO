using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Immutable snapshot of ability data captured at spawn time.
	/// Allows an <see cref="AbilityObject"/> to persist and function independently
	/// after the owning character disconnects, dies, or is otherwise cleaned up.
	/// When the live <see cref="Ability"/> reference becomes null (e.g., after detach),
	/// the AbilityObject falls back to this snapshot for lifetime checks,
	/// event dispatch, and collision handling.
	/// </summary>
	public sealed class AbilityObjectSnapshot
	{
		private static readonly IReadOnlyDictionary<int, AbilityOnTickEvent> emptyOnTickEvents = new Dictionary<int, AbilityOnTickEvent>(0);
		private static readonly IReadOnlyDictionary<int, AbilityOnHitEvent> emptyOnHitEvents = new Dictionary<int, AbilityOnHitEvent>(0);
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
		/// Stored as <see cref="IReadOnlyDictionary{TKey,TValue}"/> backed by a shallow copy
		/// of the live <see cref="Ability"/> dictionary. The <see cref="Dictionary{TKey,TValue}"/>
		/// implements <see cref="IReadOnlyDictionary{TKey,TValue}"/> directly, preventing
		/// new entries without the overhead of a wrapper.
		/// This ensures the snapshot is effectively immutable even if the live ability's event
		/// dictionaries are later modified by <see cref="Ability.RemoveAbilityEvent"/>.
		/// </summary>
		public readonly IReadOnlyDictionary<int, AbilityOnTickEvent> OnTickEvents;

		/// <summary>
		/// Snapshot of OnHit event triggers for collision hit dispatching.
		/// See <see cref="OnTickEvents"/> for immutability rationale.
		/// </summary>
		public readonly IReadOnlyDictionary<int, AbilityOnHitEvent> OnHitEvents;

		/// <summary>
		/// Snapshot of OnDestroy event triggers for cleanup dispatching.
		/// See <see cref="OnTickEvents"/> for immutability rationale.
		/// </summary>
		public readonly IReadOnlyDictionary<int, AbilityOnDestroyEvent> OnDestroyEvents;

		/// <summary>
		/// Creates a snapshot from a live <see cref="Ability"/> instance.
		/// Each event dictionary structure is copied into a new <see cref="Dictionary{TKey,TValue}"/>
		/// and assigned to an <see cref="IReadOnlyDictionary{TKey,TValue}"/> field. This isolates the
		/// snapshot from later mutations via <see cref="Ability.RemoveAbilityEvent"/> while
		/// still sharing the immutable <see cref="ScriptableObject"/> event template values.
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
		/// Copies a source event map into an isolated dictionary, or returns a shared
		/// empty instance when the source is null or empty.
		/// </summary>
		private static IReadOnlyDictionary<int, TEvent> CopyOrEmpty<TEvent>(
			IReadOnlyDictionary<int, TEvent> source,
			IReadOnlyDictionary<int, TEvent> empty)
		{
			if (source == null || source.Count == 0)
			{
				return empty;
			}

			return new Dictionary<int, TEvent>(source);
		}

	}
}