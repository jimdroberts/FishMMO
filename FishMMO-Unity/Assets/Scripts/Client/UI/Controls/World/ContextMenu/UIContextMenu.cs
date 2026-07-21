using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// A floating context menu that displays a dynamic list of action buttons at the mouse position.
	/// Used for right-click interactions on player targets (Inspect, Add Friend, Invite to Party, Trade, etc.).
	/// </summary>
	public class UIContextMenu : UIControl
	{
		/// <summary>
		/// Prefab for each menu button. Must have a Button and TMP_Text child.
		/// </summary>
		[Tooltip("Prefab for each menu button. Must have a Button and TMP_Text child.")]
		public GameObject ButtonPrefab;

		/// <summary>
		/// Parent transform where buttons are instantiated. Should have a VerticalLayoutGroup.
		/// </summary>
		[Tooltip("Parent transform where buttons are instantiated.")]
		public RectTransform ButtonParent;

		/// <summary>
		/// Pool of instantiated button GameObjects to reduce allocation.
		/// </summary>
		private readonly List<GameObject> buttonPool = new List<GameObject>();

		/// <summary>
		/// Called when the control is starting. Hides the context menu by default.
		/// </summary>
		public override void OnStarting()
		{
		}

		/// <summary>
		/// Called when the control is being destroyed. Clears button pool.
		/// </summary>
		public override void OnDestroying()
		{
			ClearButtons();
		}

		/// <summary>
		/// Opens the context menu at the current mouse position with the specified entries.
		/// Each entry is a label and callback pair.
		/// </summary>
		/// <param name="entries">List of (label, callback) pairs for the menu buttons.</param>
		public void Open(List<(string label, Action callback)> entries)
		{
			if (entries == null || entries.Count == 0)
			{
				return;
			}

			ClearButtons();

			for (int i = 0; i < entries.Count; ++i)
			{
				GameObject buttonObj = GetOrCreateButton();
				buttonObj.SetActive(true);

				TMP_Text label = buttonObj.GetComponentInChildren<TMP_Text>();
				if (label != null)
				{
					label.text = entries[i].label;
				}

				Button button = buttonObj.GetComponent<Button>();
				if (button != null)
				{
					// Capture by value for the lambda
					Action action = entries[i].callback;
					button.onClick.RemoveAllListeners();
					button.onClick.AddListener(() =>
					{
						action?.Invoke();
						Hide();
					});
				}
			}

			// Position at mouse
			if (MainPanel != null)
			{
				Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

				RectTransformUtility.ScreenPointToLocalPointInRectangle(
					MainCanvas.transform as RectTransform,
					mousePos,
					MainCanvas.worldCamera,
					out Vector2 localPoint);

				MainPanel.anchoredPosition = localPoint;
			}

			Show();
		}

		/// <summary>
		/// Hides all buttons without destroying them, returning them to the pool.
		/// </summary>
		private void ClearButtons()
		{
			for (int i = 0; i < buttonPool.Count; ++i)
			{
				if (buttonPool[i] != null)
				{
					Button button = buttonPool[i].GetComponent<Button>();
					if (button != null)
					{
						button.onClick.RemoveAllListeners();
					}
					buttonPool[i].SetActive(false);
				}
			}
		}

		/// <summary>
		/// Returns an inactive button from the pool, or instantiates a new one if none are available.
		/// </summary>
		private GameObject GetOrCreateButton()
		{
			for (int i = 0; i < buttonPool.Count; ++i)
			{
				if (buttonPool[i] != null && !buttonPool[i].activeSelf)
				{
					return buttonPool[i];
				}
			}

			if (ButtonPrefab == null || ButtonParent == null)
			{
				return null;
			}

			GameObject newButton = Instantiate(ButtonPrefab, ButtonParent);
			buttonPool.Add(newButton);
			return newButton;
		}

		/// <summary>
		/// Closes the context menu when clicking outside of it.
		/// </summary>
		void Update()
		{
			if (!Visible)
			{
				return;
			}

			// Close on left click outside the menu
			Mouse mouse = Mouse.current;
			if (mouse != null && mouse.leftButton.wasPressedThisFrame && !HasFocus)
			{
				Hide();
			}
		}
	}
}