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
		/// The transform this controller last told its subscribers was the target.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This exists because <c>Current.Target != Last.Target</c> cannot detect a target that
		/// was destroyed. <see cref="Transform"/> derives from <see cref="UnityEngine.Object"/>,
		/// whose <c>==</c> is overloaded to report a destroyed native object as equal to
		/// <c>null</c>. When the target dies and despawns, <c>Last.Target</c> holds a live C#
		/// reference to a destroyed object ("fake null") and <c>Current.Target</c> is a real
		/// <c>null</c> — so the overloaded <c>!=</c> compares two things that both answer "I am
		/// null" and returns <b>false</b>. Neither the change branch nor the update branch fired:
		/// the frame stayed on the corpse, <c>onTargetClearTriggers</c> never ran, and the
		/// overhead label was never released.
		/// </para>
		/// <para>
		/// Keeping the last-reported transform in a plain field and comparing it with
		/// <see cref="object.ReferenceEquals"/> restores the distinction the overload erases:
		/// reference identity does not care whether the native object behind it is still alive,
		/// so "was a transform, now nothing" is visible as the reference change it actually is.
		/// The Unity-semantics test is still used — deliberately — to normalise a destroyed
		/// target into a real null before it is stored, so the destruction is reported exactly
		/// once instead of on every tick thereafter.
		/// </para>
		/// </remarks>
		private Transform lastReportedTarget;

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
			lastReportedTarget = null;
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

				/* Normalised through Unity's overloaded == on purpose: a destroyed transform
				 * answers "I am null" here, which is exactly the collapse we want on the way IN.
				 * The comparison below then uses reference identity, which is the only test that
				 * can tell a destroyed previous target apart from never having had one. */
				Transform resolvedTarget = Current.Target != null ? Current.Target : null;
				Transform previousTarget = lastReportedTarget;

				// If the target has changed, invoke change/clear events.
				if (!ReferenceEquals(resolvedTarget, previousTarget))
				{
					lastReportedTarget = resolvedTarget;

					// Disable the previous outline and target label.
					if (previousTarget != null)
					{
						/* Unity semantics again: a target that was destroyed rather than
						 * deselected fails this test, so subscribers are not handed a reference
						 * whose GetComponent / gameObject access would throw
						 * MissingReferenceException. The clear still fires — with no argument —
						 * because the frame must come down either way. */
						OnClearTarget?.Invoke(previousTarget);
						Character.Invoke(onTargetClearTriggers, new EventData(Character, previousTarget.gameObject));
					}
					else if (!ReferenceEquals(previousTarget, null))
					{
						// The previous target was destroyed out from under us.
						OnClearTarget?.Invoke(null);
						Character.Invoke(onTargetClearTriggers, new EventData(Character, null));
					}

					// Invoke change target event.
					OnChangeTarget?.Invoke(resolvedTarget);
					Character.Invoke(onTargetChangeTriggers, new EventData(Character, resolvedTarget != null ? resolvedTarget.gameObject : null));
				}
				else
				{
					// Invoke update event if the target remains the same.
					if (resolvedTarget != null)
					{
						OnUpdateTarget?.Invoke(resolvedTarget);
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
			/* The character's OWN physics scene, for NPCs as well as players. A scene server hosts
			 * several scenes with local physics; the previous NPC fallback to the global
			 * Physics.Raycast traced the default scene, so an NPC's target ray never met the
			 * colliders around it. */
			PhysicsScene physicsScene = ResolvePhysicsScene();
			bool hasHit = physicsScene.Raycast(origin, direction, out hit, distance, LayerMask);
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
					physicsScene.Raycast(newRayOrigin, direction, out hit, (distance - hit.distance).Max(0.0f), LayerMask);
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

#if UNITY_SERVER
		/// <summary>
		/// The physics scene this character lives in: the KCC motor's for players, the owning
		/// Unity scene's for everything else.
		/// </summary>
		private PhysicsScene ResolvePhysicsScene()
		{
			if (PlayerCharacter != null)
			{
				return PlayerCharacter.Motor.PhysicsScene;
			}
			return gameObject.scene.IsValid() ? gameObject.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
		}
#endif
	}
}