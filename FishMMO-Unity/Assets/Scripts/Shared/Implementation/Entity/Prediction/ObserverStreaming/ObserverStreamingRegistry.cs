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

		/// <summary>
		/// Per viewer, the visibility rank of each character it could observe: object id to rank,
		/// rank 0 being the most relevant. Rebuilt every pass and read by
		/// <c>ObserverBudgetCondition</c>. Inner dictionaries are reused rather than reallocated.
		/// </summary>
		private static readonly Dictionary<int, Dictionary<int, int>> ranksByClientId = new Dictionary<int, Dictionary<int, int>>();

		/// <summary>Viewers that produced a ranking on the most recent pass.</summary>
		private static readonly HashSet<int> rankedClientIds = new HashSet<int>();

		/// <summary>Per viewer, characters pinned into the budget regardless of rank.</summary>
		private static readonly Dictionary<int, HashSet<int>> pinnedByClientId = new Dictionary<int, HashSet<int>>();
		private static TimeManager timeManager;
		private static uint nextPassTick;

		private struct Candidate
		{
			public ObserverStreamingEntry Entry;
			public float Score;
			public float Distance;
			/// <summary>Never evicted by the visibility budget; sorts ahead of everything else.</summary>
			public bool Pinned;
			/// <summary>Inside the viewer's engagement radius, so a candidate for full-rate updates.</summary>
			public bool Engaged;
		}

		/// <summary>Number of characters currently registered.</summary>
		public static int Count => entries.Count;

		/// <summary>Viewers ranked in the last pass.</summary>
		public static int LastPassViewers { get; private set; }

		/// <summary>(viewer, observed) pairs rate limited in the last pass.</summary>
		public static int LastPassLimitedPairs { get; private set; }

		/// <summary>Characters whose observer range was changed in the last pass.</summary>
		public static int LastPassRangeChanges { get; private set; }

		/// <summary>Characters pinned into a visibility budget in the last pass.</summary>
		public static int LastPassPinned { get; private set; }

		/// <summary>(viewer, observed) pairs excluded by the visibility budget in the last pass.</summary>
		public static int LastPassBudgetExcluded { get; private set; }

		/// <summary>
		/// Whether a character is inside a viewer's visibility budget.
		/// </summary>
		/// <remarks>
		/// The budget condition's whole implementation. <paramref name="hasRanking"/> distinguishes
		/// "ranked and excluded" from "no pass has run for this viewer yet" — the caller must not
		/// treat the second as a rejection, or everything stays hidden until the first pass lands.
		/// </remarks>
		/// <param name="viewerClientId">Client id of the viewing connection.</param>
		/// <param name="observedObjectId">Object id of the character being tested.</param>
		/// <param name="currentlyVisible">True when the viewer already observes it; widens the budget by the hysteresis.</param>
		/// <param name="hasRanking">False when this viewer has no ranking yet.</param>
		/// <returns>True when the character is inside the budget.</returns>
		public static bool IsWithinVisibilityBudget(int viewerClientId, int observedObjectId, bool currentlyVisible, out bool hasRanking)
		{
			hasRanking = rankedClientIds.Contains(viewerClientId);
			if (!hasRanking)
			{
				return false;
			}

			if (pinnedByClientId.TryGetValue(viewerClientId, out HashSet<int> pinned) &&
				pinned.Contains(observedObjectId))
			{
				return true;
			}

			if (!ranksByClientId.TryGetValue(viewerClientId, out Dictionary<int, int> ranks) ||
				!ranks.TryGetValue(observedObjectId, out int rank))
			{
				/* Ranked viewer, unranked object: out of range on this pass. Not the budget's
				 * business — the distance condition has already decided that, and rejecting here
				 * would be a second vote on the same question. */
				return true;
			}

			int budget = ObserverStreamingPolicy.VisibilityBudget;
			if (budget <= 0)
			{
				return true;
			}
			if (currentlyVisible)
			{
				budget = Mathf.CeilToInt(budget * (1f + ObserverStreamingPolicy.VisibilityBudgetHysteresis));
			}
			return rank < budget;
		}

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
		/// <summary>Reusable rank map for one viewer, cleared rather than reallocated.</summary>
		private static Dictionary<int, int> RanksFor(int clientId)
		{
			if (!ranksByClientId.TryGetValue(clientId, out Dictionary<int, int> ranks))
			{
				ranks = new Dictionary<int, int>();
				ranksByClientId[clientId] = ranks;
			}
			ranks.Clear();
			return ranks;
		}

		/// <summary>Reusable pin set for one viewer, cleared rather than reallocated.</summary>
		private static HashSet<int> PinsFor(int clientId)
		{
			if (!pinnedByClientId.TryGetValue(clientId, out HashSet<int> pins))
			{
				pins = new HashSet<int>();
				pinnedByClientId[clientId] = pins;
			}
			pins.Clear();
			return pins;
		}

		public static void Clear()
		{
			for (int i = entries.Count - 1; i >= 0; --i)
			{
				Unregister(entries[i].NetworkObject);
			}
			ranksByClientId.Clear();
			rankedClientIds.Clear();
			pinnedByClientId.Clear();
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
			LastPassPinned = 0;
			LastPassBudgetExcluded = 0;
			rankedClientIds.Clear();

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
			int rateCap = ObserverStreamingPolicy.FullRateObserverCap;
			int engagedBudget = ObserverStreamingPolicy.EngagedFullRateBudget;
			byte engagedOverflow = ObserverStreamingPolicy.EngagedOverflowInterval;
			int visibilityBudget = ObserverStreamingPolicy.VisibilityBudget;

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
				float engagementRange = ObserverStreamingPolicy.ResolveEngagementRange(viewer.LongestAbilityRange);
				long viewerTargetId = viewer.CurrentTargetObjectId;

				Dictionary<int, int> ranks = RanksFor(viewerConnection.ClientId);
				HashSet<int> pins = PinsFor(viewerConnection.ClientId);

				for (int o = 0; o < sceneEntries.Count; ++o)
				{
					ObserverStreamingEntry observed = sceneEntries[o];
					if (ReferenceEquals(observed, viewer))
					{
						continue;
					}

					/* Candidacy by DISTANCE, not by current observer membership.
					 *
					 * Ranking only what the viewer already observes would deadlock the visibility
					 * budget: a character that is not yet an observer would never be ranked, the
					 * budget condition would never admit it, and it could never become an observer.
					 * The viewer's applied range is the same one the distance condition is using,
					 * so this ranks exactly the set that condition can admit. */
					float distance = Vector3.Distance(viewer.Position, observed.Position);
					if (distance > viewerRange)
					{
						continue;
					}

					bool sameParty = viewer.PartyID != 0 && viewer.PartyID == observed.PartyID;
					bool sameGuild = viewer.GuildID != 0 && viewer.GuildID == observed.GuildID;

					/* Pins: never evicted, whatever the crowd.
					 *
					 * Party members inside the ability ceiling, because a group that cannot see each
					 * other cannot play together — and the character the viewer currently targets,
					 * because losing your target mid-fight is the worst possible moment for a
					 * despawn. Pins still occupy budget slots, so a full party in a crowd simply
					 * leaves fewer slots for strangers. */
					bool pinned = (sameParty && distance <= ObserverStreamingPolicy.EngagementRangeCeiling) ||
						(viewerTargetId != 0 && observed.NetworkObject.ObjectId == viewerTargetId);
					if (pinned)
					{
						pins.Add(observed.NetworkObject.ObjectId);
						LastPassPinned++;
					}

					candidates.Add(new Candidate
					{
						Entry = observed,
						Score = ObserverStreamingPolicy.Score(observed.InCombat, sameParty, sameGuild, distance, viewerRange),
						Distance = distance,
						Pinned = pinned,
						Engaged = distance <= engagementRange,
					});
				}

				candidates.Sort(CompareCandidates);

				int engagedSoFar = 0;
				for (int i = 0; i < candidates.Count; ++i)
				{
					Candidate candidate = candidates[i];
					ranks[candidate.Entry.NetworkObject.ObjectId] = i;

					if (visibilityBudget > 0 && !candidate.Pinned && i >= visibilityBudget)
					{
						LastPassBudgetExcluded++;
					}

					/* Rate limiting, in one place, most relevant first.
					 *
					 * An engaged character keeps every tick while the engaged budget lasts, then
					 * falls to the overflow interval rather than to its distance band — it is still
					 * close enough to be shot at, and one tick of compensation error is a different
					 * thing from six. Everything else follows the relevance cap as before. */
					if (candidate.Engaged)
					{
						if (engagedSoFar < engagedBudget)
						{
							engagedSoFar++;
							continue;
						}
						if (engagedOverflow > 1)
						{
							candidate.Entry.SetInterval(viewerConnection, engagedOverflow);
							LastPassLimitedPairs++;
						}
						continue;
					}

					if (i >= rateCap)
					{
						byte interval = ObserverStreamingPolicy.LodInterval(candidate.Distance);
						if (interval > 1)
						{
							candidate.Entry.SetInterval(viewerConnection, interval);
							LastPassLimitedPairs++;
						}
					}
				}

				rankedClientIds.Add(viewerConnection.ClientId);
			}
		}

		private static int CompareCandidates(Candidate a, Candidate b)
		{
			// Pins occupy the lowest ranks so they can never be pushed out by a crowd.
			if (a.Pinned != b.Pinned)
			{
				return a.Pinned ? -1 : 1;
			}
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
