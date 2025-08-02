using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject event triggered before the primary ability object is spawned.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability On Pre Spawn Event", menuName = "FishMMO/Abilities/Events/Ability On Pre Spawn Event", order = 0)]
	public class AbilityOnPreSpawnEvent : AbilityEvent
	{
	}
}