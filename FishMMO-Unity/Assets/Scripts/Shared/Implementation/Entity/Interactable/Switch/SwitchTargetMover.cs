using FishMMO.Shared.Core;
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
	/// The motion is driven in <c>Update</c> on both peers rather than replicated per-frame. Only
	/// the switch's state crosses the wire — once, when it changes — and each peer plays the same
	/// deterministic interpolation from wherever it currently is. A client that arrives mid-swing,
	/// or misses the message entirely and gets the state from the spawn payload, converges on the
	/// correct end pose instead of holding a wrong one.
	/// </para>
	/// <para>
	/// The server moves too, and must: this transform carries the collider the character controller
	/// tests against, so a door that only opened on clients would be an invisible wall.
	/// </para>
	/// </remarks>
	public class SwitchTargetMover : MonoBehaviour, ISwitchTarget
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
			ApplyPose();
		}

		/// <inheritdoc />
		public void Activate(IPlayerCharacter activator)
		{
			IsActivated = true;
		}

		/// <inheritdoc />
		public void Deactivate(IPlayerCharacter activator)
		{
			IsActivated = false;
		}

		/// <inheritdoc />
		public void SnapTo(bool activated)
		{
			IsActivated = activated;

			// Travel is set to the destination as well as the state, so Update finds nothing left
			// to move and the pose is written once, here.
			travel = activated ? 1.0f : 0.0f;
			ApplyPose();
		}

		private void Update()
		{
			float target = IsActivated ? 1.0f : 0.0f;
			if (Mathf.Approximately(travel, target))
			{
				return;
			}

			if (travelSeconds <= 0.0f)
			{
				travel = target;
			}
			else
			{
				travel = Mathf.MoveTowards(travel, target, Time.deltaTime / travelSeconds);
			}

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
