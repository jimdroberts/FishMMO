using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI button with tooltip support, event callbacks, and optional parameters.
	/// </summary>
	public class UITooltipButton : Button
	{
		/// <summary>
		/// Cached sprite for restoring the icon when clearing the button.
		/// </summary>
		private Sprite cachedSprite;
		/// <summary>
		/// Cached label text for restoring the tooltip label when clearing the button.
		/// </summary>
		private string cachedLabel;
		/// <summary>
		/// Reference to the currently displayed UITooltip instance.
		/// </summary>
		private UITooltip currentUITooltip;

		/// <summary>
		/// The icon image displayed on the button.
		/// </summary>
		public Image Icon;
		/// <summary>
		/// The label text displayed for the tooltip.
		/// </summary>
		public TMP_Text TooltipLabel;
		/// <summary>
		/// Additional information to append to the tooltip.
		/// </summary>
		public string ExtraTooltipInfo;
		/// <summary>
		/// Optional parameters passed to click event handlers.
		/// </summary>
		public object[] OptionalParams;

		/// <summary>
		/// Event invoked on left mouse click.
		/// </summary>
		public Action<int, object[]> OnLeftClick;
		/// <summary>
		/// Event invoked on right mouse click.
		/// </summary>
		public Action<int, object[]> OnRightClick;
		/// <summary>
		/// Event invoked on Ctrl+click (left or right).
		/// </summary>
		public Action<int, object[]> OnCtrlClick;

		/// <summary>
		/// Index of the button, used for event callbacks.
		/// </summary>
		public int Index { get; private set; }
		/// <summary>
		/// Tooltip data associated with this button.
		/// </summary>
		public ITooltip Tooltip { get; private set; }
		/// <summary>
		/// Player character associated with this button, if any.
		/// </summary>
		public IPlayerCharacter Character { get; private set; }

		/// <summary>
		/// Unity Awake callback. Caches initial icon and label for later restoration.
		/// </summary>
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

		/// <summary>
		/// Unity OnDestroy callback. Clears event handlers and resets state.
		/// </summary>
		protected override void OnDestroy()
		{
			base.OnDestroy();

			OnLeftClick = null;
			OnRightClick = null;
			OnCtrlClick = null;

			Clear();
		}

		/// <summary>
		/// Unity OnDisable callback. Hides tooltip if active.
		/// </summary>
		protected override void OnDisable()
		{
			base.OnDisable();

			ClearTooltip();
		}

		/// <summary>
		/// Initializes the button with event handlers, tooltip, and optional parameters.
		/// </summary>
		/// <param name="index">Button index for event callbacks.</param>
		/// <param name="onLeftClick">Handler for left click.</param>
		/// <param name="onRightClick">Handler for right click.</param>
		/// <param name="tooltip">Tooltip data.</param>
		/// <param name="extraTooltipInfo">Extra info to append to tooltip.</param>
		/// <param name="onCtrlClick">Handler for Ctrl+click.</param>
		/// <param name="optionalParams">Optional parameters for event handlers.</param>
		public void Initialize(int index, Action<int, object[]> onLeftClick, Action<int, object[]> onRightClick, ITooltip tooltip = null, string extraTooltipInfo = "", Action<int, object[]> onCtrlClick = null, object[] optionalParams = null)
		{
			Index = index;
			OnLeftClick = null; // Clear previous handler
			OnLeftClick = onLeftClick;
			OnRightClick = null; // Clear previous handler
			OnRightClick = onRightClick;
			OnCtrlClick = null; // Clear previous handler
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
			gameObject.SetActive(true);
		}

		/// <summary>
		/// Initializes the button with a player character and tooltip.
		/// </summary>
		/// <param name="character">Player character associated with the button.</param>
		/// <param name="tooltip">Tooltip data.</param>
		public void Initialize(IPlayerCharacter character, ITooltip tooltip)
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

		/// <summary>
		/// Handles pointer enter event to show tooltip.
		/// </summary>
		/// <param name="eventData">Pointer event data.</param>
		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			if (Tooltip != null &&
				UIManager.TryGet("UITooltip", out currentUITooltip))
			{
				currentUITooltip.Open(Tooltip.Tooltip() + ExtraTooltipInfo);
			}
		}

		/// <summary>
		/// Handles pointer exit event to hide tooltip.
		/// </summary>
		/// <param name="eventData">Pointer event data.</param>
		public override void OnPointerExit(PointerEventData eventData)
		{
			base.OnPointerExit(eventData);

			ClearTooltip();
		}

		/// <summary>
		/// Handles pointer click event, invoking appropriate event handler based on mouse button and modifier keys.
		/// </summary>
		/// <param name="eventData">Pointer event data.</param>
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

		/// <summary>
		/// Hides the currently displayed tooltip, if any.
		/// </summary>
		private void ClearTooltip()
		{
			if (currentUITooltip != null)
			{
				currentUITooltip.Hide();
				currentUITooltip = null;
			}
		}

		/// <summary>
		/// Clears the button state, restoring cached icon and label, and hiding tooltip.
		/// </summary>
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
