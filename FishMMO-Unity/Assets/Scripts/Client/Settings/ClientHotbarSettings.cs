using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// How a game with more than one hotkey bar shows them: every bar at once, stacked above the
	/// first, or one bar at a time with pages.
	/// </summary>
	/// <remarks>
	/// <para><b>Whose choice is what.</b> How MANY bars there are is the game's decision
	/// (<see cref="Constants.Configuration.HotkeyBarCount"/>), because the server stores and
	/// validates every slot on every bar. How they are LAID OUT is the player's: both layouts drive
	/// the same slots with the same keys, so nothing about the choice reaches the server.</para>
	///
	/// <para><b>Why both layouts.</b> Stacked shows everything and costs screen: each extra row
	/// pushes the resource bars, the buff strips, the cast bar and the chat log's lower edge up by
	/// a row (see <see cref="UITKHudLayout"/>). Paged costs nothing on screen and shows one bar at a
	/// time; holding a bar's modifier shows that bar for as long as it is held, so what a key is
	/// about to fire is always what is on screen.</para>
	///
	/// <para>With a single bar neither layout changes anything, which is why the Options row is
	/// hidden then rather than offered as a choice between two identical results.</para>
	/// </remarks>
	public static class ClientHotbarSettings
	{
		/// <summary>The ways the hotkey bars can be arranged.</summary>
		/// <remarks>Stored as the ordinal, so the order here is part of the configuration format.</remarks>
		public enum HotbarLayout
		{
			/// <summary>Every bar on screen at once, the first at the bottom.</summary>
			Stacked = 0,

			/// <summary>One bar on screen, chosen with the page controls or by holding its modifier.</summary>
			Paged = 1,
		}

		/// <summary>Player-facing names of <see cref="HotbarLayout"/>, indexed by ordinal.</summary>
		public static readonly string[] LayoutLabels = { "Stacked", "Paged" };

		/// <summary>The layout a fresh install uses.</summary>
		/// <remarks>
		/// Stacked, because a game that ships several bars has presumably done so in order that
		/// they be seen; paging is the opt-in for a player who wants the screen back.
		/// </remarks>
		public const HotbarLayout DefaultLayout = HotbarLayout.Stacked;

		/// <summary>
		/// Raised when the layout changes.
		/// </summary>
		/// <remarks>
		/// The hotkey bar rebuilds its rows on it, and every panel that sits above the bar moves on
		/// it: the Options panel is a separate document, and a layout change that waited for the
		/// next scene load would leave the stacked rows drawn under the resource bars until then.
		/// </remarks>
		public static event Action OnChanged;

		/// <summary>Number of bars the game gives every character.</summary>
		public static int BarCount => Mathf.Max(1, Constants.Configuration.HotkeyBarCount);

		/// <summary>The chosen layout.</summary>
		public static HotbarLayout Layout
		{
			get
			{
				int stored = ClientSettings.GetInt(ClientSettings.HotbarLayoutKey, (int)DefaultLayout);
				if (stored < 0 || stored >= LayoutLabels.Length)
				{
					return DefaultLayout;
				}
				return (HotbarLayout)stored;
			}
		}

		/// <summary>
		/// How many bars are on screen at once: all of them when stacked, one when paged.
		/// </summary>
		public static int VisibleRows => Layout == HotbarLayout.Paged ? 1 : BarCount;

		/// <summary>
		/// The bar a paged hotbar shows while no modifier is held, as a zero-based bar index.
		/// </summary>
		/// <remarks>
		/// Remembered across sessions, like a window position: a player who keeps their gathering
		/// tools on the third page should not have to page back to it at every login. Clamped on
		/// read, so a count lowered by the game since it was saved lands on a bar that exists.
		/// </remarks>
		public static int Page
		{
			get
			{
				int stored = ClientSettings.GetInt(ClientSettings.HotbarPageKey, 0);
				return stored < 0 || stored >= BarCount ? 0 : stored;
			}
		}

		/// <summary>Writes the layout and notifies every panel that depends on it.</summary>
		public static void SetLayout(HotbarLayout value)
		{
			int ordinal = (int)value;
			if (ordinal < 0 || ordinal >= LayoutLabels.Length)
			{
				ordinal = (int)DefaultLayout;
			}

			ClientSettings.Set(ClientSettings.HotbarLayoutKey, ordinal);
			Raise();
		}

		/// <summary>Writes the resting page of a paged hotbar.</summary>
		/// <remarks>
		/// Raises nothing. The only writer is the hotkey bar itself, which repaints as it pages, and
		/// nothing else on screen depends on which bar a paged hotbar is showing — every row is the
		/// same height, so the panels above it stay where they are.
		/// </remarks>
		public static void SetPage(int page)
		{
			ClientSettings.Set(ClientSettings.HotbarPageKey, Mathf.Clamp(page, 0, BarCount - 1));
		}

		/// <summary>Notifies subscribers, reporting rather than propagating a handler's failure.</summary>
		private static void Raise()
		{
			try
			{
				OnChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientHotbarSettings", "A hotbar-settings subscriber threw.", ex);
			}
		}
	}
}
