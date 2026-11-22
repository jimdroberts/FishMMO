using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// While it lives under the LOCAL player's own character (a See Invisible buff's effect), this client sees invisible
	/// characters (<see cref="ConcealmentView"/>). Under anyone else's character it does nothing: what another player can see
	/// is theirs to draw.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class FXSeeInvisible : MonoBehaviour
	{
		private bool held;

		private void OnEnable()
		{
			ICharacter character = GetComponentInParent<ICharacter>();
			if (character != null && character.NetworkObject != null && character.NetworkObject.IsOwner)
			{
				ConcealmentView.HoldSeeInvisible(true);
				held = true;
			}
		}

		private void OnDisable()
		{
			if (held)
			{
				ConcealmentView.HoldSeeInvisible(false);
				held = false;
			}
		}
	}
}
