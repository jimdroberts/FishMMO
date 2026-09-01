using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Camera preferences the player owns: how far the mouse turns the view.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Separate from <see cref="ClientDisplaySettings"/> because it writes to a scene object rather
	/// than to Unity's global state, and that object does not exist for the whole session. The
	/// camera lives in ClientPreboot and is picked up by <c>KCCPlayer</c> when it takes ownership,
	/// so a value applied at boot has to survive until then — it does, because
	/// <see cref="KCCCamera.RotationSpeed"/> is a plain field on a component that outlives the
	/// scene loads between boot and play.
	/// </para>
	/// <para>
	/// Sensitivity only has an observable effect while the cursor is locked, which is the state the
	/// mouse drives the camera in. With mouse mode on the cursor is free and the camera does not
	/// turn at all, so the slider looks broken if tested there — it is not, there is simply no look
	/// input to scale.
	/// </para>
	/// </remarks>
	public static class ClientCameraSettings
	{
		/// <summary>
		/// Sensitivity when nothing has been chosen.
		/// </summary>
		/// <remarks>
		/// Half, not the 1.0 authored on the camera. That value turns the view faster than anyone
		/// tested comfortable — a new player would have to find this slider before the game felt
		/// right, which is the wrong way round for a default. Only players who have never touched
		/// the slider are affected; a saved value always wins.
		/// </remarks>
		public const float DefaultLookSensitivity = 0.5f;

		/// <summary>Slowest the view may turn. Above zero: zero is a camera that cannot be moved.</summary>
		public const float MinimumLookSensitivity = 0.1f;

		/// <summary>
		/// Fastest the view may turn.
		/// </summary>
		/// <remarks>
		/// One, down from an initial five. The camera's authored 1.0 already turns faster than
		/// comfortable, so everything above it was travel spent on speeds nobody picks — and it
		/// crammed the usable band into the slider's first tenth. The range a control offers is a
		/// claim about where the answer lies; this one now spans it rather than hiding it.
		/// </remarks>
		public const float MaximumLookSensitivity = 1.0f;

		/// <summary>
		/// Applies the stored look sensitivity to the live camera.
		/// </summary>
		/// <remarks>
		/// Reports what it did, at Debug. This apply is the one that decides whether a player's
		/// saved sensitivity is honoured, and it previously failed in total silence: the value was
		/// stored, the options panel displayed it, and the camera ran at its authored speed for the
		/// whole session with nothing anywhere saying so. A line here is what turns "the slider
		/// feels wrong" from a guess into something answerable from a log.
		///
		/// Only this path logs. ApplyLookSensitivity is also called for every value change while
		/// the player drags the slider, which would be pure noise.
		/// </remarks>
		public static void ApplySaved()
		{
			float sensitivity = ClientSettings.GetFloat(
				ClientSettings.LookSensitivityKey,
				DefaultLookSensitivity,
				MinimumLookSensitivity,
				MaximumLookSensitivity);

			if (ApplyLookSensitivity(sensitivity))
			{
				Log.Debug("ClientCameraSettings",
					$"Look sensitivity {sensitivity} applied to the camera.");
			}
			else
			{
				Log.Debug("ClientCameraSettings",
					$"Look sensitivity {sensitivity} not applied: no camera yet. Expected during " +
					"boot, and applied again once the character is in the world.");
			}
		}

		/// <summary>
		/// Writes a look sensitivity onto the camera and remembers it.
		/// </summary>
		/// <remarks>
		/// Clamped rather than trusted. The value is a float in a text file the player can edit, and
		/// it multiplies raw mouse delta: a large one makes the view unusable at exactly the moment
		/// the player would need to reach the menu to put it back, and zero makes the camera
		/// immovable.
		/// </remarks>
		/// <param name="value">Sensitivity multiplier. Clamped to the supported range.</param>
		/// <returns><c>true</c> if a camera received the value; <c>false</c> if there is none yet.</returns>
		public static bool ApplyLookSensitivity(float value)
		{
			float clamped = float.IsNaN(value)
				? DefaultLookSensitivity
				: Mathf.Clamp(value, MinimumLookSensitivity, MaximumLookSensitivity);

			KCCCamera camera = ResolveCamera();
			if (camera == null)
			{
				/* Not an error, and not the whole story either: boot applies settings before a
				 * world camera exists, so this returns having done nothing. That is fine only
				 * because PlayerInputController.Initialize applies again once the local character
				 * is in the world. Without that second apply the camera keeps its authored
				 * RotationSpeed for the whole session, and the saved value appears to be ignored
				 * until the player happens to move the slider. */
				return false;
			}

			camera.RotationSpeed = clamped;
			return true;
		}

		/// <summary>
		/// Finds the <see cref="KCCCamera"/> on the main camera, if there is one yet.
		/// </summary>
		/// <remarks>
		/// Resolved on each call rather than cached. <c>Camera.main</c> answers differently across
		/// scene loads, and a cached reference to a camera from a previous session is the kind of
		/// stale handle that fails silently — the setting appears to save and never applies.
		/// </remarks>
		private static KCCCamera ResolveCamera()
		{
			Camera main = Camera.main;
			return main == null ? null : main.GetComponent<KCCCamera>();
		}
	}
}
