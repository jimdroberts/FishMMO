using TMPro;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UICastBar : UIControl
	{
		public Slider slider;
		public TMP_Text castText;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void OnUpdate(float remainingTime, float totalTime)
		{
			if (!Visible)
			{
				Visible = true;
			}

			float value = remainingTime / totalTime;
			if (slider != null) slider.value = value;
			if (castText != null) castText.text = remainingTime + "/" + totalTime;
		}

		public void OnCancel()
		{
			Visible = false;
		}
	}
}