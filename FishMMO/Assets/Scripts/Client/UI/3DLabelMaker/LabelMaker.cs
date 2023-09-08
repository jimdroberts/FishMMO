using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class LabelMaker : MonoBehaviour
	{
		private static Queue<Cached3DLabel> cache = new Queue<Cached3DLabel>();

		public Cached3DLabel LabelPrefab;

		public Cached3DLabel Display(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache)
		{
			if (LabelPrefab != null && cache != null)
			{
				Cached3DLabel label;
				if (!cache.TryDequeue(out label))
				{
					label = Instantiate(LabelPrefab);
				}
				label.Initialize(text, position, color, fontSize, persistTime, manualCache);
				label.gameObject.SetActive(true);
				return label;
			}
			return null;
		}

		internal static void Cache(Cached3DLabel label)
		{
			if (label == null)
			{
				return;
			}

			if (cache != null)
			{
				label.gameObject.SetActive(false);
				cache.Enqueue(label);
			}
			else
			{
				Destroy(label.gameObject);
			}
		}
	}
}