using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that asks the interacting player whether to hire the Mercenary they spoke to: sends the offer (its kit,
	/// name and price), naming the Mercenary so the hire can be checked against it. Server only.
	/// </summary>
	[Serializable]
	public class SendMercenaryOfferAction : BaseAction
	{
		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Runtime check, not #if UNITY_SERVER: the editor runs the scene server without that define (BaseAction.IsServer).
			if (!IsServer(initiator))
			{
				return;
			}
			if (initiator is not IPlayerCharacter player)
			{
				return;
			}
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable is not MercenaryRecruiter recruiter ||
				recruiter.Kit == null)
			{
				return;
			}
			SendToOwner(player, new MercenaryOfferBroadcast()
			{
				InteractableID = recruiter.ID,
				KitID = recruiter.Kit.ID,
				Name = MercenaryRules.CleanName(recruiter.DisplayName),
				Cost = Math.Max(0L, recruiter.Kit.HireCost),
			});
		}
	}
}
