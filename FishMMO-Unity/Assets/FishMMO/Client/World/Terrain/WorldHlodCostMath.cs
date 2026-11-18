using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>Prunes clusters whose cost exceeds the model commands they can eliminate under global instancing.</summary>
	public static class WorldHlodCostMath
	{
		public readonly struct Cluster
		{
			public readonly IReadOnlyList<int> Models;
			public readonly int Commands;
			public Cluster(IReadOnlyList<int> models, int commands) { Models = models; Commands = commands; }
		}
		public sealed class Scratch
		{
			public bool[] Selected = Array.Empty<bool>();
			internal bool[] Complete = Array.Empty<bool>();
			internal int[] Loss = Array.Empty<int>();
			internal List<int>[] Users = Array.Empty<List<int>>();
			internal void Prepare(int models, int groups)
			{
				if (Selected.Length < groups) { Array.Resize(ref Selected, groups); Array.Resize(ref Loss, groups); }
				if (Complete.Length < models) { Array.Resize(ref Complete, models); int before = Users.Length; Array.Resize(ref Users, models); for (int i = before; i < models; i++) Users[i] = new List<int>(); }
				for (int i = 0; i < models; i++) Users[i].Clear();
			}
		}
		public static int Select(int[] visible, int[] replaced, int[] modelCommands, IReadOnlyList<Cluster> groups, Scratch scratch, out int avoided, out int proxies)
		{
			scratch.Prepare(visible.Length, groups.Count); avoided = proxies = 0;
			for (int m = 0; m < visible.Length; m++) { bool complete = visible[m] > 0 && visible[m] == replaced[m]; scratch.Complete[m] = complete; if (complete) avoided += modelCommands[m]; }
			for (int g = 0; g < groups.Count; g++)
			{
				scratch.Selected[g] = true; scratch.Loss[g] = 0; proxies += groups[g].Commands;
				foreach (int model in groups[g].Models) { scratch.Users[model].Add(g); if (scratch.Complete[model]) scratch.Loss[g] += modelCommands[model]; }
			}
			while (true)
			{
				int remove = -1, gain = 0;
				for (int g = 0; g < groups.Count; g++) if (scratch.Selected[g] && groups[g].Commands - scratch.Loss[g] > gain) { remove = g; gain = groups[g].Commands - scratch.Loss[g]; }
				if (remove < 0) break;
				scratch.Selected[remove] = false; proxies -= groups[remove].Commands;
				foreach (int model in groups[remove].Models)
				{
					if (!scratch.Complete[model]) continue;
					scratch.Complete[model] = false; avoided -= modelCommands[model];
					foreach (int user in scratch.Users[model]) scratch.Loss[user] -= modelCommands[model];
				}
			}
			if (proxies == 0 || avoided < proxies + 4) { Array.Clear(scratch.Selected, 0, groups.Count); return 0; }
			return avoided - proxies;
		}
	}
}
