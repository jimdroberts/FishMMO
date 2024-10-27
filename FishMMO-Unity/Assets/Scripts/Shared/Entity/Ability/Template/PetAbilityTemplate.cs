using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Pet Ability", menuName = "Character/Ability/Pet Ability", order = 1)]
	public class PetAbilityTemplate : AbilityTemplate
	{
		public NetworkObject PetPrefab;
		public Vector3 SpawnBoundingBox;
		public float SpawnDistance;
	}
}