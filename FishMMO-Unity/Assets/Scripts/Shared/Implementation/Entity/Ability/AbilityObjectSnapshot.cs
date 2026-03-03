using System.Collections.Generic;
using FishMMO.Shared.Core;

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
		/// </summary>
		public readonly Dictionary<int, AbilityOnTickEvent> OnTickEvents;

		/// <summary>
		/// Snapshot of OnHit event triggers for collision hit dispatching.
		/// </summary>
		public readonly Dictionary<int, AbilityOnHitEvent> OnHitEvents;

		/// <summary>
		/// Snapshot of OnDestroy event triggers for cleanup dispatching.
		/// </summary>
		public readonly Dictionary<int, AbilityOnDestroyEvent> OnDestroyEvents;

		/// <summary>
		/// The TargetTrigger from the ability template for collision handling.
		/// </summary>
		public readonly AbilityEvent TargetTrigger;

		/// <summary>
		/// Creates a snapshot from a live <see cref="Ability"/> instance.
		/// </summary>
		/// <param name="ability">The ability to snapshot. Must not be null.</param>
		public AbilityObjectSnapshot(Ability ability)
		{
			Speed = ability.Speed;
			LifeTime = ability.LifeTime;
			OnTickEvents = ability.OnTickEvents;
			OnHitEvents = ability.OnHitEvents;
			OnDestroyEvents = ability.OnDestroyEvents;
			TargetTrigger = ability.Template?.TargetTrigger;
		}

		/// <summary>
		/// Creates a <see cref="SnapshotCharacter"/> from a live caster, freezing identity
		/// and attribute data so that detached ability objects can continue to resolve
		/// stat-scaled calculations.
		/// </summary>
		/// <param name="liveCaster">The live character to snapshot.</param>
		/// <param name="abilityObjectTransform">The ability object's transform, used as the phantom's positional reference.</param>
		/// <returns>A new <see cref="SnapshotCharacter"/> or null if <paramref name="liveCaster"/> is null.</returns>
		public static SnapshotCharacter CreatePhantomCaster(ICharacter liveCaster, UnityEngine.Transform abilityObjectTransform)
		{
			if (liveCaster == null)
			{
				return null;
			}
			return new SnapshotCharacter(liveCaster, abilityObjectTransform);
		}
	}
}