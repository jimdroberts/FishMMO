using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UICastBar : UIControl
	{
		public Slider slider;
		public TMP_Text castText;

		private float targetTotalTime;
		private float elapsedTime = 0f;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		private void Update()
		{
			if (slider != null)
			{
				float smoothedValue = Mathf.SmoothStep(1.0f, 0.0f, Mathf.Clamp01(elapsedTime / targetTotalTime));

				slider.value = smoothedValue;

				if (elapsedTime < targetTotalTime)
				{
					elapsedTime += Time.deltaTime;
				}
				else
				{
					Hide();
					elapsedTime = 0f;
				}
			}
		}

		public void OnUpdate(string label, float remainingTime, float totalTime)
		{
			if (remainingTime <= 0.001f)
			{
				Hide();

				return;
			}

			if (!Visible)
			{
				Show();
			}

			targetTotalTime = totalTime;
			elapsedTime = totalTime - remainingTime;

			if (castText != null) castText.text = label;
		}

		public void OnCancel()
		{
			Hide();
		}
	}
}