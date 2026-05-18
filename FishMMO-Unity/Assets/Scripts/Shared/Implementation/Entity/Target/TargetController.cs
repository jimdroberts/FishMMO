using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls targeting logic for a character, including raycasting, target selection, and target events.
	/// </summary>
	public class TargetController : CharacterBehaviour, ITargetController
	{
		/// <summary>
		/// The maximum distance allowed for targeting.
		/// </summary>
		public const float MAX_TARGET_DISTANCE = 50.0f;

		/// <summary>
		/// The update rate (in seconds) for target checks.
		/// </summary>
		public const float TARGET_UPDATE_RATE = 0.05f;

		/// <summary>
		/// The layer mask used for target raycasts.
		/// </summary>
		public LayerMask LayerMask;

		/// <summary>
		/// The previous target information.
		/// </summary>
		public TargetInfo Last;

		/// <summary>
		/// The current target information.
		/// </summary>
		public TargetInfo Current { get; private set; }

		/// <summary>
		/// Event triggered when the target changes.
		/// </summary>
		public event Action<Transform> OnChangeTarget;

		/// <summary>
		/// Event triggered when the target is updated (but not changed).
		/// </summary>
		public event Action<Transform> OnUpdateTarget;

		/// <summary>
		/// Event triggered when the target is cleared (e.g., deselected).
		/// </summary>
		public event Action<Transform> OnClearTarget;

		[Header("ECA - Target")]
		[Tooltip("Triggers invoked when the character acquires a new target.")]
		[SerializeField]
		private List<Trigger> onTargetChangeTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when the character's target is cleared.")]
		[SerializeField]
		private List<Trigger> onTargetClearTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnTargetChangeTriggers => onTargetChangeTriggers;
		/// <inheritdoc />
		public List<Trigger> OnTargetClearTriggers => onTargetClearTriggers;

#if !UNITY_SERVER
		/// <summary>
		/// Internal timer for controlling target update rate.
		/// </summary>
		private float nextTick = 0.0f;

		/// <summary>
		/// Called when the object is being destroyed. Clears target events and resets state.
		/// </summary>
		public override void OnDestroying()
		{
			OnChangeTarget = null;
			OnUpdateTarget = null;
			OnClearTarget = null;
			Last = default;
			Current = default;
		}

		/// <summary>
		/// Updates the target selection each frame, performing raycasts and invoking target events as needed.
		/// </summary>
		void Update()
		{
			if (Camera.main == null)
			{
				return;
			}

			// Update target label for the client at the specified tick rate.
			if (nextTick < 0.0f)
			{
				nextTick = TARGET_UPDATE_RATE;

				Mouse mouse = Mouse.current;
				Vector2 mousePosition = mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
				Ray ray = Camera.main.ScreenPointToRay(mousePosition);
				// Optionally, use screen center for targeting:
				// Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));

				UpdateTarget(ray.origin, ray.direction, MAX_TARGET_DISTANCE);

				// If the target has changed, invoke change/clear events.
				if (Current.Target != Last.Target)
				{
					// Disable the previous outline and target label.
					if (Last.Target != null)
					{
						OnClearTarget?.Invoke(Last.Target);
						Character.Invoke(onTargetClearTriggers, new EventData(Character, Last.Target.gameObject));
					}

					// Invoke change target event.
					OnChangeTarget?.Invoke(Current.Target);
					Character.Invoke(onTargetChangeTriggers, new EventData(Character, Current.Target?.gameObject));
				}
				else
				{
					// Invoke update event if the target remains the same.
					if (Current.Target != null)
					{
						OnUpdateTarget?.Invoke(Current.Target);
					}
				}
			}
			nextTick -= Time.deltaTime;
		}
#endif

		/// <summary>
		/// Updates and returns the TargetInfo for the current target, performing a raycast from the given origin and direction.
		/// </summary>
		/// <param name="origin">The origin of the ray.</param>
		/// <param name="direction">The direction of the ray.</param>
		/// <param name="maxDistance">The maximum distance for the raycast.</param>
		/// <returns>The updated TargetInfo.</returns>
		public TargetInfo UpdateTarget(Vector3 origin, Vector3 direction, float maxDistance)
		{
			Last = Current;

			float distance = maxDistance.Clamp(0.0f, MAX_TARGET_DISTANCE);
			Ray ray = new Ray(origin, direction);
			RaycastHit hit;
#if !UNITY_SERVER
			bool hasHit = Physics.Raycast(ray, out hit, distance, LayerMask);
#else
			bool hasHit = PlayerCharacter != null
				? PlayerCharacter.Motor.PhysicsScene.Raycast(origin, direction, out hit, distance, LayerMask)
				: Physics.Raycast(ray, out hit, distance, LayerMask);
#endif
			if (hasHit)
			{
				// If the raycast hits the character itself, shoot another ray through the character to find the next target.
				IPlayerCharacter hitPlayerCharacter = hit.transform.GetComponent<IPlayerCharacter>();
				if (hitPlayerCharacter != null &&
					hitPlayerCharacter.ID == Character.ID)
				{
					// Adjust the ray origin slightly forward in the direction so the ray starts inside the character.
					Vector3 newRayOrigin = hit.point + direction.normalized * 0.1f;
#if !UNITY_SERVER
					ray = new Ray(newRayOrigin, direction);
					Physics.Raycast(ray, out hit, (distance - hit.distance).Max(0.0f), LayerMask);
#else
					ray = new Ray(newRayOrigin, direction);
					if (PlayerCharacter != null)
					{
						PlayerCharacter.Motor.PhysicsScene.Raycast(newRayOrigin, direction, out hit, (distance - hit.distance).Max(0.0f), LayerMask);
					}
					else
					{
						Physics.Raycast(ray, out hit, (distance - hit.distance).Max(0.0f), LayerMask);
					}
#endif
				}
				//Debug.DrawLine(ray.origin, hit.point, Color.red, 1);
				//Log.Debug("hit: " + hit.transform.name + " pos: " + hit.point);
				Current = new TargetInfo(hit.transform, hit.point);
			}
			else
			{
				// If no target is hit, set Current to null and use the ray's endpoint.
				Current = new TargetInfo(null, ray.GetPoint(distance));
			}
			return Current;
		}
	}
}