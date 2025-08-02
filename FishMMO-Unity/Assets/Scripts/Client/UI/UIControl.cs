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
		/// <summary>
		/// Default color for UI controls.
		/// </summary>
		public static readonly Color DEFAULT_COLOR = Hex.ColorNormalize(0.0f, 160.0f, 255.0f, 255.0f);
		/// <summary>
		/// Default color for selected UI controls.
		/// </summary>
		public static readonly Color DEFAULT_SELECTED_COLOR = Hex.ColorNormalize(0.0f, 255.0f, 255.0f, 255.0f);

		/// <summary>
		/// Reference to the main canvas containing this UI control.
		/// </summary>
		public Canvas MainCanvas;
		/// <summary>
		/// Reference to the canvas scaler for screen scaling.
		/// </summary>
		public CanvasScaler CanvasScaler;
		/// <summary>
		/// Main panel RectTransform for this UI control.
		/// </summary>
		public RectTransform MainPanel = null;
		/// <summary>
		/// Helper field to check input field focus status in UIManager.
		/// </summary>
		[Tooltip("Helper field to check input field focus status in UIManager.")]
		public TMP_InputField InputField = null;
		/// <summary>
		/// If true, UI starts open when created.
		/// </summary>
		public bool StartOpen = true;
		/// <summary>
		/// If true, UI cannot be closed.
		/// </summary>
		public bool IsAlwaysOpen = false;
		/// <summary>
		/// True if the UI currently has focus.
		/// </summary>
		public bool HasFocus = false;
		/// <summary>
		/// If true, puts the UI on top when selected.
		/// </summary>
		[Tooltip("Puts the UI on top.")]
		public bool FocusOnSelect = false;
		/// <summary>
		/// If true, closes the UI when quitting to menu.
		/// </summary>
		public bool CloseOnQuitToMenu = true;
		/// <summary>
		/// If true, closes the UI when Escape is pressed.
		/// </summary>
		[Tooltip("Closes the UI when Esc is pressed if true.")]
		public bool CloseOnEscape = false;

		/// <summary>
		/// If true, allows the UI to be dragged.
		/// </summary>
		[Header("Drag")]
		public bool CanDrag = false;
		/// <summary>
		/// If true, clamps the UI to the screen bounds when dragging.
		/// </summary>
		public bool ClampToScreen = true;
		/// <summary>
		/// Starting position of the UI control.
		/// </summary>
		private Vector2 startPosition;
		/// <summary>
		/// Offset used for dragging calculations.
		/// </summary>
		private Vector2 dragOffset = Vector2.zero;
		/// <summary>
		/// True if the UI is currently being dragged.
		/// </summary>
		private bool isDragging;

		/// <summary>
		/// Container that stores a reference to all InputFields on the UIControl. This is used internally to tab between Input Fields.
		/// </summary>
		private TMP_InputField[] inputFields;
		/// <summary>
		/// Index of the currently focused input field.
		/// </summary>
		private int currentInputFieldIndex = 0;
		/// <summary>
		/// Returns true if any input field on this control is focused.
		/// </summary>
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

		/// <summary>
		/// Event called when the UI loses focus.
		/// </summary>
		public Action OnLoseFocus;

		/// <summary>
		/// Reference to the injected Client instance.
		/// </summary>
		public Client Client { get; private set; }
		/// <summary>
		/// Cached transform of this UI control.
		/// </summary>
		public Transform Transform { get; private set; }
		/// <summary>
		/// Name of the UI control (maps to GameObject name).
		/// </summary>
		public string Name { get { return gameObject.name; } set { gameObject.name = value; } }
		/// <summary>
		/// True if the UI is currently visible.
		/// </summary>
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

		/// <summary>
		/// Called every frame. Handles tab navigation between input fields.
		/// </summary>
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

		/// <summary>
		/// Cycles focus to the next input field in the array, allowing tab navigation between fields.
		/// </summary>
		void CycleInputFields()
		{
			// Move to the next input field in the array
			currentInputFieldIndex = (currentInputFieldIndex + 1) % inputFields.Length;

			// Select the new input field
			SelectInputField(currentInputFieldIndex);
		}

		/// <summary>
		/// Selects the input field at the specified index and sets it as the active field in the EventSystem.
		/// </summary>
		/// <param name="index">Index of the input field to select.</param>
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

		/// <summary>
		/// Clamps the UI panel's position to the screen bounds, preventing it from being dragged off-screen.
		/// </summary>
		/// <param name="x">Target X position.</param>
		/// <param name="y">Target Y position.</param>
		/// <param name="ignoreDimensions">If true, clamps to screen edges without considering panel size.</param>
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

					// If using a CanvasScaler with screen size scaling, adjust for scale
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

		/// <summary>
		/// Called when quitting to the login menu. Stops all coroutines for this UI control.
		/// </summary>
		public virtual void OnQuitToLogin()
		{
			StopAllCoroutines();
		}

		/// <summary>
		/// Handles client quit-to-login event, toggles visibility and calls OnQuitToLogin.
		/// </summary>
		private void Client_OnQuitToLogin()
		{
			Visible = !CloseOnQuitToMenu;
			OnQuitToLogin();
		}

		/// <summary>
		/// Injects the Client instance for network/UI interaction. Handles cleanup and event registration.
		/// </summary>
		/// <param name="client">Client instance to inject.</param>
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

		/// <summary>
		/// Called when the Client is set via SetClient. Override to handle custom logic on client assignment.
		/// </summary>
		public virtual void OnClientSet() { }

		/// <summary>
		/// Called when the Client is unset or removed. Override to handle custom cleanup logic.
		/// </summary>
		public virtual void OnClientUnset() { }

		/// <summary>
		/// Called when the UIControl is destroyed. Cleans up input field listeners, client references, and unregisters from UIManager.
		/// </summary>
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

		/// <summary>
		/// Called by UIManager when this control is added to the circular buffer. Stores the buffer node reference.
		/// </summary>
		/// <param name="node">The circular buffer node for this control.</param>
		public void UIManager_OnAdd(CircularBuffer<UIControl>.Node node)
		{
			CurrentNode = node;
		}

		/// <summary>
		/// Called by UIManager when this control is removed from the circular buffer. Clears the buffer node reference.
		/// </summary>
		public void UIManager_OnRemove()
		{
			CurrentNode = null;
		}

		/// <summary>
		/// Called when the pointer enters the UI control. Sets HasFocus to true.
		/// </summary>
		/// <param name="eventData">Pointer event data.</param>
		public void OnPointerEnter(PointerEventData eventData)
		{
			HasFocus = true;
		}

		/// <summary>
		/// Called when the pointer exits the UI control. Sets HasFocus to false and invokes OnLoseFocus event.
		/// </summary>
		/// <param name="eventData">Pointer event data.</param>
		public void OnPointerExit(PointerEventData eventData)
		{
			HasFocus = false;

			OnLoseFocus?.Invoke();
		}

		/// <summary>
		/// Toggles the visibility of the UI control.
		/// </summary>
		public virtual void ToggleVisibility()
		{
			Visible = !Visible;
		}

		/// <summary>
		/// Shows the UI control if it is not already visible.
		/// </summary>
		public virtual void Show()
		{
			if (Visible)
			{
				return;
			}

			Visible = true;
		}

		/// <summary>
		/// Hides the UI control, unless IsAlwaysOpen is true.
		/// </summary>
		public virtual void Hide()
		{
			Hide(IsAlwaysOpen);
		}

		/// <summary>
		/// Hides the UI control, unless overrideIsAlwaysOpen is true.
		/// </summary>
		/// <param name="overrideIsAlwaysOpen">If true, prevents hiding the control.</param>
		public virtual void Hide(bool overrideIsAlwaysOpen)
		{
			if (overrideIsAlwaysOpen)
			{
				return;
			}

			Visible = false;
		}

		/// <summary>
		/// Resets the UI control's position to its starting position.
		/// </summary>
		public virtual void OnResetPosition()
		{
			Transform.position = startPosition;
		}

		/// <summary>
		/// Called when the pointer is pressed down on the UI control. Handles drag and focus logic.
		/// </summary>
		/// <param name="data">Pointer event data.</param>
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

		/// <summary>
		/// Brings the UI control to the front of its parent and re-registers for Escape-close if needed.
		/// </summary>
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

		/// <summary>
		/// Called when the pointer is released on the UI control. Ends drag operation.
		/// </summary>
		/// <param name="data">Pointer event data.</param>
		public void OnPointerUp(PointerEventData data)
		{
			if (!CanDrag) return;

			isDragging = false;
			dragOffset = Vector2.zero;
		}

		/// <summary>
		/// Called when the UI control is dragged. Updates position if dragging is enabled.
		/// </summary>
		/// <param name="data">Pointer event data.</param>
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

		/// <summary>
		/// Resets the UI control's position and drag state to the initial values.
		/// </summary>
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