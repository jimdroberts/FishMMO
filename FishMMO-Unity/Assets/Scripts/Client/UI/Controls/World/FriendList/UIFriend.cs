using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	public class UIFriend : MonoBehaviour
	{
		[NonSerialized]
		public long FriendID;
		public TMP_Text Name;
		public TMP_Text Status;
		public Button Button; 
		public Action<long> OnRemoveFriend;

		public void OnButtonRemoveFriend()
		{
			OnRemoveFriend?.Invoke(FriendID);
		}
	}
}
