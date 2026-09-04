using UnityEngine;
using UnityEngine.InputSystem;

namespace FishMMO.Client
{
	/// <summary>
	/// A free-flying camera for a game master spectating an arena match.
	/// </summary>
	/// <remarks>
	/// <para>
	/// While <see cref="Active"/>, the player input controller stops driving the third-person
	/// camera and stops moving the spectator's own character, and this component flies the main
	/// camera instead: hold the right mouse button to look, WASD to move, Q/E for down and up,
	/// Shift for speed. Switching it off simply lets the character camera take the transform back
	/// on its next frame; nothing has to be restored.
	/// </para>
	/// <para>
	/// Client-side only: where a spectator's camera is has no bearing on anything the server
	/// tracks. The team registry already reports a spectator as an ally to everyone, so the free
	/// camera grants no advantage a seated player could use — the HUD only offers it to a player
	/// with no seat in the match.
	/// </para>
	/// </remarks>
	public sealed class ArenaSpectatorCamera : MonoBehaviour
	{
		/// <summary>Whether a spectator camera is flying the main camera right now.</summary>
		public static bool Active { get; private set; }

		private static ArenaSpectatorCamera instance;

		/// <summary>Metres per second at the base speed.</summary>
		public float MoveSpeed = 12.0f;

		/// <summary>Multiplier while Shift is held.</summary>
		public float FastMultiplier = 3.0f;

		/// <summary>Degrees per pixel of mouse movement.</summary>
		public float LookSensitivity = 0.15f;

		private float yaw;
		private float pitch;

		/// <summary>Starts flying the main camera from where it is.</summary>
		public static void Enable()
		{
			Camera camera = Camera.main;
			if (camera == null)
			{
				return;
			}
			if (instance == null)
			{
				instance = camera.gameObject.GetComponent<ArenaSpectatorCamera>() ?? camera.gameObject.AddComponent<ArenaSpectatorCamera>();
			}
			Vector3 euler = camera.transform.rotation.eulerAngles;
			instance.yaw = euler.y;
			instance.pitch = euler.x > 180f ? euler.x - 360f : euler.x;
			instance.enabled = true;
			Active = true;
		}

		/// <summary>Hands the camera back to the character.</summary>
		public static void Disable()
		{
			Active = false;
			if (instance != null)
			{
				instance.enabled = false;
			}
		}

		public static void Toggle()
		{
			if (Active) Disable();
			else Enable();
		}

		private void OnDisable()
		{
			if (instance == this)
			{
				Active = false;
			}
		}

		private void LateUpdate()
		{
			if (!Active)
			{
				return;
			}

			Keyboard keyboard = Keyboard.current;
			Mouse mouse = Mouse.current;
			float dt = Time.unscaledDeltaTime;

			if (mouse != null && mouse.rightButton.isPressed)
			{
				Vector2 delta = mouse.delta.ReadValue();
				yaw += delta.x * LookSensitivity;
				pitch = Mathf.Clamp(pitch - delta.y * LookSensitivity, -89f, 89f);
			}
			transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

			if (keyboard == null)
			{
				return;
			}

			Vector3 move = Vector3.zero;
			if (keyboard.wKey.isPressed) move += transform.forward;
			if (keyboard.sKey.isPressed) move -= transform.forward;
			if (keyboard.dKey.isPressed) move += transform.right;
			if (keyboard.aKey.isPressed) move -= transform.right;
			if (keyboard.eKey.isPressed) move += Vector3.up;
			if (keyboard.qKey.isPressed) move -= Vector3.up;

			if (move.sqrMagnitude > 0f)
			{
				float speed = MoveSpeed * (keyboard.leftShiftKey.isPressed ? FastMultiplier : 1f);
				transform.position += move.normalized * speed * dt;
			}
		}
	}
}
