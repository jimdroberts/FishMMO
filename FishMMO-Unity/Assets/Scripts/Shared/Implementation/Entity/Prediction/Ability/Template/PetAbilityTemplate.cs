using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining a pet ability, including prefab and spawn parameters.
	/// </summary>
	[CreateAssetMenu(fileName = "New Pet Ability", menuName = "FishMMO/Character/Ability/Pet Ability", order = 1)]
	public class PetAbilityTemplate : AbilityTemplate
	{
		/// <summary>
		/// The prefab for the pet to be spawned.
		/// </summary>
		public NetworkObject PetPrefab;

		/// <summary>
		/// Abilities the summoned pet starts knowing.
		/// </summary>
		/// <remarks>
		/// These are taught to the pet's <see cref="FishMMO.Shared.Core.IAbilityController"/> on
		/// summon and persisted with it, so the pet keeps them across a log out. The pet prefab's
		/// own <see cref="NPC.Abilities"/> list is learned as well; use that for abilities every
		/// pet of this species has, and this list for abilities granted by the summoning spell.
		/// </remarks>
		[Tooltip("Abilities the summoned pet starts with. Learned on summon and persisted.")]
		public List<AbilityTemplate> PetAbilities = new List<AbilityTemplate>();

		/// <summary>
		/// The bounding box for spawning the pet.
		/// </summary>
		public Vector3 SpawnBoundingBox;

		/// <summary>
		/// The distance from the spawner to spawn the pet.
		/// </summary>
		public float SpawnDistance;

		/// <summary>
		/// Called when the pet ability is loaded. Calculates spawn parameters based on the prefab's collider.
		/// </summary>
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