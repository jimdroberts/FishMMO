using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject event triggered when an ability object ticks (e.g., moves or applies continuous effects).
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability On Tick Event", menuName = "FishMMO/Abilities/Events/Ability On Tick Event", order = 0)]
	public class AbilityOnTickEvent : AbilityEvent
	{
	}
}