using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIHotkeyGroup : MonoBehaviour
	{
		public UIHotkeyButton Button;
		public TMP_Text Label;
		public Slider CooldownMask;

		private void Awake()
		{
			if (Button != null &&
				CooldownMask != null)
			{
				Button.CooldownMask = CooldownMask;
				CooldownMask.value = 0;
			}
		}

		private void OnDestroy()
		{
			if (Button != null)
			{
				Button.CooldownMask = null;
			}
		}
	}
}