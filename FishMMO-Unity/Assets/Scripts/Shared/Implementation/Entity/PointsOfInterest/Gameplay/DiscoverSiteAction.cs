using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action for a point of interest's region: records the site as discovered by the entering
	/// character. Server only.
	/// </summary>
	/// <remarks>
	/// <para>Abortable, and authored with <see cref="BaseAction.StopChainOnFailure"/> set, exactly like
	/// <see cref="UnlockWaypointAction"/>: it fails when the site was already discovered, so the
	/// exploration achievement increment after it counts each site once per character rather than once
	/// per entry. It also fails, without counting, while the character's record is not loaded yet; the
	/// next entry counts it.</para>
	/// <para>The site is the region's <see cref="PointOfInterestDiscovery"/>; a region without one does
	/// nothing.</para>
	/// </remarks>
	[Serializable]
	public class DiscoverSiteAction : BaseAction, IAbortableAction
	{
		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			TryExecute(initiator, eventData);
		}

		/// <inheritdoc />
		public bool TryExecute(ICharacter initiator, EventData eventData)
		{
			if (!EcaAuthority.IsServer(initiator, eventData) || initiator is not IPlayerCharacter player)
			{
				return false;
			}
			if (eventData == null || !eventData.TryGet(out RegionEventData regionData) || regionData.IsReconciling ||
				regionData.Region == null)
			{
				return false;
			}
			PointOfInterestDiscovery site = regionData.Region.GetComponent<PointOfInterestDiscovery>();
			if (site == null || !WaypointUnlockMask.IsValidIndex(site.SiteIndex))
			{
				return false;
			}

			PortalActivationStore store = PortalActivationStore.Current;
			if (!store.IsCharacterLoaded(player.ID))
			{
				store.RequestLoad(player.ID, null);
				return false;
			}
			return store.ActivateForCharacter(player.ID, PointOfInterestDiscovery.RecordKey(site.gameObject.scene.name), site.SiteIndex);
		}
	}
}
