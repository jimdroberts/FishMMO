using UnityEngine;
using UnityEditor;

namespace FishMMO.Shared
{
	public abstract class BaseHeightAdjustEditor : Editor
	{
		private GameObject clickedObject;

		protected void OnSceneGUI()
		{
			HandleMouseEvents();
		}

		protected void HandleMouseEvents()
		{
			Event currentEvent = Event.current;
			EventType eventType = currentEvent.type;

			switch (eventType)
			{
				case EventType.MouseDown:
					if (currentEvent.button == 0) // Left mouse button down
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
					if (currentEvent.button == 0 && clickedObject != null) // Left mouse button up and object was previously clicked
					{
						HandleMouseUp(clickedObject);
						clickedObject = null;
					}
					break;
			}
		}

		protected virtual void HandleMouseDown(GameObject clickedObject)
		{
			if (clickedObject == null)
			{
				return;
			}
			//Debug.Log("Mouse click detected on: " + clickedObject.name);
		}

		protected virtual void HandleMouseUp(GameObject clickedObject)
		{
			if (clickedObject == null)
			{
				return;
			}

			//Debug.Log("Mouse release detected on: " + clickedObject.name);

			Vector3 clickedObjectPosition = clickedObject.transform.position;

			float distanceAbove = 2f; // Adjust this value as needed

			Vector3 raycastStart = clickedObjectPosition + Vector3.up * distanceAbove;

			float raycastDistance = 5f;

			RaycastHit hitInfo;
			if (Physics.SphereCast(raycastStart, 1.0f, Vector3.down, out hitInfo, raycastDistance))
			{
				Debug.Log("Raycast hit object: " + hitInfo.collider.gameObject.name);
				Debug.DrawLine(raycastStart, hitInfo.point, Color.red, 0.5f); // Visualize the raycast

				clickedObject.transform.position = hitInfo.point + new Vector3(0.0f, 0.1f, 0.0f);
			}
			else
			{
				Debug.DrawLine(raycastStart, raycastStart + Vector3.down * raycastDistance, Color.green, 0.5f);
			}
		}
	}
}