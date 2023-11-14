using UnityEngine;
using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIAbilityEntry : Button
	{
		public int TemplateID;
		[SerializeField]
		public Image Icon;

		public event Action<int> OnLeft;
		public event Action<int> OnRight;

		protected void Internal_OnLeftClick(int templateID)
		{
			OnLeft?.Invoke(templateID);
		}

		public virtual void OnLeftClick()
		{
			if (UIManager.TryGet("UISelector", out UISelector selector))
			{
				selector.Open((id) =>
				{
					AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(id);
					if (template != null)
					{
						TemplateID = template.ID;
						Icon.sprite = template.Icon;
					}

					Internal_OnLeftClick(id);
				});
			}
		}

		public virtual void OnRightClick()
		{
			OnRight?.Invoke(TemplateID);
			Clear();
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			Character character = Character.localCharacter;
			if (character != null)
			{
				AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(TemplateID);
				if (template != null &&
					UIManager.TryGet("UITooltip", out UITooltip tooltip))
				{
					tooltip.SetText(template.Tooltip(), true);
				}
			}
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			base.OnPointerExit(eventData);

			if (UIManager.TryGet("UITooltip", out UITooltip tooltip))
			{
				tooltip.OnHide();
			}
		}

		public override void OnPointerClick(PointerEventData eventData)
		{
			base.OnPointerClick(eventData);

			if (eventData.button == PointerEventData.InputButton.Left)
			{
				OnLeftClick();
			}
			else if (eventData.button == PointerEventData.InputButton.Right)
			{
				OnRightClick();
			}
		}

		public virtual void Clear()
		{
			TemplateID = 0;
			if (Icon != null) Icon.sprite = null;
			OnLeft = null;
			OnRight = null;
		}
	}
}
