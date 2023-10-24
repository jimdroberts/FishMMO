using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIAbilities : UIControl
	{
		public RectTransform AbilityParent;
		public UIAbilityEntry AbilityEntryPrefab;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}
	}
}