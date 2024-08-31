using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using TMPro;

namespace FishMMO.Client
{
	public class UICursor : UIControl
	{
		private GraphicRaycaster rayCaster;
		public Color highlightColor = Color.green;
		private Graphic currentHighlighted;
		private Color originalColor;
		private GameObject draggingObject; // Track the currently dragged object

		public override void OnStarting()
		{
			rayCaster = MainCanvas.GetComponent<GraphicRaycaster>();

			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;

			MainPanel.transform.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
		}

		public override void OnDestroying()
		{
		}

		void Update()
		{
			UpdatePosition();
			HighlightUIElementUnderMouse();
		}

		private void UpdatePosition()
		{
			Vector3 mousePosition = new Vector3(Input.GetAxis("Mouse X") * 20.0f,
							    Input.GetAxis("Mouse Y") * 20.0f,
							    0.0f);

			Vector3 position = MainPanel.transform.position + mousePosition;
			ClampUIToScreen(position.x, position.y, true);
		}

		void HighlightUIElementUnderMouse()
		{
			PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
			{
				position = MainPanel.transform.position
			};

			List<RaycastResult> raycastResults = new List<RaycastResult>();
			rayCaster.Raycast(pointerEventData, raycastResults);

			GameObject newHighlightedObject = raycastResults.Count > 0 ? raycastResults[0].gameObject : null;

			if (newHighlightedObject != null)
			{
				// Traverse up the hierarchy to find a parent with Button or InputField component
				Button button = newHighlightedObject.GetComponentInParent<Button>();
				InputField inputField = newHighlightedObject.GetComponentInParent<InputField>();
				TMP_InputField tmpInputField = newHighlightedObject.GetComponentInParent<TMP_InputField>();

				if (button != null)
				{
					HandleExecuteEvents(newHighlightedObject, button.gameObject, pointerEventData);
				}
				else if (inputField != null)
				{
					HandleExecuteEvents(newHighlightedObject, inputField.gameObject, pointerEventData);
				}
				else if (tmpInputField != null)
				{
					HandleExecuteEvents(newHighlightedObject, tmpInputField.gameObject, pointerEventData);
				}
			}
			else if (currentHighlighted != null)
			{
				// Reset highlight if no UI element is under the mouse
				ResetHighlight(currentHighlighted);
				ExecuteEvents.Execute(currentHighlighted.gameObject, pointerEventData, ExecuteEvents.endDragHandler);
				ExecuteEvents.Execute(currentHighlighted.gameObject, pointerEventData, ExecuteEvents.pointerExitHandler);
				ExecuteEvents.Execute(currentHighlighted.gameObject, pointerEventData, ExecuteEvents.deselectHandler);
				currentHighlighted = null;
			}
		}

		void HandleExecuteEvents(GameObject newHighlightObject, GameObject target, PointerEventData pointerEventData)
		{
			Graphic newHighlighted = newHighlightObject.GetComponent<Graphic>();

			if (newHighlighted != null)
			{
				if (currentHighlighted != newHighlighted)
				{
					// Reset highlight on the previously highlighted element
					if (currentHighlighted != null)
					{
						ResetHighlight(currentHighlighted);
						ExecuteEvents.Execute(currentHighlighted.gameObject, pointerEventData, ExecuteEvents.pointerExitHandler);
					}

					// Highlight the new element
					Highlight(newHighlighted);
					ExecuteEvents.Execute(newHighlighted.gameObject, pointerEventData, ExecuteEvents.pointerEnterHandler);
					currentHighlighted = newHighlighted;
				}
			}

			// Handle drag events
			if (Input.GetMouseButtonDown(0))
			{
				pointerEventData.button = PointerEventData.InputButton.Left;
				pointerEventData.pressPosition = pointerEventData.position;
				draggingObject = target;
				ExecuteEvents.Execute(target, pointerEventData, ExecuteEvents.pointerDownHandler);
				ExecuteEvents.Execute(target, pointerEventData, ExecuteEvents.pointerClickHandler);
				ExecuteEvents.Execute(target, pointerEventData, ExecuteEvents.selectHandler);
				ExecuteEvents.Execute(target, pointerEventData, ExecuteEvents.beginDragHandler);
			}

			if (Input.GetMouseButton(0) && draggingObject != null)
			{
				pointerEventData.position = Input.mousePosition;
				ExecuteEvents.Execute(draggingObject, pointerEventData, ExecuteEvents.dragHandler);
			}

			if (Input.GetMouseButtonUp(0) && draggingObject != null)
			{
				ExecuteEvents.Execute(draggingObject, pointerEventData, ExecuteEvents.endDragHandler);
				draggingObject = null;
			}

			if (Input.GetKeyDown(KeyCode.Return))
			{
				ExecuteEvents.Execute(target, pointerEventData, ExecuteEvents.submitHandler);
			}
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				ExecuteEvents.Execute(target, pointerEventData, ExecuteEvents.cancelHandler);
			}
		}

		void Highlight(Graphic graphic)
		{
			originalColor = graphic.color;
			graphic.color = highlightColor;
		}

		void ResetHighlight(Graphic graphic)
		{
			graphic.color = originalColor;
		}
	}
}