using System;
using System.Collections.Generic;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// A single caller's unit of work inside <see cref="AddressableLoadProcessor"/>.
	/// Returned by <see cref="AddressableLoadProcessor.BeginProcessQueue"/>, it tracks
	/// exactly the items that caller enqueued and raises <see cref="Completed"/> once
	/// those items — and only those items — have finished.
	/// </summary>
	/// <remarks>
	/// <para>This type exists to remove completion signalling from the processor's global
	/// <see cref="AddressableLoadProcessor.OnProgressUpdate"/> event. That event is a
	/// single multicast delegate shared by every bootstrap system, both loading screens,
	/// and the world-scene readiness handshake, so any queue drain finishing anywhere
	/// reported "done" to all of them. Worse, a handler that resubscribed during dispatch
	/// mutated the invocation list while the runtime was iterating a stale snapshot of it,
	/// which could run another subscriber twice.</para>
	/// <para>Each <c>BeginProcessQueue</c> call produces its own batch with its own event,
	/// so callers cannot observe one another's completion and a handler that starts new
	/// work during dispatch is operating on a different object entirely.</para>
	/// <para>A batch counts an item as finished whether it succeeded, failed, or was
	/// dropped. Load failures are surfaced through <see cref="FailedItems"/> rather than by
	/// withholding completion — a batch that never completes would stall the caller
	/// forever, which is precisely the failure this replaces.</para>
	/// </remarks>
	public sealed class AddressableLoadBatch
	{
		/// <summary>Asset keys this batch is still waiting on.</summary>
		private readonly HashSet<AddressableAssetKey> pendingAssets;
		/// <summary>Scene names this batch is still waiting on.</summary>
		private readonly HashSet<string> pendingScenes;
		/// <summary>Names/keys of items that did not load successfully.</summary>
		private readonly List<string> failedItems = new List<string>();

		/// <summary>Total number of items claimed when this batch was created.</summary>
		public int TotalItems { get; }

		/// <summary>Number of items that have finished, successfully or otherwise.</summary>
		public int CompletedItems { get; private set; }

		/// <summary>True once every claimed item has finished and <see cref="Completed"/> has been raised.</summary>
		public bool IsComplete { get; private set; }

		/// <summary>
		/// Items in this batch that failed to load. Empty on a fully successful batch.
		/// </summary>
		public IReadOnlyList<string> FailedItems => failedItems;

		/// <summary>True when at least one item in this batch failed to load.</summary>
		public bool HasFailures => failedItems.Count > 0;

		/// <summary>
		/// Fraction of this batch's items that have finished, in the range 0..1. A batch
		/// with nothing to do reports 1.
		/// </summary>
		public float Progress => TotalItems <= 0 ? 1f : Mathf01(CompletedItems / (float)TotalItems);

		/// <summary>
		/// Raised exactly once, when the last item in this batch finishes. Handlers added
		/// after completion are invoked immediately, so a caller cannot miss the event by
		/// subscribing late (a batch whose items were all already loaded completes inside
		/// <see cref="AddressableLoadProcessor.BeginProcessQueue"/>, before it returns).
		/// </summary>
		public event Action<AddressableLoadBatch> Completed
		{
			add
			{
				if (value == null)
				{
					return;
				}
				if (IsComplete)
				{
					value.Invoke(this);
					return;
				}
				completed += value;
			}
			remove { completed -= value; }
		}
		private Action<AddressableLoadBatch> completed;

		/// <summary>
		/// Raised as items in this batch finish, with <see cref="Progress"/>. Not raised
		/// after completion; use <see cref="Completed"/> for the terminal notification.
		/// </summary>
		public event Action<float> Progressed;

		/// <summary>
		/// Creates a batch that waits on the supplied asset keys and scene names.
		/// </summary>
		internal AddressableLoadBatch(HashSet<AddressableAssetKey> assets, HashSet<string> scenes)
		{
			pendingAssets = assets ?? new HashSet<AddressableAssetKey>();
			pendingScenes = scenes ?? new HashSet<string>();
			TotalItems = pendingAssets.Count + pendingScenes.Count;
		}

		/// <summary>Clamps to 0..1 without pulling in UnityEngine for one call.</summary>
		private static float Mathf01(float value)
		{
			if (value < 0f) return 0f;
			if (value > 1f) return 1f;
			return value;
		}

		/// <summary>
		/// Notifies this batch that an asset key finished. No-op when the key is not part
		/// of this batch.
		/// </summary>
		/// <param name="key">The asset key that finished.</param>
		/// <param name="succeeded">False when the item failed or was dropped.</param>
		internal void NotifyAssetFinished(AddressableAssetKey key, bool succeeded)
		{
			if (IsComplete || key == null || !pendingAssets.Remove(key))
			{
				return;
			}
			if (!succeeded)
			{
				failedItems.Add(key.ToString());
			}
			AdvanceOne();
		}

		/// <summary>
		/// Notifies this batch that a scene finished. No-op when the scene is not part of
		/// this batch.
		/// </summary>
		/// <param name="sceneName">The scene that finished.</param>
		/// <param name="succeeded">False when the scene failed to load.</param>
		internal void NotifySceneFinished(string sceneName, bool succeeded)
		{
			if (IsComplete || string.IsNullOrEmpty(sceneName) || !pendingScenes.Remove(sceneName))
			{
				return;
			}
			if (!succeeded)
			{
				failedItems.Add(sceneName);
			}
			AdvanceOne();
		}

		/// <summary>
		/// Records one finished item and raises progress, then completion when the batch
		/// empties.
		/// </summary>
		private void AdvanceOne()
		{
			CompletedItems++;

			if (pendingAssets.Count == 0 && pendingScenes.Count == 0)
			{
				RaiseCompleted();
				return;
			}

			Dispatch(Progressed, Progress, "Progress");
		}

		/// <summary>
		/// Invokes each subscriber of <paramref name="handler"/> independently.
		/// </summary>
		/// <remarks>
		/// A plain <c>handler?.Invoke(...)</c> walks the invocation list until one throws
		/// and then abandons the rest, so a single bad subscriber silently starves every
		/// later one — here that would mean one bootstrap system's exception preventing
		/// another from ever being told its load finished.
		/// </remarks>
		private static void Dispatch<T>(Action<T> handler, T argument, string label)
		{
			if (handler == null)
			{
				return;
			}

			Delegate[] subscribers = handler.GetInvocationList();
			for (int i = 0; i < subscribers.Length; ++i)
			{
				try
				{
					((Action<T>)subscribers[i]).Invoke(argument);
				}
				catch (Exception ex)
				{
					Log.Error("AddressableLoadBatch", $"{label} handler threw: {ex}");
				}
			}
		}

		/// <summary>
		/// Marks the batch complete and raises <see cref="Completed"/> exactly once.
		/// </summary>
		/// <remarks>
		/// Handler exceptions are caught and logged rather than propagated. This runs from
		/// inside the processor's load coroutine and from Addressables completion
		/// callbacks; letting an exception escape would abort the drain and strand every
		/// other batch in flight.
		/// </remarks>
		internal void RaiseCompleted()
		{
			if (IsComplete)
			{
				return;
			}
			IsComplete = true;

			// Detach before dispatch so a handler that inspects or re-enters cannot
			// observe a half-torn-down batch, and so the delegate cannot fire twice.
			Action<AddressableLoadBatch> handlers = completed;
			completed = null;
			Progressed = null;

			if (HasFailures)
			{
				Log.Warning("AddressableLoadBatch",
					$"Load batch completed with {failedItems.Count} failed item(s): {string.Join(", ", failedItems)}");
			}

			Dispatch(handlers, this, "Completion");
		}
	}
}
