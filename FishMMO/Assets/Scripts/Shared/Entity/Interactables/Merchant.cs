using UnityEditor;
using UnityEngine;

public class Merchant : Interactable
{
	public override bool OnInteract(Character character)
	{
		if (!base.OnInteract(character))
		{
			return false;
		}

		//Item chest = new Item(-443507152, 1);
		//chest.GenerateAttributes();
		//character.InventoryController.AddItem(chest);

		return true;
	}
}