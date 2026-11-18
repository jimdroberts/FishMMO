using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// Named runtime switches modules offer the rendering test beds (the browser probe's SetSetting, the native player's
	/// --set-a / --set-b): "ambientLife=0" turns the Ambient Life module's animals off for an A/B, and the test bed names no
	/// module (Phase 4 of the core and modules plan, 2026-11-17).
	/// </summary>
	public static class RenderSwitches
	{
		private static readonly Dictionary<string, Action<string>> switches = new Dictionary<string, Action<string>>(StringComparer.Ordinal);

		/// <summary>Registers a switch; registering a name again replaces it.</summary>
		public static void Register(string name, Action<string> apply)
		{
			if (!string.IsNullOrEmpty(name) && apply != null)
			{
				switches[name] = apply;
			}
		}

		/// <summary>Applies a registered switch; false when no installed module offers <paramref name="name"/>.</summary>
		public static bool TryApply(string name, string value)
		{
			if (name == null || !switches.TryGetValue(name, out Action<string> apply))
			{
				return false;
			}
			apply(value);
			return true;
		}

		/// <summary>The registered switches' names.</summary>
		public static IEnumerable<string> Names => switches.Keys;
	}

	/// <summary>
	/// One-line work reports modules offer the rendering probes (ScenePerfProbe's report): "ambient life: …" from the Ambient
	/// Life module. The probe prints each registered report under its name and names no module.
	/// </summary>
	public static class RenderReports
	{
		private static readonly SortedDictionary<string, Func<string>> reports = new SortedDictionary<string, Func<string>>(StringComparer.Ordinal);

		/// <summary>Registers a report; registering a name again replaces it.</summary>
		public static void Register(string name, Func<string> describe)
		{
			if (!string.IsNullOrEmpty(name) && describe != null)
			{
				reports[name] = describe;
			}
		}

		/// <summary>The registered reports, by name.</summary>
		public static IEnumerable<KeyValuePair<string, Func<string>>> All => reports;
	}
}
