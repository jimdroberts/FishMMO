using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Pet Ability", menuName = "FishMMO/Character/Ability/Pet Ability", order = 1)]
	public class PetAbilityTemplate : AbilityTemplate
	{
		public NetworkObject PetPrefab;
		public Vector3 SpawnBoundingBox;
		public float SpawnDistance;

        public override void OnLoad(string typeName, string resourceName, int resourceID)
        {
            base.OnLoad(typeName, resourceName, resourceID);

			if (PetPrefab == null)
			{
				return;
			}

			// get the collider height
			Collider collider = PetPrefab.GetComponent<Collider>();
			switch (collider)
			{
				case CapsuleCollider capsule:
					SpawnDistance = capsule.radius * 2;
					SpawnBoundingBox.y = capsule.height;
					break;
				case BoxCollider box:
					SpawnDistance = box.bounds.extents.magnitude;
					SpawnBoundingBox.y = box.bounds.extents.y;
					break;
				case SphereCollider sphere:
					SpawnDistance = sphere.radius * 2;
					SpawnBoundingBox.y = sphere.radius;
					break;
				default: break;
			}
        }
    }
}