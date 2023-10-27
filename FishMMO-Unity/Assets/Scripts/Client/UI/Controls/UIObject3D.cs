using UnityEngine;

namespace FishMMO.Client
{
	public class UIObject3D : MonoBehaviour
	{
		public bool isVisible = false;
		public bool isFade = false;

		public Vector2 size = Vector2.zero;
		public GUIStyle style = new GUIStyle();
		public Vector2 center = Vector2.zero;
		public Vector2 pixelOffset = Vector2.zero;

		//fade
		public float fadeStartTime = 0.0f;
		public float fadeTime = 2.0f;
		public float oldY = 0.0f;
		public float increaseY = 200.0f;
		public Color oldColor = Color.clear;
		public Color clearColor = Color.clear;

		//bounce
		public float bounce = 0.0f;
		public float bounceDecay = 0.1f;

		void Update()
		{
			if (isFade)
			{
				float t = Mathf.Clamp((Time.time - fadeStartTime) / fadeTime, 0.0f, 1.0f);
				if (t >= 1.0f)
				{
					Destroy(this.gameObject);
				}
				Color c = style.normal.textColor;
				c.a = Mathf.Lerp(oldColor.a, clearColor.a, t);
				style.normal.textColor = c;
				pixelOffset.y = Mathf.Lerp(oldY, increaseY, t);
			}
		}

		public void Setup(Vector2 pixelOffset)
		{
			Setup(pixelOffset, isFade);
		}

		public void Setup(Vector2 pixelOffset, bool isFade)
		{
			center = new Vector2(size.x * 0.5f, size.y * 0.5f);
			this.pixelOffset = pixelOffset;
			this.isFade = isFade;
			isVisible = true;
			if (!isFade)
			{
				return;
			}
			oldY = pixelOffset.y;
			increaseY = oldY + increaseY;
			fadeStartTime = Time.time;
			oldColor = style.normal.textColor;
		}
	}
}