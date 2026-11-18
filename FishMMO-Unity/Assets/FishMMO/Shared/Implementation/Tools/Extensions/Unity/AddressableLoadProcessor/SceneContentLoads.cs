using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// The content a scene fetches for itself after it has loaded: its terrain arrays, biome templates, river flow and
	/// the like, each an Addressable load a component starts from its own lifecycle. Those run outside the scene load and
	/// outside <see cref="AddressableLoadProcessor"/>'s batches, so nothing that waits on either sees them.
	/// The client's loading screen holds world entry until this is empty, so the player arrives in a finished world
	/// instead of watching ground and water arrive from the cache or the web server.
	/// </summary>
	/// <remarks>
	/// A load is a token: <see cref="Begin"/> when the fetch starts, <see cref="Load.End"/> on every way it can finish
	/// (success, failure, the owner disabled or destroyed). End is idempotent, so an owner can call it from both its
	/// completion callback and its teardown without counting twice. A forgotten End holds the overlay only until the
	/// loading screen's own deadline, never forever.
	/// <para>
	/// <b>Work.</b> Content that has arrived is not finished: a biome registering makes the grass and scatter take their
	/// terrains again, and props merge into pools, each a few milliseconds a frame. A system with such work adds a
	/// <see cref="Work"/> probe once (<see cref="AddWork"/>); the loading screen keeps the world covered while any probe says
	/// it is busy, and while it does (<see cref="Covered"/>) those systems spend <see cref="Budget"/> a frame instead of
	/// their usual slice, so the work finishes behind the overlay rather than as hitches on the screen after it.
	/// </para>
	/// <para>
	/// <b>Progress.</b> Loads begun while others are in flight are one wave: the bar reads the wave's finished loads plus
	/// the running ones' progress over all it has begun, so it only moves forward as loads join and end.
	/// </para>
	/// </remarks>
	public static class SceneContentLoads
	{
		/// <summary>One fetch in flight.</summary>
		public sealed class Load
		{
			/// <summary>What is loading, for the overlay's hint and for the timeout warning.</summary>
			public readonly string What;
			private AsyncOperationHandle progressFrom;
			internal bool Ended;

			internal Load(string what)
			{
				What = what;
			}

			/// <summary>
			/// The operation whose download progress this load reports. Optional: without one the load counts as
			/// not started until it ends.
			/// </summary>
			public void Track(AsyncOperationHandle handle)
			{
				progressFrom = handle;
			}

			/// <summary>
			/// 0 to 1: the tracked operation's download progress, else 0. Never goes down: an operation reports its own
			/// completion until the download's size is known and then the bytes, so its raw reading drops when the download
			/// starts; the highest reading so far is kept.
			/// </summary>
			public float Progress
			{
				get
				{
					if (!progressFrom.IsValid())
					{
						return best;
					}
					DownloadStatus status = progressFrom.GetDownloadStatus();
					// Nothing to download (already cached): the load step itself is the rest.
					float now = Mathf.Clamp01(status.TotalBytes > 0 ? status.Percent : progressFrom.PercentComplete);
					if (now > best)
					{
						best = now;
					}
					return best;
				}
			}

			/// <summary>The highest progress this load has reported.</summary>
			private float best;

			/// <summary>Finishes this load. Safe to call more than once.</summary>
			public void End()
			{
				if (Ended)
				{
					return;
				}
				Ended = true;
				progressFrom = default;
				if (pending.Remove(this))
				{
					waveEnded++;
				}
			}
		}

		private static readonly List<Load> pending = new List<Load>();

		/// <summary>Number of fetches still in flight.</summary>
		public static int Pending => pending.Count;

		/// <summary>Starts tracking a fetch named <paramref name="what"/> (e.g. "terrain").</summary>
		public static Load Begin(string what)
		{
			if (pending.Count == 0)
			{
				// Nothing in flight: a new wave.
				waveBegun = 0;
				waveEnded = 0;
			}
			var load = new Load(what);
			pending.Add(load);
			waveBegun++;
			return load;
		}

		/// <summary>Loads begun in the current wave (those in flight and those ended since the queue was last empty).</summary>
		private static int waveBegun;
		/// <summary>Loads of the current wave that have ended.</summary>
		private static int waveEnded;

		/// <summary>Loads begun in the current wave.</summary>
		public static int WaveBegun => waveBegun;
		/// <summary>Loads of the current wave that have ended.</summary>
		public static int WaveEnded => waveEnded;

		/// <summary>The first fetch still in flight, or null when none is.</summary>
		public static string FirstPending => pending.Count > 0 ? pending[0].What : null;

		/// <summary>
		/// Progress of the current wave (1 when nothing is in flight): its ended loads plus the running ones' progress, over
		/// every load it has begun. A mean of the running loads alone went backwards each time one finished and the next
		/// started from 0.
		/// </summary>
		public static float Progress
		{
			get
			{
				if (pending.Count == 0 || waveBegun <= 0)
				{
					return 1.0f;
				}
				float sum = waveEnded;
				for (int i = 0; i < pending.Count; i++)
				{
					sum += Mathf.Clamp01(pending[i].Progress);
				}
				return Mathf.Clamp01(sum / waveBegun);
			}
		}

		/// <summary>
		/// Work a system still has to do on content that has arrived before the world looks finished (grass taking its
		/// terrains, props merging into pools). Polled, not counted: it is added once and asked each frame the loading
		/// screen is up.
		/// </summary>
		public sealed class Work
		{
			/// <summary>What the work prepares, for the overlay's hint ("grass").</summary>
			public readonly string What;
			private readonly Func<bool> busy;
			private readonly Func<float> progress;

			internal Work(string what, Func<bool> busy, Func<float> progress)
			{
				What = what;
				this.busy = busy;
				this.progress = progress;
			}

			/// <summary>Whether the work is still running. A probe that throws counts as idle (and must not hold the overlay).</summary>
			public bool Busy
			{
				get
				{
					try
					{
						return busy();
					}
					catch (Exception e)
					{
						Debug.LogException(e);
						return false;
					}
				}
			}

			/// <summary>0 to 1 when the probe reports progress, else -1.</summary>
			public float Progress
			{
				get
				{
					if (progress == null)
					{
						return -1.0f;
					}
					try
					{
						return Mathf.Clamp01(progress());
					}
					catch (Exception e)
					{
						Debug.LogException(e);
						return -1.0f;
					}
				}
			}
		}

		private static readonly List<Work> work = new List<Work>();

		/// <summary>
		/// Adds (or replaces, by <paramref name="what"/>) a probe of work done on content after it arrives. Systems add one
		/// from their hook; replacing by name keeps a re-hook (domain reload off) from adding it twice.
		/// </summary>
		/// <param name="what">What the work prepares, for the hint ("grass").</param>
		/// <param name="busy">True while the work the camera would see is not done.</param>
		/// <param name="progress">Optional 0 to 1 of that work.</param>
		public static Work AddWork(string what, Func<bool> busy, Func<float> progress = null)
		{
			if (busy == null)
			{
				throw new ArgumentNullException(nameof(busy));
			}
			RemoveWork(what);
			var entry = new Work(what, busy, progress);
			work.Add(entry);
			return entry;
		}

		/// <summary>Removes the probe added as <paramref name="what"/>, if any.</summary>
		public static void RemoveWork(string what)
		{
			for (int i = work.Count - 1; i >= 0; i--)
			{
				if (work[i].What == what)
				{
					work.RemoveAt(i);
				}
			}
		}

		/// <summary>The first probe that reports busy, or null.</summary>
		public static Work BusyWork()
		{
			for (int i = 0; i < work.Count; i++)
			{
				if (work[i].Busy)
				{
					return work[i];
				}
			}
			return null;
		}

		/// <summary>Whether anything is loading, or being prepared from what has loaded.</summary>
		public static bool Busy => pending.Count > 0 || BusyWork() != null;

		/// <summary>
		/// What the overlay says about the content stage: the loads while any is in flight (with the wave's count), else the
		/// first busy work. False when nothing is running.
		/// </summary>
		/// <param name="hint">The line under the bar.</param>
		/// <param name="progress">0 to 1, or -1 when the work reports none (the bar is left as it is).</param>
		public static bool TryDescribe(out string hint, out float progress)
		{
			if (pending.Count > 0)
			{
				hint = waveBegun > 1 ? $"Loading {FirstPending} ({waveEnded + 1} of {waveBegun})" : $"Loading {FirstPending}";
				progress = Progress;
				return true;
			}
			Work busyWork = BusyWork();
			if (busyWork != null)
			{
				hint = $"Preparing {busyWork.What}";
				progress = busyWork.Progress;
				return true;
			}
			hint = null;
			progress = 1.0f;
			return false;
		}

		/// <summary>
		/// Set by the loading screen while it covers the world for content: nothing is on screen, so work may take far more of
		/// a frame than its usual slice (<see cref="Budget"/>).
		/// </summary>
		public static bool Covered { get; set; }

		/// <summary>The most milliseconds a frame a builder may spend while <see cref="Covered"/> (still yields, so the overlay animates).</summary>
		public const double CoveredBudgetMilliseconds = 24.0;

		/// <summary><paramref name="milliseconds"/>, or <see cref="CoveredBudgetMilliseconds"/> when larger and the world is covered.</summary>
		public static double Budget(double milliseconds) => Covered && CoveredBudgetMilliseconds > milliseconds ? CoveredBudgetMilliseconds : milliseconds;

		/// <summary>The names of the fetches still in flight and of busy work, for a warning.</summary>
		public static string Describe()
		{
			var names = new List<string>(pending.Count + 1);
			foreach (Load load in pending)
			{
				names.Add(load.What);
			}
			foreach (Work entry in work)
			{
				if (entry.Busy)
				{
					names.Add("preparing " + entry.What);
				}
			}
			return string.Join(", ", names);
		}

		/// <summary>Domain reload off: a previous play session's loads must not leak into the next one.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			foreach (Load load in pending)
			{
				load.Ended = true;
			}
			pending.Clear();
			waveBegun = 0;
			waveEnded = 0;
			Covered = false;
			// Work probes are left: their owners add them from hooks that may already have run (replaced by name on a re-hook).
		}
	}
}
