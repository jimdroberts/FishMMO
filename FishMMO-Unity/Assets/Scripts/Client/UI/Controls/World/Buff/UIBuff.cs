using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIBuff : UICharacterControl
	{
		public RectTransform BuffParent;
		public GameObject BuffButtonPrefab;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
		}

		public override void OnPreUnsetCharacter()
		{
		}

		public override void OnQuitToLogin()
		{
			ClearAllBuffs();
		}

		private void InstantiateBuff(long id, Sprite icon, ReferenceButtonType buttonType, AbilityTabType tabType, string toolTip, ref List<UIAbilityButton> container)
		{
			/*UIAbilityButton button = Instantiate(AbilityButtonPrefab, AbilityParent);
			button.Character = Character;
			button.ReferenceID = id;
			button.Type = buttonType;
			if (button.DescriptionLabel != null)
			{
				button.DescriptionLabel.text = toolTip;
			}
			if (button.Icon != null)
			{
				button.Icon.sprite = icon;
			}
			if (container == null)
			{
				container = new List<UIAbilityButton>();
			}
			container.Add(button);
			button.gameObject.SetActive(CurrentTab == tabType ? true : false);*/
		}

		public void ClearAllBuffs()
		{
		}
	}
}