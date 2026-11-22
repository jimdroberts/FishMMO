using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Holds the character it is instantiated under in the pose it has now, for as long as this object lives: the ice of a
	/// freeze. Put on a buff's effect prefab (the Buffs module's <c>BaseBuffTemplate.FXPrefabReference</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Derived, not sent.</b> A buff's effect is instantiated under the character's model on the owner and on every
	/// observer, and destroyed when the buff leaves (<c>BaseBuffTemplate.OnApplyFX</c>), so every peer that shows the
	/// ice also stops the body, and stops it for exactly as long. The movement and casting are stopped elsewhere, by the
	/// freeze's flag (<see cref="CharacterIncapacitation"/>); this is only how it looks.
	/// </para>
	/// <para>
	/// The hold is counted on <see cref="CharacterAnimationController.HoldPose"/>, so two overlapping freezes let go only
	/// when both have ended. The server builds no body, and nothing happens there.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class FXFreezePose : MonoBehaviour
	{
		// The class stays on the server (prefabs name it); its body is the client's, like HoldPose itself.
#if !UNITY_SERVER
		private CharacterAnimationController held;

		private void OnEnable()
		{
			held = GetComponentInParent<CharacterAnimationController>();
			held?.HoldPose(true);
		}

		private void OnDisable()
		{
			if (held != null)
			{
				held.HoldPose(false);
				held = null;
			}
		}
#endif
	}
}
