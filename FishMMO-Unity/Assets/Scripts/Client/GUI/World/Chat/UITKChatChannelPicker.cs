using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit chat channel picker. Lets the player toggle which chat channels a tab listens to
	/// and rename the current tab.
	/// </summary>
	public class UITKChatChannelPicker : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		private const string PANEL_NAME = "channel-picker-panel";
		private const string CHANNEL_LIST_NAME = "channel-list";
		private const string NAME_INPUT_NAME = "channel-name-input";

		/// <summary>The root panel element of the channel picker.</summary>
		private VisualElement panel;
		/// <summary>The container holding the channel toggle elements.</summary>
		private VisualElement channelList;
		/// <summary>The text field for renaming the current tab.</summary>
		private TextField nameInput;

		/// <summary>
		/// Per-channel toggles keyed by channel.
		/// </summary>
		private readonly Dictionary<ChatChannel, Toggle> toggles = new Dictionary<ChatChannel, Toggle>();

		/// <summary>
		/// When true, toggle callbacks are ignored (used while applying values programmatically).
		/// </summary>
		private bool suppressCallbacks;

		/// <summary>
		/// The position Activate asked for, before clamping it into view.
		/// </summary>
		private Vector3 requestedPosition;

		/// <summary>
		/// Builds a toggle per chat channel (except Command) and wires the rename input.
		/// </summary>
		public override void OnStarting()
		{
			/* The picker dismisses itself when the pointer moves off it — no click required. */
			OnLoseFocus -= Hide;
			OnLoseFocus += Hide;

			if (Root == null)
			{
				return;
			}

			panel = Root.Q<VisualElement>(PANEL_NAME);
			channelList = Root.Q<VisualElement>(CHANNEL_LIST_NAME);
			nameInput = Root.Q<TextField>(NAME_INPUT_NAME);

			if (channelList != null)
			{
				foreach (string channelName in Enum.GetNames(typeof(ChatChannel)))
				{
					if (channelName.Equals("Command"))
					{
						continue;
					}
					if (!Enum.TryParse(channelName, out ChatChannel channel))
					{
						continue;
					}

					Toggle toggle = new Toggle(channelName)
					{
						name = $"channel-toggle-{channelName}",
					};
					toggle.AddToClassList("fish-toggle");
					toggle.AddToClassList("channel-toggle");
					toggle.RegisterValueChangedCallback((evt) => OnToggleChannel(channel, evt.newValue));
					channelList.Add(toggle);
					toggles[channel] = toggle;
				}
			}

			if (nameInput != null)
			{
				/* Trickle-down for the same reason the chat input uses it: a TextField handles
				 * Return itself, and on the bubble phase that handling consumes the event before
				 * this callback is reached — so the one gesture that committed a rename never
				 * actually ran. */
				nameInput.RegisterCallback<KeyDownEvent>((evt) =>
				{
					if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
					{
						ChangeTabName();
						evt.StopPropagation();
					}
				}, TrickleDown.TrickleDown);

				/* Enter was also the ONLY thing that committed. This panel dismisses itself as
				 * soon as the pointer leaves it, so typing a name and clicking a channel — or
				 * simply moving away — threw the edit away with no indication it had been
				 * ignored. Commit on losing the field instead. */
				nameInput.RegisterCallback<FocusOutEvent>((evt) => ChangeTabName());
			}

			Root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
		}

		/// <summary>
		/// Drops the lose-focus subscription on teardown.
		/// </summary>
		public override void OnDestroying()
		{
			OnLoseFocus -= Hide;
		}

		/// <summary>
		/// Hides the picker when a pointer-down lands outside its panel.
		/// </summary>
		private void OnRootPointerDown(PointerDownEvent evt)
		{
			if (!Visible || panel == null)
			{
				return;
			}
			if (evt.target is VisualElement target && (target == panel || IsDescendantOf(target, panel)))
			{
				return;
			}
			Hide();
		}

		/// <summary>
		/// Checks whether a visual element is a descendant of the specified ancestor.
		/// </summary>
		/// <param name="element">The element to check.</param>
		/// <param name="ancestor">The potential ancestor.</param>
		/// <returns>True if the element is a descendant of the ancestor.</returns>
		private bool IsDescendantOf(VisualElement element, VisualElement ancestor)
		{
			VisualElement parent = element.parent;
			while (parent != null)
			{
				if (parent == ancestor)
				{
					return true;
				}
				parent = parent.parent;
			}
			return false;
		}

		/// <summary>
		/// Sets the toggle states for the tab's active channels, fills the rename field and positions the panel.
		/// </summary>
		/// <param name="activeChannels">The channels currently active for the tab.</param>
		/// <param name="name">The tab's display name.</param>
		/// <param name="position">Panel-space position to move the picker to.</param>
		public void Activate(HashSet<ChatChannel> activeChannels, string name, Vector3 position)
		{
			suppressCallbacks = true;
			foreach (KeyValuePair<ChatChannel, Toggle> pair in toggles)
			{
				pair.Value.value = activeChannels != null && activeChannels.Contains(pair.Key);
			}
			suppressCallbacks = false;

			if (nameInput != null)
			{
				nameInput.value = name;
			}

			if (panel != null)
			{
				panel.style.position = Position.Absolute;
				panel.style.left = position.x;
				panel.style.top = position.y;

				/* The panel is as tall as the channel list makes it, and Activate runs before
				 * layout, so its height cannot be measured here — resolvedStyle reports NaN
				 * until a layout pass has run. Clamp on the first geometry pass instead, once
				 * both the panel and its container have real sizes. */
				requestedPosition = position;
				panel.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
				panel.RegisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
			}
		}

		/// <summary>
		/// Keeps the panel fully on screen once its size is known.
		/// </summary>
		private void OnPanelGeometryChanged(GeometryChangedEvent evt)
		{
			if (panel == null || panel.parent == null)
			{
				return;
			}

			float containerWidth = panel.parent.contentRect.width;
			float containerHeight = panel.parent.contentRect.height;
			float panelWidth = panel.resolvedStyle.width;
			float panelHeight = panel.resolvedStyle.height;

			if (float.IsNaN(containerWidth) || float.IsNaN(containerHeight) ||
				float.IsNaN(panelWidth) || float.IsNaN(panelHeight) ||
				containerWidth <= 0.0f || containerHeight <= 0.0f)
			{
				return;
			}

			float left = Mathf.Clamp(requestedPosition.x, 0.0f, Mathf.Max(0.0f, containerWidth - panelWidth));
			float top = Mathf.Clamp(requestedPosition.y, 0.0f, Mathf.Max(0.0f, containerHeight - panelHeight));

			panel.style.left = left;
			panel.style.top = top;
		}

		/// <summary>
		/// Renames the current chat tab to the value in the rename field, reverting on failure.
		/// </summary>
		public void ChangeTabName()
		{
			if (nameInput == null || string.IsNullOrWhiteSpace(nameInput.value))
			{
				return;
			}

			if (UIManager.TryGetTK("UIChat", out UITKChat chat))
			{
				string currentName = chat.CurrentTab;
				if (!chat.RenameCurrentTab(nameInput.value))
				{
					nameInput.value = currentName;
				}
			}
		}

		/// <summary>
		/// Applies a channel toggle change to the current chat tab.
		/// </summary>
		/// <param name="channel">The channel being toggled.</param>
		/// <param name="value">Whether the channel should be active.</param>
		private void OnToggleChannel(ChatChannel channel, bool value)
		{
			if (suppressCallbacks)
			{
				return;
			}
			if (UIManager.TryGetTK("UIChat", out UITKChat chat))
			{
				chat.ToggleChannel(channel, value);
			}
		}
	}
}
