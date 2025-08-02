using UnityEngine;
using System;

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

				Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
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
					}

					// Invoke change target event.
					OnChangeTarget?.Invoke(Current.Target != null ? Current.Target : null);
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
			RaycastHit hit;
#if !UNITY_SERVER
			Ray ray = new Ray(origin, direction);
			if (Physics.Raycast(ray, out hit, distance, LayerMask))
#else
			if (PlayerCharacter != null &&
				PlayerCharacter.Motor.PhysicsScene.Raycast(origin, direction, out hit, distance, LayerMask))
#endif
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
					Physics.Raycast(ray, out hit, (distance - hit.distance).Min(0.0f), LayerMask);
#else
					if (PlayerCharacter != null &&
						PlayerCharacter.Motor.PhysicsScene.Raycast(newRayOrigin, direction, out hit, (distance - hit.distance).Min(0.0f), LayerMask));
#endif
				}
				//Debug.DrawLine(ray.origin, hit.point, Color.red, 1);
				//Log.Debug("hit: " + hit.transform.name + " pos: " + hit.point);
				Current = new TargetInfo(hit.transform, hit.point);
			}
			else
			{
#if UNITY_SERVER
				Ray ray = new Ray(origin, direction);
#endif
				// If no target is hit, set Current to null and use the ray's endpoint.
				Current = new TargetInfo(null, ray.GetPoint(distance));
			}
			return Current;
		}
	}
}