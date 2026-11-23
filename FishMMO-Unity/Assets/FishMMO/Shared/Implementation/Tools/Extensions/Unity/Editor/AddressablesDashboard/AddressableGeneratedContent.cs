#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Generated content modules tell the Addressables dashboard about: the
	/// paths each machine regenerates and builds into its own groups (the World Design module's procedural art and planet
	/// surfaces), and the clean-up that keeps them out of the committed groups. The dashboard names no module.
	/// </summary>
	public static class AddressableGeneratedContent
	{
		private static readonly SortedDictionary<string, Func<string, bool>> generated = new SortedDictionary<string, Func<string, bool>>(StringComparer.Ordinal);
		private static readonly SortedDictionary<string, Func<List<string>, int>> cleanups = new SortedDictionary<string, Func<List<string>, int>>(StringComparer.Ordinal);

		/// <summary>Registers what a module generates: true for a path of its generated content. Registering an id again replaces it.</summary>
		public static void RegisterGenerated(string id, Func<string, bool> isGenerated)
		{
			if (!string.IsNullOrEmpty(id) && isGenerated != null)
			{
				generated[id] = isGenerated;
			}
		}

		/// <summary>Registers a clean-up that takes a module's generated entries out of the committed groups; it returns how many.</summary>
		public static void RegisterCommittedCleanup(string id, Func<List<string>, int> removeCommittedEntries)
		{
			if (!string.IsNullOrEmpty(id) && removeCommittedEntries != null)
			{
				cleanups[id] = removeCommittedEntries;
			}
		}

		/// <summary>True for a path some installed module generates.</summary>
		public static bool IsGenerated(string path)
		{
			foreach (Func<string, bool> isGenerated in generated.Values)
			{
				if (isGenerated(path))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Runs every registered clean-up; the entries removed in all.</summary>
		public static int RemoveCommittedEntries(List<string> log)
		{
			int removed = 0;
			foreach (Func<List<string>, int> cleanup in cleanups.Values)
			{
				removed += cleanup(log);
			}
			return removed;
		}
	}
}
#endif
