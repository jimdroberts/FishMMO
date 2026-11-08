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

			/// <summary>0 to 1: the tracked operation's download progress, else 0.</summary>
			public float Progress
			{
				get
				{
					if (!progressFrom.IsValid())
					{
						return 0.0f;
					}
					DownloadStatus status = progressFrom.GetDownloadStatus();
					// Nothing to download (already cached): the load step itself is the rest.
					return status.TotalBytes > 0 ? status.Percent : progressFrom.PercentComplete;
				}
			}

			/// <summary>Finishes this load. Safe to call more than once.</summary>
			public void End()
			{
				if (Ended)
				{
					return;
				}
				Ended = true;
				progressFrom = default;
				pending.Remove(this);
			}
		}

		private static readonly List<Load> pending = new List<Load>();

		/// <summary>Number of fetches still in flight.</summary>
		public static int Pending => pending.Count;

		/// <summary>Starts tracking a fetch named <paramref name="what"/> (e.g. "terrain").</summary>
		public static Load Begin(string what)
		{
			var load = new Load(what);
			pending.Add(load);
			return load;
		}

		/// <summary>The first fetch still in flight, or null when none is.</summary>
		public static string FirstPending => pending.Count > 0 ? pending[0].What : null;

		/// <summary>Mean progress of the fetches still in flight (1 when none is).</summary>
		public static float Progress
		{
			get
			{
				if (pending.Count == 0)
				{
					return 1.0f;
				}
				float sum = 0.0f;
				for (int i = 0; i < pending.Count; i++)
				{
					sum += Mathf.Clamp01(pending[i].Progress);
				}
				return sum / pending.Count;
			}
		}

		/// <summary>The names of the fetches still in flight, for a warning.</summary>
		public static string Describe()
		{
			var names = new List<string>(pending.Count);
			foreach (Load load in pending)
			{
				names.Add(load.What);
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
		}
	}
}
