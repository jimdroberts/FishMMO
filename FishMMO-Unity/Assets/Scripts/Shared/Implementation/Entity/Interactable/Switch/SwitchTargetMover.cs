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
	public class SwitchTargetMover : MonoBehaviour, ITimedSwitchTarget, IClearanceSwitchTarget, ITickPosed
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

		[Tooltip("Colliders that block only while the target is fully closed and still (a door's leaf). While it moves, and while it stands open, they are triggers: the character controller ignores them, so a swinging door never shoves a player or fights their prediction, and the switch's raycast still finds them.")]
		[SerializeField]
		private Collider[] solidWhenClosed;

		[Tooltip("Also solid when fully open and still: a drawbridge, which is the way across when it is down.")]
		[SerializeField]
		private bool solidWhenOpenToo;

		[Tooltip("The transform is authored in the OPEN pose (the closed pose is worked out from it and the offsets): a drawbridge saved lowered, so the scene, the NavMesh baked from it and the editor show the way across.")]
		[SerializeField]
		private bool authoredOpen;

		/// <inheritdoc />
		public bool IsActivated { get; private set; }

		/// <summary>
		/// The closed pose, captured once from the authored transform (or worked out from it, <see cref="authoredOpen"/>).
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

		/// <summary><see cref="SolidTick"/> for a pose settled since before anyone watched: solid at every tick.</summary>
		private const uint SolidSettled = 1u;

		/// <summary>The server tick the resting pose is solid from; 0 while undecided (see <see cref="ITimedSwitchTarget.SolidTick"/>).</summary>
		private uint solidTick = SolidSettled;

		/// <summary>The last tick the server looked for characters in the resting pose (once a tick, not once a frame).</summary>
		private uint clearanceCheckedTick;

		/// <inheritdoc />
		public uint SolidTick => solidTick;

		/// <inheritdoc />
		public event Action<ITimedSwitchTarget> SolidDecided;

		private void Awake()
		{
			if (movedTransform == null)
			{
				movedTransform = transform;
			}

			if (authoredOpen)
			{
				openPosition = movedTransform.localPosition;
				openRotation = movedTransform.localRotation;
				closedPosition = openPosition - openPositionOffset;
				closedRotation = openRotation * Quaternion.Inverse(Quaternion.Euler(openRotationOffset));
			}
			else
			{
				closedPosition = movedTransform.localPosition;
				closedRotation = movedTransform.localRotation;
				openPosition = closedPosition + openPositionOffset;
				openRotation = closedRotation * Quaternion.Euler(openRotationOffset);
			}

			IsActivated = startActivated;
			travel = startActivated ? 1.0f : 0.0f;
			changeTick = 0u;
			changeTravel = travel;
			solidTick = SolidSettled;
			ApplyPose();
			ApplySolid(!startActivated || solidWhenOpenToo);
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
			solidTick = SolidSettled;
			ApplyPose();
			ApplySolid(!activated || solidWhenOpenToo);
		}

		/// <inheritdoc />
		public void SetState(bool activated, uint tick, float fromTravel, uint solidFrom)
		{
			// What a replay of the ticks before this change must still find: the state in force until now.
			if (tick != changeTick && tick != 0u)
			{
				TimeManager timeManager = SharedTimeManager();
				solidBeforeChange = timeManager != null ? SolidAt(tick, timeManager.TickDelta) : solid;
			}
			IsActivated = activated;
			changeTick = tick;
			changeTravel = Mathf.Clamp01(fromTravel);
			solidTick = solidFrom;
			Update();
		}

		/// <summary>Candidates near a pose's collider (<see cref="IsObstructed"/>); server, on an interaction only.</summary>
		private static readonly Collider[] nearby = new Collider[32];

		/// <inheritdoc />
		/// <remarks>
		/// Each <see cref="solidWhenClosed"/> collider is placed where that pose puts it, and every character collider near
		/// it is tested against it there (<see cref="Physics.ComputePenetration"/> takes both poses, so the leaf is never
		/// moved to ask). The broad phase is a sphere round the posed collider with slack for transforms physics has not
		/// synced yet; the test itself reads the transforms.
		/// </remarks>
		public bool IsObstructed(bool activated)
		{
			// Only a pose that turns solid can close around anyone: a door's open pose never does, a drawbridge's lowered one does.
			if (solidWhenClosed == null || solidWhenClosed.Length == 0 || movedTransform == null || (activated && !solidWhenOpenToo))
			{
				return false;
			}
			Transform parent = movedTransform.parent;
			Vector3 localPosition = activated ? openPosition : closedPosition;
			Quaternion localRotation = activated ? openRotation : closedRotation;
			Vector3 posePosition = parent != null ? parent.TransformPoint(localPosition) : localPosition;
			Quaternion poseRotation = parent != null ? parent.rotation * localRotation : localRotation;
			// From where the moved transform stands now to the pose; everything under it is carried rigidly.
			Quaternion turn = poseRotation * Quaternion.Inverse(movedTransform.rotation);
			Vector3 pivot = movedTransform.position;
			PhysicsScene physicsScene = gameObject.scene.IsValid() ? gameObject.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;

			foreach (Collider leaf in solidWhenClosed)
			{
				if (leaf == null || !leaf.enabled || !leaf.gameObject.activeInHierarchy)
				{
					continue;
				}
				Transform leafTransform = leaf.transform;
				Vector3 leafPosition = posePosition + turn * (leafTransform.position - pivot);
				Quaternion leafRotation = turn * leafTransform.rotation;
				Bounds bounds = leaf.bounds;
				Vector3 centre = posePosition + turn * (bounds.center - pivot);
				int count = physicsScene.OverlapSphere(centre, bounds.extents.magnitude + 0.5f, nearby, ~0, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < count; i++)
				{
					Collider other = nearby[i];
					nearby[i] = null;
					if (other == null || System.Array.IndexOf(solidWhenClosed, other) >= 0 || other.GetComponentInParent<ICharacter>() == null)
					{
						continue;
					}
					Transform otherTransform = other.transform;
					if (Physics.ComputePenetration(leaf, leafPosition, leafRotation, other, otherTransform.position, otherTransform.rotation, out _, out _))
					{
						for (int j = i + 1; j < count; j++)
						{
							nearby[j] = null;
						}
						return true;
					}
				}
			}
			return false;
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
			solidBeforeChange = SolidAt(now, timeManager.TickDelta);
			changeTravel = TravelAt(now, timeManager.TickDelta);
			changeTick = now;
			IsActivated = activated;
			// Not solid from the tick it starts to move (the server's controller sees it at once), nor at rest until the
			// server finds its resting place clear (Update).
			solidTick = 0u;
			ApplySolid(false);
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
				// Solid again only from a whole tick it is at rest in a solid pose (the motion is timed by the shared tick;
				// the frame's fraction is presentation), and only from the tick the server found that pose clear of
				// characters: shut on someone in the doorway, a door stays passable until they have stepped out of it,
				// rather than closing its leaf round them for the controller to pop them out through it.
				uint tick = timeManager.Tick;
				float settled = TravelAt(tick, timeManager.TickDelta);
				bool restsSolid = (!IsActivated && settled <= 0.0f) || (solidWhenOpenToo && IsActivated && settled >= 1.0f);
				if (restsSolid && solidTick == 0u && tick != clearanceCheckedTick && timeManager.NetworkManager.IsServerStarted)
				{
					// The server's decision alone, once a tick, told to the observers (Switch): a client deciding for
					// itself from interpolated characters would disagree with it about where the controller may go.
					clearanceCheckedTick = tick;
					if (!IsObstructed(IsActivated))
					{
						solidTick = Math.Max(tick, SolidSettled + 1u);
						SolidDecided?.Invoke(this);
					}
				}
				/* The solid state is NOT applied here any more: a frame's idea of the tick is not the tick a character is
				 * simulated at (an owner predicts ahead of it, and replays earlier ones). TickPoser applies it before every
				 * live and replayed tick, at the server tick that tick runs at, as the ships are posed. */
				TickPoser.Ensure();
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
			if (timeManager == null)
			{
				ApplySolid((!IsActivated && next <= 0.0f) || (solidWhenOpenToo && IsActivated && next >= 1.0f));
			}

			if (Mathf.Approximately(next, travel))
			{
				return;
			}
			travel = next;
			ApplyPose();
		}

		private bool solid = true;
		private bool solidApplied;
		/// <summary>Whether the colliders blocked just before the latest change: what a replay of earlier ticks must find.</summary>
		private bool solidBeforeChange = true;

		/// <summary>
		/// Whether the colliders block at server tick <paramref name="tick"/>: at rest in a solid pose from the tick the server
		/// found it clear, and in the state before the latest change for a tick before it (a replay reaching back past it).
		/// </summary>
		private bool SolidAt(uint tick, double tickDelta)
		{
			if (changeTick != 0u && tick < changeTick)
			{
				return solidBeforeChange;
			}
			float settled = TravelAt(tick, tickDelta);
			bool restsSolid = (!IsActivated && settled <= 0.0f) || (solidWhenOpenToo && IsActivated && settled >= 1.0f);
			return restsSolid && solidTick != 0u && (solidTick == SolidSettled || tick >= solidTick);
		}

		/// <inheritdoc />
		/// <remarks>
		/// Blocking or not, and when blocking, in the resting pose the controller collides with. Applied by
		/// <see cref="TickPoser"/> before every live and replayed tick, at the server tick that tick runs at. Before it, a
		/// door's collider turned solid or passable on the client's FRAME estimate of the server tick, which is not the tick
		/// the owner's prediction runs at and was never rewound for a replay, so walking through a door as it opened or shut
		/// was corrected.
		/// </remarks>
		public bool PoseForTick(uint tick, double tickDelta)
		{
			bool blocks = SolidAt(tick, tickDelta);
			bool changed = solidApplied && blocks != solid;
			ApplySolid(blocks);
			if (blocks)
			{
				float rest = changeTick != 0u && tick < changeTick ? changeTravel : TravelAt(tick, tickDelta);
				if (!Mathf.Approximately(rest, travel))
				{
					travel = rest;
					ApplyPose();
					changed = true;
				}
			}
			return changed;
		}

		private void OnEnable()
		{
			TickPoser.Add(this);
		}

		private void OnDisable()
		{
			TickPoser.Remove(this);
		}

		/// <summary>Makes the <see cref="solidWhenClosed"/> colliders block, or turns them into triggers; only when it changes.</summary>
		private void ApplySolid(bool blocks)
		{
			if (solidWhenClosed == null || solidWhenClosed.Length == 0 || (solidApplied && blocks == solid))
			{
				return;
			}
			solid = blocks;
			solidApplied = true;
			foreach (Collider c in solidWhenClosed)
			{
				if (c != null)
				{
					c.isTrigger = !blocks;
				}
			}
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
