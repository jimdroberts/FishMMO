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
				pool.Enqueue(CreateLabel());
			}
		}

		/// <summary>
		/// Retrieves a label from the pool or builds a new one if the pool is empty.
		/// Skips any pool entries that have been externally destroyed.
		/// </summary>
		/// <param name="label">The dequeued or newly created label.</param>
		/// <returns>True if a label is provided, false otherwise.</returns>
		public bool Dequeue(out UITKWorldLabel label)
		{
			while (pool.TryDequeue(out label))
			{
				if (label != null)
				{
					return true;
				}
			}

			label = CreateLabel();
			return true;
		}

		/// <summary>
		/// Returns a label to the pool for reuse, or destroys it if the pool has reached maximum capacity.
		/// </summary>
		/// <param name="label">The label to enqueue.</param>
		public void Enqueue(UITKWorldLabel label)
		{
			if (label == null)
			{
				return;
			}

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
		/// Clears all cached labels from the pool and destroys their game objects.
		/// </summary>
		public void ClearCache()
		{
			while (pool.TryDequeue(out UITKWorldLabel label))
			{
				if (label != null)
				{
					Destroy(label.gameObject);
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
		/// <returns>The displayed label, or null if the instance is unavailable.</returns>
		public static UITKWorldLabel Display3D(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache, int effectFlags = 0)
		{
			if (instance == null)
			{
				return null;
			}

			if (instance.Dequeue(out UITKWorldLabel label))
			{
				label.Initialize(text, position, color, fontSize, persistTime, manualCache, effectFlags);
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
