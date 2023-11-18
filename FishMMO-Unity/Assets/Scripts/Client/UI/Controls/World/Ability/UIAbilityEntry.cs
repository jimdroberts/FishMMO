using UnityEngine;
using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIAbilityEntry : Button
	{
		[SerializeField]
		public Image Icon;

		public event Action<int> OnLeft;
		public event Action<int> OnRight;

		public int Index { get; private set; }
		public AbilityTemplate Template { get; private set; }
		public Character Character { get; private set; }

		public void Initialize(Character character, int index, AbilityTemplate template)
		{
			Index = index;
			Template = template;
			Character = character;
		}

		public virtual void OnLeftClick()
		{
			if (UIManager.TryGet("UISelector", out UISelector selector))
			{
				/*selector.Open((id) =>
				{
					AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(id);
					if (template != null)
					{
						Icon.sprite = template.Icon;
					}

					OnLeft?.Invoke(Index);
				});*/
			}
		}

		public virtual void OnRightClick()
		{
			OnRight?.Invoke(Index);
			Clear();
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			if (Character != null)
			{
				if (Template != null &&
					UIManager.TryGet("UITooltip", out UITooltip tooltip))
				{
					tooltip.SetText(Template.Tooltip(), true);
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
			Template = null;
			if (Icon != null) Icon.sprite = null;
			OnLeft = null;
			OnRight = null;
		}
	}
}
