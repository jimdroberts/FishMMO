#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
#if !UNITY_SERVER
using FishMMO.Client;
#endif

namespace FishMMO.Shared
{
	public class AbilityCrafter : Interactable
	{
		public override bool OnInteract(Character character)
		{
			if (!base.OnInteract(character) ||
				!character.IsOwner)
			{
				return false;
			}

#if !UNITY_SERVER
			if (UIManager.TryGet("UIAbilityCraft", out UIAbilityCraft uiAbilityCraft))
			{
				uiAbilityCraft.Show(character);
			}
#endif
			return true;
		}
	}
}