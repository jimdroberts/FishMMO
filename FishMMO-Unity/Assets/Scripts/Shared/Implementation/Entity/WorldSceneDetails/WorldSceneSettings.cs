using UnityEngine;
using FishNet.Managing;
using FishNet.Managing.Timing;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;
using UnityEngine.Serialization;

namespace FishMMO.Shared
{
	/// <summary>
	/// Inline ECA trigger data for world scene events.
	/// </summary>
	[Serializable]
	public class WorldSceneTrigger
	{
		/// <summary>
		/// Display name for this trigger entry.
		/// </summary>
		public string Name = "New Trigger";

		/// <summary>
		/// Optional selector used to choose scene targets for this trigger entry.
		/// </summary>
		[Tooltip("Optional selector used to choose scene targets for this trigger entry. When empty, the trigger executes once as a world event.")]
		[SerializeReference, SubclassSelector]
		public TargetSelector TargetSelector;

		/// <summary>
		/// Conditions that must pass before actions execute.
		/// </summary>
		[Tooltip("Conditions that must pass before actions execute.")]
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Actions to execute when all conditions pass.
		/// </summary>
		[Tooltip("Actions to execute when all conditions pass.")]
		[FormerlySerializedAs("Actions")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsMetActions = new List<BaseAction>();

		/// <summary>
		/// Actions to execute when one or more conditions fail.
		/// </summary>
		[Tooltip("Actions to execute when one or more conditions fail.")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsNotMetActions = new List<BaseAction>();

		/// <summary>
		/// Executes this trigger against the supplied event data. The configured
		/// <see cref="TargetSelector"/> (when set) is used to fan out across multiple targets;
		/// each selected target gets its own forked <see cref="EventData"/>.
		/// </summary>
		/// <param name="eventData">The event data used by conditions and actions.</param>
		public void Execute(EventData eventData)
		{
			if (eventData == null)
			{
				return;
			}

			if (TargetSelector == null)
			{
				ExecuteMatchingActions(eventData);
				return;
			}

			foreach (GameObject target in TargetSelector.SelectTargets(eventData))
			{
				if (target == null)
				{
					continue;
				}
				ExecuteMatchingActions(eventData.Fork(target));
			}
		}

		/// <summary>
		/// Executes the action branch matching this trigger's condition result.
		/// </summary>
		/// <param name="eventData">The event data used by conditions and actions.</param>
		private void ExecuteMatchingActions(EventData eventData)
		{
			if (!TriggerExecution.AreConditionsMet(Conditions, eventData))
			{
				TriggerExecution.ExecuteActions(OnConditionsNotMetActions, eventData);
				return;
			}

			TriggerExecution.ExecuteActions(OnConditionsMetActions, eventData);
		}
	}

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
		private List<WorldSceneTrigger> onSceneLoadTriggers = new List<WorldSceneTrigger>();

		[Tooltip("Triggers executed when day begins. EventData: DayNightEventData (IsDaytime = true).")]
		[SerializeField]
		private List<WorldSceneTrigger> onDayStartTriggers = new List<WorldSceneTrigger>();

		[Tooltip("Triggers executed when night begins. EventData: DayNightEventData (IsDaytime = false).")]
		[SerializeField]
		private List<WorldSceneTrigger> onNightStartTriggers = new List<WorldSceneTrigger>();

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
		private NetworkManager networkManager;
		private TimeManager timeManager;
		private float offlineElapsedSeconds;
		private bool sceneLoadTriggersPending = true;

		/// <summary>
		/// Unity Awake callback. Initializes the day/night cycle, sets the initial skybox,
		/// creates the blend material, and fires scene-load ECA triggers.
		/// </summary>
		private void Awake()
		{
			DayCycleDuration = Mathf.Max(1, DayCycleDuration);
			NightCycleDuration = Mathf.Max(1, NightCycleDuration);
			networkManager = FindSceneNetworkManager();
			timeManager = networkManager == null ? null : networkManager.TimeManager;
			if (timeManager != null)
			{
				timeManager.OnTick += TimeManager_OnTick;
			}

#if !UNITY_SERVER
			// Create a runtime copy of the day material for skybox blending.
			if (DaySkyboxMaterial != null)
			{
				blendSkyboxMaterial = new Material(DaySkyboxMaterial);
				RenderSettings.skybox = blendSkyboxMaterial;
			}
#endif

			// Initialize the day/night state based on synchronized network time when available.
			UpdateDayNightState(GetGameTimeOfDay(), true);
			TryInvokeSceneLoadTriggers();
		}

		private void OnDestroy()
		{
			if (timeManager != null)
			{
				timeManager.OnTick -= TimeManager_OnTick;
				timeManager = null;
			}
			networkManager = null;

#if !UNITY_SERVER
			if (blendSkyboxMaterial != null)
			{
				Destroy(blendSkyboxMaterial);
			}
#endif
		}

		/// <summary>
		/// Unity Update callback. Advances the day/night cycle, updates object states, rotations, and fading each frame.
		/// </summary>
		void Update()
		{
			TryInvokeSceneLoadTriggers();

			if (DayNightCycle)
			{
				float currentGameTimeOfDay = GetGameTimeOfDay();

				if (!CanExecuteWorldTriggers())
				{
					UpdateDayNightState(currentGameTimeOfDay);
				}

				UpdateDayNightRotation(currentGameTimeOfDay, RotateObjects);
				UpdateDayNightFading(currentGameTimeOfDay, DayFadeObjects, NightFadeObjects);
			}
		}

		/// <summary>
		/// FishNet tick callback used for authoritative day/night state transitions.
		/// </summary>
		private void TimeManager_OnTick()
		{
			TryInvokeSceneLoadTriggers();

			if (!DayNightCycle || !CanExecuteWorldTriggers())
			{
				return;
			}

			UpdateDayNightState(GetGameTimeOfDay());
		}

		/// <summary>
		/// Gets the current game time of day in seconds.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private float GetGameTimeOfDay()
		{
			float secondsPerGameDay = DayCycleDuration + NightCycleDuration;
			if (timeManager != null && timeManager.TickDelta > 0.0d)
			{
				return (float)(timeManager.TicksToTime(TickType.Tick) % secondsPerGameDay);
			}

			offlineElapsedSeconds += Time.deltaTime;
			return offlineElapsedSeconds % secondsPerGameDay;
		}

		/// <summary>
		/// Executes pending scene-load triggers once the server is authoritative for this scene.
		/// </summary>
		private void TryInvokeSceneLoadTriggers()
		{
			if (!sceneLoadTriggersPending || !CanExecuteWorldTriggers())
			{
				return;
			}

			sceneLoadTriggersPending = false;
			InvokeTriggers(onSceneLoadTriggers);
		}

		/// <summary>
		/// Returns true when world-scene ECA triggers may execute authoritatively.
		/// </summary>
		private bool CanExecuteWorldTriggers()
		{
			return networkManager != null && networkManager.IsServerStarted;
		}

		/// <summary>
		/// Finds the NetworkManager loaded in this component's scene.
		/// </summary>
		/// <returns>The scene-local NetworkManager, or null if none exists.</returns>
		private NetworkManager FindSceneNetworkManager()
		{
			NetworkManager[] networkManagers = FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < networkManagers.Length; i++)
			{
				NetworkManager candidate = networkManagers[i];
				if (candidate != null && candidate.gameObject.scene == gameObject.scene)
				{
					return candidate;
				}
			}

			return null;
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
		private void InvokeTriggers(List<WorldSceneTrigger> triggers)
		{
			if (!CanExecuteWorldTriggers() || triggers == null || triggers.Count == 0)
			{
				return;
			}

			DayNightEventData eventData = new DayNightEventData(isDaytime);
			for (int i = 0; i < triggers.Count; ++i)
			{
				if (triggers[i] != null)
				{
					triggers[i].Execute(eventData, gameObject);
				}
			}
		}
	}
}