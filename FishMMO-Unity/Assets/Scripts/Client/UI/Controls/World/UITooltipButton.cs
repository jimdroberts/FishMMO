using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UITooltipButton : Button
	{
		private Sprite cachedSprite;
		private string cachedLabel;
		private UITooltip currentUITooltip;

		public Image Icon;
		public TMP_Text TooltipLabel;
		public string ExtraTooltipInfo;
		public object[] OptionalParams;

		public Action<int, object[]> OnLeftClick;
		public Action<int, object[]> OnRightClick;
		public Action<int, object[]> OnCtrlClick;

		public int Index { get; private set; }
		public ITooltip Tooltip { get; private set; }
		public Character Character { get; private set; }

		protected override void Awake()
		{
			if (Icon != null)
			{
				cachedSprite = Icon.sprite;
			}
			if (TooltipLabel != null)
			{
				cachedLabel = TooltipLabel.text;
			}
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			OnLeftClick = null;
			OnRightClick = null;
			OnCtrlClick = null;

			Clear();
		}

		protected override void OnDisable()
		{
			base.OnDisable();

			ClearTooltip();
		}

		public void Initialize(int index, Action<int, object[]> onLeftClick, Action<int, object[]> onRightClick, ITooltip tooltip = null, string extraTooltipInfo = "", Action<int, object[]> onCtrlClick = null, object[] optionalParams = null)
		{
			Index = index;
			OnLeftClick = null;
			OnLeftClick = onLeftClick;
			OnRightClick = null;
			OnRightClick = onRightClick;
			OnCtrlClick = null;
			OnCtrlClick = onCtrlClick;
			if (tooltip != null)
			{
				Tooltip = tooltip;
				ExtraTooltipInfo = extraTooltipInfo;
				if (Icon != null)
				{
					Icon.sprite = tooltip.Icon;
				}
				if (TooltipLabel != null)
				{
					TooltipLabel.text = tooltip.Name;
				}
			}
			OptionalParams = optionalParams;
		}
		public void Initialize(Character character, ITooltip tooltip)
		{
			Character = character;
			Tooltip = tooltip;
			if (Icon != null)
			{
				Icon.sprite = tooltip.Icon;
			}
			if (TooltipLabel != null)
			{
				TooltipLabel.text = tooltip.Name;
			}
		}

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			if (Tooltip != null &&
				UIManager.TryGet("UITooltip", out currentUITooltip))
			{
				currentUITooltip.Open(Tooltip.Tooltip() + ExtraTooltipInfo);
			}
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			base.OnPointerExit(eventData);

			ClearTooltip();
		}

		public override void OnPointerClick(PointerEventData eventData)
		{
			base.OnPointerClick(eventData);

			if (eventData.button == PointerEventData.InputButton.Left)
			{
				if (Input.GetKey(KeyCode.LeftControl))
				{
					OnCtrlClick?.Invoke(Index, OptionalParams);
				}
				else
				{
					OnLeftClick?.Invoke(Index, OptionalParams);
				}
			}
			else if (eventData.button == PointerEventData.InputButton.Right)
			{
				if (Input.GetKey(KeyCode.LeftControl))
				{
					OnCtrlClick?.Invoke(Index, OptionalParams);
				}
				else
				{
					OnRightClick?.Invoke(Index, OptionalParams);
				}
			}
		}

		private void ClearTooltip()
		{
			if (currentUITooltip != null)
			{
				currentUITooltip.Hide();
				currentUITooltip = null;
			}
		}

		public virtual void Clear()
		{
			Character = null;
			Tooltip = null;
			if (Icon != null)
			{
				Icon.sprite = cachedSprite;
			}
			if (TooltipLabel != null)
			{
				TooltipLabel.text = cachedLabel;
			}
			OptionalParams = null;
			ClearTooltip();
		}
	}
}
