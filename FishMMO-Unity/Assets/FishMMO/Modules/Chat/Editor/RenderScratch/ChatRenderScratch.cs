using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The Chat module's part of the panel render rig: the chat window, filled with sample lines, in the all-panels render,
	/// the interface scaling render and the HUD overlap probe (moved out of the core rig with the module, 2026-11-20).
	/// </summary>
	public static class ChatRenderScratch
	{
		/// <summary>The chat window's layout.</summary>
		public const string Uxml = "Assets/FishMMO/Modules/Chat/Client/UIChat.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			AllPanelsRender.ExtraPopulators["UIChat"] = Window;
			if (!InterfaceScalingRender.Hud.Exists(m => m.Uxml == Uxml))
			{
				InterfaceScalingRender.Hud.Add(new InterfaceScalingRender.HudMount { Uxml = Uxml, PanelType = typeof(UITKChat) });
			}
			if (!HudOverlapProbe.Mounts.Exists(m => m.Name == "UIChat"))
			{
				// Before the pet control, where it always stood.
				int pet = HudOverlapProbe.Mounts.FindIndex(m => m.Name == "UIPetControl");
				HudOverlapProbe.Mounts.Insert(pet < 0 ? HudOverlapProbe.Mounts.Count : pet,
					new HudOverlapProbe.Mount { Name = "UIChat", Uxml = Uxml, Selector = "chat-root", PanelType = typeof(UITKChat) });
			}
		}

		public static void Window(GameObject h, UIDocument d)
		{
			UITKChat c = h.AddComponent<UITKChat>();
			c.Document = d;
			c.OnStarting();

			MethodInfo received = typeof(UITKChat).GetMethod("OnClientChatBroadcastReceived",
				BindingFlags.NonPublic | BindingFlags.Instance);

			(ChatChannel ch, long sender, string text)[] lines =
			{
				(ChatChannel.Say,     1002, "Anyone up for the Sunken Cathedral run?"),
				(ChatChannel.Party,   1001, "Give me two minutes, repairing."),
				(ChatChannel.Guild,   1003, "Raid forms at 19:00 — read the notice."),
				(ChatChannel.Say,     1004, "Where does the tide-warding charm drop?"),
				(ChatChannel.Guild,   1005, "Reef wing, second boss. Rare-ish."),
				(ChatChannel.Tell,    1002, "Invite me when you're ready?"),
				(ChatChannel.World,   1008, "Selling Emberbrand scrolls, whisper me."),
				(ChatChannel.Party,   1001, "Ready. Pulling."),
			};

			long now = DateTime.UtcNow.Ticks;
			for (int i = 0; i < lines.Length; ++i)
			{
				ChatBroadcast msg = new ChatBroadcast
				{
					Channel = lines[i].ch,
					SenderID = lines[i].sender,
					Text = lines[i].text,
					ReceivedUtcTicks = now - (lines[i].Length() * 0),
				};
				if (received != null)
				{
					/* The second parameter is FishNet's Channel enum. Its default is built from the
					 * signature so this assembly needs no FishNet reference. */
					Type channelType = received.GetParameters()[1].ParameterType;
					object channel = channelType.IsValueType ? Activator.CreateInstance(channelType) : null;
					received.Invoke(c, new object[] { msg, channel });
				}
			}
		}

		private static int Length(this (ChatChannel ch, long sender, string text) t) => 0;
	}
}
