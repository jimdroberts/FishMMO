using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour for configuring world scene settings, including client limits, transition visuals, day/night cycle, and object activations.
	/// </summary>
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

		// ───── ECA Trigger Lists ────────────────────────────────────────────

		[Header("ECA - Day/Night Triggers")]
		[Tooltip("Triggers executed once when this scene loads (e.g. apply default fog). EventData: DayNightEventData.")]
		[SerializeField]
		private List<Trigger> onSceneLoadTriggers = new List<Trigger>();

		[Tooltip("Triggers executed when day begins. EventData: DayNightEventData (IsDaytime = true).")]
		[SerializeField]
		private List<Trigger> onDayStartTriggers = new List<Trigger>();

		[Tooltip("Triggers executed when night begins. EventData: DayNightEventData (IsDaytime = false).")]
		[SerializeField]
		private List<Trigger> onNightStartTriggers = new List<Trigger>();

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
		/// Returns true if it's currently day time.
		/// </summary>
		[ShowReadonly]
		[SerializeField]
		private bool isDaytime = true;

		// Runtime blend material — we lerp this instance so we never mutate the shared material assets.
		private Material blendSkyboxMaterial;

		/// <summary>
		/// Unity Awake callback. Initializes the day/night cycle, sets the initial skybox,
		/// creates the blend material, and fires scene-load ECA triggers.
		/// </summary>
		private void Awake()
		{
#if !UNITY_SERVER
			// Create a runtime copy of the day material for skybox blending.
			if (DaySkyboxMaterial != null)
			{
				blendSkyboxMaterial = new Material(DaySkyboxMaterial);
				RenderSettings.skybox = blendSkyboxMaterial;
			}
#endif

			// Fire scene-load triggers (e.g. apply default fog via ChangeFogAction).
			InvokeTriggers(onSceneLoadTriggers);

			// Initialize the day/night state based on the current time.
			UpdateDayNightState(GetGameTimeOfDay(DateTime.UtcNow), true);
		}

		/// <summary>
		/// Unity Update callback. Advances the day/night cycle, updates object states, rotations, and fading each frame.
		/// </summary>
		void Update()
		{
			if (DayNightCycle)
			{
				float currentGameTimeOfDay = GetGameTimeOfDay(DateTime.UtcNow);

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
			return (float)(now.TimeOfDay.TotalSeconds % secondsPerGameDay);
		}

		/// <summary>
		/// Updates the day/night state and fires ECA triggers on transitions.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void UpdateDayNightState(float currentGameTimeOfDay, bool ignoreCurrentState = false)
		{
			if (currentGameTimeOfDay <= DayCycleDuration)
			{
				if (!isDaytime || ignoreCurrentState)
				{
					isDaytime = true;

					UpdateDayNightActivations(true, DayObjects);
					UpdateDayNightActivations(false, NightObjects);

					fadeTime = FadeThreshold;

					if (!ignoreCurrentState)
					{
						InvokeTriggers(onDayStartTriggers);
					}
				}
			}
			else
			{
				if (isDaytime || ignoreCurrentState)
				{
					isDaytime = false;

					UpdateDayNightActivations(false, DayObjects);
					UpdateDayNightActivations(true, NightObjects);

					fadeTime = FadeThreshold;

					if (!ignoreCurrentState)
					{
						InvokeTriggers(onNightStartTriggers);
					}
				}
			}
		}

		/// <summary>
		/// Tracks the last applied rotation angle for objects affected by the day/night cycle.
		/// </summary>
		private float lastRotationAngle = 0.0f;

		/// <summary>
		/// Rotates objects based on the current game time of day and lerps the skybox blend material.
		/// </summary>
		private void UpdateDayNightRotation(float currentGameTimeOfDay, List<GameObject> objects)
		{
			if (objects == null || objects.Count == 0)
			{
				return; // Early exit if there are no objects to rotate
			}

			float lerpTime;
			float rotationAngle;

			if (currentGameTimeOfDay <= DayCycleDuration)
			{
				lerpTime = currentGameTimeOfDay / DayCycleDuration;
				rotationAngle = Mathf.Lerp(0f, 180f, lerpTime);

#if !UNITY_SERVER
				if (blendSkyboxMaterial != null &&
					DaySkyboxMaterial != null &&
					NightSkyBoxMaterial != null)
				{
					blendSkyboxMaterial.Lerp(DaySkyboxMaterial, NightSkyBoxMaterial, lerpTime);
					DynamicGI.UpdateEnvironment();
				}
#endif
			}
			else
			{
				lerpTime = (currentGameTimeOfDay - DayCycleDuration) / NightCycleDuration;
				rotationAngle = Mathf.Lerp(180f, 360f, lerpTime);

#if !UNITY_SERVER
				if (blendSkyboxMaterial != null &&
					DaySkyboxMaterial != null &&
					NightSkyBoxMaterial != null)
				{
					blendSkyboxMaterial.Lerp(NightSkyBoxMaterial, DaySkyboxMaterial, lerpTime);
					DynamicGI.UpdateEnvironment();
				}
#endif
			}

			float rotationDiff = rotationAngle - lastRotationAngle;

			for (int i = 0; i < objects.Count; ++i)
			{
				GameObject obj = objects[i];
				if (obj == null)
				{
					continue;
				}
				obj.transform.rotation *= Quaternion.AngleAxis(rotationDiff, Vector3.right);
			}

			lastRotationAngle = rotationAngle;
		}

		/// <summary>
		/// Enables or disables all GameObjects in the provided list.
		/// </summary>
		private void UpdateDayNightActivations(bool enable, List<GameObject> objects)
		{
			if (objects == null || objects.Count < 1)
			{
				return;
			}

			for (int i = 0; i < objects.Count; ++i)
			{
				GameObject obj = objects[i];
				if (obj != null)
				{
					obj.SetActive(enable);
				}
			}
		}

		/// <summary>
		/// Fades objects in or out based on day/night status.
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

			if (dayFadeObjects != null && dayFadeObjects.Count > 0)
			{
				SetAlpha(dayFadeObjects, isDaytime ? 1 - alpha : alpha);
			}

			if (nightFadeObjects != null && nightFadeObjects.Count > 0)
			{
				SetAlpha(nightFadeObjects, isDaytime ? alpha : 1 - alpha);
			}
#endif
		}

		/// <summary>
		/// Sets the alpha of each object's material.
		/// </summary>
		private void SetAlpha(List<GameObject> objects, float alpha)
		{
#if !UNITY_SERVER
			if (objects == null || objects.Count < 1)
			{
				return;
			}

			for (int i = 0; i < objects.Count; ++i)
			{
				GameObject obj = objects[i];
				if (obj == null)
				{
					continue;
				}
				Renderer r = obj.GetComponent<Renderer>();
				if (r != null)
				{
					Color color = r.material.color;
					color.a = alpha;
					r.material.color = color;
				}
			}
#endif
		}

		/// <summary>
		/// Executes all triggers in the list using a world-level DayNightEventData (null initiator).
		/// </summary>
		private void InvokeTriggers(List<Trigger> triggers)
		{
			if (triggers == null || triggers.Count == 0)
			{
				return;
			}

			DayNightEventData eventData = new DayNightEventData(isDaytime);
			for (int i = 0; i < triggers.Count; ++i)
			{
				if (triggers[i] != null)
				{
					triggers[i].Execute(eventData);
				}
			}
		}
	}
}