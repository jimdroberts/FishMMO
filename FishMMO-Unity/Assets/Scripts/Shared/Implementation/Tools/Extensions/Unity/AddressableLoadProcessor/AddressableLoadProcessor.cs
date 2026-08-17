using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Central queue for Addressable asset and scene loading.
	/// </summary>
	/// <remarks>
	/// <para><b>Completion vs. progress.</b> Callers learn that <i>their</i> work finished
	/// from the <see cref="AddressableLoadBatch"/> returned by
	/// <see cref="BeginProcessQueue"/>. <see cref="OnProgressUpdate"/> is a display-only
	/// aggregate across everything in flight and must never be used to detect completion —
	/// it is global, so it fires for work the subscriber never requested.</para>
	/// <para><b>Ordering.</b> The drain processes assets before scenes within a pass. Code
	/// relies on this: bootstrap systems enqueue template labels and scenes together and
	/// expect the templates to be in the cache before a scene that references them
	/// deserializes.</para>
	/// <para><b>Termination invariant.</b> Every item that enters the queue must
	/// eventually call <see cref="FinishAsset"/> or <see cref="FinishScene"/> exactly once,
	/// on every path — success, failure, duplicate, or invalid handle. An item that leaves
	/// without being finished is never removed from the in-flight tables, so
	/// <see cref="PendingItemCount"/> never reaches zero, the drain loop never exits, and
	/// every batch waiting on it stalls forever.</para>
	/// </remarks>
	public static class AddressableLoadProcessor
	{
		/// <summary>Coroutine host. Created on demand; survives scene loads.</summary>
		private static AddressableLoadHelper helper;

		/// <summary>MonoBehaviour used purely to own the load coroutine.</summary>
		public class AddressableLoadHelper : MonoBehaviour
		{
			void Awake()
			{
				DontDestroyOnLoad(this.gameObject);
			}
		}

		#region STATE
		/// <summary>Currently loaded prefab assets, keyed by AssetReference runtime key.</summary>
		private static Dictionary<object, AsyncOperationHandle<GameObject>> loadedPrefabs = new Dictionary<object, AsyncOperationHandle<GameObject>>();
		/// <summary>Prefab loads currently in flight.</summary>
		private static Dictionary<object, AsyncOperationHandle<GameObject>> currentPrefabOperations = new Dictionary<object, AsyncOperationHandle<GameObject>>();

		/// <summary>The currently loaded label-based assets.</summary>
		private static Dictionary<AddressableAssetKey, AsyncOperationHandle<IList<UnityEngine.Object>>> loadedAssets = new Dictionary<AddressableAssetKey, AsyncOperationHandle<IList<UnityEngine.Object>>>();

		/// <summary>
		/// Currently loaded scenes, keyed by the <b>requested</b> scene name.
		/// </summary>
		/// <remarks>
		/// Keyed by the requested name, not <c>Scene.name</c>. Every lookup in this class
		/// and in callers (<c>EnqueueLoad</c>, <c>IsSceneLoaded</c>,
		/// <c>UnloadSceneByLabelAsync</c>) uses the requested name, so keying the write by
		/// the loaded scene's asset name instead made dedupe and unload silently fail
		/// whenever an Addressable key differed from its scene asset name.
		/// </remarks>
		private static Dictionary<string, AsyncOperationHandle<SceneInstance>> loadedScenes = new Dictionary<string, AsyncOperationHandle<SceneInstance>>();

		/// <summary>Asset keys awaiting resource-location validation.</summary>
		private static List<AddressableAssetKey> waitingResourceValidationQueue = new List<AddressableAssetKey>();
		/// <summary>Validation operations in flight.</summary>
		private static Dictionary<AddressableAssetKey, AsyncOperationHandle<IList<IResourceLocation>>> currentValidationOperations = new Dictionary<AddressableAssetKey, AsyncOperationHandle<IList<IResourceLocation>>>();
		/// <summary>Validated asset keys waiting to be loaded.</summary>
		private static HashSet<AddressableAssetKey> operationQueue = new HashSet<AddressableAssetKey>();
		/// <summary>Asset loads in flight.</summary>
		private static Dictionary<AddressableAssetKey, AsyncOperationHandle> currentOperations = new Dictionary<AddressableAssetKey, AsyncOperationHandle>();

		/// <summary>Scenes waiting to load, keyed by requested scene name.</summary>
		private static Dictionary<string, AddressableSceneLoadData> sceneOperationQueue = new Dictionary<string, AddressableSceneLoadData>();
		/// <summary>Scene loads in flight, keyed by requested scene name.</summary>
		private static Dictionary<string, AddressableSceneLoadData> currentSceneOperations = new Dictionary<string, AddressableSceneLoadData>();

		/// <summary>
		/// Items enqueued since the last <see cref="BeginProcessQueue"/>, awaiting a batch
		/// to claim them.
		/// </summary>
		private static HashSet<AddressableAssetKey> stagedAssetKeys = new HashSet<AddressableAssetKey>();
		/// <summary>Scene names enqueued since the last <see cref="BeginProcessQueue"/>.</summary>
		private static HashSet<string> stagedSceneNames = new HashSet<string>();

		/// <summary>Batches waiting on in-flight work.</summary>
		private static List<AddressableLoadBatch> activeBatches = new List<AddressableLoadBatch>();

		/// <summary>Items finished during the current drain.</summary>
		private static int itemsCompletedThisRun;
		/// <summary>
		/// High-water mark of total items for the current drain. Only ever grows while a
		/// drain is running, so aggregate progress is monotonic even when work is enqueued
		/// mid-drain.
		/// </summary>
		private static int itemsTotalThisRun;
		/// <summary>True while the drain coroutine is running.</summary>
		private static bool isProcessingQueue = false;
		/// <summary>
		/// Set once <see cref="ReleaseAllAssets"/> has torn the processor down for
		/// application shutdown. All further load requests are refused.
		/// </summary>
		/// <remarks>
		/// Without this, teardown could restart the very work it was tearing down:
		/// retiring a bootstrap system's batch runs that system's completion handler,
		/// which enqueues its next phase and calls <see cref="BeginProcessQueue"/> — which
		/// would recreate the helper GameObject and kick off a fresh drain in the middle of
		/// <c>Application.Quit()</c>.
		/// </remarks>
		private static bool isShuttingDown = false;
		#endregion

		#region EVENTS
		/// <summary>
		/// Aggregate progress across everything currently in flight, 0..1.
		/// </summary>
		/// <remarks>
		/// <b>Display only.</b> This is global: it reports on work the subscriber did not
		/// request, and it fires 1 whenever the whole queue happens to drain. Do not use it
		/// to detect that your own load finished — use the
		/// <see cref="AddressableLoadBatch"/> returned by <see cref="BeginProcessQueue"/>.
		/// </remarks>
		public static Action<float> OnProgressUpdate;
		/// <summary>Invoked when an Addressable (non scene) is loaded.</summary>
		public static Action<UnityEngine.Object> OnAddressableLoaded;
		/// <summary>Invoked when an Addressable (non scene) is unloaded.</summary>
		public static Action<UnityEngine.Object> OnAddressableUnloaded;
		/// <summary>Invoked when an Addressable Scene is loaded.</summary>
		public static Action<Scene> OnSceneLoaded;
		/// <summary>Invoked when an Addressable Scene is unloaded.</summary>
		public static Action<string> OnSceneUnloaded;
		#endregion

		#region LIFECYCLE
		/// <summary>
		/// Clears all static state at the start of every play session.
		/// </summary>
		/// <remarks>
		/// Static state survives play sessions when Enter Play Mode Options disables domain
		/// reload. Without this reset a second session would start with stale loaded-asset
		/// tables, subscribers from the previous session still attached to the public
		/// events, and — worst — <see cref="isProcessingQueue"/> stuck true, which makes
		/// <see cref="BeginProcessQueue"/> a permanent no-op and hangs boot.
		/// </remarks>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState()
		{
			helper = null;

			loadedPrefabs.Clear();
			currentPrefabOperations.Clear();
			loadedAssets.Clear();
			loadedScenes.Clear();

			waitingResourceValidationQueue.Clear();
			currentValidationOperations.Clear();
			operationQueue.Clear();
			currentOperations.Clear();
			sceneOperationQueue.Clear();
			currentSceneOperations.Clear();

			stagedAssetKeys.Clear();
			stagedSceneNames.Clear();
			activeBatches.Clear();

			itemsCompletedThisRun = 0;
			itemsTotalThisRun = 0;
			isProcessingQueue = false;
			isShuttingDown = false;

			OnProgressUpdate = null;
			OnAddressableLoaded = null;
			OnAddressableUnloaded = null;
			OnSceneLoaded = null;
			OnSceneUnloaded = null;
		}

		/// <summary>
		/// Returns the coroutine host, creating it if needed.
		/// </summary>
		/// <remarks>
		/// Created on demand rather than in a static constructor: a static constructor runs
		/// at an arbitrary first-touch moment, which may be during deserialization or on a
		/// non-main thread, where <c>new GameObject</c> throws. The <c>== null</c> test also
		/// catches a destroyed host (Unity's overloaded equality), so a torn-down helper is
		/// rebuilt instead of throwing MissingReferenceException.
		/// </remarks>
		private static AddressableLoadHelper EnsureHelper()
		{
			if (isShuttingDown)
			{
				// Never resurrect the coroutine host during teardown.
				return null;
			}
			if (helper == null)
			{
				GameObject helperObj = new GameObject("AddressableLoadHelper");
				helper = helperObj.AddComponent<AddressableLoadHelper>();
			}
			return helper;
		}
		#endregion

		#region PROGRESS
		/// <summary>
		/// Number of items still queued or in flight. Each item is counted exactly once
		/// regardless of which phase it currently sits in.
		/// </summary>
		private static int PendingItemCount =>
			waitingResourceValidationQueue.Count +
			currentValidationOperations.Count +
			operationQueue.Count +
			currentOperations.Count +
			sceneOperationQueue.Count +
			currentSceneOperations.Count;

		/// <summary>
		/// True while any asset is queued, being validated, or loading. Scenes wait on this
		/// so a template label enqueued mid-drain cannot be overtaken by a scene that needs
		/// it in the cache.
		/// </summary>
		private static bool HasPendingAssetWork =>
			waitingResourceValidationQueue.Count > 0 ||
			currentValidationOperations.Count > 0 ||
			operationQueue.Count > 0 ||
			currentOperations.Count > 0;

		/// <summary>
		/// Aggregate progress of the current drain, 0..1.
		/// </summary>
		/// <remarks>
		/// Divides by the run's total, not by the remaining count. The previous
		/// <c>processed / remaining</c> form produced 1/3, 2/2, 3/1, 4/0 for a four-item
		/// load — values at or above 1 from the halfway point on, which the caller then
		/// discarded, so bars never animated past the first item.
		/// </remarks>
		public static float CurrentProgress
		{
			get
			{
				if (itemsTotalThisRun <= 0)
				{
					return 1f;
				}
				return Mathf.Clamp01(itemsCompletedThisRun / (float)itemsTotalThisRun);
			}
		}

		/// <summary>
		/// Number of items still queued or in flight.
		/// </summary>
		public static float RemainingAssetsToLoad => PendingItemCount;

		/// <summary>
		/// True while the drain is running.
		/// </summary>
		/// <remarks>
		/// Lets a subscriber created mid-drain seed its own state instead of waiting for the
		/// next <see cref="OnProgressUpdate"/> tick. A loading screen living in a scene the
		/// processor is itself loading is exactly that case: every event raised before its
		/// Awake is lost, so without this it cannot tell "boot is still running" from
		/// "nothing is happening" until the next item happens to finish.
		/// </remarks>
		public static bool IsLoading => isProcessingQueue;

		/// <summary>
		/// Raises the run's total to at least the currently known item count. Never lowers
		/// it, so aggregate progress cannot move backwards when work is enqueued mid-drain.
		/// </summary>
		private static void RefreshRunTotal()
		{
			int known = itemsCompletedThisRun + PendingItemCount;
			if (known > itemsTotalThisRun)
			{
				itemsTotalThisRun = known;
			}
		}

		/// <summary>Raises <see cref="OnProgressUpdate"/>, isolating subscriber exceptions.</summary>
		private static void RaiseProgress(float value)
		{
			try
			{
				OnProgressUpdate?.Invoke(value);
			}
			catch (Exception ex)
			{
				Log.Error("AddressableLoadProcessor", $"Progress subscriber threw: {ex}");
			}
		}
		#endregion

		#region ENQUEUE
		/// <summary>
		/// Enqueues a single addressable asset label for loading.
		/// </summary>
		/// <param name="label">The addressable label to load.</param>
		/// <param name="mergeMode">The merge mode for label processing.</param>
		public static void EnqueueLoad(string label, Addressables.MergeMode mergeMode = Addressables.MergeMode.None)
		{
			if (string.IsNullOrEmpty(label))
			{
				return;
			}
			StageAssetKey(new AddressableAssetKey(new List<string>() { label, }, mergeMode));
		}

		/// <summary>
		/// Enqueues multiple addressable asset labels for loading. Ignores null or empty collections.
		/// </summary>
		/// <param name="labels">Enumerable of addressable labels to load.</param>
		/// <param name="mergeMode">The merge mode for label processing.</param>
		public static void EnqueueLoad(IEnumerable<string> labels, Addressables.MergeMode mergeMode = Addressables.MergeMode.None)
		{
			if (labels == null)
			{
				return;
			}
			foreach (var label in labels)
			{
				EnqueueLoad(label, mergeMode);
			}
		}

		/// <summary>
		/// Enqueues a label and key pair for loading. Uses intersection merge mode by default.
		/// </summary>
		/// <param name="label">The addressable label.</param>
		/// <param name="key">The addressable key.</param>
		/// <param name="mergeMode">The merge mode for label processing.</param>
		public static void EnqueueLoad(string label, string key, Addressables.MergeMode mergeMode = Addressables.MergeMode.Intersection)
		{
			if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(key))
			{
				return;
			}
			StageAssetKey(new AddressableAssetKey(new List<string>() { label, key, }, mergeMode));
		}

		/// <summary>
		/// Enqueues multiple label-key pairs for loading. Uses intersection merge mode by default.
		/// </summary>
		/// <param name="labels">Enumerable of label-key pairs.</param>
		/// <param name="mergeMode">The merge mode for label processing.</param>
		public static void EnqueueLoad(IEnumerable<KeyValuePair<string, string>> labels, Addressables.MergeMode mergeMode = Addressables.MergeMode.Intersection)
		{
			if (labels == null)
			{
				return;
			}
			foreach (var pair in labels)
			{
				EnqueueLoad(pair.Key, pair.Value, mergeMode);
			}
		}

		/// <summary>
		/// Stages an asset key for the next batch and queues it for loading if it is not
		/// already loaded or in flight.
		/// </summary>
		/// <remarks>
		/// A key that is already in flight for an earlier batch is still staged: the new
		/// batch must wait for it too, and would otherwise complete before the asset it
		/// asked for exists. A key that is already fully loaded is not staged, so a batch
		/// that asks only for cached assets completes immediately.
		/// </remarks>
		private static void StageAssetKey(AddressableAssetKey assetKey)
		{
			if (isShuttingDown || loadedAssets.ContainsKey(assetKey))
			{
				return;
			}

			stagedAssetKeys.Add(assetKey);

			if (!waitingResourceValidationQueue.Contains(assetKey) &&
				!currentValidationOperations.ContainsKey(assetKey) &&
				!operationQueue.Contains(assetKey) &&
				!currentOperations.ContainsKey(assetKey))
			{
				waitingResourceValidationQueue.Add(assetKey);
			}
		}

		/// <summary>
		/// Returns true when a scene with the given name is currently tracked as loaded by
		/// this processor.
		/// </summary>
		/// <param name="sceneName">The scene name to test.</param>
		public static bool IsSceneLoaded(string sceneName)
		{
			return !string.IsNullOrEmpty(sceneName) && loadedScenes.ContainsKey(sceneName);
		}

		/// <summary>
		/// Enqueues a single scene load operation. Optionally attaches a post-process callback.
		/// </summary>
		/// <remarks>
		/// When the same scene is enqueued twice before it loads, the callbacks are merged
		/// onto the queued request rather than the second request being dropped. The
		/// previous implementation deduped on scene name alone and silently discarded the
		/// second caller's callback along with it.
		/// </remarks>
		/// <param name="sceneLoadData">Scene load data to enqueue.</param>
		/// <param name="globalOnScenePostProcess">Optional callback invoked after scene is loaded.</param>
		public static void EnqueueLoad(AddressableSceneLoadData sceneLoadData, Action<Scene> globalOnScenePostProcess = null)
		{
			if (isShuttingDown || sceneLoadData == null || string.IsNullOrEmpty(sceneLoadData.SceneName))
			{
				return;
			}

			string sceneName = sceneLoadData.SceneName;

			// Already loaded: nothing to wait for, and re-running the callback here would
			// fire it from inside the caller's enqueue loop. Callers that need to act on an
			// already-loaded scene test IsSceneLoaded first.
			if (loadedScenes.ContainsKey(sceneName))
			{
				return;
			}

			stagedSceneNames.Add(sceneName);

			// Merge onto whichever request is already tracking this scene, if any.
			if (sceneOperationQueue.TryGetValue(sceneName, out AddressableSceneLoadData queued))
			{
				MergeSceneCallbacks(queued, sceneLoadData, globalOnScenePostProcess);
				return;
			}
			if (currentSceneOperations.TryGetValue(sceneName, out AddressableSceneLoadData inFlight))
			{
				MergeSceneCallbacks(inFlight, sceneLoadData, globalOnScenePostProcess);
				return;
			}

			if (globalOnScenePostProcess != null)
			{
				sceneLoadData.OnSceneLoaded += globalOnScenePostProcess;
			}
			Log.Debug("AddressableLoadProcessor", $"Enqueued scene: {sceneName}");
			sceneOperationQueue.Add(sceneName, sceneLoadData);
		}

		/// <summary>
		/// Folds a duplicate scene request's callbacks onto the request already tracking
		/// that scene.
		/// </summary>
		private static void MergeSceneCallbacks(AddressableSceneLoadData target, AddressableSceneLoadData duplicate, Action<Scene> globalOnScenePostProcess)
		{
			if (!ReferenceEquals(target, duplicate) && duplicate.OnSceneLoaded != null)
			{
				target.OnSceneLoaded += duplicate.OnSceneLoaded;
				// Clear the duplicate so a serialized instance re-enqueued later does not
				// accumulate the same handlers again.
				duplicate.OnSceneLoaded = null;
			}
			if (globalOnScenePostProcess != null)
			{
				target.OnSceneLoaded += globalOnScenePostProcess;
			}
		}

		/// <summary>
		/// Enqueues multiple scene load operations. Optionally attaches a post-process callback to each.
		/// </summary>
		/// <param name="sceneLoadDatas">Enumerable of scene load data to enqueue.</param>
		/// <param name="globalOnScenePostProcess">Optional callback invoked after each scene is loaded.</param>
		public static void EnqueueLoad(IEnumerable<AddressableSceneLoadData> sceneLoadDatas, Action<Scene> globalOnScenePostProcess = null)
		{
			if (sceneLoadDatas == null)
			{
				return;
			}
			foreach (var sceneLoadData in sceneLoadDatas)
			{
				EnqueueLoad(sceneLoadData, globalOnScenePostProcess);
			}
		}
		#endregion

		#region DRAIN
		/// <summary>
		/// Claims everything enqueued since the previous call into a new batch and starts
		/// the drain if it is not already running.
		/// </summary>
		/// <returns>
		/// A batch that completes when exactly the claimed items have finished. A batch
		/// with nothing to claim is already complete on return; subscribing to its
		/// <see cref="AddressableLoadBatch.Completed"/> still invokes the handler.
		/// </returns>
		/// <remarks>
		/// Safe to call while a drain is running: the new batch's items join the running
		/// drain and the batch completes when its own items finish, independently of any
		/// other batch.
		/// </remarks>
		public static AddressableLoadBatch BeginProcessQueue()
		{
			if (isShuttingDown)
			{
				// Refuse new work during teardown, but hand back a completed batch so a
				// caller awaiting it is released rather than stalled mid-quit.
				Log.Debug("AddressableLoadProcessor", "BeginProcessQueue ignored: the processor is shutting down.");
				AddressableLoadBatch empty = new AddressableLoadBatch(null, null);
				empty.RaiseCompleted();
				return empty;
			}

			AddressableLoadBatch batch = new AddressableLoadBatch(
				new HashSet<AddressableAssetKey>(stagedAssetKeys),
				new HashSet<string>(stagedSceneNames));

			stagedAssetKeys.Clear();
			stagedSceneNames.Clear();

			if (batch.TotalItems == 0)
			{
				// Nothing to wait for (everything requested was already loaded, or nothing
				// was requested). Complete synchronously so sequential bootstrap chains
				// advance without waiting a frame.
				batch.RaiseCompleted();
				return batch;
			}

			activeBatches.Add(batch);

			if (!isProcessingQueue)
			{
				itemsCompletedThisRun = 0;
				itemsTotalThisRun = 0;
				RefreshRunTotal();

				isProcessingQueue = true;
				RaiseProgress(0f);

				try
				{
					EnsureHelper().StartCoroutine(ProcessLoadQueue());
				}
				catch (Exception ex)
				{
					// The drain never started, so nothing will ever finish these items.
					// Clear the flag (leaving it set makes every future BeginProcessQueue a
					// silent no-op) and release the waiting batches instead of hanging them.
					isProcessingQueue = false;
					itemsCompletedThisRun = 0;
					itemsTotalThisRun = 0;
					Log.Error("AddressableLoadProcessor", $"Failed to start the load coroutine: {ex}");
					AbortActiveBatches();

					/* Terminal progress, matching the drain's own exit. RaiseProgress(0f) above
					 * has already told every loading screen that work started; without a closing
					 * 1 they sit on screen over a dead queue forever, because the only other
					 * source of that edge is the drain that just failed to start. */
					RaiseProgress(1f);
					throw;
				}
			}
			else
			{
				// Joining a drain already in progress; fold the new work into its total.
				RefreshRunTotal();
			}

			return batch;
		}

		/// <summary>
		/// Completes every active batch without waiting for its items. Used when the drain
		/// cannot run, so callers are released rather than left waiting forever.
		/// </summary>
		private static void AbortActiveBatches()
		{
			List<AddressableLoadBatch> pending = new List<AddressableLoadBatch>(activeBatches);
			activeBatches.Clear();
			foreach (AddressableLoadBatch batch in pending)
			{
				batch.RaiseCompleted();
			}
		}

		/// <summary>
		/// Drains the queues: validation, then assets, then scenes, repeating until empty.
		/// </summary>
		/// <remarks>
		/// Private by design — a second concurrent drain would double-start operations and
		/// corrupt the run counters.
		/// </remarks>
		private static IEnumerator ProcessLoadQueue()
		{
			while (PendingItemCount > 0)
			{
				/* Strict priority, re-evaluated after every single item: validation, then
				 * asset loads, then scenes. Each branch loops back to the top rather than
				 * draining its own phase to exhaustion.
				 *
				 * The phase-at-a-time form this replaces only ever looked forward, so work
				 * enqueued while the drain sat in a later phase could not be pulled back
				 * into an earlier one. A bootstrap that enqueues templates and scenes
				 * together while a drain is already running (ServerLauncher does exactly
				 * this, and its own comment says the templates must be cached first) had its
				 * scenes loaded before its templates, because the drain resumed inside the
				 * scene phase and never revisited validation. */

				// Phase 1: resolve resource locations for staged asset keys.
				if (waitingResourceValidationQueue.Count > 0)
				{
					AddressableAssetKey assetKey = waitingResourceValidationQueue[0];
					waitingResourceValidationQueue.RemoveAt(0);

					if (assetKey == null || assetKey.Keys == null || assetKey.Keys.Count < 1)
					{
						FinishAsset(assetKey, false);
						continue;
					}

					AsyncOperationHandle<IList<IResourceLocation>> handle = Addressables.LoadResourceLocationsAsync(assetKey.Keys, assetKey.MergeMode);
					if (!handle.IsValid())
					{
						// No operation was created, so no Completed callback will ever run.
						// Finish it here or the key stays counted forever.
						Log.Warning("AddressableLoadProcessor", $"Invalid validation handle for: {assetKey}");
						FinishAsset(assetKey, false);
						continue;
					}

					currentValidationOperations[assetKey] = handle;
					AttachValidationCompletion(assetKey, handle);
					yield return handle;

					// Released here rather than inside the completion callback: the drain is
					// what owns this handle's lifetime, and releasing it mid-callback left
					// the coroutine yielding on an already-invalidated handle.
					if (handle.IsValid())
					{
						Addressables.Release(handle);
					}
					continue;
				}

				// Phase 2: load validated assets.
				if (operationQueue.Count > 0)
				{
					AddressableAssetKey assetKey = operationQueue.First();
					operationQueue.Remove(assetKey);

					Log.Debug("AddressableLoadProcessor", $"Loading: {assetKey}");

					AsyncOperationHandle<IList<UnityEngine.Object>> operation =
						Addressables.LoadAssetsAsync<UnityEngine.Object>(assetKey.Keys, null, assetKey.MergeMode, false);

					if (!operation.IsValid())
					{
						Log.Warning("AddressableLoadProcessor", $"Invalid load handle for: {assetKey}");
						FinishAsset(assetKey, false);
						continue;
					}

					currentOperations[assetKey] = operation;
					AttachAssetCompletion(assetKey, operation);
					yield return operation;
					continue;
				}

				/* Phase 3: scenes, but only once no asset work remains anywhere — including
				 * assets still being validated or loaded. This is the guarantee callers rely
				 * on: template caches are fully populated before any scene that references
				 * them deserializes. */
				if (HasPendingAssetWork)
				{
					// An asset operation is still settling; come back next frame rather than
					// starting a scene ahead of it.
					yield return null;
					continue;
				}

				if (sceneOperationQueue.Count > 0)
				{
					KeyValuePair<string, AddressableSceneLoadData> next = sceneOperationQueue.First();
					string sceneName = next.Key;
					AddressableSceneLoadData sceneLoadData = next.Value;
					sceneOperationQueue.Remove(sceneName);

					if (loadedScenes.ContainsKey(sceneName))
					{
						// Loaded by another path between enqueue and now. Finish it so the
						// waiting batches are released instead of stalling.
						Log.Debug("AddressableLoadProcessor", $"Scene already loaded, skipping: {sceneName}");
						FinishScene(sceneName, true);
						continue;
					}

					Log.Debug("AddressableLoadProcessor", $"Scene Loading: {sceneName}");

					/* Always activate on load.
					 *
					 * Addressables does settle the handle for a deferred-activation load
					 * (SceneProvider completes the op at progress 0.9 when
					 * allowSceneActivation is false), so the drain itself would not stall.
					 * The blocker is downstream: an unactivated scene reports
					 * Scene.isLoaded == false, and GetRootGameObjects() throws on it — so
					 * the OnSceneLoaded callbacks, and BootstrapSystem's discovery of the
					 * bootstraps inside the scene, cannot run until something activates it.
					 * Nothing in this codebase calls SceneInstance.ActivateAsync(), so
					 * honouring the flag would silently break the bootstrap chain instead.
					 *
					 * Supporting it properly means deferring the callbacks to an explicit
					 * activation step owned by the caller; until that exists, an unsupported
					 * request is reported and overridden rather than silently mis-sequenced. */
					if (!sceneLoadData.ActivateOnLoad)
					{
						Log.Warning("AddressableLoadProcessor",
							$"Scene '{sceneName}' requests ActivateOnLoad=false. Deferred activation is not supported by this queue " +
							"(its load callbacks and bootstrap discovery both require an activated scene). Loading it activated instead.");
					}

					AsyncOperationHandle<SceneInstance> operation =
						Addressables.LoadSceneAsync(sceneName, sceneLoadData.LoadSceneMode, true);

					if (!operation.IsValid())
					{
						Log.Warning("AddressableLoadProcessor", $"Invalid scene load handle for: {sceneName}");
						FinishScene(sceneName, false);
						continue;
					}

					currentSceneOperations[sceneName] = sceneLoadData;
					AttachSceneCompletion(sceneName, sceneLoadData, operation);
					yield return operation;
					continue;
				}

				/* Nothing dequeuable but PendingItemCount is still positive: an operation has
				 * signalled IsDone to the coroutine scheduler but its Completed callback (which
				 * does the bookkeeping) has not run yet. Wait a frame for it to settle. */
				yield return null;
			}

			isProcessingQueue = false;
			itemsCompletedThisRun = 0;
			itemsTotalThisRun = 0;

			RaiseProgress(1f);
		}

		/// <summary>
		/// Wires validation completion: a key with locations advances to the load queue, a
		/// key without them is finished immediately.
		/// </summary>
		private static void AttachValidationCompletion(AddressableAssetKey assetKey, AsyncOperationHandle<IList<IResourceLocation>> handle)
		{
			handle.Completed += (op) =>
			{
				currentValidationOperations.Remove(assetKey);

				bool found = op.Status == AsyncOperationStatus.Succeeded && op.Result != null && op.Result.Count > 0;
				if (found)
				{
					operationQueue.Add(assetKey);
					RefreshRunTotal();
				}
				else
				{
					// No locations: this key will never load. Finish it here, otherwise it
					// leaves the accounting without ever being counted and its batches hang.
					Log.Warning("AddressableLoadProcessor", $"No resource locations for: {assetKey}");
					FinishAsset(assetKey, false);
				}
			};
		}

		/// <summary>Wires asset load completion, on both the success and failure paths.</summary>
		private static void AttachAssetCompletion(AddressableAssetKey assetKey, AsyncOperationHandle<IList<UnityEngine.Object>> handle)
		{
			handle.Completed += (op) =>
			{
				// Removed unconditionally. Leaving a failed operation in this table was what
				// made a single missing bundle wedge the drain loop forever.
				currentOperations.Remove(assetKey);

				bool succeeded = op.Status == AsyncOperationStatus.Succeeded;
				if (succeeded)
				{
					if (!loadedAssets.ContainsKey(assetKey))
					{
						loadedAssets.Add(assetKey, op);
					}

					Log.Debug("AddressableLoadProcessor", $"Loaded: {assetKey}");

					if (op.Result != null)
					{
						foreach (var asset in op.Result)
						{
							if (asset == null)
							{
								continue;
							}
							try
							{
								OnAddressableLoaded?.Invoke(asset);
							}
							catch (Exception ex)
							{
								Log.Error("AddressableLoadProcessor", $"OnAddressableLoaded subscriber threw for '{asset.name}': {ex}");
							}
						}
					}
				}
				else
				{
					Log.Warning("AddressableLoadProcessor", $"Failed to load Addressable: {assetKey} ({op.OperationException?.Message})");
					if (op.IsValid())
					{
						op.Release();
					}
				}

				FinishAsset(assetKey, succeeded);
			};
		}

		/// <summary>Wires scene load completion, on both the success and failure paths.</summary>
		private static void AttachSceneCompletion(string sceneName, AddressableSceneLoadData sceneLoadData, AsyncOperationHandle<SceneInstance> handle)
		{
			handle.Completed += (op) =>
			{
				currentSceneOperations.Remove(sceneName);

				bool succeeded = op.Status == AsyncOperationStatus.Succeeded;
				if (succeeded)
				{
					Scene loadedScene = op.Result.Scene;

					if (loadedScenes.ContainsKey(sceneName))
					{
						// Raced with another load of the same scene. Release the duplicate
						// handle, but still finish the item so waiting batches are released.
						Log.Warning("AddressableLoadProcessor", $"Duplicate load completed for scene '{sceneName}'; releasing the extra handle.");
						if (op.IsValid())
						{
							op.Release();
						}
						FinishScene(sceneName, true);
						return;
					}

					loadedScenes.Add(sceneName, op);

					// Success is otherwise silent: "Scene Loading: X" logs when the load
					// starts, but nothing previously logged when it finished, so a trace
					// showed a scene being requested with no visible confirmation it ever
					// completed — indistinguishable from a hang without attaching a debugger.
					Log.Debug("AddressableLoadProcessor", $"Scene Loaded: {sceneName}");

					// Detach before dispatch: these instances are serialized fields that get
					// re-enqueued (quit-to-login reloads the same list), so leaving handlers
					// attached accumulates duplicates across reloads.
					Action<Scene> sceneCallbacks = sceneLoadData.OnSceneLoaded;
					sceneLoadData.OnSceneLoaded = null;

					try
					{
						sceneCallbacks?.Invoke(loadedScene);
					}
					catch (Exception ex)
					{
						Log.Error("AddressableLoadProcessor", $"Scene load callback threw for '{sceneName}': {ex}");
					}

					try
					{
						OnSceneLoaded?.Invoke(loadedScene);
					}
					catch (Exception ex)
					{
						Log.Error("AddressableLoadProcessor", $"OnSceneLoaded subscriber threw for '{sceneName}': {ex}");
					}
				}
				else
				{
					Log.Warning("AddressableLoadProcessor", $"Failed to load scene Addressable: {sceneName} ({op.OperationException?.Message})");
					if (op.IsValid())
					{
						op.Release();
					}
				}

				FinishScene(sceneName, succeeded);
			};
		}

		/// <summary>
		/// Records an asset key as finished: updates run counters, notifies every batch
		/// waiting on it, and reports aggregate progress.
		/// </summary>
		private static void FinishAsset(AddressableAssetKey assetKey, bool succeeded)
		{
			itemsCompletedThisRun++;
			RefreshRunTotal();
			NotifyBatches(batch => batch.NotifyAssetFinished(assetKey, succeeded));
			ReportAggregateProgress();
		}

		/// <summary>
		/// Records a scene as finished: updates run counters, notifies every batch waiting
		/// on it, and reports aggregate progress.
		/// </summary>
		private static void FinishScene(string sceneName, bool succeeded)
		{
			itemsCompletedThisRun++;
			RefreshRunTotal();
			NotifyBatches(batch => batch.NotifySceneFinished(sceneName, succeeded));
			ReportAggregateProgress();
		}

		/// <summary>
		/// Applies a notification to every active batch and retires the ones that completed.
		/// </summary>
		/// <remarks>
		/// <para>Iterates a snapshot. A completion handler is free to call
		/// <see cref="BeginProcessQueue"/> — which appends to <see cref="activeBatches"/> —
		/// and mutating the live list mid-iteration would throw or skip entries.</para>
		/// <para>The snapshot is a local, deliberately not a reused static scratch buffer.
		/// This method is re-entrant: a completion handler can start a drain whose first
		/// operation resolves synchronously (an already-cached Addressable resolves inside
		/// the <c>Completed +=</c>), which finishes an item and lands back here. A shared
		/// buffer would be cleared and refilled by the inner call while the outer loop was
		/// still walking it, skipping or double-notifying batches.</para>
		/// </remarks>
		private static void NotifyBatches(Action<AddressableLoadBatch> notify)
		{
			if (activeBatches.Count == 0)
			{
				return;
			}

			AddressableLoadBatch[] snapshot = activeBatches.ToArray();

			for (int i = 0; i < snapshot.Length; ++i)
			{
				AddressableLoadBatch batch = snapshot[i];
				if (batch.IsComplete)
				{
					continue;
				}

				notify(batch);

				if (batch.IsComplete)
				{
					activeBatches.Remove(batch);
				}
			}
		}

		/// <summary>
		/// Publishes aggregate progress. Values at or above 1 are held back to just under 1
		/// while work remains — the terminal 1 is raised once by the drain when the whole
		/// queue empties, so subscribers get exactly one "finished" edge.
		/// </summary>
		private static void ReportAggregateProgress()
		{
			if (PendingItemCount <= 0)
			{
				return;
			}

			// Re-sample the total: batch completion handlers run before this and routinely
			// enqueue the next phase, so the total captured a moment ago can already be
			// short by however much they added.
			RefreshRunTotal();

			float progress = CurrentProgress;
			if (progress >= 1f)
			{
				progress = 0.999f;
			}
			RaiseProgress(progress);
		}
		#endregion

		#region PREFABS
		/// <summary>
		/// Loads a prefab asynchronously using an AssetReference. Invokes callback when load completes or fails.
		/// </summary>
		/// <param name="assetReference">The AssetReference to load.</param>
		/// <param name="onLoadComplete">Callback invoked with loaded GameObject or null on failure.</param>
		public static void LoadPrefabAsync(AssetReference assetReference, Action<GameObject> onLoadComplete)
		{
			if (assetReference == null || !assetReference.RuntimeKeyIsValid())
			{
				onLoadComplete?.Invoke(null);
				return;
			}

			object key = assetReference.RuntimeKey;

			if (loadedPrefabs.TryGetValue(key, out AsyncOperationHandle<GameObject> completedHandle))
			{
				if (completedHandle.IsValid() && completedHandle.Status == AsyncOperationStatus.Succeeded)
				{
					onLoadComplete?.Invoke(completedHandle.Result);
					return;
				}
				loadedPrefabs.Remove(key);
			}

			if (currentPrefabOperations.TryGetValue(key, out AsyncOperationHandle<GameObject> existingHandle))
			{
				if (existingHandle.IsValid())
				{
					if (existingHandle.IsDone)
					{
						onLoadComplete?.Invoke(existingHandle.Status == AsyncOperationStatus.Succeeded ? existingHandle.Result : null);
					}
					else
					{
						existingHandle.Completed += (op) =>
						{
							onLoadComplete?.Invoke(op.Status == AsyncOperationStatus.Succeeded ? op.Result : null);
						};
					}
					return;
				}

				Log.Warning("AddressableLoadProcessor", $"Found invalid handle for {key} in currentPrefabOperations. Removing and restarting load.");
				currentPrefabOperations.Remove(key);
			}

			AsyncOperationHandle<GameObject> newHandle = assetReference.LoadAssetAsync<GameObject>();

			currentPrefabOperations[key] = newHandle;

			newHandle.Completed += (op) =>
			{
				currentPrefabOperations.Remove(key);

				if (op.Status == AsyncOperationStatus.Succeeded)
				{
					Log.Debug("AddressableLoadProcessor", $"New Load Complete: {op.Result.name}");
					loadedPrefabs[key] = op;
					onLoadComplete?.Invoke(op.Result);
				}
				else
				{
					Log.Warning("AddressableLoadProcessor", $"Failed to load addressable {key}: {op.OperationException?.Message}");
					loadedPrefabs.Remove(key);
					onLoadComplete?.Invoke(null);
				}
			};
		}

		/// <summary>
		/// Unloads a prefab asset previously loaded by this processor. Ignores if asset is still loading or not managed.
		/// </summary>
		/// <param name="assetReference">The AssetReference to unload.</param>
		public static void UnloadPrefab(AssetReference assetReference)
		{
			if (assetReference == null)
			{
				return;
			}

			object key = assetReference.RuntimeKey;

			if (currentPrefabOperations.ContainsKey(key))
			{
				Log.Warning("AddressableLoadProcessor", $"Attempted to unload prefab {key} while it's still loading. Unload will be attempted after current load finishes.");
				return;
			}

			if (loadedPrefabs.TryGetValue(key, out var assetHandle))
			{
				if (assetHandle.IsValid())
				{
					Log.Debug("AddressableLoadProcessor", $"Unloading prefab: {(assetHandle.Result != null ? assetHandle.Result.name : key)}");
					Addressables.Release(assetHandle);
				}
				loadedPrefabs.Remove(key);
			}
		}
		#endregion

		#region UNLOAD
		/// <summary>
		/// Unload a specific asset by its key.
		/// </summary>
		public static void UnloadAssetByKey(AddressableAssetKey assetKey)
		{
			if (assetKey == null || !loadedAssets.TryGetValue(assetKey, out var assetHandle))
			{
				return;
			}

			if (assetHandle.IsValid())
			{
				if (assetHandle.Result != null)
				{
					foreach (var asset in assetHandle.Result)
					{
						if (asset == null)
						{
							continue;
						}
						try
						{
							OnAddressableUnloaded?.Invoke(asset);
						}
						catch (Exception ex)
						{
							Log.Error("AddressableLoadProcessor", $"OnAddressableUnloaded subscriber threw: {ex}");
						}
					}
				}

				Addressables.Release(assetHandle);
			}

			loadedAssets.Remove(assetKey);
			Log.Debug("AddressableLoadProcessor", $"Asset with key {assetKey} has been unloaded.");
		}

		/// <summary>
		/// Unloads multiple scenes asynchronously by their names.
		/// </summary>
		/// <param name="sceneNames">List of scene names to unload.</param>
		public static void UnloadSceneByLabelAsync(List<string> sceneNames)
		{
			if (sceneNames == null)
			{
				return;
			}
			foreach (string sceneName in sceneNames)
			{
				UnloadSceneByLabelAsync(sceneName);
			}
		}

		/// <summary>
		/// Unloads multiple scenes asynchronously using a list of AddressableSceneLoadData.
		/// </summary>
		/// <param name="sceneLoadData">List of scene load data objects to unload.</param>
		public static void UnloadSceneByLabelAsync(List<AddressableSceneLoadData> sceneLoadData)
		{
			if (sceneLoadData == null)
			{
				return;
			}
			foreach (AddressableSceneLoadData scene in sceneLoadData)
			{
				if (scene != null)
				{
					UnloadSceneByLabelAsync(scene.SceneName);
				}
			}
		}

		/// <summary>
		/// Unloads a scene asynchronously by its name. Invokes callback on completion or logs error on failure.
		/// </summary>
		/// <param name="sceneName">The name of the scene to unload.</param>
		public static void UnloadSceneByLabelAsync(string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName) || !loadedScenes.TryGetValue(sceneName, out var handle))
			{
				return;
			}

			// Drop the record up front so a re-load enqueued before the unload finishes is
			// not deduped away against a scene that is on its way out.
			loadedScenes.Remove(sceneName);

			if (!handle.IsValid())
			{
				try
				{
					OnSceneUnloaded?.Invoke(sceneName);
				}
				catch (Exception ex)
				{
					Log.Error("AddressableLoadProcessor", $"OnSceneUnloaded subscriber threw for '{sceneName}': {ex}");
				}
				return;
			}

			AsyncOperationHandle unloadHandle = Addressables.UnloadSceneAsync(handle, true);

			unloadHandle.Completed += (op) =>
			{
				if (op.Status != AsyncOperationStatus.Succeeded)
				{
					// The scene is still loaded, so put the record back. Leaving it removed
					// would let a later EnqueueLoad think the scene is absent and load a
					// second copy of it.
					Log.Warning("AddressableLoadProcessor", $"Failed to unload scene Addressable: {sceneName}. Restoring its loaded record.");
					if (handle.IsValid() && !loadedScenes.ContainsKey(sceneName))
					{
						loadedScenes[sceneName] = handle;
					}
					return;
				}

				try
				{
					OnSceneUnloaded?.Invoke(sceneName);
				}
				catch (Exception ex)
				{
					Log.Error("AddressableLoadProcessor", $"OnSceneUnloaded subscriber threw for '{sceneName}': {ex}");
				}
			};
		}

		/// <summary>
		/// Releases all loaded assets, scenes, and prefabs managed by this processor and
		/// returns it to a clean idle state.
		/// </summary>
		/// <remarks>
		/// Also clears the pending queues, retires outstanding batches, and resets
		/// <see cref="isProcessingQueue"/>. Stopping the drain coroutine without clearing
		/// that flag left <see cref="BeginProcessQueue"/> a permanent no-op, so nothing
		/// could ever load again in that session.
		/// </remarks>
		public static void ReleaseAllAssets()
		{
			// Latch first: everything below can run third-party callbacks, and any load
			// request they make must be refused rather than restarting the queue.
			isShuttingDown = true;

			if (helper != null)
			{
				helper.StopAllCoroutines();
			}

			isProcessingQueue = false;
			itemsCompletedThisRun = 0;
			itemsTotalThisRun = 0;

			waitingResourceValidationQueue.Clear();
			currentValidationOperations.Clear();
			operationQueue.Clear();
			currentOperations.Clear();
			sceneOperationQueue.Clear();
			currentSceneOperations.Clear();
			stagedAssetKeys.Clear();
			stagedSceneNames.Clear();

			/* Drop outstanding batches WITHOUT raising completion.
			 *
			 * Completing them here would run each waiting system's continuation — a
			 * bootstrap's OnCompletePreload leads straight to InitializePostload, enqueuing
			 * scenes and assets while the application is quitting. Nothing should advance
			 * during teardown, and the process is going away regardless, so the correct
			 * behaviour is to abandon them silently. (AbortActiveBatches, used when the
			 * drain coroutine cannot start mid-session, still completes them — there the
			 * caller must recover.) */
			if (activeBatches.Count > 0)
			{
				Log.Debug("AddressableLoadProcessor", $"Abandoning {activeBatches.Count} in-flight load batch(es) during shutdown.");
			}
			activeBatches.Clear();

			/* Scenes must be unloaded via UnloadSceneAsync, not released via the generic
			 * Release(). AddressablesImpl tracks live scene handles in a separate internal
			 * set (m_SceneInstances) that is ONLY cleared by the scene-unload completion
			 * path; a generic Release() disposes the handle's internal op without touching
			 * that set, leaving a stale entry behind. UnloadSceneAsync(handle,
			 * autoReleaseHandle:true) is the API that actually clears it, and it is
			 * fire-and-forget safe here: the process is already tearing down, so nothing
			 * needs to await its completion.
			 *
			 * This does NOT cover the Editor's "Exit Play Mode" teardown, and cannot.
			 * AddressablesImpl.Dispose releases m_resultToHandle and then m_SceneInstances,
			 * both of which hold every loaded scene handle, so it releases each one twice and
			 * throws "Attempting to use an invalid operation handle" on the second pass.
			 * Addressables subscribes its playModeStateChanged cleanup at
			 * InitializeOnLoad/SubsystemRegistration, so Dispose runs before this method is
			 * ever reached on ExitingPlayMode — every handle below is already invalid by then,
			 * and scene unload is asynchronous regardless, so there is no frame left for it to
			 * complete in. AddressablesPlayModeSceneHandleFix (Editor-only) handles that case
			 * by de-duplicating Addressables' own tracking before Dispose runs. */
			foreach (var sceneHandle in loadedScenes.Values)
			{
				if (sceneHandle.IsValid())
				{
					Addressables.UnloadSceneAsync(sceneHandle, true);
				}
			}
			loadedScenes.Clear();

			foreach (var assetList in loadedAssets.Values)
			{
				if (assetList.IsValid())
				{
					if (assetList.Result != null)
					{
						foreach (var asset in assetList.Result)
						{
							if (asset == null)
							{
								continue;
							}
							try
							{
								OnAddressableUnloaded?.Invoke(asset);
							}
							catch (Exception ex)
							{
								Log.Error("AddressableLoadProcessor", $"OnAddressableUnloaded subscriber threw: {ex}");
							}
						}
					}
					Addressables.Release(assetList);
				}
			}
			loadedAssets.Clear();

			foreach (var prefabHandle in loadedPrefabs.Values)
			{
				if (prefabHandle.IsValid())
				{
					Addressables.Release(prefabHandle);
				}
			}
			loadedPrefabs.Clear();
			currentPrefabOperations.Clear();
		}
		#endregion
	}
}
