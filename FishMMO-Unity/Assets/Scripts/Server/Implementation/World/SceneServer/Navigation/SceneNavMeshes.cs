using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.Server.Implementation.World.SceneServer.Navigation
{
	/// <summary>
	/// Adds a world scene's NavMesh while any instance of the scene is loaded, from <see cref="SceneNavMeshCatalogue"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Once per scene, not per instance.</b> The Unity NavMesh is global, not per physics scene, and a scene
	/// server can run several stacked instances of one world scene at the same coordinates. Their NavMeshes would be
	/// identical and overlap exactly, so the first instance adds it and the last one to unload removes it.
	/// </para>
	/// <para>
	/// <b>And its climbables' links.</b> Each of the scene's ladders, rope ladders and ropes
	/// (<see cref="ScenePointsOfInterest.Climbables"/>) becomes a two-way link from its foot on the lower floor to its
	/// landing on the upper one (<see cref="ClimbingRules.LinkEnds"/>), in the "Climb" area so only archetypes that climb
	/// path over them (<see cref="AIController.ClimbArea"/>). Links are global like the NavMesh, so they too are added
	/// once per scene and go with it. Made at load from the same list the players' climbing reads, so the two never
	/// disagree and no bake is needed for them.
	/// </para>
	/// <para>
	/// A plain class rather than part of <see cref="NavMeshSystem"/>, so tests can drive it without FishNet.
	/// </para>
	/// </remarks>
	public sealed class SceneNavMeshes
	{
		private sealed class Loaded
		{
			public NavMeshDataInstance Instance;
			public int Count;
			public readonly List<NavMeshLinkInstance> Links = new List<NavMeshLinkInstance>();
		}

		private readonly Dictionary<string, Loaded> byScene = new Dictionary<string, Loaded>();

		/// <summary>Where the NavMeshes come from.</summary>
		public SceneNavMeshCatalogue Catalogue { get; set; }

		public SceneNavMeshes(SceneNavMeshCatalogue catalogue)
		{
			Catalogue = catalogue;
		}

		/// <summary>True while <paramref name="sceneName"/>'s NavMesh is in the world.</summary>
		public bool IsLoaded(string sceneName) => sceneName != null && byScene.ContainsKey(sceneName);

		/// <summary>How many scenes' NavMeshes are in the world.</summary>
		public int Count => byScene.Count;

		/// <summary>How many climb links <paramref name="sceneName"/>'s NavMesh has in the world.</summary>
		public int LinkCount(string sceneName) => sceneName != null && byScene.TryGetValue(sceneName, out Loaded loaded) ? loaded.Links.Count : 0;

		/// <summary>
		/// An instance of <paramref name="sceneName"/> loaded, with its <paramref name="climbables"/> (null for none): the
		/// first instance adds the NavMesh and links it. Returns false when the scene has no NavMesh.
		/// </summary>
		public bool Add(string sceneName, IReadOnlyList<SceneClimbable> climbables = null)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return false;
			}
			if (byScene.TryGetValue(sceneName, out Loaded loaded))
			{
				loaded.Count++;
				return true;
			}
			if (Catalogue == null || !Catalogue.TryGet(sceneName, out SceneNavMeshCatalogue.Entry entry))
			{
				return false;
			}
			NavMeshDataInstance instance = NavMesh.AddNavMeshData(entry.Data, entry.Position, entry.Rotation);
			if (!instance.valid)
			{
				return false;
			}
			var added = new Loaded { Instance = instance, Count = 1 };
			AddClimbLinks(climbables, added.Links);
			byScene[sceneName] = added;
			return true;
		}

		/// <summary>One two-way link per valid climbable, for every agent type the project has.</summary>
		private static void AddClimbLinks(IReadOnlyList<SceneClimbable> climbables, List<NavMeshLinkInstance> into)
		{
			if (climbables == null || climbables.Count == 0)
			{
				return;
			}
			int area = AIController.ClimbArea;
			int agentTypes = NavMesh.GetSettingsCount();
			for (int i = 0; i < climbables.Count && i < SceneClimbables.MaxPerScene; i++)
			{
				SceneClimbable climbable = climbables[i];
				if (!ClimbingRules.IsValid(in climbable))
				{
					continue;
				}
				ClimbingRules.LinkEnds(in climbable, out Vector3 lower, out Vector3 upper);
				for (int a = 0; a < agentTypes; a++)
				{
					NavMeshLinkInstance link = NavMesh.AddLink(LinkFor(lower, upper, NavMesh.GetSettingsByIndex(a).agentTypeID, area));
					if (NavMesh.IsLinkValid(link))
					{
						into.Add(link);
					}
				}
			}
		}

		/// <summary>The link a climbable is given: a point at each end, both ways, costed by its area (the "Climb" area when the project has one).</summary>
		public static NavMeshLinkData LinkFor(Vector3 lower, Vector3 upper, int agentTypeID, int area)
		{
			return new NavMeshLinkData
			{
				startPosition = lower,
				endPosition = upper,
				width = 0f,
				costModifier = -1f,
				bidirectional = true,
				area = area >= 0 ? area : 0,
				agentTypeID = agentTypeID,
			};
		}

		private static void RemoveLinks(Loaded loaded)
		{
			for (int i = 0; i < loaded.Links.Count; i++)
			{
				if (NavMesh.IsLinkValid(loaded.Links[i]))
				{
					NavMesh.RemoveLink(loaded.Links[i]);
				}
			}
			loaded.Links.Clear();
		}

		/// <summary>An instance of <paramref name="sceneName"/> unloaded; the last one takes its NavMesh out.</summary>
		public void Remove(string sceneName)
		{
			if (sceneName == null || !byScene.TryGetValue(sceneName, out Loaded loaded))
			{
				return;
			}
			if (--loaded.Count > 0)
			{
				return;
			}
			RemoveLinks(loaded);
			loaded.Instance.Remove();
			byScene.Remove(sceneName);
		}

		/// <summary>Takes every NavMesh out.</summary>
		public void Clear()
		{
			foreach (Loaded loaded in byScene.Values)
			{
				RemoveLinks(loaded);
				loaded.Instance.Remove();
			}
			byScene.Clear();
		}
	}
}
