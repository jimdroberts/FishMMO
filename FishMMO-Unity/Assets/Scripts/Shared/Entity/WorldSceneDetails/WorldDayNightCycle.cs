using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public class WorldDayNightCycle : MonoBehaviour
	{
		/// <summary>
		/// Delegate for triggering fog changes when the scene loads or transitions.
		/// </summary>
		public RegionChangeFogAction SceneFog;

		/// <summary>
		/// True if the day night cycle should run. False if not.
		/// </summary>
		[Tooltip("Enable/Disable the day night cycle.")]
		public bool DayNightCycle = true;
		/// <summary>
		/// The duration of the day cycle in seconds.
		/// </summary>
		[Tooltip("The duration of the day cycle in seconds.")]
		public int DayCycleDuration = 3 * 60 * 60; // 3 hours in seconds
		/// <summary>
		/// The duration of the night cycle in seconds.
		/// </summary>
		[Tooltip("The duration of the night cycle in seconds.")]
		public int NightCycleDuration = 3 * 60 * 60; // 3 hours in seconds
		/// <summary>
		/// The skybox material used during the day cycle.
		/// </summary>
		public Material DaySkyboxMaterial;
		/// <summary>
		/// The skybox material used during the night cycle.
		/// </summary>
		public Material NightSkyBoxMaterial;
		/// <summary>
		/// These objects are constantly rotating based on current time of day.
		/// </summary>
		[Tooltip("Objects that will rotate constantly with the day night cycle.")]
		public List<GameObject> RotateObjects = new List<GameObject>();
		/// <summary>
		/// These objects are enabled during the day and disabled at night.
		/// </summary>
		[Tooltip("These objects will be enabled during the day.")]
		public List<GameObject> DayObjects = new List<GameObject>();
		/// <summary>
		/// These objects are enabled during the night and disabled during the day.
		/// </summary>
		[Tooltip("These objects will be enabled at night.")]
		public List<GameObject> NightObjects = new List<GameObject>();
		/// <summary>
		/// The current fade time.
		/// </summary>
		private float fadeTime;
		/// <summary>
		/// Duration in seconds for fading
		/// </summary>
		[Tooltip("The time in seconds that objects will take to fade in or out.")]
		public float FadeThreshold = 1f;
		/// <summary>
		/// These objects slowly fade away during the day and return at night.
		/// </summary>
		[Tooltip("The objects that will fade away during the day.")]
		public List<GameObject> DayFadeObjects = new List<GameObject>();
		/// <summary>
		/// These objects slowly fade away during the night and return during the day.
		/// </summary>
		[Tooltip("The objects that will fade away at night.")]
		public List<GameObject> NightFadeObjects = new List<GameObject>();
		/// <summary>
		/// True if it's currently day time in the game world.
		/// Used to determine which objects and effects should be active.
		/// </summary>
		[ShowReadonly]
		[SerializeField]
		private bool isDaytime = true;

		/// <summary>
		/// Unity Awake callback. Initializes the day/night cycle, sets the initial skybox, and triggers fog if needed.
		/// </summary>
		private void Awake()
		{
			// Optionally trigger a fog change when the scene loads.
			SceneFog?.Invoke(null, null, false);

			// Set the initial skybox to the day material.
			RenderSettings.skybox = DaySkyboxMaterial;

			// Initialize the day/night state based on the current time.
			UpdateDayNightState(GetGameTimeOfDay(DateTime.UtcNow), true);
		}

		/// <summary>
		/// Unity Update callback. Advances the day/night cycle, updates object states, rotations, and fading each frame.
		/// </summary>
		void Update()
		{
			// Only update the cycle if enabled.
			if (DayNightCycle)
			{
				DateTime now = DateTime.UtcNow;

				float currentGameTimeOfDay = GetGameTimeOfDay(now);

				// Update day/night state, rotate objects, and handle fading transitions.
				UpdateDayNightState(currentGameTimeOfDay);
				UpdateDayNightRotation(currentGameTimeOfDay, RotateObjects);
				UpdateDayNightFading(currentGameTimeOfDay, DayFadeObjects, NightFadeObjects);
			}
		}

		/// <summary>
		/// Gets the current game time of day in seconds.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private float GetGameTimeOfDay(DateTime now)
		{
			float secondsPerGameDay = DayCycleDuration + NightCycleDuration;

			// Calculate the total elapsed time since the start of the game day in seconds.
			// This wraps around after each full day/night cycle.
			return (float)(now.TimeOfDay.TotalSeconds % secondsPerGameDay);
		}

		/// <summary>
		/// Updates the day night state.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void UpdateDayNightState(float currentGameTimeOfDay, bool ignoreCurrentState = false)
		{
			// Determine if it's currently day or night and update state accordingly.
			if (currentGameTimeOfDay <= DayCycleDuration)
			{
				// Switch to day if not already in day state or if forced to update.
				if (!isDaytime || ignoreCurrentState)
				{
					isDaytime = true;

					// Enable day objects, disable night objects.
					UpdateDayNightActivations(isDaytime, DayObjects);
					UpdateDayNightActivations(!isDaytime, NightObjects);

					fadeTime = FadeThreshold; // Reset fade time for transitions.
				}
			}
			else
			{
				// Switch to night if not already in night state or if forced to update.
				if (isDaytime || ignoreCurrentState)
				{
					isDaytime = false;

					// Enable night objects, disable day objects.
					UpdateDayNightActivations(isDaytime, DayObjects);
					UpdateDayNightActivations(!isDaytime, NightObjects);

					fadeTime = FadeThreshold; // Reset fade time for transitions.
				}
			}
		}

		/// <summary>
		/// Tracks the last applied rotation angle for objects affected by the day/night cycle.
		/// Used to calculate incremental rotation each frame.
		/// </summary>
		private float lastRotationAngle = 0.0f;
		/// <summary>
		/// Rotate objects based on the current Game Time Of Day.
		/// </summary>
		private void UpdateDayNightRotation(float currentGameTimeOfDay, List<GameObject> objects)
		{
			if (objects == null || objects.Count == 0)
			{
				return; // Early exit if there are no objects to rotate
			}

			// The angle of rotation at the specific time.
			float lerpTime;
			float rotationAngle;

			// Calculate rotation and skybox transition based on time of day.
			if (currentGameTimeOfDay <= DayCycleDuration)
			{
				// Daytime: rotate from 0 to 180 degrees.
				lerpTime = currentGameTimeOfDay / DayCycleDuration;
				rotationAngle = Mathf.Lerp(0f, 180f, lerpTime);

#if !UNITY_SERVER
				// Lerp the skybox from day to night material as day progresses.
				if (DaySkyboxMaterial != null &&
					NightSkyBoxMaterial != null &&
					RenderSettings.skybox != null)
				{
					// Only update the skybox if it has actually changed to prevent unnecessary lerp calls
					if (RenderSettings.skybox != DaySkyboxMaterial)
					{
						RenderSettings.skybox.Lerp(DaySkyboxMaterial, NightSkyBoxMaterial, lerpTime);
						DynamicGI.UpdateEnvironment(); // Update global illumination if required
					}
				}
#endif
			}
			else
			{
				// Nighttime: rotate from 180 to 360 degrees.
				lerpTime = (currentGameTimeOfDay % DayCycleDuration) / NightCycleDuration;
				rotationAngle = Mathf.Lerp(180f, 360f, lerpTime);

#if !UNITY_SERVER
				// Lerp the skybox from night to day material as night progresses.
				if (DaySkyboxMaterial != null &&
					NightSkyBoxMaterial != null &&
					RenderSettings.skybox != null)
				{
					// Only update the skybox if it has actually changed to prevent unnecessary lerp calls
					if (RenderSettings.skybox != NightSkyBoxMaterial)
					{
						RenderSettings.skybox.Lerp(NightSkyBoxMaterial, DaySkyboxMaterial, lerpTime);
						DynamicGI.UpdateEnvironment(); // Update global illumination if required
					}
				}
#endif
			}

			float rotationDiff = rotationAngle - lastRotationAngle;

			// Apply calculated rotation difference to each object in the list.
			foreach (GameObject obj in objects)
			{
				if (obj == null)
				{
					continue;
				}
				obj.transform.rotation = obj.transform.rotation * Quaternion.AngleAxis(rotationDiff, Vector3.right);
			}

			lastRotationAngle = rotationAngle;
		}

		/// <summary>
		/// Enables or disables all GameObjects in the provided list based on the 'enable' parameter.
		/// Used to activate day or night objects as the cycle changes.
		/// </summary>
		/// <param name="enable">True to enable objects, false to disable.</param>
		/// <param name="objects">List of GameObjects to activate/deactivate.</param>
		private void UpdateDayNightActivations(bool enable, List<GameObject> objects)
		{
			if (objects == null || objects.Count < 1)
			{
				return;
			}

			// Enable or disable each GameObject in the provided list.
			foreach (GameObject gameObject in objects)
			{
				if (gameObject == null)
				{
					continue;
				}

				gameObject.SetActive(enable);
			}
		}

		/// <summary>
		/// Fade objects in or out based on day/night status.
		/// </summary>
		private void UpdateDayNightFading(float gameTimeOfDay, List<GameObject> dayFadeObjects, List<GameObject> nightFadeObjects)
		{
#if !UNITY_SERVER
			float alpha = 0.0f;

			// Handle fade transitions by decreasing fadeTime and calculating alpha.
			if (fadeTime > 0)
			{
				fadeTime -= Time.deltaTime;
				alpha = (fadeTime / FadeThreshold).Clamp(0.0f, 1.0f);
			}

			// Fade day objects out during the day, in at night.
			if (dayFadeObjects != null && dayFadeObjects.Count > 0)
			{
				SetAlpha(dayFadeObjects, isDaytime ? 1 - alpha : alpha);
			}

			// Fade night objects in during the night, out during the day.
			if (nightFadeObjects != null && nightFadeObjects.Count > 0)
			{
				SetAlpha(nightFadeObjects, isDaytime ? alpha : 1 - alpha);
			}
#endif
		}

		/// <summary>
		/// Sets the alpha of the materials of objects in the list.
		/// </summary>
		/// <param name="objects">List of GameObjects</param>
		/// <param name="alpha">Target alpha value</param>
		private void SetAlpha(List<GameObject> objects, float alpha)
		{
			if (objects == null || objects.Count < 1)
			{
				return;
			}
			// Set the alpha value of each object's material to achieve fade effect.
			foreach (GameObject obj in objects)
			{
				if (obj == null)
				{
					continue;
				}
				Renderer renderer = obj.GetComponent<Renderer>();
				if (renderer != null)
				{
					Color color = renderer.material.color;
					color.a = alpha;
					renderer.material.color = color;
				}
			}
		}
	}
}