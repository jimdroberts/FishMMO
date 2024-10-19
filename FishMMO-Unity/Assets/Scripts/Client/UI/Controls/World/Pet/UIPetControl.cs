using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
    public class UIPetControl : UICharacterControl
    {
        public TMP_Text PetNameLabel;
        public Slider PetHealth;
        public Button AttackButton;
        public Button StayButton;
        public Button FollowButton;
        public Button BanishButton;
        public Button SummonButton;
        public Button ReleaseButton;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}
    }
}