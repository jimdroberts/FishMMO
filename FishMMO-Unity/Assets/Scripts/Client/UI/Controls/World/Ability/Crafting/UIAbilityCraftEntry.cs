using UnityEngine.EventSystems;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIAbilityCraftEntry : UIAbilityEntry
	{
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UISelector", out UISelector selector))
			{
				selector.Open((id) =>
				{
					AbilityEvent template = AbilityEvent.Get<AbilityEvent>(id);
					if (template != null)
					{
						TemplateID = template.ID;
						Icon.sprite = template.Icon;
					}

					Internal_OnLeftClick(id);
				});
			}
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			Character character = Character.localCharacter;
			if (character != null)
			{
				AbilityEvent eventTemplate = AbilityEvent.Get<AbilityEvent>(TemplateID);
				if (eventTemplate != null &&
					UIManager.TryGet("UITooltip", out UITooltip tooltip))
				{
					tooltip.SetText(eventTemplate.Tooltip(), true);
				}
			}
		}
	}
}