using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// Input actions a module creates at run time, and the names the key bindings page shows for them. The Hotkeys module
	/// registers its extra bars' modifier and page actions here (2026-11-20); the core never names it.
	/// </summary>
	public static class ModuleInputActions
	{
		/// <summary>Tries to name an action the module created; false when it is not one of its.</summary>
		public delegate bool DisplayNameLookup(string actionName, out string displayName);

		private static readonly List<Action<PlayerControls>> adders = new List<Action<PlayerControls>>();
		private static readonly List<DisplayNameLookup> namers = new List<DisplayNameLookup>();

		/// <summary>
		/// Registers a module's actions: <paramref name="addActions"/> runs on the controls after the input asset is
		/// built and before the saved binding overrides are applied (an enabled map refuses new actions, and overrides
		/// only apply to bindings that exist). <paramref name="displayName"/> names them on the key bindings page.
		/// </summary>
		public static void Register(Action<PlayerControls> addActions, DisplayNameLookup displayName)
		{
			if (addActions != null && !adders.Contains(addActions))
			{
				adders.Add(addActions);
			}
			if (displayName != null && !namers.Contains(displayName))
			{
				namers.Add(displayName);
			}
		}

		/// <summary>Adds every module's actions to <paramref name="controls"/>.</summary>
		public static void AddActions(PlayerControls controls)
		{
			for (int i = 0; i < adders.Count; i++)
			{
				adders[i](controls);
			}
		}

		/// <summary>The display name a module gives one of its actions.</summary>
		public static bool TryGetDisplayName(string actionName, out string displayName)
		{
			for (int i = 0; i < namers.Count; i++)
			{
				if (namers[i](actionName, out displayName))
				{
					return true;
				}
			}
			displayName = null;
			return false;
		}
	}
}
