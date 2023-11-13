using System;

namespace FishMMO.Client
{
	public class UIAbilityCraftEntry : UIAbilityEntry
	{
		public Action<long> OnAdd;

		public void OnButtonAddAbility()
		{
			OnAdd?.Invoke(AbilityID);
		}
	}
}