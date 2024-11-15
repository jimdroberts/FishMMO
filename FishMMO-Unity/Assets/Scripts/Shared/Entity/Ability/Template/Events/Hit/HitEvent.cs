using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class HitEvent : AbilityEvent
	{
		public GameObject FXPrefab;

		/// <summary>
		/// Returns the number of hits the event has issued,
		/// </summary>
		public abstract int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject);
		
		public void OnApplyFX(Vector3 position)
		{
#if !UNITY_SERVER
			if (FXPrefab != null)
			{
				GameObject fxPrefab = Instantiate(FXPrefab, position, Quaternion.identity);
			}
#endif
		}
	}
}