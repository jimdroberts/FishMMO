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

		public void OnUpdate(string label, float remainingTime, float totalTime)
		{
			if (!Visible)
			{
				Visible = true;
			}

			float value = remainingTime / totalTime;
			if (slider != null) slider.value = value;
			if (castText != null) castText.text = label;
		}

		public void OnCancel()
		{
			Visible = false;
		}
	}
}