using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Stops the caster's rush (<see cref="CasterRushAction"/>): a charge that has struck its target stops at it.
	/// </summary>
	/// <remarks>
	/// Wire it to the rush object's OnHit. Server and owner (<see cref="EcaAuthority.MayPredict"/>); a replay does not repeat
	/// it, because the hit is not replayed either — the reconciled motor state already carries where the server stopped.
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
				player.CharacterController.EndMotion();
			}
		}
	}
}
