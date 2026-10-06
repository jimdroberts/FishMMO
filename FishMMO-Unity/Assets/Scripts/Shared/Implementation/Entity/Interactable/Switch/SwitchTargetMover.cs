using System;
using FishMMO.Shared.Core;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Timing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A <see cref="ISwitchTarget"/> that slides and/or rotates a transform between a closed pose
	/// and an open pose — a door, a portcullis, a drawbridge, a moving platform.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The closed pose is wherever the moved transform sits when the scene loads, so a designer
	/// places the door shut and describes only the offset that opens it. Offsets are local to the
	/// moved transform's parent, so a door rotated to fit a wall opens along its own axis rather
	/// than a world one.
	/// </para>
	/// <para>
	/// The motion is worked out in <c>Update</c> on both peers rather than replicated per-frame. Only
	/// the switch's state crosses the wire — once, when it changes — with the server tick the change
	/// began at and how far along the travel it stood then (<see cref="ITimedSwitchTarget"/>). Every
	/// peer evaluates the same pose from FishNet's synchronised tick, so the door stands in the same
	/// place on every screen and on the server, whose collider the character controller tests. It
	/// used to start on each peer when that peer heard of the change (a round trip apart), run on its
	/// own frame times, and snap a late arrival to the end pose while the server was still mid-swing.
	/// </para>
	/// <para>
	/// The server moves too, and must: this transform carries the collider the character controller
	/// tests against, so a door that only opened on clients would be an invisible wall.
	/// </para>
	/// </remarks>
	public class SwitchTargetMover : MonoBehaviour, ITimedSwitchTarget
	{
		[Tooltip("Transform to move. Defaults to this GameObject's own transform.")]
		[SerializeField]
		private Transform movedTransform;

		[Tooltip("Local position offset from the closed pose to the open pose.")]
		[SerializeField]
		private Vector3 openPositionOffset = Vector3.zero;

		[Tooltip("Local euler rotation offset from the closed pose to the open pose.")]
		[SerializeField]
		private Vector3 openRotationOffset = new Vector3(0.0f, 90.0f, 0.0f);

		[Tooltip("Seconds the full open or close takes. 0 snaps instantly.")]
		[Min(0.0f)]
		[SerializeField]
		private float travelSeconds = 1.0f;

		[Tooltip("State this target starts in when the scene loads.")]
		[SerializeField]
		private bool startActivated;

		/// <inheritdoc />
		public bool IsActivated { get; private set; }

		/// <summary>
		/// The closed pose, captured once from the authored transform.
		/// </summary>
		private Vector3 closedPosition;
		private Quaternion closedRotation;

		/// <summary>
		/// The open pose, derived from the closed pose and the authored offsets.
		/// </summary>
		private Vector3 openPosition;
		private Quaternion openRotation;

		/// <summary>
		/// How far along the travel this target currently is. 0 is closed, 1 is open.
		/// </summary>
		/// <remarks>
		/// Kept as a normalised scalar rather than a timer so that reversing mid-swing continues
		/// from where the door actually is. A timer would snap the door back to the far end before
		/// starting the return, which reads as a glitch every time a toggle is double-tapped.
		/// </remarks>
		private float travel;

		/// <summary>The server tick the last change began at, and how far along it stood then.</summary>
		private uint changeTick;
		private float changeTravel;

		/// <inheritdoc />
		public uint ChangeTick => changeTick;

		/// <inheritdoc />
		public float ChangeTravel => changeTravel;

		private void Awake()
		{
			if (movedTransform == null)
			{
				movedTransform = transform;
			}

			closedPosition = movedTransform.localPosition;
			closedRotation = movedTransform.localRotation;

			openPosition = closedPosition + openPositionOffset;
			openRotation = closedRotation * Quaternion.Euler(openRotationOffset);

			IsActivated = startActivated;
			travel = startActivated ? 1.0f : 0.0f;
			changeTick = 0u;
			changeTravel = travel;
			ApplyPose();
		}

		/// <inheritdoc />
		public void Activate(IPlayerCharacter activator)
		{
			Change(true);
		}

		/// <inheritdoc />
		public void Deactivate(IPlayerCharacter activator)
		{
			Change(false);
		}

		/// <inheritdoc />
		public void SnapTo(bool activated)
		{
			IsActivated = activated;

			// Travel is set to the destination as well as the state, so Update finds nothing left
			// to move and the pose is written once, here. Tick 0: a change long settled.
			travel = activated ? 1.0f : 0.0f;
			changeTick = 0u;
			changeTravel = travel;
			ApplyPose();
		}

		/// <inheritdoc />
		public void SetState(bool activated, uint tick, float fromTravel)
		{
			IsActivated = activated;
			changeTick = tick;
			changeTravel = Mathf.Clamp01(fromTravel);
			Update();
		}

		/// <summary>
		/// Begins a change at this tick, from wherever the travel stands at it: reversing mid-swing
		/// carries on from where the door actually is. The same state again is nothing (both callers
		/// rely on Activate and Deactivate being idempotent).
		/// </summary>
		private void Change(bool activated)
		{
			if (activated == IsActivated)
			{
				return;
			}
			TimeManager timeManager = SharedTimeManager();
			if (timeManager == null)
			{
				IsActivated = activated;
				return;
			}
			uint now = timeManager.Tick;
			changeTravel = TravelAt(now, timeManager.TickDelta);
			changeTick = now;
			IsActivated = activated;
		}

		/// <summary>
		/// The tick everything here is timed by, or null without a NetworkManager (an editor scene played on
		/// its own). Asked of the instances first: InstanceFinder logs "NetworkManager not found" every time
		/// it finds none, and this is asked every frame by every mover.
		/// </summary>
		private static TimeManager SharedTimeManager()
		{
			return NetworkManager.Instances.Count > 0 ? InstanceFinder.TimeManager : null;
		}

		/// <summary>How far along the travel is at a (fractional) server tick.</summary>
		private float TravelAt(double tick, double tickDelta)
		{
			float target = IsActivated ? 1.0f : 0.0f;
			// Instant, or a change long settled (tick 0: none since the scene loaded, or snapped).
			if (travelSeconds <= 0.0f || changeTick == 0u)
			{
				return target;
			}
			double elapsed = Math.Max(0.0, (tick - changeTick) * tickDelta);
			return Mathf.MoveTowards(changeTravel, target, (float)(elapsed / travelSeconds));
		}

		private void Update()
		{
			float target = IsActivated ? 1.0f : 0.0f;
			TimeManager timeManager = SharedTimeManager();
			float next;
			if (timeManager != null)
			{
				next = TravelAt(timeManager.Tick + timeManager.GetTickPercentAsDouble(), timeManager.TickDelta);
			}
			else if (travelSeconds <= 0.0f)
			{
				next = target;
			}
			else
			{
				// Offline (an editor preview): no tick to share, so its own frame time.
				next = Mathf.MoveTowards(travel, target, Time.deltaTime / travelSeconds);
			}

			if (Mathf.Approximately(next, travel))
			{
				return;
			}
			travel = next;
			ApplyPose();
		}

		/// <summary>
		/// Writes the pose for the current <see cref="travel"/> value.
		/// </summary>
		private void ApplyPose()
		{
			if (movedTransform == null)
			{
				return;
			}

			// Smoothstep, so the door eases in and out rather than starting and stopping abruptly.
			float eased = Mathf.SmoothStep(0.0f, 1.0f, travel);

			movedTransform.localPosition = Vector3.Lerp(closedPosition, openPosition, eased);
			movedTransform.localRotation = Quaternion.Slerp(closedRotation, openRotation, eased);
		}

#if UNITY_EDITOR
		private void OnDrawGizmosSelected()
		{
			Transform moved = movedTransform != null ? movedTransform : transform;
			Transform parent = moved.parent;

			Vector3 closedWorld = moved.position;
			Vector3 openLocal = moved.localPosition + openPositionOffset;
			Vector3 openWorld = parent != null ? parent.TransformPoint(openLocal) : openLocal;

			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(closedWorld, 0.25f);
			Gizmos.DrawWireSphere(openWorld, 0.25f);
			Gizmos.DrawLine(closedWorld, openWorld);
		}
#endif
	}
}
