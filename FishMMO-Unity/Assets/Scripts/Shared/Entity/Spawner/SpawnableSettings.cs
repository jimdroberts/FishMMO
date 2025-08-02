using UnityEngine;
using FishNet.Object;
using System;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable settings for configuring a spawnable object, including respawn times, spawn chance, and Y offset for placement.
	/// </summary>
	[Serializable]
	public class SpawnableSettings
	{
		/// <summary>
		/// The network object to be spawned.
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
		/// </summary>
		public void OnValidate()
		{
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
	}
}