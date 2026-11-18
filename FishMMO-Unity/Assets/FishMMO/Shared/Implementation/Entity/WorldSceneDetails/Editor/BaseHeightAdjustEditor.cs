using UnityEngine;
using UnityEditor;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract Unity editor class for adjusting the height of selected GameObjects in the scene view using mouse events and raycasting.
	/// </summary>
	public abstract class BaseHeightAdjustEditor : Editor
	{
		/// <summary>
		/// The GameObject that was clicked and is being adjusted.
		/// </summary>
		private GameObject clickedObject;

		/// <summary>
		/// Called by Unity to draw the scene GUI. Handles mouse events for height adjustment.
		/// </summary>
		protected void OnSceneGUI()
		{
			HandleMouseEvents();
		}

		/// <summary>
		/// Handles mouse down and up events to trigger height adjustment logic.
		/// </summary>
		protected void HandleMouseEvents()
		{
			Event currentEvent = Event.current;
			EventType eventType = currentEvent.type;

			switch (eventType)
			{
				case EventType.MouseDown:
					// Left mouse button down: select the first GameObject in the current selection.
					if (currentEvent.button == 0)
					{
						for (int i = 0; i < Selection.objects.Length; i++)
						{
							Object obj = Selection.objects[i];
							if (obj != null)
							{
								clickedObject = obj as GameObject;
								break;
							}
						}

						HandleMouseDown(clickedObject);
					}
					break;
				case EventType.MouseUp:
					// Left mouse button up: adjust height if an object was previously clicked.
					if (currentEvent.button == 0 && clickedObject != null)
					{
						HandleMouseUp(clickedObject);
						clickedObject = null;
					}
					break;
			}
		}

		/// <summary>
		/// Called when the left mouse button is pressed on a GameObject. Can be overridden for custom logic.
		/// </summary>
		/// <param name="clickedObject">The GameObject that was clicked.</param>
		protected virtual void HandleMouseDown(GameObject clickedObject)
		{
			if (clickedObject == null)
			{
				return;
			}
			// Optional: Add custom logic for mouse down event.
		}

		/// <summary>
		/// Called when the left mouse button is released on a GameObject. Performs a sphere cast to adjust the object's height to the hit point.
		/// </summary>
		/// <param name="clickedObject">The GameObject that was released.</param>
		protected virtual void HandleMouseUp(GameObject clickedObject)
		{
			if (clickedObject == null)
			{
				return;
			}

			// Get the object's current position.
			Vector3 clickedObjectPosition = clickedObject.transform.position;

			float distanceAbove = 2f; // Height above the object to start the raycast.

			Vector3 raycastStart = clickedObjectPosition + Vector3.up * distanceAbove;

			float raycastDistance = 5f; // Distance to cast the ray downwards.

			RaycastHit hitInfo;
			// Perform a sphere cast downwards to find the ground or surface below the object.
			if (Physics.SphereCast(raycastStart, 1.0f, Vector3.down, out hitInfo, raycastDistance))
			{
				Debug.Log("Raycast hit object: " + hitInfo.collider.gameObject.name);
				Debug.DrawLine(raycastStart, hitInfo.point, Color.red, 0.5f); // Visualize the raycast

				// Move the object to the hit point, slightly above the surface.
				clickedObject.transform.position = hitInfo.point + new Vector3(0.0f, 0.1f, 0.0f);
			}
			else
			{
				// If no hit, draw a green line to indicate the raycast path.
				Debug.DrawLine(raycastStart, raycastStart + Vector3.down * raycastDistance, Color.green, 0.5f);
			}
		}
	}
}