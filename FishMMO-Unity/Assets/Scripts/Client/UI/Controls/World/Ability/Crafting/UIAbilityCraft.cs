using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	public class UIAbilityCraft : UIControl
	{
		public UIAbilityCraftEntry MainEntry;
		public TMP_Text AbilityDescription; 
		public RectTransform AbilityEventParent;
		public UIAbilityCraftEntry AbilityEventPrefab;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}
	}
}