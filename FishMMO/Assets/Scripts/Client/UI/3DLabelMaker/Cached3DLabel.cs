using UnityEngine;
using TMPro;

namespace FishMMO.Client
{
	public class Cached3DLabel : MonoBehaviour
	{
		private float remainingTime;

		public TextMeshPro TMP;

		void Update()
		{
			if (remainingTime < 0.0f)
			{
				LabelMaker.Cache(this);
				return;
			}
			remainingTime -= Time.deltaTime;
		}

		public void Initialize(Vector3 position, Color color, float persistTime, string text)
		{
			TMP.transform.position = position;
			TMP.color = color;
			TMP.text = text;
			remainingTime = persistTime;
		}
	}
}
