using System;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What this client can see of invisible characters: whether its own character sees invisible (a See Invisible buff,
	/// <see cref="FXSeeInvisible"/>). Client-side and derived: nothing on the wire.
	/// </summary>
	/// <remarks>
	/// Only the buff-made invisibility (<see cref="FXConcealment"/>) answers to this. <c>/gm invis</c> is not revealed by it:
	/// a hidden staff character is culled from every observer by the server and drawn faded only to the owner and staff
	/// (<see cref="CharacterAnimationController.SetFaded"/>), and neither path reads this.
	/// </remarks>
	public static class ConcealmentView
	{
		private static int seeInvisibleHolds;

		/// <summary>True while the local player's character sees invisible characters.</summary>
		public static bool SeesInvisible => seeInvisibleHolds > 0;

		/// <summary>Raised when <see cref="SeesInvisible"/> changes, so every concealed character re-draws.</summary>
		public static event Action Changed;

		/// <summary>Takes or releases one hold on <see cref="SeesInvisible"/>.</summary>
		public static void HoldSeeInvisible(bool hold)
		{
			bool before = SeesInvisible;
			seeInvisibleHolds = hold ? seeInvisibleHolds + 1 : Math.Max(0, seeInvisibleHolds - 1);
			if (before != SeesInvisible)
			{
				Changed?.Invoke();
			}
		}

		/// <summary>Whether an invisible character's nameplate is drawn on this client: only to a viewer who sees invisible.</summary>
		public static bool ShowsConcealedPlate => SeesInvisible;

		/// <summary>Clears every hold (a new session, a test).</summary>
		public static void Reset()
		{
			bool before = SeesInvisible;
			seeInvisibleHolds = 0;
			if (before)
			{
				Changed?.Invoke();
			}
		}
	}

	/// <summary>
	/// Makes the character it is instantiated under invisible to look at, for as long as this object lives: the model drawn
	/// at <see cref="CharacterAnimationController.ConcealedOpacity"/> and its nameplate hidden
	/// (<see cref="Nameplate.ConcealCount"/>). Put on an invisibility buff's effect prefab.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Derived, not sent.</b> A buff's effect is instantiated under the character's model on the owner and every observer
	/// and destroyed when the buff ends, so every client hides exactly what the server's buff says, for exactly as long.
	/// What NPCs do about it is the buff's flag (<see cref="CharacterFlags.IsInvisible"/>) on the server.
	/// </para>
	/// <para>
	/// A viewer who sees invisible (<see cref="ConcealmentView"/>) sees the model at
	/// <see cref="CharacterAnimationController.RevealedOpacity"/> and its nameplate as usual.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class FXConcealment : MonoBehaviour
	{
		private CharacterAnimationController animation;
		private Nameplate plate;

		private void OnEnable()
		{
			animation = GetComponentInParent<CharacterAnimationController>();
			animation?.SetConcealed(true);
			ICharacter character = GetComponentInParent<ICharacter>();
			plate = character?.CharacterNameplate;
			if (plate != null && !ConcealmentView.ShowsConcealedPlate)
			{
				plate.ConcealCount++;
				plateHeld = true;
			}
			ConcealmentView.Changed += OnViewChanged;
		}

		private bool plateHeld;

		private void OnDisable()
		{
			ConcealmentView.Changed -= OnViewChanged;
			if (animation != null)
			{
				animation.SetConcealed(false);
				animation = null;
			}
			ReleasePlate();
			plate = null;
		}

		private void ReleasePlate()
		{
			if (plateHeld && plate != null)
			{
				plate.ConcealCount = Math.Max(0, plate.ConcealCount - 1);
			}
			plateHeld = false;
		}

		private void OnViewChanged()
		{
			animation?.RefreshConcealment();
			if (plate == null)
			{
				return;
			}
			if (ConcealmentView.ShowsConcealedPlate)
			{
				ReleasePlate();
			}
			else if (!plateHeld)
			{
				plate.ConcealCount++;
				plateHeld = true;
			}
		}
	}
}
