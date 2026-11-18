using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages a pool of reusable <see cref="UITKWorldLabel"/> instances for efficient world-anchored
	/// text, drawn by <see cref="UITKWorldLabelLayer"/>.
	/// </summary>
	/// <remarks>
	/// The UI Toolkit successor to <c>LabelMaker</c>. The pooling contract is unchanged — the same
	/// Dequeue/Enqueue/Display3D/Cache surface, so combat display and target frame code carries
	/// over with only the type names swapped.
	///
	/// What changed is that a pooled label no longer needs a prefab. The old one carried a
	/// TextMeshPro component, a MeshRenderer and a material, so it had to be authored as an asset;
	/// a label is now a transform plus two plain components, which this builds on demand. That
	/// removes the "labels silently do nothing because the prefab reference was lost" failure the
	/// old pool had, and takes <c>Cached3DLabel.prefab</c> out of the project with it.
	///
	/// <para><b>The budget.</b> The pool used to be bounded and nothing else was: the cap only
	/// limited how many <em>idle</em> labels were kept, and <see cref="Dequeue"/> built a new
	/// GameObject whenever the pool ran dry. Nothing bounded how many labels could be live at once
	/// or how fast they could be created, so a large area-of-effect hit — or a server sending
	/// combat events faster than a client can draw them — allocated GameObjects without limit
	/// until the client stalled. That is reachable from the network, so it is a denial of service
	/// and not merely a performance note.</para>
	///
	/// <para>Two limits close it. <see cref="maxLiveLabels"/> caps how many labels exist at once;
	/// past the cap the oldest auto-expiring label is recycled instead of a new one being built,
	/// which for damage numbers is also the behaviour a player wants — the newest hit is the one
	/// worth reading. <see cref="maxSpawnsPerSecond"/> is a token bucket that caps the rate, so a
	/// burst that would churn the whole cap in one frame is dropped rather than served. Labels the
	/// caller holds itself (<c>manualCache</c>, used by nameplates and the target frame) are never
	/// recycled out from under it.</para>
	///
	/// <para>The draw half of the budget lives in <see cref="UITKWorldLabelLayer"/>; both are
	/// needed, because capping the draw alone still leaves the GameObjects allocated.</para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class UITKLabelMaker : MonoBehaviour
	{
		/// <summary>
		/// Singleton instance of UITKLabelMaker.
		/// </summary>
		private static UITKLabelMaker instance;

		/// <summary>
		/// Singleton instance accessor.
		/// </summary>
		internal static UITKLabelMaker Instance => instance;

		/// <summary>
		/// Pool of cached labels for reuse.
		/// </summary>
		private readonly Queue<UITKWorldLabel> pool = new Queue<UITKWorldLabel>();

		/// <summary>
		/// Every label currently handed out, in the order it was handed out.
		/// </summary>
		/// <remarks>
		/// A list rather than a queue because a label can be returned from anywhere in it — a
		/// timed label expiring, or a caller cancelling a manual one — and the recycling policy
		/// needs to scan from the oldest end for the first eligible victim rather than blindly
		/// taking the head.
		/// </remarks>
		private readonly List<UITKWorldLabel> live = new List<UITKWorldLabel>();

		/// <summary>
		/// Initial number of labels to pre-instantiate into the pool on startup.
		/// </summary>
		[SerializeField]
		[Min(0)]
		private int preWarmCount = 16;

		/// <summary>
		/// Maximum number of labels allowed in the pool. Zero means unlimited.
		/// </summary>
		[SerializeField]
		[Min(0)]
		private int maxPoolSize = 64;

		/// <summary>
		/// Maximum number of labels that may be live at once. Zero means unlimited.
		/// </summary>
		[SerializeField]
		[Tooltip("Hard cap on simultaneously displayed labels. The oldest timed label is recycled past this. 0 = unlimited.")]
		[Min(0)]
		private int maxLiveLabels = 128;

		/// <summary>
		/// Maximum number of labels that may be created per second. Zero means unlimited.
		/// </summary>
		[SerializeField]
		[Tooltip("Rate limit on label creation, as a token bucket. 0 = unlimited.")]
		[Min(0)]
		private int maxSpawnsPerSecond = 90;

		/// <summary>
		/// Remaining spawn allowance in the current bucket.
		/// </summary>
		private float spawnTokens;

		/// <summary>
		/// Initializes the singleton instance and pre-warms the pool.
		/// </summary>
		private void Awake()
		{
			if (instance != null)
			{
				Destroy(gameObject);
				return;
			}
			instance = this;
			gameObject.name = nameof(UITKLabelMaker);
			spawnTokens = maxSpawnsPerSecond;
			PreWarm();
		}

		/// <summary>
		/// Clears the pool and releases the singleton reference on destruction.
		/// </summary>
		private void OnDestroy()
		{
			ClearCache();
			if (instance == this)
			{
				instance = null;
			}
		}

		/// <summary>
		/// Refills the spawn allowance and drives every live label's effect timeline.
		/// </summary>
		/// <remarks>
		/// The effect timeline used to run from a <c>MonoBehaviour.Update</c> on each label, which
		/// meant Unity paid the managed-to-native dispatch cost once per label per frame — the
		/// single most expensive thing about a screen full of damage numbers, and entirely
		/// avoidable given the pool already knows exactly which labels are live. Driving them from
		/// one place also makes the "expired" transition ordinary code rather than a component
		/// caching itself from inside its own update.
		/// <para>
		/// Iterated backwards because <see cref="Enqueue"/> removes from <see cref="live"/>.
		/// </para>
		/// </remarks>
		private void Update()
		{
			float dt = Time.deltaTime;

			if (maxSpawnsPerSecond > 0)
			{
				spawnTokens = Mathf.Min(maxSpawnsPerSecond, spawnTokens + (maxSpawnsPerSecond * dt));
			}

			for (int i = live.Count - 1; i >= 0; --i)
			{
				UITKWorldLabel label = live[i];
				if (label == null)
				{
					// Destroyed behind the pool's back; drop the slot rather than leak it.
					live.RemoveAt(i);
					continue;
				}

				if (label.Tick(dt))
				{
					Enqueue(label);
				}
			}
		}

		/// <summary>
		/// Builds one pooled label object.
		/// </summary>
		/// <returns>A new, inactive label.</returns>
		/// <remarks>
		/// Parented to this pool so the scene hierarchy does not fill with loose label objects,
		/// and created inactive so <see cref="WorldLabel"/> does not register with the render layer
		/// until the label is actually handed out.
		/// </remarks>
		private UITKWorldLabel CreateLabel()
		{
			GameObject go = new GameObject("WorldLabel", typeof(WorldLabel), typeof(UITKWorldLabel));
			go.transform.SetParent(transform, false);
			go.SetActive(false);
			return go.GetComponent<UITKWorldLabel>();
		}

		/// <summary>
		/// Pre-instantiates labels into the pool for immediate availability.
		/// </summary>
		private void PreWarm()
		{
			for (int i = 0; i < preWarmCount; ++i)
			{
				UITKWorldLabel label = CreateLabel();
				label.MarkPooled();
				pool.Enqueue(label);
			}
		}

		/// <summary>
		/// Retrieves a label from the pool, recycles the oldest timed label when the live cap is
		/// reached, or builds a new one.
		/// </summary>
		/// <param name="label">The dequeued, recycled or newly created label.</param>
		/// <param name="persistent">
		/// True for a label the caller will hold and cache itself. Persistent labels bypass the
		/// rate limit, because the budget exists to bound transient combat spam and a nameplate
		/// that silently fails to appear because a damage burst exhausted the bucket is a worse
		/// outcome than the spam it was protecting against. They are still subject to the live cap.
		/// </param>
		/// <returns>True if a label is provided, false when the budget refuses one.</returns>
		/// <remarks>
		/// Returning false is a normal outcome, not an error: it is how the rate limit and the
		/// live cap are enforced, and <see cref="Display3D"/> turns it into a dropped label rather
		/// than a stall.
		/// </remarks>
		public bool Dequeue(out UITKWorldLabel label, bool persistent = false)
		{
			// Rate limit first, so a burst cannot churn the whole live cap in a single frame.
			if (maxSpawnsPerSecond > 0 && !persistent)
			{
				if (spawnTokens < 1.0f)
				{
					label = null;
					return false;
				}
				spawnTokens -= 1.0f;
			}

			while (pool.TryDequeue(out label))
			{
				if (label != null)
				{
					return true;
				}
			}

			if (maxLiveLabels > 0 && live.Count >= maxLiveLabels)
			{
				/* At the cap, so something has to give. Recycling the oldest timed label is
				 * preferable to refusing the new one: the newest damage number is the one the
				 * player is trying to read, and the oldest is already most of the way through
				 * fading out. Manually cached labels are skipped — those are nameplates and
				 * target frames that a caller still holds a reference to, and pulling one out
				 * from under its owner would leave the owner pointing at a reused label. */
				for (int i = 0; i < live.Count; ++i)
				{
					UITKWorldLabel candidate = live[i];
					if (candidate == null)
					{
						live.RemoveAt(i);
						--i;
						continue;
					}
					if (candidate.IsManuallyCached)
					{
						continue;
					}

					Enqueue(candidate);
					if (pool.TryDequeue(out label) && label != null)
					{
						return true;
					}
					break;
				}

				// Every live label belongs to a caller; refuse rather than exceed the cap.
				if (live.Count >= maxLiveLabels)
				{
					label = null;
					return false;
				}
			}

			label = CreateLabel();
			label.MarkPooled();
			return true;
		}

		/// <summary>
		/// Returns a label to the pool for reuse, or destroys it if the pool has reached maximum capacity.
		/// </summary>
		/// <param name="label">The label to enqueue.</param>
		/// <remarks>
		/// Re-entrant by design. The old version enqueued unconditionally, so a label that was
		/// cached twice — trivially reachable, because the expiry path cached it and a caller
		/// holding the same reference could cache it again — sat in the pool twice and was then
		/// handed to two different callers at once, each overwriting the other's text. The pooled
		/// flag on the label makes the second call a no-op.
		/// </remarks>
		public void Enqueue(UITKWorldLabel label)
		{
			if (label == null || label.IsPooled)
			{
				return;
			}

			label.MarkPooled();
			live.Remove(label);

			// Deactivating deregisters the WorldLabel, which drops its element from the layer.
			label.gameObject.SetActive(false);

			if (maxPoolSize > 0 && pool.Count >= maxPoolSize)
			{
				Destroy(label.gameObject);
				return;
			}

			pool.Enqueue(label);
		}

		/// <summary>
		/// Records a label as live, so the pool can drive its effects and enforce the live cap.
		/// </summary>
		/// <param name="label">The label that was just initialized.</param>
		internal void MarkLive(UITKWorldLabel label)
		{
			if (label == null)
			{
				return;
			}
			live.Add(label);
		}

		/// <summary>
		/// Clears all cached labels from the pool and destroys their game objects.
		/// </summary>
		/// <remarks>
		/// Live labels are released first. Leaving them out would strand every label currently on
		/// screen: nothing else holds them, so they would tick forever against a pool that no
		/// longer knows about them.
		/// </remarks>
		public void ClearCache()
		{
			for (int i = live.Count - 1; i >= 0; --i)
			{
				UITKWorldLabel label = live[i];
				if (label != null)
				{
					Enqueue(label);
				}
			}
			live.Clear();

			while (pool.TryDequeue(out UITKWorldLabel pooled))
			{
				if (pooled != null)
				{
					Destroy(pooled.gameObject);
				}
			}
		}

		/// <summary>
		/// Displays a world-anchored label at the specified position with the given properties.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="position">World position for the label.</param>
		/// <param name="color">Text color.</param>
		/// <param name="fontSize">Font size in world units, scaled by distance at render time.</param>
		/// <param name="persistTime">Duration in seconds before the label is automatically cached. Ignored if manualCache is true.</param>
		/// <param name="manualCache">If true, the label must be cached manually via <see cref="Cache"/>.</param>
		/// <param name="effectFlags">Bit-flag field of <see cref="LabelEffect"/> values. 0 for no effects.</param>
		/// <returns>The displayed label, or null if the instance is unavailable or the budget refused one.</returns>
		public static UITKWorldLabel Display3D(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache, int effectFlags = 0)
		{
			if (instance == null)
			{
				return null;
			}

			if (instance.Dequeue(out UITKWorldLabel label, manualCache))
			{
				label.Initialize(text, position, color, fontSize, persistTime, manualCache, effectFlags);
				instance.MarkLive(label);
				return label;
			}
			return null;
		}

		/// <summary>
		/// Returns the given label to the pool for reuse.
		/// </summary>
		/// <param name="label">The label to cache.</param>
		public static void Cache(UITKWorldLabel label)
		{
			if (label == null || instance == null)
			{
				return;
			}

			instance.Enqueue(label);
		}

		/// <summary>
		/// Clears all cached labels from the pool.
		/// </summary>
		public static void Clear()
		{
			if (instance == null)
			{
				return;
			}

			instance.ClearCache();
		}
	}
}
