using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Stops the caster's rush (<see cref="CasterRushAction"/>): a charge that has struck its target stops at it.
	/// </summary>
	/// <remarks>
	/// Wire it to the rush object's OnHit. Server and owner (<see cref="EcaAuthority.MayPredict"/>). The hit is not run again
	/// on a replay, so the owner's motor remembers the stop and makes it again (<see cref="KCCController.EndMotionFromHit"/>);
	/// without that, a correction from before the hit carried the rush on past the target until the server's stop snapped it back.
	/// </remarks>
	[Serializable]
	public class EndCasterRushAction : BaseAction
	{
		/// <inheritdoc/>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			ICharacter caster = initiator ?? eventData?.Initiator;
			if (caster == null || !EcaAuthority.MayPredict(caster, eventData))
			{
				return;
			}
			if (caster is IPlayerCharacter player && player.CharacterController != null)
			{
				// From outside the movement step: the owner remembers it for its replays (EndMotionFromHit).
				player.CharacterController.EndMotionFromHit();
			}
		}
	}
}
