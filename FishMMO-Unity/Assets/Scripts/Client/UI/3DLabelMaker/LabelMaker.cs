using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages a pool of reusable <see cref="Cached3DLabel"/> instances for efficient 3D text display in world space.
	/// </summary>
	public class LabelMaker : MonoBehaviour
	{
		/// <summary>
		/// Singleton instance of LabelMaker.
		/// </summary>
		private static LabelMaker instance;
		/// <summary>
		/// Singleton instance accessor.
		/// </summary>
		internal static LabelMaker Instance => instance;

		/// <summary>
		/// Pool of cached 3D labels for reuse.
		/// </summary>
		private readonly Queue<Cached3DLabel> pool = new Queue<Cached3DLabel>();

		/// <summary>
		/// Prefab used to instantiate new 3D labels.
		/// </summary>
		[SerializeField]
		private Cached3DLabel labelPrefab;

		/// <summary>
		/// Initial number of labels to pre-instantiate into the pool on startup.
		/// </summary>
		[SerializeField]
		[Min(0)]
		private int preWarmCount;

		/// <summary>
		/// Maximum number of labels allowed in the pool. Zero means unlimited.
		/// </summary>
		[SerializeField]
		[Min(0)]
		private int maxPoolSize;

		/// <summary>
		/// Initializes the singleton instance and pre-warms the pool.
		/// </summary>
		void Awake()
		{
			if (instance != null)
			{
				Destroy(gameObject);
				return;
			}
			instance = this;
			gameObject.name = nameof(LabelMaker);
			PreWarm();
		}

		/// <summary>
		/// Clears the pool and releases the singleton reference on destruction.
		/// </summary>
		void OnDestroy()
		{
			ClearCache();
			if (instance == this)
			{
				instance = null;
			}
		}

		/// <summary>
		/// Pre-instantiates labels into the pool for immediate availability.
		/// </summary>
		private void PreWarm()
		{
			if (labelPrefab == null) return;

			for (int i = 0; i < preWarmCount; ++i)
			{
				Cached3DLabel label = Instantiate(labelPrefab);
				label.gameObject.SetActive(false);
				pool.Enqueue(label);
			}
		}

		/// <summary>
		/// Retrieves a label from the pool or instantiates a new one if the pool is empty.
		/// Skips any pool entries that have been externally destroyed.
		/// </summary>
		/// <param name="label">The dequeued or newly instantiated label.</param>
		/// <returns>True if a label is provided, false otherwise.</returns>
		public bool Dequeue(out Cached3DLabel label)
		{
			if (labelPrefab == null)
			{
				label = null;
				return false;
			}

			while (pool.TryDequeue(out label))
			{
				if (label != null)
				{
					return true;
				}
			}

			label = Instantiate(labelPrefab);
			return true;
		}

		/// <summary>
		/// Returns a label to the pool for reuse, or destroys it if the pool has reached maximum capacity.
		/// </summary>
		/// <param name="label">The label to enqueue.</param>
		public void Enqueue(Cached3DLabel label)
		{
			if (label == null) return;

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
			while (pool.TryDequeue(out Cached3DLabel label))
			{
				if (label != null)
				{
					Destroy(label.gameObject);
				}
			}
		}

		/// <summary>
		/// Displays a 3D label at the specified position with the given properties.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="position">World position for the label.</param>
		/// <param name="color">Text color.</param>
		/// <param name="fontSize">Font size in Unity units.</param>
		/// <param name="persistTime">Duration in seconds before the label is automatically cached. Ignored if manualCache is true.</param>
		/// <param name="manualCache">If true, the label must be cached manually via <see cref="Cache"/>.</param>
		/// <param name="effectFlags">Bit-flag field of <see cref="LabelEffect"/> values. 0 for no effects.</param>
		/// <returns>The displayed label, or null if the instance is unavailable.</returns>
		public static Cached3DLabel Display3D(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache, int effectFlags = 0)
		{
			if (instance == null) return null;

			if (instance.Dequeue(out Cached3DLabel label))
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
		public static void Cache(Cached3DLabel label)
		{
			if (label == null || instance == null) return;

			instance.Enqueue(label);
		}

		/// <summary>
		/// Clears all cached labels from the pool.
		/// </summary>
		public static void Clear()
		{
			if (instance == null) return;

			instance.ClearCache();
		}
	}
}