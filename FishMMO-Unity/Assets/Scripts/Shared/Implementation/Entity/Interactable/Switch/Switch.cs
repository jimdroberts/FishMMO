using FishMMO.Shared.Core;
using FishNet.Connection;
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

		private string title = "Switch";

		/// <summary>
		/// Display title shown above the switch.
		/// </summary>
		public override string Title { get { return title; } }

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
		}

		/// <summary>
		/// Reads the switch's state and drives the target to match it.
		/// </summary>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);

			bool activated = reader.ReadBoolean();
			if (SwitchTarget == null)
			{
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