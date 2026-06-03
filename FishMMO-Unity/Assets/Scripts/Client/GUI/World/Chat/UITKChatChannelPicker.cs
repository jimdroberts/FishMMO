using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit chat channel picker. Lets the player toggle which chat channels a tab listens to
	/// and rename the current tab. Mirrors the legacy UGUI <c>UIChatChannelPicker</c>.
	/// </summary>
	public class UITKChatChannelPicker : UITKControl
	{
		private const string PANEL_NAME = "channel-picker-panel";
		private const string CHANNEL_LIST_NAME = "channel-list";
		private const string NAME_INPUT_NAME = "channel-name-input";

		private VisualElement panel;
		private VisualElement channelList;
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
		/// Builds a toggle per chat channel (except Command) and wires the rename input.
		/// </summary>
		public override void OnStarting()
		{
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
					toggle.AddToClassList("channel-toggle");
					toggle.RegisterValueChangedCallback((evt) => OnToggleChannel(channel, evt.newValue));
					channelList.Add(toggle);
					toggles[channel] = toggle;
				}
			}

			if (nameInput != null)
			{
				nameInput.RegisterCallback<KeyDownEvent>((evt) =>
				{
					if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
					{
						ChangeTabName();
					}
				});
			}

			Root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
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
			}
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
