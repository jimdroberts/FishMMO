using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Addressables labels a module asks the server to load at boot, beside the core's own (Phase 4 of the core and modules plan,
	/// 2026-11-18): the World Simulation module's biome templates. The server launcher preloads the core labels and then these;
	/// a module that is not installed asks for nothing, so a core-only server preloads no label it does not have.
	/// </summary>
	public static class ServerPreloadLabels
	{
		private static readonly List<string> labels = new List<string>();

		/// <summary>The registered labels, in registration order.</summary>
		public static IReadOnlyList<string> All => labels;

		/// <summary>Asks for a label at the server's boot; asking again for the same label changes nothing.</summary>
		public static void Register(string label)
		{
			if (!string.IsNullOrEmpty(label) && !labels.Contains(label))
			{
				labels.Add(label);
			}
		}
	}
}
