#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class Merchant : Interactable
	{
		public List<AbilityTemplate> Abilities;
		public List<AbilityEvent> AbilityEvents;

		public override bool OnInteract(Character character)
		{
			if (!base.OnInteract(character))
			{
				return false;
			}

			if (Abilities == null ||
				Abilities.Count < 1 ||
				AbilityEvents == null ||
				AbilityEvents.Count < 1)
			{
				return true;
			}

			character.AbilityController.LearnAbilityTypes(Abilities, AbilityEvents);

			//Item chest = new Item(-443507152, 1);
			//chest.GenerateAttributes();
			//character.InventoryController.AddItem(chest);

			return true;
		}
	}
}