using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class LabelMaker : MonoBehaviour
	{
		private static Queue<Cached3DLabel> cache = new Queue<Cached3DLabel>();

		public Cached3DLabel LabelPrefab;

		public void Display(Vector3 position, Color color, float persistTime, string text)
		{
			if (LabelPrefab == null)
			{
				return;
			}
			if (cache != null)
			{
				Cached3DLabel label;
				if (!cache.TryDequeue(out label))
				{
					label = Instantiate(LabelPrefab);
				}
				label.Initialize(position, color, persistTime, text);
				label.gameObject.SetActive(true);
			}
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