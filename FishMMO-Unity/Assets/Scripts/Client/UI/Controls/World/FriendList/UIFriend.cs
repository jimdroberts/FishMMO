using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component representing a friend entry in the friend list.
	/// </summary>
	public class UIFriend : MonoBehaviour
	{
		/// <summary>
		/// The character ID of the friend.
		/// </summary>
		[NonSerialized]
		public long FriendID;

		/// <summary>
		/// The name text field for the friend.
		/// </summary>
		public TMP_Text Name;

		/// <summary>
		/// The status text field for the friend (e.g., Online/Offline).
		/// </summary>
		public TMP_Text Status;

		/// <summary>
		/// The button used to trigger friend actions.
		/// </summary>
		public Button Button;

		/// <summary>
		/// Event invoked when the friend is removed.
		/// </summary>
		public Action<long> OnRemoveFriend;

		/// <summary>
		/// Handles the remove friend button click. Invokes the OnRemoveFriend event.
		/// </summary>
		public void OnButtonRemoveFriend()
		{
			OnRemoveFriend?.Invoke(FriendID);
		}
	}
}
