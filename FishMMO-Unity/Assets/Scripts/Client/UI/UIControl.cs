using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public abstract class UIControl : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IDragHandler
	{
		public static readonly Color DEFAULT_COLOR = Hex.ColorNormalize(0.0f, 160.0f, 255.0f, 255.0f);
		public static readonly Color DEFAULT_SELECTED_COLOR = Hex.ColorNormalize(0.0f, 255.0f, 255.0f, 255.0f);

		public Canvas MainCanvas;
		public CanvasScaler CanvasScaler;
		public RectTransform MainPanel = null;
		[Tooltip("Helper field to check input field focus status in UIManager.")]
		public TMP_InputField InputField = null;
		public bool StartOpen = true;
		public bool IsAlwaysOpen = false;
		public bool HasFocus = false;
		[Tooltip("Puts the UI on top.")]
		public bool FocusOnSelect = false;
		public bool CloseOnQuitToMenu = true;
		[Tooltip("Closes the UI when Esc is pressed if true.")]
		public bool CloseOnEscape = false;

		[Header("Drag")]
		public bool CanDrag = false;
		public bool ClampToScreen = true;
		private Vector2 startPosition;
		private Vector2 dragOffset = Vector2.zero;
		private bool isDragging;

		/// <summary>
		/// Container that stores a reference to all InputFields on the UIControl. This is used internally to tab between Input Fields.
		/// </summary>
		private TMP_InputField[] inputFields;
		private int currentInputFieldIndex = 0;
		public bool IsInputFieldFocused
		{
			get
			{
				if (inputFields != null && inputFields.Length > 0)
				{
					for (int i = 0; i < inputFields.Length; ++i)
					{
						if (inputFields[i].isFocused)
						{
							return true;
						}
					}
				}
				return false;
			}
		}

		public Action OnLoseFocus;

		public Client Client { get; private set; }
		public Transform Transform { get; private set; }
		public string Name { get { return gameObject.name; } set { gameObject.name = value; } }
		public bool Visible
		{
			get
			{
				return gameObject.activeSelf;
			}
			private set
			{
				gameObject.SetActive(value);
				if (value)
				{
					if (CloseOnEscape)
					{
						UIManager.RegisterCloseOnEscapeUI(this);
						InputManager.MouseMode = true;
					}
					if (FocusOnSelect)
					{
						OnFocus();
					}
				}
				if (!value)
				{
					if (CloseOnEscape)
					{
						UIManager.UnregisterCloseOnEscapeUI(this);
					}
					if (HasFocus)
					{
						EventSystem.current.SetSelectedGameObject(null);
						EventSystem.current.sendNavigationEvents = false;
						HasFocus = false;
					}
				}
			}
		}
		/// <summary>
		/// This field is used by the UIManager's circle buffer to assist in opening/closing UI windows.
		/// </summary>
		internal CircularBuffer<UIControl>.Node CurrentNode { get; set; }

		private void Awake()
		{
			UIManager.Register(this);

			Transform = transform;
			MainCanvas = GetComponentInParent<Canvas>();
			CanvasScaler = GetComponentInParent<CanvasScaler>();

			startPosition = transform.position;

			if (MainPanel == null)
			{
				MainPanel = transform as RectTransform;
			}

			inputFields = MainPanel.GetComponentsInChildren<TMP_InputField>(true);
			if (inputFields != null && inputFields.Length > 0)
			{
				for (int i = 0; i < inputFields.Length; i++)
				{
					int index = i;  // Capture the current index in the closure
					inputFields[i].onSelect.AddListener((string text) => { currentInputFieldIndex = index; });
				}
			}

			OnStarting();

			if (!StartOpen)
			{
				Hide();
			}
			AdjustPositionForPivotChange(MainPanel, new Vector2(0.5f, 0.5f));
			AdjustPositionForAnchorChange(MainPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
		}

		/// <summary>
		/// Called at the end of the MonoBehaviour Awake function.
		/// </summary>
		public virtual void OnStarting() { }

		/// <summary>
		/// Adjusts the RectTransform's local position to keep its visual position the same when its pivot is changed.
		/// </summary>
		/// <param name="rectTransform">The RectTransform to adjust.</param>
		/// <param name="newPivot">The new pivot value (0 to 1 in X and Y).</param>
		public void AdjustPositionForPivotChange(RectTransform rectTransform, Vector2 newPivot)
		{
			if (rectTransform == null)
			{
				Log.Error("UIControl", "RectTransform is null. Cannot adjust position for pivot change.");
				return;
			}

			Vector2 oldPivot = rectTransform.pivot;
			Vector2 oldAnchoredPosition = rectTransform.anchoredPosition;

			// Apply the new pivot
			rectTransform.pivot = newPivot;

			// Calculate the pivot shift relative to the RectTransform's current size
			Vector2 pivotShift = newPivot - oldPivot;
			Vector2 pivotOffset = new Vector2(rectTransform.rect.width * pivotShift.x, rectTransform.rect.height * pivotShift.y);

			// Adjust anchoredPosition to compensate for the pivot change.
			// If the pivot moves right, the anchoredPosition needs to move left to keep the content still.
			rectTransform.anchoredPosition = oldAnchoredPosition - pivotOffset;
		}

		/// <summary>
		/// Adjusts the RectTransform's position to keep its visual position the same when its anchors are changed.
		/// </summary>
		/// <param name="rectTransform">The RectTransform to adjust.</param>
		/// <param name="newAnchorMin">The new minimum anchor value (0 to 1 in X and Y).</param>
		/// <param name="newAnchorMax">The new maximum anchor value (0 to 1 in X and Y).</param>
		public void AdjustPositionForAnchorChange(RectTransform rectTransform, Vector2 newAnchorMin, Vector2 newAnchorMax)
		{
			if (rectTransform == null)
			{
				Log.Error("UIControl", "RectTransform is null. Cannot adjust position for anchor change.");
				return;
			}

			// Store the current world position of the RectTransform's pivot.
			// This is the most reliable point to maintain.
			Vector3 oldWorldPivotPosition = rectTransform.position;

			// Store original sizeDelta to restore it if the element is fixed size
			// (i.e., anchors are not stretching).
			Vector2 oldSizeDelta = rectTransform.sizeDelta;

			// Apply the new anchors
			rectTransform.anchorMin = newAnchorMin;
			rectTransform.anchorMax = newAnchorMax;

			// After changing anchors, set the world position back.
			// Unity will internally recalculate anchoredPosition and sizeDelta
			// to achieve this world position with the new anchor/pivot configuration.
			rectTransform.position = oldWorldPivotPosition;

			// If the RectTransform is not set to stretch (anchors are not different),
			// we often want to preserve its original sizeDelta to prevent unwanted resizing.
			// If anchors are different, sizeDelta will adjust to maintain the world position.
			if (rectTransform.anchorMin.x == rectTransform.anchorMax.x &&
				rectTransform.anchorMin.y == rectTransform.anchorMax.y)
			{
				rectTransform.sizeDelta = oldSizeDelta;
			}
		}

		void Update()
		{
			if (inputFields != null && inputFields.Length > 0)
			{
				if (Input.GetKeyDown(KeyCode.Tab))
				{
					CycleInputFields();
				}
			}
		}

		void CycleInputFields()
		{
			// Move to the next input field in the array
			currentInputFieldIndex = (currentInputFieldIndex + 1) % inputFields.Length;

			// Select the new input field
			SelectInputField(currentInputFieldIndex);
		}

		void SelectInputField(int index)
		{
			if (index < 0 || index >= inputFields.Length || EventSystem.current == null)
			{
				return;
			}

			TMP_InputField selectedField = inputFields[index];
			if (EventSystem.current.currentSelectedGameObject != selectedField.gameObject)
			{
				EventSystem.current.SetSelectedGameObject(selectedField.gameObject);
				selectedField.OnSelect(null);
			}
		}

		/*void LateUpdate()
		{
			if (nextPump < 0)
			{
				nextPump = updateRate;

				ClampUIToScreen(Transform.position.x, Transform.position.y);
			}
			nextPump -= Time.deltaTime;
		}*/

		public void ClampUIToScreen(float x, float y, bool ignoreDimensions = false)
		{
			if (!ClampToScreen) return;

			if (MainPanel != null)
			{
				if (ignoreDimensions)
				{
					x = Mathf.Clamp(x, 0.0f, Screen.width);
					y = Mathf.Clamp(y, 0.0f, Screen.height);
				}
				else
				{
					float halfWidth = MainPanel.rect.width * 0.5f;
					float halfHeight = MainPanel.rect.height * 0.5f;

					if (CanvasScaler != null &&
						CanvasScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
					{
						halfWidth *= CanvasScaler.transform.localScale.x;
						halfHeight *= CanvasScaler.transform.localScale.y;
					}

					x = Mathf.Clamp(x, halfWidth, Screen.width - halfWidth);
					y = Mathf.Clamp(y, halfHeight, Screen.height - halfHeight);
				}
			}
			Transform.position = new Vector2(x, y);
		}

		public virtual void OnQuitToLogin()
		{
			StopAllCoroutines();
		}

		private void Client_OnQuitToLogin()
		{
			Visible = !CloseOnQuitToMenu;
			OnQuitToLogin();
		}

		/// <summary>
		/// Dependency injection for the Client.
		/// </summary>
		public void SetClient(Client client)
		{
			// Unset previous client.
			if (Client != null)
			{
				OnClientUnset();
				Client.OnQuitToLogin -= Client_OnQuitToLogin;
				Client = null;
			}

			// Set new client.
			if (client != null)
			{
				Client = client;
				Client.OnQuitToLogin += Client_OnQuitToLogin;
				OnClientSet();
			}
		}

		public virtual void OnClientSet() { }

		public virtual void OnClientUnset() { }

		private void OnDestroy()
		{
			if (inputFields != null && inputFields.Length > 0)
			{
				for (int i = 0; i < inputFields.Length; i++)
				{
					int index = i;
					inputFields[i].onSelect.RemoveListener((string text) => { currentInputFieldIndex = index; });
				}
			}
			inputFields = null;

			OnDestroying();
			if (Client != null)
			{
				OnClientUnset();
				Client.OnQuitToLogin -= Client_OnQuitToLogin;
			}
			Client = null;

			if (CloseOnEscape)
			{
				UIManager.UnregisterCloseOnEscapeUI(this);
			}
			UIManager.Unregister(this);
		}

		/// <summary>
		/// Called at the start of the MonoBehaviour OnDestroy function.
		/// </summary>
		public virtual void OnDestroying() { }

		public void UIManager_OnAdd(CircularBuffer<UIControl>.Node node)
		{
			CurrentNode = node;
		}

		public void UIManager_OnRemove()
		{
			CurrentNode = null;
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			HasFocus = true;
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			HasFocus = false;

			OnLoseFocus?.Invoke();
		}

		public virtual void ToggleVisibility()
		{
			Visible = !Visible;
		}

		public virtual void Show()
		{
			if (Visible)
			{
				return;
			}

			Visible = true;
		}

		public virtual void Hide()
		{
			Hide(IsAlwaysOpen);
		}

		public virtual void Hide(bool overrideIsAlwaysOpen)
		{
			if (overrideIsAlwaysOpen)
			{
				return;
			}

			Visible = false;
		}

		public virtual void OnResetPosition()
		{
			Transform.position = startPosition;
		}

		public void OnPointerDown(PointerEventData data)
		{
			if (data == null)
			{
				return;
			}
			if (MainPanel != null &&
				RectTransformUtility.ScreenPointToLocalPointInRectangle(MainPanel, data.pressPosition, data.pressEventCamera, out dragOffset))
			{
				// reset parent transform, this will focus the UI Control
				if (FocusOnSelect)
				{
					OnFocus();
				}

				if (CanDrag)
				{
					if (CanvasScaler != null &&
					CanvasScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
					{
						dragOffset.x *= CanvasScaler.transform.localScale.x;
						dragOffset.y *= CanvasScaler.transform.localScale.y;
					}

					isDragging = true;
				}
			}
			else
			{
				dragOffset = Vector2.zero;
			}
		}

		private void OnFocus()
		{
			Transform parent = Transform.parent;
			Vector3 pos = Transform.position;
			Transform.SetParent(null);
			Transform.SetParent(parent);
			Transform.position = pos;

			if (CloseOnEscape)
			{
				UIManager.UnregisterCloseOnEscapeUI(this);
				UIManager.RegisterCloseOnEscapeUI(this);
			}
		}

		public void OnPointerUp(PointerEventData data)
		{
			if (!CanDrag) return;

			isDragging = false;
			dragOffset = Vector2.zero;
		}

		public void OnDrag(PointerEventData data)
		{
			if (!CanDrag) return;

			if (isDragging)
			{
				float x = data.position.x - dragOffset.x;
				float y = data.position.y - dragOffset.y;

				ClampUIToScreen(x, y);
			}
		}

		public void ResetPosition()
		{
			Transform.position = startPosition;
			dragOffset = Vector2.zero;
			isDragging = false;
		}

		/*public virtual void OnButtonEnter()
		{
			AudioClip clip;
			if (InternalResourceCache.TryGetAudioClip("uibeep", out clip))
			{
				GUIAudioSource.PlayOneShot(clip);
			}
		}

		public virtual void OnButtonClick()
		{
			AudioClip clip;
			if (InternalResourceCache.TryGetAudioClip("uibeep", out clip))
			{
				GUIAudioSource.PlayOneShot(clip);
			}
		}*/
	}
}