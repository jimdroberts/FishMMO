using UnityEngine;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Camera preferences the player owns: how far the mouse turns the view, and how its edges
	/// are smoothed.
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
		/// Two on the scale described at <see cref="MouseDegreesPerCount"/>: about 13 inches of mouse for a full turn at
		/// 800 DPI, a moderate setting.
		/// </remarks>
		public const float DefaultLookSensitivity = 2.0f;

		/// <summary>Slowest the view may turn. Above zero: zero is a camera that cannot be moved.</summary>
		public const float MinimumLookSensitivity = 0.05f;

		/// <summary>Fastest the view may turn: wide enough for any value a player brings from another game.</summary>
		public const float MaximumLookSensitivity = 20.0f;

		/// <summary>
		/// Degrees the view turns per mouse count at sensitivity 1, at <see cref="ReferenceVerticalFieldOfView"/>.
		/// </summary>
		/// <remarks>
		/// The common scale for mouse look in first-person games, so a sensitivity a player already uses carries
		/// over unchanged.
		/// </remarks>
		private const float DegreesPerCount = 0.022f;

		/// <summary>
		/// The vertical field of view the scale is matched at: 90 degrees across a 4:3 screen, 73.74 degrees up it.
		/// </summary>
		private const float ReferenceVerticalFieldOfView = 73.7398f;

		/// <summary>
		/// Degrees a second the view turns at full deflection of a stick, per unit of sensitivity, at the reference
		/// field of view: 150 at the default.
		/// </summary>
		private const float StickDegreesPerSecond = 75.0f;

		/// <summary>The sensitivity in effect, clamped. Read every frame by the look input.</summary>
		public static float LookSensitivity { get; private set; } = DefaultLookSensitivity;

		/// <summary>
		/// Degrees the view turns for one count of mouse movement, at a camera's vertical field of view.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Sensitivity × 0.022 degrees per count, the common mouse-look scale, so the same number turns the
		/// view by the same amount for the same movement of the mouse; nothing is scaled by frame time, and the
		/// counts come in raw (the browser is asked for unaccelerated movement, see PointerLockGuard.jslib).
		/// </para>
		/// <para>
		/// Then scaled by the field of view so the feel stays constant: by tan(fov/2) against the reference's, the
		/// ratio of the two views' focal lengths. What that holds fixed is how far a point near the crosshair
		/// moves across the screen per count, so a narrower view turns more slowly and a wider one faster, and at
		/// the reference view it is exactly 0.022 degrees per count. Recomputed from the live camera every frame,
		/// so a zoom or a field-of-view setting keeps it constant too.
		/// </para>
		/// </remarks>
		/// <param name="verticalFieldOfView">The camera's <see cref="Camera.fieldOfView"/>, in degrees.</param>
		public static float MouseDegreesPerCount(float verticalFieldOfView)
		{
			return LookSensitivity * DegreesPerCount * FieldOfViewScale(verticalFieldOfView);
		}

		/// <summary>Degrees a second the view turns at full deflection of a stick, at a camera's vertical field of view.</summary>
		/// <remarks>A stick reports a position, not a movement, so unlike the mouse this is a rate, scaled by frame time.</remarks>
		public static float StickDegreesPerSecondAt(float verticalFieldOfView)
		{
			return LookSensitivity * StickDegreesPerSecond * FieldOfViewScale(verticalFieldOfView);
		}

		/// <summary>
		/// How much faster the view turns at a vertical field of view than at the reference one: the ratio of the
		/// tangents of the half-angles. 1 for anything that is not a usable perspective angle.
		/// </summary>
		public static float FieldOfViewScale(float verticalFieldOfView)
		{
			if (float.IsNaN(verticalFieldOfView) || verticalFieldOfView <= 1f || verticalFieldOfView >= 179f)
			{
				return 1f;
			}
			return Mathf.Tan(0.5f * verticalFieldOfView * Mathf.Deg2Rad) / Mathf.Tan(0.5f * ReferenceVerticalFieldOfView * Mathf.Deg2Rad);
		}

		/// <summary>
		/// The saved sensitivity, carrying over one stored under the old meaning.
		/// </summary>
		/// <remarks>
		/// The setting used to be the degrees turned per count outright (0.1 to 1, default 0.5), stored under
		/// <see cref="ClientSettings.LegacyLookSensitivityKey"/>. It is converted once into the sensitivity that
		/// turns the same amount per count at the camera's authored 60-degree view, so nobody's view changes
		/// speed under them except where the old value lies past the new maximum; the old key is then removed.
		/// </remarks>
		public static float LoadSaved()
		{
			if (!ClientSettings.Exists(ClientSettings.LookSensitivityKey) && ClientSettings.Exists(ClientSettings.LegacyLookSensitivityKey))
			{
				float degreesPerCount = ClientSettings.GetFloat(ClientSettings.LegacyLookSensitivityKey, 0.5f, 0.001f, 100f);
				float carried = Mathf.Clamp(degreesPerCount / (DegreesPerCount * FieldOfViewScale(LegacyFieldOfView)),
					MinimumLookSensitivity, MaximumLookSensitivity);
				ClientSettings.Set(ClientSettings.LookSensitivityKey, carried);
				ClientSettings.Remove(ClientSettings.LegacyLookSensitivityKey);
				Log.Debug("ClientCameraSettings", $"Look sensitivity {degreesPerCount} degrees per count carried over as {carried}.");
			}
			return ClientSettings.GetFloat(
				ClientSettings.LookSensitivityKey,
				DefaultLookSensitivity,
				MinimumLookSensitivity,
				MaximumLookSensitivity);
		}

		/// <summary>The main camera's authored vertical field of view (ClientPreboot), at which old values are carried over.</summary>
		private const float LegacyFieldOfView = 60f;

		/// <summary>
		/// Applies the stored look sensitivity.
		/// </summary>
		/// <remarks>
		/// Reports what it did, at Debug. Only this path logs: ApplyLookSensitivity is also called for every
		/// value change while the player drags the slider, which would be pure noise.
		/// </remarks>
		public static void ApplySaved()
		{
			float sensitivity = LoadSaved();

			/* Applied alongside sensitivity so both reach the camera at the same moment, which
			 * is the point where one exists. */
			ApplySavedAntialiasing();

			if (ApplyLookSensitivity(sensitivity))
			{
				Log.Debug("ClientCameraSettings",
					$"Look sensitivity {sensitivity} applied; the camera takes its input in degrees.");
			}
			else
			{
				Log.Debug("ClientCameraSettings",
					$"Look sensitivity {sensitivity} applied; no camera yet. Expected during " +
					"boot, and applied again once the character is in the world.");
			}
		}

		/// <summary>
		/// Sets the look sensitivity, and puts the camera into degrees.
		/// </summary>
		/// <remarks>
		/// Clamped rather than trusted. The value is a float in a text file the player can edit, and it scales
		/// raw mouse movement: a large one makes the view unusable at exactly the moment the player would need to
		/// reach the menu to put it back, and zero makes the camera immovable.
		/// <para>
		/// The look input now arrives at the camera already in degrees (PlayerInputController converts counts with
		/// <see cref="MouseDegreesPerCount"/>, which needs the live field of view), so the camera's own
		/// <see cref="KCCCamera.RotationSpeed"/> multiplier is held at 1 rather than carrying the sensitivity.
		/// </para>
		/// </remarks>
		/// <param name="value">The sensitivity. Clamped to the supported range.</param>
		/// <returns><c>true</c> if a camera was found and set to take degrees; <c>false</c> if there is none yet.</returns>
		public static bool ApplyLookSensitivity(float value)
		{
			LookSensitivity = float.IsNaN(value)
				? DefaultLookSensitivity
				: Mathf.Clamp(value, MinimumLookSensitivity, MaximumLookSensitivity);

			KCCCamera camera = ResolveCamera();
			if (camera == null)
			{
				/* Not an error: boot applies settings before a world camera exists. The sensitivity is held
				 * here and read every frame, so it already takes effect; PlayerInputController.Initialize
				 * applies again once the character is in the world, which sets the camera to degrees. */
				return false;
			}

			camera.RotationSpeed = 1f;
			return true;
		}

		/// <summary>
		/// Edge smoothing modes offered to the player, in the order the dropdown shows them.
		/// </summary>
		/// <remarks>
		/// Deliberately not <c>AntialiasingMode</c> itself. What is stored is a player setting that
		/// has to keep its meaning across Unity upgrades, and persisting a framework enum means a
		/// reordering there silently changes what an existing saved file says. This order is ours.
		///
		/// Post-process antialiasing rather than MSAA. MSAA lives on the render pipeline asset and has
		/// its own option (ClientDisplaySettings.MsaaOption), written onto whichever asset the quality
		/// level uses; the two stack rather than fight: MSAA for geometry and leaf edges, this over
		/// the finished image.
		/// </remarks>
		public enum AntialiasingOption
		{
			/// <summary>No edge smoothing. Cheapest, and what the camera currently ships with.</summary>
			Off = 0,

			/// <summary>Fast approximate. Very cheap, softens the whole image somewhat.</summary>
			Fast = 1,

			/// <summary>Subpixel morphological. Sharper than FXAA for a modest cost.</summary>
			Balanced = 2,

			/// <summary>Temporal. Best on still edges, and the only one that can ghost in motion.</summary>
			Temporal = 3,
		}

		/// <summary>
		/// Edge smoothing when nothing has been chosen.
		/// </summary>
		/// <remarks>
		/// SMAA rather than Off. The camera ships with antialiasing disabled, which is what made
		/// character silhouettes visibly stair-stepped; a default that looks broken is a poor one.
		/// Chosen over FXAA because it keeps texture detail instead of softening the whole frame,
		/// and over TAA because TAA ghosts on a camera that orbits the player.
		/// </remarks>
		public const AntialiasingOption DefaultAntialiasing = AntialiasingOption.Balanced;

		/// <summary>
		/// Applies the stored antialiasing mode to the live camera.
		/// </summary>
		public static void ApplySavedAntialiasing()
		{
			/* Clamped rather than cast straight through. The value comes from a text file the
			 * player can edit, and an out-of-range ordinal would reach the switch below as an
			 * unnamed enum value -- which the default arm already handles, but landing on the
			 * default by accident and by intent should not be the same code path. */
			int stored = Mathf.Clamp(
				ClientSettings.GetInt(ClientSettings.AntialiasingKey, (int)DefaultAntialiasing),
				(int)AntialiasingOption.Off,
				(int)AntialiasingOption.Temporal);

			ApplyAntialiasing((AntialiasingOption)stored);
		}

		/// <summary>
		/// Writes an antialiasing mode onto the camera.
		/// </summary>
		/// <param name="option">The mode to apply. An unrecognised value falls back to the default.</param>
		/// <returns><c>true</c> if a camera received it; <c>false</c> if there is none yet.</returns>
		public static bool ApplyAntialiasing(AntialiasingOption option)
		{
			Camera main = Camera.main;
			if (main == null)
			{
				return false;
			}

			UniversalAdditionalCameraData data = main.GetComponent<UniversalAdditionalCameraData>();
			if (data == null)
			{
				/* Not an error. A camera the pipeline is not driving has nothing to smooth, which is
				 * a legitimate configuration rather than a fault. */
				return false;
			}

			switch (option)
			{
				case AntialiasingOption.Off:
					data.antialiasing = AntialiasingMode.None;
					break;
				case AntialiasingOption.Fast:
					data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
					break;
				case AntialiasingOption.Temporal:
					data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
					break;
				case AntialiasingOption.Balanced:
				default:
					data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
					break;
			}

			/* SMAA is the only mode that reads this and is also the default, so it is set here
			 * rather than left at whatever the scene happened to author. */
			data.antialiasingQuality = AntialiasingQuality.High;
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
