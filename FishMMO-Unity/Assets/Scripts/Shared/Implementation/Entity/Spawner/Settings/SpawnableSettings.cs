using UnityEngine;
using FishNet.Object;
using System;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Base serializable settings for configuring a spawnable object. Supports polymorphic subclassing
	/// via <see cref="SerializeReference"/> for type-specific data injection (e.g., items, NPCs).
	/// Use the <see cref="SubclassSelector"/> attribute on the containing field/list for Inspector support.
	/// </summary>
	[Serializable]
	public class SpawnableSettings
	{
		/// <summary>
		/// The network object prefab to be spawned.
		/// </summary>
		public NetworkObject NetworkObject;

		/// <summary>
		/// The minimum respawn time (in seconds) for this object.
		/// </summary>
		/// <remarks>
		/// Subclasses whose prefab carries its own respawn cadence treat this as an OVERRIDE that
		/// is only consulted when set — see <see cref="NPCSpawnableSettings"/>. For everything
		/// else it is simply the authored value.
		/// </remarks>
		[Tooltip("Shortest respawn delay in seconds. For NPCs, leave at 0 to use the prefab's own value.")]
		[Min(0f)]
		public float MinimumRespawnTime;

		/// <summary>
		/// The maximum respawn time (in seconds) for this object.
		/// </summary>
		/// <remarks>
		/// See <see cref="MinimumRespawnTime"/> for the override semantics.
		/// </remarks>
		[Tooltip("Longest respawn delay in seconds. For NPCs, leave at 0 to use the prefab's own value.")]
		[Min(0f)]
		public float MaximumRespawnTime;

		/// <summary>
		/// The chance (0 to 1) that this object will be selected for spawning. Default is 0.5 (50%).
		/// </summary>
		[Range(0f, 1f)]
		public float SpawnChance = 0.5f;

		/// <summary>
		/// The vertical offset used when placing the object in the world, calculated from its collider.
		/// </summary>
		[ShowReadonly]
		public float YOffset;

		/// <summary>
		/// Resolves the respawn delay range this spawnable should use.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Exists so the spawner asks the settings for a range rather than reading the two fields
		/// directly. That indirection is the whole point: a subclass whose prefab carries its own
		/// respawn cadence can answer with the prefab's values when the spawner has not overridden
		/// them, and the spawner does not need to know which subclass it is holding.
		/// </para>
		/// <para>
		/// The base implementation has no prefab defaults to fall back on, so it returns the
		/// authored fields unchanged.
		/// </para>
		/// </remarks>
		/// <param name="minimum">Receives the shortest respawn delay in seconds.</param>
		/// <param name="maximum">Receives the longest respawn delay in seconds.</param>
		public virtual void ResolveRespawnTimeRange(out float minimum, out float maximum)
		{
			minimum = MinimumRespawnTime;
			maximum = MaximumRespawnTime;

			// The RNG collapses an inverted range to its minimum, which would silently pin the
			// delay to a constant. Order it here so every caller gets a usable range.
			if (maximum < minimum)
			{
				maximum = minimum;
			}
		}

		/// <summary>
		/// Validates the spawnable settings, ensuring the network object is spawnable and calculates YOffset from its collider.
		/// </summary>
		public virtual void OnValidate()
		{
			if (MinimumRespawnTime < 0f)
			{
				MinimumRespawnTime = 0f;
			}
			/* Raise the maximum to meet the minimum rather than leaving the pair incoherent.
			 *
			 * This is what makes "is the override set?" answerable from the maximum alone, which
			 * is how NPCSpawnableSettings decides whether to defer to the prefab. An author who
			 * fills in a minimum of 10 and leaves the maximum at 0 has plainly asked for an
			 * override; without this the pair would still read as unset and their value would be
			 * silently discarded in favour of the prefab's. It also orders a range someone
			 * inverted, which the RNG would otherwise collapse to a constant.
			 */
			if (MaximumRespawnTime < MinimumRespawnTime)
			{
				MaximumRespawnTime = MinimumRespawnTime;
			}

			if (NetworkObject == null)
			{
				return;
			}

			// Ensure the network object is marked as spawnable.
			if (!NetworkObject.GetIsSpawnable())
			{
				Log.Error("SpawnableSettings", $"{NetworkObject.name} is not spawnable. Mark it as spawnable and re-assign the object.");
				NetworkObject = null;
				return;
			}

			// Get the collider and calculate YOffset for proper placement.
			Collider collider = NetworkObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.TryGetDimensions(out float height, out float radius);
				YOffset = height;
				// If the collider is a sphere, use its radius for YOffset.
				if (collider is SphereCollider)
				{
					YOffset = radius;
				}
			}
		}

		/// <summary>
		/// Called after the network object has been instantiated but before it is spawned on the network.
		/// Override in subclasses to inject type-specific data into the spawned object.
		/// </summary>
		/// <param name="nob">The instantiated network object to configure.</param>
		/// <param name="spawner">The spawner that created this object.</param>
		public virtual void OnSpawned(NetworkObject nob, ObjectSpawner spawner)
		{
		}
	}
}