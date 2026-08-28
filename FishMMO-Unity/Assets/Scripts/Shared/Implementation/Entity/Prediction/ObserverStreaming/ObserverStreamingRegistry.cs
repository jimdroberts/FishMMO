using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server-side scheduler that applies <see cref="ObserverStreamingPolicy"/> to every
	/// registered character: scales each one's observer range by local density, and for every
	/// viewing client ranks what it can see and rate limits everything beyond the cap.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Characters register from <c>CharacterPredictionController.OnStartServer</c>, so this covers
	/// exactly the predicted characters — players, monsters, pets — whose NetworkTransform and
	/// other per-tick traffic is what an observer's bandwidth is spent on. Interactables and
	/// world items are static and keep their prefab conditions untouched.
	/// </para>
	/// <para>
	/// Runs one pass every <see cref="ObserverStreamingPolicy.RescheduleIntervalTicks"/> ticks
	/// off <see cref="TimeManager.OnPostTick"/>. A pass is O(entries) for density (uniform grid)
	/// plus O(viewers × entries) for ranking, with entries grouped by Unity scene so characters
	/// in different scenes never compare positions. At 200 players and 700 characters that is
	/// ~140k score evaluations per half second, all field reads.
	/// </para>
	/// </remarks>
	public static class ObserverStreamingRegistry
	{
		private const string LogTag = "ObserverStreaming";

		private static readonly Dictionary<NetworkObject, ObserverStreamingEntry> entriesByObject = new Dictionary<NetworkObject, ObserverStreamingEntry>();
		private static readonly List<ObserverStreamingEntry> entries = new List<ObserverStreamingEntry>();
		private static readonly Dictionary<int, List<ObserverStreamingEntry>> entriesByScene = new Dictionary<int, List<ObserverStreamingEntry>>();
		private static readonly Dictionary<long, int> cellCounts = new Dictionary<long, int>();
		private static readonly List<Candidate> candidates = new List<Candidate>();
		private static TimeManager timeManager;
		private static uint nextPassTick;

		private struct Candidate
		{
			public ObserverStreamingEntry Entry;
			public float Score;
			public float Distance;
		}

		/// <summary>Number of characters currently registered.</summary>
		public static int Count => entries.Count;

		/// <summary>Viewers ranked in the last pass.</summary>
		public static int LastPassViewers { get; private set; }

		/// <summary>(viewer, observed) pairs rate limited in the last pass.</summary>
		public static int LastPassLimitedPairs { get; private set; }

		/// <summary>Characters whose observer range was changed in the last pass.</summary>
		public static int LastPassRangeChanges { get; private set; }

		/// <summary>
		/// Registers a character. Installs the entry as the object's observer send filter.
		/// </summary>
		public static ObserverStreamingEntry Register(NetworkObject networkObject, ICharacter character)
		{
			if (networkObject == null || character == null)
			{
				return null;
			}
			if (entriesByObject.TryGetValue(networkObject, out ObserverStreamingEntry existing))
			{
				return existing;
			}

			ObserverStreamingEntry entry = new ObserverStreamingEntry(networkObject, character);
			entriesByObject[networkObject] = entry;
			entries.Add(entry);
			networkObject.ObserverSendFilter = entry;

			if (timeManager == null && networkObject.TimeManager != null)
			{
				timeManager = networkObject.TimeManager;
				timeManager.OnPostTick += OnPostTick;
				nextPassTick = timeManager.LocalTick;
			}
			return entry;
		}

		/// <summary>Unregisters a character, restoring its prefab range and removing the send filter.</summary>
		public static void Unregister(NetworkObject networkObject)
		{
			if (networkObject == null || !entriesByObject.TryGetValue(networkObject, out ObserverStreamingEntry entry))
			{
				return;
			}
			entriesByObject.Remove(networkObject);
			entries.Remove(entry);
			entry.ClearIntervals();
			entry.ApplyRange(entry.BaseRange);
			if (ReferenceEquals(networkObject.ObserverSendFilter, entry))
			{
				networkObject.ObserverSendFilter = null;
			}

			if (entries.Count == 0 && timeManager != null)
			{
				timeManager.OnPostTick -= OnPostTick;
				timeManager = null;
			}
		}

		/// <summary>Returns the entry for a registered object, or null.</summary>
		public static ObserverStreamingEntry Get(NetworkObject networkObject)
		{
			return networkObject != null && entriesByObject.TryGetValue(networkObject, out ObserverStreamingEntry entry) ? entry : null;
		}

		/// <summary>Drops every registration. For tests and server shutdown.</summary>
		public static void Clear()
		{
			for (int i = entries.Count - 1; i >= 0; --i)
			{
				Unregister(entries[i].NetworkObject);
			}
		}

		private static void OnPostTick()
		{
			if (timeManager == null)
			{
				return;
			}
			uint tick = timeManager.LocalTick;
			if ((int)(tick - nextPassTick) < 0)
			{
				return;
			}
			nextPassTick = tick + ObserverStreamingPolicy.RescheduleIntervalTicks;
			RunPass();
		}

		/// <summary>
		/// Runs one scheduling pass over every registered character. Public so a server can
		/// force a pass after a bulk change (a load boundary) and so tests can drive it.
		/// </summary>
		public static void RunPass()
		{
			LastPassViewers = 0;
			LastPassLimitedPairs = 0;
			LastPassRangeChanges = 0;

			// Prune destroyed objects and refresh cached inputs.
			for (int i = entries.Count - 1; i >= 0; --i)
			{
				ObserverStreamingEntry entry = entries[i];
				if (entry.NetworkObject == null || !entry.NetworkObject.IsSpawned)
				{
					entriesByObject.Remove(entry.NetworkObject);
					entries.RemoveAt(i);
					continue;
				}
				entry.RefreshForPass();
				entry.ClearIntervals();
			}

			// Group by scene: cross-scene distances are meaningless.
			foreach (List<ObserverStreamingEntry> list in entriesByScene.Values)
			{
				list.Clear();
			}
			for (int i = 0; i < entries.Count; ++i)
			{
				int sceneHandle = entries[i].NetworkObject.gameObject.scene.handle;
				if (!entriesByScene.TryGetValue(sceneHandle, out List<ObserverStreamingEntry> list))
				{
					list = new List<ObserverStreamingEntry>();
					entriesByScene[sceneHandle] = list;
				}
				list.Add(entries[i]);
			}

			foreach (List<ObserverStreamingEntry> sceneEntries in entriesByScene.Values)
			{
				if (sceneEntries.Count == 0)
				{
					continue;
				}
				ApplyDensityRanges(sceneEntries);
				RankForViewers(sceneEntries);
			}
		}

		/// <summary>
		/// Counts neighbours within <see cref="ObserverStreamingPolicy.DensityRadius"/> with a
		/// uniform grid (3×3 cells of that size is a conservative superset of the radius) and
		/// applies the scaled range to each character.
		/// </summary>
		private static void ApplyDensityRanges(List<ObserverStreamingEntry> sceneEntries)
		{
			float cellSize = Mathf.Max(1f, ObserverStreamingPolicy.DensityRadius);
			cellCounts.Clear();
			for (int i = 0; i < sceneEntries.Count; ++i)
			{
				long key = CellKey(sceneEntries[i].Position, cellSize);
				cellCounts.TryGetValue(key, out int count);
				cellCounts[key] = count + 1;
			}

			for (int i = 0; i < sceneEntries.Count; ++i)
			{
				ObserverStreamingEntry entry = sceneEntries[i];
				if (!entry.HasDistanceCondition)
				{
					continue;
				}

				int cx = Mathf.FloorToInt(entry.Position.x / cellSize);
				int cz = Mathf.FloorToInt(entry.Position.z / cellSize);
				int neighbours = -1; // exclude self
				for (int dx = -1; dx <= 1; ++dx)
				{
					for (int dz = -1; dz <= 1; ++dz)
					{
						if (cellCounts.TryGetValue(CellKey(cx + dx, cz + dz), out int count))
						{
							neighbours += count;
						}
					}
				}

				float range = ObserverStreamingPolicy.ScaledRange(entry.BaseRange, Mathf.Max(0, neighbours));
				if (entry.ApplyRange(range))
				{
					LastPassRangeChanges++;
				}
			}
		}

		/// <summary>
		/// For every player in the scene, ranks the characters it observes and rate limits all
		/// but the top <see cref="ObserverStreamingPolicy.FullRateObserverCap"/>.
		/// </summary>
		private static void RankForViewers(List<ObserverStreamingEntry> sceneEntries)
		{
			int cap = ObserverStreamingPolicy.FullRateObserverCap;

			for (int v = 0; v < sceneEntries.Count; ++v)
			{
				ObserverStreamingEntry viewer = sceneEntries[v];
				if (!viewer.IsPlayer)
				{
					continue;
				}
				NetworkConnection viewerConnection = viewer.NetworkObject.Owner;
				if (viewerConnection == null || !viewerConnection.IsValid)
				{
					continue;
				}

				LastPassViewers++;
				candidates.Clear();
				float viewerRange = viewer.AppliedRange > 0f ? viewer.AppliedRange : ObserverStreamingPolicy.DensityRadius;

				for (int o = 0; o < sceneEntries.Count; ++o)
				{
					ObserverStreamingEntry observed = sceneEntries[o];
					if (ReferenceEquals(observed, viewer) || !observed.NetworkObject.Observers.Contains(viewerConnection))
					{
						continue;
					}

					float distance = Vector3.Distance(viewer.Position, observed.Position);
					bool sameParty = viewer.PartyID != 0 && viewer.PartyID == observed.PartyID;
					bool sameGuild = viewer.GuildID != 0 && viewer.GuildID == observed.GuildID;
					candidates.Add(new Candidate
					{
						Entry = observed,
						Score = ObserverStreamingPolicy.Score(observed.InCombat, sameParty, sameGuild, distance, viewerRange),
						Distance = distance,
					});
				}

				if (candidates.Count <= cap)
				{
					continue;
				}

				candidates.Sort(CompareCandidates);
				for (int i = cap; i < candidates.Count; ++i)
				{
					byte interval = ObserverStreamingPolicy.LodInterval(candidates[i].Distance);
					if (interval > 1)
					{
						candidates[i].Entry.SetInterval(viewerConnection, interval);
						LastPassLimitedPairs++;
					}
				}
			}
		}

		private static int CompareCandidates(Candidate a, Candidate b)
		{
			int byScore = b.Score.CompareTo(a.Score);
			if (byScore != 0)
			{
				return byScore;
			}
			int byDistance = a.Distance.CompareTo(b.Distance);
			if (byDistance != 0)
			{
				return byDistance;
			}
			// Deterministic tie-break so the same set produces the same cap membership each pass.
			return a.Entry.NetworkObject.ObjectId.CompareTo(b.Entry.NetworkObject.ObjectId);
		}

		private static long CellKey(Vector3 position, float cellSize)
		{
			return CellKey(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));
		}

		private static long CellKey(int cx, int cz)
		{
			return ((long)cx << 32) ^ (uint)cz;
		}

		/// <summary>Logs a one-line summary of the last pass. For diagnostics.</summary>
		public static void LogSummary()
		{
			Log.Debug(LogTag,
				$"registered={entries.Count} viewers={LastPassViewers} limitedPairs={LastPassLimitedPairs} rangeChanges={LastPassRangeChanges}");
		}
	}
}
