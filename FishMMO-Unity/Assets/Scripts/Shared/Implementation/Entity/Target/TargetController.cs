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
			/* Mouse targeting belongs to the player holding the mouse.
			 *
			 * This component sits on every playable character, so without this guard every copy of
			 * every character runs it: on a client watching fifty other players, fifty instances
			 * each read the local mouse, build the same ray from the same camera, and trace it.
			 * They necessarily agree — there is one mouse — so forty-nine of those traces exist
			 * only to arrive at the answer the fiftieth already had, and each is a physics raycast
			 * (two, where the first ray starts inside the caster and has to be pushed through).
			 *
			 * The events are the more serious half. A non-owner instance that resolves a target
			 * raises OnChangeTarget and fires the character's target triggers, so ECA logic hung on
			 * "this character acquired a target" runs on other people's characters, driven by where
			 * the local player happens to be pointing.
			 *
			 * A dedicated server never reaches any of it — this method is inside #if !UNITY_SERVER.
			 * The editor is the exception that matters: UNITY_SERVER is undefined there, so an
			 * in-editor scene server runs this for every character against the developer's mouse.
			 *
			 * Deliberately not a guard on the whole component. UpdateTarget is still called
			 * directly, on the server, by the ability and pet systems with their own aim origins,
			 * and that path — including its lag compensation — is untouched by this. */
			if (!base.IsOwner)
			{
				return;
			}

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
		/// Updates and returns the <see cref="TargetInfo"/> for the current target, tracing from the
		/// given origin and direction.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Lag compensated on the server.</b> This is the acquisition step for every targeted
		/// ability: <c>AbilityController.ResolveTargetAndSpawn</c> calls it from the replicated aim,
		/// and <see cref="TargetedEntitySelector"/> then resolves the ability onto whatever it
		/// decided. A ray is infinitely thin, so it has no width to absorb the gap between where a
		/// client renders its peers and where the server holds them — measured at 0.64&#160;m for a
		/// 40&#160;ms connection and 2.2&#160;m at 300&#160;ms, against character capsules 0.6&#160;m
		/// across. Uncompensated, a laggy player's ray simply misses the character they aimed at.
		/// Rewinding the scene for the caster's view puts the ray back where it was pointed.
		/// </para>
		/// <para>
		/// <b>One physics scene, both peers.</b> The server branch used to be behind
		/// <c>#if UNITY_SERVER</c>, which is undefined in the editor the scene server is developed
		/// in — so in that configuration the server took the client branch and traced the
		/// <i>default</i> physics scene, which for a multi-scene server holds none of the colliders
		/// around the character. Asking the character which scene it is in answers correctly on
		/// every peer and in every build, and it is what makes the owner and the server trace the
		/// same geometry. The aim itself already matches: <c>KCCPlayer</c> quantises the direction
		/// through <c>AimDirectionCompression</c> before it enters the replicate, so the owner
		/// simulates with the value the server will receive rather than with its raw camera.
		/// </para>
		/// </remarks>
		/// <param name="origin">The origin of the ray.</param>
		/// <param name="direction">The direction of the ray.</param>
		/// <param name="maxDistance">The maximum distance for the raycast.</param>
		/// <returns>The updated TargetInfo.</returns>
		public TargetInfo UpdateTarget(Vector3 origin, Vector3 direction, float maxDistance)
		{
			Last = Current;

			float distance = maxDistance.Clamp(0.0f, MAX_TARGET_DISTANCE);
			PhysicsScene physicsScene = ResolvePhysicsScene();

			/* Compensation is a server-side query against authoritative history and is deliberately
			 * not part of the deterministic simulation — a client has no history to rewind and must
			 * not try. TryResolve also declines for server-driven characters (an NPC brain aims at
			 * live positions, so rewinding its targets would move them away from where it aimed) and
			 * for connections whose tick bookkeeping is not established yet. */
			if (base.IsServerInitialized &&
				LagCompensationTick.TryResolve(Character, base.TimeManager, out RewindTarget rewindTarget))
			{
				/* The caster is excluded: it aims from where it is now, not where it was. A nested
				 * scope is refused rather than stacked by the registry, so an acquisition made from
				 * inside another rewind runs against that one instead of stranding characters in the
				 * past. Disposal restores every displaced character, including if the trace throws. */
				using (LagCompensationRegistry.Rewind(gameObject.scene, rewindTarget, Character))
				{
					return Trace(physicsScene, origin, direction, distance);
				}
			}

			return Trace(physicsScene, origin, direction, distance);
		}

		/// <summary>
		/// Performs the acquisition trace and writes <see cref="Current"/>.
		/// </summary>
		/// <remarks>
		/// <b>A miss clears the target.</b> Every path that does not end on a collider writes an
		/// empty <see cref="TargetInfo"/> — including the second trace, the one fired through the
		/// caster's own capsule, whose result used to be dropped on the floor. Leaving the previous
		/// acquisition in place on a miss is how a cast that hit nothing lands on whoever the
		/// <i>previous</i> cast was aimed at, which is both wrong and unattributable.
		/// </remarks>
		private TargetInfo Trace(PhysicsScene physicsScene, Vector3 origin, Vector3 direction, float distance)
		{
			Ray ray = new Ray(origin, direction);

			if (!physicsScene.Raycast(origin, direction, out RaycastHit hit, distance, LayerMask))
			{
				Current = new TargetInfo(null, ray.GetPoint(distance));
				return Current;
			}

			// A ray that starts inside the caster hits the caster. Push through it and take whatever
			// is behind, rather than reporting that a character targeted itself.
			IPlayerCharacter hitPlayerCharacter = hit.transform.GetComponent<IPlayerCharacter>();
			if (hitPlayerCharacter != null &&
				Character != null &&
				hitPlayerCharacter.ID == Character.ID)
			{
				Vector3 newRayOrigin = hit.point + direction.normalized * 0.1f;
				float remaining = (distance - hit.distance).Max(0.0f);
				if (!physicsScene.Raycast(newRayOrigin, direction, out hit, remaining, LayerMask))
				{
					Current = new TargetInfo(null, ray.GetPoint(distance));
					return Current;
				}
			}

			Current = new TargetInfo(hit.transform, hit.point);
			return Current;
		}

		/// <summary>
		/// The physics scene this character lives in: the KCC motor's for players, the owning
		/// Unity scene's for everything else.
		/// </summary>
		/// <remarks>
		/// Resolved at runtime rather than compiled per build target, so an editor-hosted scene
		/// server traces the same colliders a headless one does.
		/// </remarks>
		private PhysicsScene ResolvePhysicsScene()
		{
			if (PlayerCharacter != null && PlayerCharacter.Motor != null)
			{
				// Validity checked because the motor's scene is only populated once it has
				// initialised; tracing a default PhysicsScene hits nothing at all, which would read
				// as "you are aiming at empty air" rather than as the not-ready-yet that it is.
				PhysicsScene motorScene = PlayerCharacter.Motor.PhysicsScene;
				if (motorScene.IsValid())
				{
					return motorScene;
				}
			}
			return gameObject.scene.IsValid() ? gameObject.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
		}
	}
}
