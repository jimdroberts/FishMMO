using System;
using UnityEngine;
using FishNet.Object;
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
		public float MinimumRespawnTime;

		/// <summary>
		/// The maximum respawn time (in seconds) for this object.
		/// </summary>
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
		/// Validates the spawnable settings, ensuring the network object is spawnable and calculates YOffset from its collider.
		/// Safe on dedicated servers where renderers/shaders may be stripped — never throws.
		/// </summary>
		public virtual void OnValidate()
		{
			if (NetworkObject == null)
			{
				return;
			}

			try
			{
				// Ensure the network object is marked as spawnable.
				if (!NetworkObject.GetIsSpawnable())
				{
					Log.Error("SpawnableSettings", $"{NetworkObject.name} is not spawnable. Mark it as spawnable and re-assign the object.");
					NetworkObject = null;
					return;
				}

				// Get the collider and calculate YOffset for proper placement.
				// Collider-only: no materials/shaders (safe under Dedicated Server Optimizations).
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
			catch (Exception ex)
			{
				// Dedicated server builds can surface unexpected missing-type/prefab issues;
				// clear the ref so ObjectSpawner can sanitize and continue scene Ready.
				Log.Warning("SpawnableSettings",
					$"OnValidate failed for '{NetworkObject?.name}': {ex.Message}. Clearing NetworkObject.");
				NetworkObject = null;
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