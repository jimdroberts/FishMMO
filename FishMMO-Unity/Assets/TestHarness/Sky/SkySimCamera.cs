using UnityEngine;
using UnityEngine.InputSystem;

namespace FishMMO.TestHarness.Sky
{
	/// <summary>
	/// The sky test bed's camera: right mouse to look, WASD/QE to move, Shift to hurry, scroll to
	/// change the field of view (a narrow view is how you check a moon's real size).
	/// </summary>
	public sealed class SkySimCamera : MonoBehaviour
	{
		public float Speed = 10f;
		public float LookSpeed = 0.15f;
		public float MinimumHeight = 0.5f;

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
				if (camera != null && Mathf.Abs(scroll) > 0.01f)
				{
					camera.fieldOfView = Mathf.Clamp(camera.fieldOfView - scroll * 0.02f, 8f, 90f);
				}
			}
			if (keyboard == null)
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
				position.y = Mathf.Max(MinimumHeight, position.y);
				transform.position = position;
			}
			// The camera's own rotation stays level: the sky turns, not the horizon.
			if (keyboard.rKey.wasPressedThisFrame && camera != null)
			{
				camera.fieldOfView = 60f;
			}
		}
	}
}
