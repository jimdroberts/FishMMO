using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class LabelMaker : MonoBehaviour
	{
		private static LabelMaker instance;
		internal static LabelMaker Instance
		{
			get
			{
				return instance;
			}
		}
		private Queue<Cached3DLabel> pool = new Queue<Cached3DLabel>();

		public Cached3DLabel LabelPrefab3D;

		void Awake()
		{
			if (instance != null)
			{
				Destroy(this.gameObject);
				return;
			}
			instance = this;

			gameObject.name = typeof(LabelMaker).Name;

			DontDestroyOnLoad(this.gameObject);
		}

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

		public static Cached3DLabel Display3D(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache)
		{
			if (LabelMaker.Instance.Dequeue(out Cached3DLabel label))
			{
				label.Initialize(text, position, color, fontSize, persistTime, manualCache);
				label.gameObject.SetActive(true);
				return label;
			}
			return null;
		}

		public static void Cache(Cached3DLabel label)
		{
			if (label == null)
			{
				return;
			}

			LabelMaker.Instance.Enqueue(label);
		}

		public static void Clear()
		{
			LabelMaker.Instance.ClearCache();
		}
	}
}