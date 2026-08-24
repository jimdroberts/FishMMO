using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that applies a buff to a character in a region.
	/// Suppressed during prediction reconciliation.
	/// </summary>
	[Serializable]
	public class ApplyRegionBuffAction : BaseAction
	{
		/// <summary>
		/// The buff template to apply.
		/// </summary>
		[Tooltip("The buff template to apply to the character.")]
		public BaseBuffTemplate Buff;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null || Buff == null) return;

			if (eventData != null &&
				eventData.TryGet(out RegionEventData regionData) &&
				regionData.IsReconciling)
			{
				return;
			}

			if (!initiator.TryGet(out IBuffController buffController)) return;

			// ApplyAuthoritative self-corrects to the replicate domain via
			// BuffController's last replicate tick. The tick argument is only used as a
			// fallback before the first OnReplicate fires.
			/* No caster: a region applies its buff by virtue of the character standing in it, so
			 * there is nobody to credit for a tick. Damage from an environmental hazard should not
			 * be attributed to the person it happens to be hurting. */
			buffController.ApplyAuthoritative(Buff, initiator.GetLocalTick());
		}
	}
}