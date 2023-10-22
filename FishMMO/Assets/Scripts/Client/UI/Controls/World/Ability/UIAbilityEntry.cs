using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	public class UIAbilityEntry : MonoBehaviour
	{
		[NonSerialized]
		public long AbilityID;
		public Image Icon;
		public TMP_Text Name;
		public TMP_Text Description;
		public Button ForgetButton;
		public Action<long> OnForget;

		public void OnButtonForgetAbility()
		{
			OnForget?.Invoke(AbilityID);
		}
	}
}
