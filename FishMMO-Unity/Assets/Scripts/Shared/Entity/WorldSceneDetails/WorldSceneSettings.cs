using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public class WorldSceneSettings : MonoBehaviour
	{
		/// <summary>
		/// The maximum number of clients allowed in this scene.
		/// </summary>
		[Tooltip("The maximum number of clients allowed in this scene.")]
		public int MaxClients = 100;
		/// <summary>
		/// The image that will be displayed when entering this scene.
		/// </summary>
		[Tooltip("The image that will be displayed when entering this scene.")]
		public Sprite SceneTransitionImage;

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
		public Material DaySkyboxMaterial;
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
		/// Returns true if it's currently day time.
		/// </summary>
		[ShowReadonly]
		[SerializeField]
		private bool isDaytime = true;

		private void Awake()
		{
			UpdateDayNightState(GetGameTimeOfDay(DateTime.UtcNow), true);
		}

		void Update()
		{
			if (DayNightCycle)
			{
				DateTime now = DateTime.UtcNow;

				float currentGameTimeOfDay = GetGameTimeOfDay(now);

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

			// Calculate the total elapsed time since the start of the day in seconds
			return (float)(now.TimeOfDay.TotalSeconds % secondsPerGameDay);
		}

		/// <summary>
		/// Updates the day night state.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void UpdateDayNightState(float currentGameTimeOfDay, bool ignoreCurrentState = false)
		{
			// handle daytime and nighttime state update
			if (currentGameTimeOfDay <= DayCycleDuration)
			{
				if (!isDaytime || ignoreCurrentState)
				{
					isDaytime = true;

					UpdateDayNightActivations(isDaytime, DayObjects);
					UpdateDayNightActivations(!isDaytime, NightObjects);

					fadeTime = FadeThreshold; // Reset fade time
				}
			}
			else
			{
				if (isDaytime || ignoreCurrentState)
				{
					isDaytime = false;

					UpdateDayNightActivations(isDaytime, DayObjects);
					UpdateDayNightActivations(!isDaytime, NightObjects);

					fadeTime = FadeThreshold; // Reset fade time
				}
			}
		}

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

			// Determine the rotation angle based on the time of day

			// Day Time
			if (currentGameTimeOfDay <= DayCycleDuration)
			{
				lerpTime = currentGameTimeOfDay / DayCycleDuration;
				rotationAngle = Mathf.Lerp(0f, 180f, lerpTime);

#if !UNITY_SERVER
				// Attempt to lerp the skyboxes.
				if (DaySkyboxMaterial != null &&
					NightSkyBoxMaterial != null &&
					RenderSettings.skybox != null)
				{
					RenderSettings.skybox.Lerp(DaySkyboxMaterial, NightSkyBoxMaterial, lerpTime);
					DynamicGI.UpdateEnvironment();
				}
#endif
			}
			// Night Time
			else
			{
				lerpTime = (currentGameTimeOfDay % DayCycleDuration) / NightCycleDuration;
				rotationAngle = Mathf.Lerp(180f, 360f, lerpTime);

#if !UNITY_SERVER
				// Attempt to lerp the skyboxes.
				if (DaySkyboxMaterial != null &&
					NightSkyBoxMaterial != null &&
					RenderSettings.skybox != null)
				{
					RenderSettings.skybox.Lerp(NightSkyBoxMaterial, DaySkyboxMaterial, lerpTime);
					DynamicGI.UpdateEnvironment();
				}
#endif
			}

			float rotationDiff = rotationAngle - lastRotationAngle;

			// Apply rotation to each object
			foreach (GameObject obj in objects)
			{
				obj.transform.rotation = obj.transform.rotation * Quaternion.AngleAxis(rotationDiff, Vector3.right);
			}

			lastRotationAngle = rotationAngle;
		}

		private void UpdateDayNightActivations(bool enable, List<GameObject> objects)
		{
			if (objects == null ||
				objects.Count < 1)
			{
				return;
			}

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

			if (fadeTime > 0)
			{
				fadeTime -= Time.deltaTime;
				alpha = (fadeTime / FadeThreshold).Clamp(0.0f, 1.0f);
			}

			if (dayFadeObjects == null ||
				dayFadeObjects.Count < 1)
			{
				SetAlpha(dayFadeObjects, isDaytime ? 1 - alpha : alpha);
			}

			if (nightFadeObjects == null ||
				nightFadeObjects.Count < 1)
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
			foreach (GameObject obj in objects)
			{
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