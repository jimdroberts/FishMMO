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
		/// <param name="allowClearInvalidRefs">
		/// When true (editor / design-time), clears invalid NetworkObject refs.
		/// When false (runtime ObjectSpawner init), never nulls the prefab reference —
		/// dedicated-server builds often report GetIsSpawnable false or throw on stripped
		/// graphics while PrefabId is still valid for pooling.
		/// </param>
		public virtual void OnValidate(bool allowClearInvalidRefs = true)
		{
			if (NetworkObject == null)
			{
				return;
			}

			try
			{
				// Ensure the network object is marked as spawnable (editor hygiene).
				if (!NetworkObject.GetIsSpawnable())
				{
					Log.Warning("SpawnableSettings",
						$"{NetworkObject.name} is not marked spawnable. " +
						(allowClearInvalidRefs
							? "Clearing NetworkObject reference."
							: "Keeping reference for runtime PrefabId spawn."));
					if (allowClearInvalidRefs)
					{
						NetworkObject = null;
					}
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
				// Dedicated server builds can surface unexpected missing-type/prefab issues.
				// Only clear in editor — runtime must keep PrefabId-capable refs so
				// NPCSpawner / OrcSpawner are not wiped empty on headless scene load.
				Log.Warning("SpawnableSettings",
					$"OnValidate failed for '{NetworkObject?.name}': {ex.Message}." +
					(allowClearInvalidRefs ? " Clearing NetworkObject." : " Keeping reference for runtime spawn."));
				if (allowClearInvalidRefs)
				{
					NetworkObject = null;
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