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

		public Image Icon;
		public TMP_Text TooltipLabel;

		public Action<int> OnLeftClick;
		public Action<int> OnRightClick;

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

			Clear();
		}

		public void Initialize(int index, Action<int> onLeftClick, Action<int> onRightClick)
		{
			Index = index;
			OnLeftClick = null;
			OnLeftClick += onLeftClick;
			OnRightClick = null;
			OnRightClick += onRightClick;
		}
		public void Initialize(int index, Action<int> onLeftClick, Action<int> onRightClick, ITooltip tooltip)
		{
			Index = index;
			OnLeftClick = null;
			OnLeftClick += onLeftClick;
			OnRightClick = null;
			OnRightClick += onRightClick;
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
				UIManager.TryGet("UITooltip", out UITooltip tooltip))
			{
				tooltip.Open(Tooltip.Tooltip());
			}
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			base.OnPointerExit(eventData);

			if (UIManager.TryGet("UITooltip", out UITooltip tooltip))
			{
				tooltip.Hide();
			}
		}

		public override void OnPointerClick(PointerEventData eventData)
		{
			base.OnPointerClick(eventData);

			if (eventData.button == PointerEventData.InputButton.Left)
			{
				OnLeftClick?.Invoke(Index);
			}
			else if (eventData.button == PointerEventData.InputButton.Right)
			{
				OnRightClick?.Invoke(Index);
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
		}
	}
}
