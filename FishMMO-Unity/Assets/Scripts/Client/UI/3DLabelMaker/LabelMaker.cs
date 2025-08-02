using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class LabelMaker : MonoBehaviour
	{
		/// <summary>
		/// Singleton instance of LabelMaker.
		/// </summary>
		private static LabelMaker instance;
		/// <summary>
		/// Internal accessor for the singleton instance.
		/// </summary>
		internal static LabelMaker Instance
		{
			get
			{
				return instance;
			}
		}
		/// <summary>
		/// Pool of cached 3D labels for reuse.
		/// </summary>
		private Queue<Cached3DLabel> pool = new Queue<Cached3DLabel>();

		/// <summary>
		/// Prefab used to instantiate new 3D labels.
		/// </summary>
		public Cached3DLabel LabelPrefab3D;

		/// <summary>
		/// Unity Awake method. Initializes the singleton instance and sets the object name.
		/// </summary>
		void Awake()
		{
			if (instance != null)
			{
				Destroy(this.gameObject);
				return;
			}
			instance = this;

			gameObject.name = typeof(LabelMaker).Name;
		}

		/// <summary>
		/// Retrieves a label from the pool or instantiates a new one if the pool is empty.
		/// </summary>
		/// <param name="label">The dequeued or newly instantiated label.</param>
		/// <returns>True if a label is provided, false otherwise.</returns>
		public bool Dequeue(out Cached3DLabel label)
		{
			if (LabelPrefab3D != null && pool != null)
			{
				if (!pool.TryDequeue(out label))
				{
					label = Instantiate(LabelPrefab3D);
					label.gameObject.SetActive(true);
				}
				return true;
			}
			label = null;
			return false;
		}

		/// <summary>
		/// Returns a label to the pool for reuse, or destroys it if the pool is unavailable.
		/// </summary>
		/// <param name="label">The label to enqueue.</param>
		public void Enqueue(Cached3DLabel label)
		{
			if (pool != null)
			{
				label.gameObject.SetActive(false);
				pool.Enqueue(label);
			}
			else
			{
				Destroy(label.gameObject);
			}
		}

		/// <summary>
		/// Clears all cached labels from the pool and destroys their game objects.
		/// </summary>
		public void ClearCache()
		{
			if (pool == null ||
				pool.Count < 1)
			{
				return;
			}
			while (pool.TryDequeue(out Cached3DLabel label))
			{
				Destroy(label.gameObject);
			}
		}

		/// <summary>
		/// Displays a 3D label at the specified position with the given properties.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="position">World position for the label.</param>
		/// <param name="color">Text color.</param>
		/// <param name="fontSize">Font size.</param>
		/// <param name="persistTime">Time to persist the label.</param>
		/// <param name="manualCache">Whether to manually cache the label after use.</param>
		/// <returns>The displayed label, or null if unavailable.</returns>
		public static Cached3DLabel Display3D(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache)
		{
			if (LabelMaker.Instance.Dequeue(out Cached3DLabel label))
			{
				label.Initialize(text, position, color, fontSize, persistTime, manualCache);
				return label;
			}
			return null;
		}

		/// <summary>
		/// Caches the given label for reuse.
		/// </summary>
		/// <param name="label">The label to cache.</param>
		public static void Cache(Cached3DLabel label)
		{
			if (label == null)
			{
				return;
			}

			LabelMaker.Instance.Enqueue(label);
		}

		/// <summary>
		/// Clears all cached labels from the pool.
		/// </summary>
		public static void Clear()
		{
			LabelMaker.Instance.ClearCache();
		}
	}
}