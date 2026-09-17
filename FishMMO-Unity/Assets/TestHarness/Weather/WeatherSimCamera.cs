using UnityEngine;
using UnityEngine.InputSystem;

namespace FishMMO.TestHarness.Weather
{
	/// <summary>A fly camera for the weather test bed: right mouse to look, WASD/QE to move, Shift to hurry.</summary>
	public sealed class WeatherSimCamera : MonoBehaviour
	{
		public float Speed = 8f;
		public float LookSpeed = 0.15f;

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
			if (mouse != null && mouse.rightButton.isPressed)
			{
				Vector2 delta = mouse.delta.ReadValue();
				yaw += delta.x * LookSpeed;
				pitch = Mathf.Clamp(pitch - delta.y * LookSpeed, -89f, 89f);
				transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
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
			float speed = Speed * (keyboard.leftShiftKey.isPressed ? 4f : 1f);
			transform.Translate(move * speed * Time.deltaTime, Space.Self);
		}
	}
}
