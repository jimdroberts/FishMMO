using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A <see cref="ISwitchTarget"/> that enables and disables a set of GameObjects.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The simplest thing a switch can usefully drive: force fields, barriers, light sources,
	/// particle effects, a bridge that appears. Objects in <see cref="ActivateObjects"/> are
	/// switched on when the switch is activated; objects in <see cref="DeactivateObjects"/> are
	/// switched off at the same moment, which is what lets one switch swap a closed barrier for an
	/// open one rather than needing two switches.
	/// </para>
	/// <para>
	/// Runs identically on server and client. The server toggles because colliders in these
	/// hierarchies are real to the character controller; the client toggles because the player has
	/// to see it. Neither derives the state locally — <see cref="Switch"/> is authoritative and the
	/// client is driven by <c>SwitchStateBroadcast</c>.
	/// </para>
	/// </remarks>
	public class SwitchTargetObject : MonoBehaviour, ISwitchTarget
	{
		[Tooltip("Objects switched ON when activated, and OFF when deactivated.")]
		[SerializeField]
		private List<GameObject> activateObjects = new List<GameObject>();

		[Tooltip("Objects switched OFF when activated, and ON when deactivated.")]
		[SerializeField]
		private List<GameObject> deactivateObjects = new List<GameObject>();

		[Tooltip("State this target starts in when the scene loads.")]
		[SerializeField]
		private bool startActivated;

		/// <inheritdoc />
		public bool IsActivated { get; private set; }

		/// <summary>
		/// Objects switched on by activation.
		/// </summary>
		public List<GameObject> ActivateObjects => activateObjects;

		/// <summary>
		/// Objects switched off by activation.
		/// </summary>
		public List<GameObject> DeactivateObjects => deactivateObjects;

		private void Awake()
		{
			// Apply the authored starting state so the scene matches IsActivated from frame one.
			IsActivated = startActivated;
			Apply(IsActivated);
		}

		/// <inheritdoc />
		public void Activate(IPlayerCharacter activator)
		{
			IsActivated = true;
			Apply(true);
		}

		/// <inheritdoc />
		public void Deactivate(IPlayerCharacter activator)
		{
			IsActivated = false;
			Apply(false);
		}

		/// <inheritdoc />
		/// <remarks>
		/// Identical to <see cref="Activate"/> and <see cref="Deactivate"/> here: toggling a
		/// GameObject has no transition to skip. It exists so the contract holds for every target.
		/// </remarks>
		public void SnapTo(bool activated)
		{
			IsActivated = activated;
			Apply(activated);
		}

		/// <summary>
		/// Drives both object lists to match the given state.
		/// </summary>
		/// <param name="activated">The state to apply.</param>
		private void Apply(bool activated)
		{
			SetActive(activateObjects, activated);
			SetActive(deactivateObjects, !activated);
		}

		/// <summary>
		/// Sets the active state of every non-null entry in a list.
		/// </summary>
		/// <remarks>
		/// Null entries are skipped rather than thrown on. An empty slot in an inspector list is an
		/// ordinary authoring slip, and taking the whole switch down for one — losing every object
		/// after it in the list as well — is the failure this project has already had to fix in
		/// <c>Interactable.ExecuteOnInteract</c>.
		/// </remarks>
		/// <param name="objects">The objects to drive.</param>
		/// <param name="active">The state to set.</param>
		private static void SetActive(List<GameObject> objects, bool active)
		{
			if (objects == null)
			{
				return;
			}
			for (int i = 0; i < objects.Count; ++i)
			{
				GameObject target = objects[i];
				if (target == null)
				{
					continue;
				}
				target.SetActive(active);
			}
		}
	}
}
