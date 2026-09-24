using UnityEngine;
using UnityEngine.InputSystem;

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// The world test bed's camera: right mouse to look, WASD/QE to move, Shift to hurry, scroll to
	/// change the field of view (a narrow view is how you check a moon's real size), R to put it back.
	/// </summary>
	/// <remarks>
	/// The weather bed had a plainer version of this with no zoom and no floor under it, which meant
	/// the same fly-around behaved differently depending on which scene you happened to be in. This
	/// is the one that could do everything both needed.
	/// </remarks>
	public sealed class WorldSimCamera : MonoBehaviour
	{
		public float Speed = 10f;
		public float LookSpeed = 0.15f;
		[Tooltip("Metres above the ground under it that the camera never sinks below, so a fly-around cannot end up inside the terrain. From the ground, not from y = 0: y is altitude, so a scene cut from the sea floor has all its ground below zero and one cut from a plateau all of it far above.")]
		public float MinimumHeight = 0.5f;

		private static readonly System.Collections.Generic.List<Terrain> terrains = new System.Collections.Generic.List<Terrain>();
		public float DefaultFieldOfView = 60f;

		private float yaw;
		private float pitch;

		private void Start()
		{
			Vector3 euler = transform.eulerAngles;
			yaw = euler.y;
			pitch = euler.x > 180f ? euler.x - 360f : euler.x;
		}

		private void Update()
		{
			Mouse mouse = Mouse.current;
			Keyboard keyboard = Keyboard.current;
			var camera = GetComponent<Camera>();
			if (mouse != null)
			{
				if (mouse.rightButton.isPressed)
				{
					Vector2 delta = mouse.delta.ReadValue();
					yaw += delta.x * LookSpeed;
					pitch = Mathf.Clamp(pitch - delta.y * LookSpeed, -89f, 89f);
					transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
				}
				float scroll = mouse.scroll.ReadValue().y;
				// Over the panel the wheel scrolls the panel. It used to do both.
				if (camera != null && Mathf.Abs(scroll) > 0.01f && !WorldSimPanel.PointerOverPanel)
				{
					camera.fieldOfView = Mathf.Clamp(camera.fieldOfView - scroll * 0.02f, 8f, 90f);
				}
			}
			// While a value is being typed the keys are digits and letters for the box, and R is not
			// a request to reset the zoom.
			if (keyboard == null || WorldSimPanel.TypingInPanel)
			{
				return;
			}
			var move = Vector3.zero;
			if (keyboard.wKey.isPressed) move += Vector3.forward;
			if (keyboard.sKey.isPressed) move += Vector3.back;
			if (keyboard.aKey.isPressed) move += Vector3.left;
			if (keyboard.dKey.isPressed) move += Vector3.right;
			if (keyboard.eKey.isPressed) move += Vector3.up;
			if (keyboard.qKey.isPressed) move += Vector3.down;
			if (move.sqrMagnitude > 0f)
			{
				float speed = Speed * (keyboard.leftShiftKey.isPressed ? 4f : 1f);
				Vector3 position = transform.position + transform.TransformDirection(move.normalized) * speed * Time.deltaTime;
				position.y = Mathf.Max(GroundBelow(position) + MinimumHeight, position.y);
				transform.position = position;
			}
			// The camera's own rotation stays level: the sky turns, not the horizon.
			if (keyboard.rKey.wasPressedThisFrame && camera != null)
			{
				camera.fieldOfView = DefaultFieldOfView;
			}
		}

		/// <summary>World Y of the terrain under a point; negative infinity off the edge of every tile.</summary>
		private static float GroundBelow(Vector3 position)
		{
			Terrain.GetActiveTerrains(terrains);
			for (int i = 0; i < terrains.Count; i++)
			{
				Terrain terrain = terrains[i];
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (position.x >= origin.x && position.x <= origin.x + size.x && position.z >= origin.z && position.z <= origin.z + size.z)
				{
					return origin.y + terrain.SampleHeight(position);
				}
			}
			return float.NegativeInfinity;
		}
	}
}
