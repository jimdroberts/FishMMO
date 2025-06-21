using UnityEngine;
using FishNet.Object;
using System;

namespace FishMMO.Shared
{
	[Serializable]
	public class SpawnableSettings
	{
		public NetworkObject NetworkObject;
		public float MinimumRespawnTime;
		public float MaximumRespawnTime;
		[Range(0f, 1f)]
		public float SpawnChance = 0.5f; // Default is 50% chance to spawn

		[ShowReadonly]
		public float YOffset;

		public void OnValidate()
		{
			if (NetworkObject == null)
			{
				return;
			}

			if (!NetworkObject.GetIsSpawnable())
			{
				Log.Error($"{NetworkObject.name} is not spawnable. Mark it as spawnable and re-assign the object.");
				NetworkObject = null;
				return;
			}

			// get the collider height
			Collider collider = NetworkObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.TryGetDimensions(out float height, out float radius);
				YOffset = height;
				if (collider is SphereCollider)
				{
					YOffset = radius;
				}
			}
		}
	}
}