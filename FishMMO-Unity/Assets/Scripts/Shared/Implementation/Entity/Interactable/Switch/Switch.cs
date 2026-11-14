using FishMMO.Shared.Core;
using FishMMO.Logging;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Switch interactable that executes a function on another script when activated.
	/// Used for opening doors, unlocking chests, stopping or engaging traps, and similar mechanisms.
	/// The target object must implement <see cref="ISwitchTarget"/>.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Switch : Interactable, ISwitch
	{
		/// <summary>
		/// The target GameObject containing an <see cref="ISwitchTarget"/> component.
		/// Resolved at runtime via GetComponent.
		/// </summary>
		[Tooltip("The target GameObject with an ISwitchTarget component (door, chest, trap, etc).")]
		[SerializeField] private GameObject target;

		/// <summary>
		/// When true, the switch toggles between activated and deactivated states.
		/// When false, the switch can only be activated once.
		/// </summary>
		[Tooltip("When true, the switch toggles between activated and deactivated states.")]
		public bool IsToggle = true;

		/// <summary>
		/// Achievement to increment when a player operates this switch.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		bool ISwitch.IsToggle => IsToggle;

		/// <inheritdoc />
		AchievementTemplate ISwitch.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Cached reference to the resolved <see cref="ISwitchTarget"/> on the target GameObject.
		/// </summary>
		public ISwitchTarget SwitchTarget { get; private set; }

		[Tooltip("What the switch is called above it (\"Door\" for a door, \"Lever\" for a lever).")]
		[SerializeField] private string title = "Switch";

		/// <summary>
		/// Display title shown above the switch.
		/// </summary>
		public override string Title { get { return string.IsNullOrWhiteSpace(title) ? "Switch" : title; } }

		/// <summary>
		/// Title color for the switch UI label.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.silver); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (target != null)
			{
				SwitchTarget = target.GetComponent<ISwitchTarget>();
			}
		}

		public override void OnStartServer()
		{
			base.OnStartServer();

			if (SwitchTarget is ITimedSwitchTarget timed)
			{
				timed.SolidDecided -= OnTargetSolidDecided;
				timed.SolidDecided += OnTargetSolidDecided;
			}
		}

		public override void OnStopServer()
		{
			base.OnStopServer();

			if (SwitchTarget is ITimedSwitchTarget timed)
			{
				timed.SolidDecided -= OnTargetSolidDecided;
			}
		}

		/// <summary>The server found the target's resting place clear and made it solid: every observer turns it solid on the same tick.</summary>
		private void OnTargetSolidDecided(ITimedSwitchTarget timed)
		{
			BroadcastState(this, timed);
		}

		/// <summary>
		/// Tells the observers of the switch's NetworkObject the target's state (server only): world state, so sent to
		/// everyone who can see the switch, not to whoever threw it.
		/// </summary>
		/// <remarks>
		/// The NetworkObject that owns the switch, not one on the switch's own GameObject. A generated door's switch is
		/// on Door N/Leaf/Panel and its NetworkObject on Door N: looked up with GetComponent (2026-11-13) no broadcast was
		/// ever sent, the server opened the door (its leaf no longer solid) while every client kept it shut and solid,
		/// walking in was corrected through a closed door, and the next press closed it again unseen.
		/// </remarks>
		public static void BroadcastState(IInteractable interactable, ISwitchTarget switchTarget)
		{
			if (interactable == null || switchTarget == null)
			{
				return;
			}
			NetworkObject switchObject = interactable is NetworkBehaviour behaviour && behaviour.NetworkObject != null
				? behaviour.NetworkObject
				: interactable.GameObject.GetComponentInParent<NetworkObject>();
			if (switchObject == null)
			{
				Log.Warning("Switch", $"'{interactable.Name}' has no NetworkObject above it; its new state reached no client.");
				return;
			}
			ITimedSwitchTarget timed = switchTarget as ITimedSwitchTarget;
			switchObject.Broadcast(new SwitchStateBroadcast()
			{
				InteractableID = interactable.ID,
				Activated = switchTarget.IsActivated,
				ChangeTick = timed != null ? timed.ChangeTick : 0u,
				ChangeTravel = timed != null ? timed.ChangeTravel : 0f,
				SolidTick = timed != null ? timed.SolidTick : 0u,
			});
		}

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (SwitchTarget == null ||
				!base.CanInteract(character))
			{
				return false;
			}

			// Non-toggle switches can only be activated once
			if (!IsToggle && SwitchTarget.IsActivated)
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Writes the switch's current state so a client that arrives later sees the right pose.
		/// </summary>
		/// <remarks>
		/// <c>SwitchStateBroadcast</c> only reaches clients that were already observing when the
		/// switch was thrown. Without the state in the payload, a player walking into a room whose
		/// door was opened an hour ago finds it shut on their screen alone — and pressing the
		/// switch then <em>closes</em> it for everyone else, because the server's copy was open all
		/// along. This is the same gap NPCs had with their dead flag, and the same fix.
		/// </remarks>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);
			writer.WriteBoolean(SwitchTarget != null && SwitchTarget.IsActivated);
			// And when it changed, for a target that plays a change out: a player arriving mid-swing
			// sees it mid-swing, and one arriving later sees it settled (ITimedSwitchTarget).
			ITimedSwitchTarget timed = SwitchTarget as ITimedSwitchTarget;
			writer.WriteUInt32(timed != null ? timed.ChangeTick : 0u);
			writer.WriteSingle(timed != null ? timed.ChangeTravel : 0f);
			// And whether the server has made its resting pose solid yet (held passable while someone stands in it).
			writer.WriteUInt32(timed != null ? timed.SolidTick : 0u);
		}

		/// <summary>
		/// Reads the switch's state and drives the target to match it.
		/// </summary>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);

			bool activated = reader.ReadBoolean();
			uint changeTick = reader.ReadUInt32();
			float changeTravel = reader.ReadSingle();
			uint solidTick = reader.ReadUInt32();
			if (SwitchTarget == null)
			{
				return;
			}
			// Worked out from the tick the change began at: settled if that was long ago, partway if not.
			if (SwitchTarget is ITimedSwitchTarget timed)
			{
				timed.SetState(activated, changeTick, changeTravel, solidTick);
				return;
			}

			/* Snapped, not activated. This state is history — it describes a switch that was
			 * thrown before this client could see it — so the transition must not be replayed, or
			 * walking into a room would set every door in it swinging as though someone had just
			 * pulled the lever. */
			SwitchTarget.SnapTo(activated);
		}

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			if (target == null)
			{
				return;
			}

			Gizmos.color = GizmoColor;
			Gizmos.DrawLine(transform.position, target.transform.position);
			Gizmos.DrawWireSphere(target.transform.position, 0.3f);
		}
#endif
	}
}