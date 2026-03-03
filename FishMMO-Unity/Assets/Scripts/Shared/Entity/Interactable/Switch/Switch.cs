using FishMMO.Server.Core.World.SceneServer;
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