using FishMMO.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What the core's generic ECA pieces (<c>PlayFXAction</c>, <c>TargetSelector</c>) read of a live ability object, the
	/// Abilities module's spell (2026-11-20): where it is, which scene it lives in, and whether this peer resolves its hits.
	/// </summary>
	public interface IAbilityObject
	{
		/// <summary>The object's transform (its position is where an effect anchored on it plays).</summary>
		Transform Transform { get; }

		/// <summary>The object's GameObject (its scene is where effects it plays are spawned).</summary>
		GameObject GameObject { get; }

		/// <summary>True when this peer judges the object's hits and selections itself.</summary>
		bool ResolvesHitsLocally { get; }
	}

	/// <summary>
	/// The core's access to ability objects, installed by the Abilities module: find the object an event belongs to, and
	/// hold an effect a spawn chain plays until its object is drawn. Without the module there are none.
	/// </summary>
	public static class AbilityObjects
	{
		/// <summary>Resolves the ability object an event's payloads carry, or null. Installed by the Abilities module.</summary>
		public static System.Func<EventData, IAbilityObject> Resolver;

		/// <summary>Holds an effect for the cast being spawned; false when nothing holds it. Installed by the Abilities module.</summary>
		public delegate bool HoldSpawnEffectHandler(GameObject prefab, Scene scene, IAbilityObject anchor, Vector3 position, float size, float heightOffset);

		/// <summary>The installed hold, or null.</summary>
		public static HoldSpawnEffectHandler SpawnEffectHolder;

		/// <summary>The ability object <paramref name="eventData"/> belongs to, if any.</summary>
		public static bool TryResolveFrom(EventData eventData, out IAbilityObject abilityObject)
		{
			abilityObject = eventData != null && Resolver != null ? Resolver(eventData) : null;
			return abilityObject != null;
		}

		/// <summary>Holds an effect a spawn chain plays until its object is drawn; false when it should play now.</summary>
		public static bool TryHoldSpawnEffect(GameObject prefab, Scene scene, IAbilityObject anchor, Vector3 position, float size = 0f, float heightOffset = 0f)
		{
			return SpawnEffectHolder != null && SpawnEffectHolder(prefab, scene, anchor, position, size, heightOffset);
		}
	}
}
