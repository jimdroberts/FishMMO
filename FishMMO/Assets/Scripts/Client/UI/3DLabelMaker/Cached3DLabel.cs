using UnityEngine;
using TMPro;

namespace FishMMO.Client
{
	public sealed class Cached3DLabel : MonoBehaviour
	{
		private bool manualCache;
		private float remainingTime;

		public TextMeshPro TMP;

		void Update()
		{
			if (!manualCache)
			{
				remainingTime -= Time.deltaTime;
				if (remainingTime < 0.0f)
				{
					LabelMaker.Cache(this);
					return;
				}
			}
		}

		public void Initialize(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache)
		{
			TMP.transform.position = position;
			TMP.text = text;
			TMP.fontSize = fontSize;
			TMP.color = color;
			remainingTime = persistTime;
			this.manualCache = manualCache;
		}

		public void SetPosition(Vector3 position)
		{
			transform.position = position;
		}

		public void SetText(string text)
		{
			if (TMP != null)
			{
				TMP.text = text;
			}
		}
	}
}
