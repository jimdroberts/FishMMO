#if UNITY_EDITOR
using System;
using FishMMO.Shared.Core;
using FishNet.Component.Prediction;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A <see cref="Region"/> over the site that shows its name when a player walks in (Jim, 2026-10-10: settlements
	/// get a name-toast region).
	/// </summary>
	/// <remarks>
	/// <para>The region is a networked scene object (NetworkObject + <see cref="NetworkTrigger"/>, like every region in
	/// the shipped scenes) named after the site, because the toast shows <see cref="Region.Name"/>, the GameObject's
	/// name. Its enter list gets the shipped <c>AreaOfInterestDisplayNameAction</c> trigger.</para>
	/// <para>Other features ride the same region (<see cref="Ensure"/>): the exploration feature adds its discovery
	/// trigger to it.</para>
	/// </remarks>
	[Serializable]
	public class RegionFeature : PointOfInterestFeature
	{
		/// <summary>Layers a region's trigger reacts to; the shipped regions use Default and Player.</summary>
		public static int TriggerLayers => LayerMask.GetMask("Default", "Player");

		[Tooltip("The region's half-width as a share of the site's footprint radius.")]
		[Min(0.1f)]
		public float RadiusShare = 1.25f;

		[Tooltip("The region's height, metres.")]
		[Min(4.0f)]
		public float Height = 60.0f;

		[Tooltip("Show the site's name on entry.")]
		public bool ShowName = true;

		public override void Build(PointOfInterestSiteContext context)
		{
			Region region = Ensure(context, RadiusShare, Height);
			if (region == null || !ShowName)
			{
				return;
			}
			Trigger toast = PointOfInterestFeatureKit.Load<Trigger>(context, PointOfInterestFeatureKit.RegionNameTriggerPath);
			if (toast != null && !region.OnRegionEnter.Contains(toast))
			{
				region.OnRegionEnter.Add(toast);
				EditorUtility.SetDirty(region);
			}
		}

		/// <summary>The site's region, made (with the default size) if no feature has made it yet.</summary>
		public static Region Ensure(PointOfInterestSiteContext context, float radiusShare = 1.25f, float height = 60.0f)
		{
			Region existing = context.Root.GetComponentInChildren<Region>(true);
			if (existing != null)
			{
				return existing;
			}

			// Named after the site: Region.Name is the GameObject's name, and that is what the toast shows.
			GameObject host = context.AddChild(PointOfInterestFeatureKit.SiteName(context));
			host.transform.SetPositionAndRotation(context.Record.Position, Quaternion.Euler(0.0f, context.Record.Yaw, 0.0f));
			NetworkObject networkObject = PointOfInterestFeatureKit.AddNetworkObject(host);

			float halfWidth = Mathf.Max(8.0f, context.Record.Radius * radiusShare);
			BoxCollider box = host.AddComponent<BoxCollider>();
			box.isTrigger = true;
			box.size = new Vector3(halfWidth * 2.0f, height, halfWidth * 2.0f);
			box.center = new Vector3(0.0f, height * 0.25f, 0.0f);

			Region region = host.AddComponent<Region>();
			region.Collider = box;
			NetworkTrigger trigger = host.GetComponent<NetworkTrigger>();
			if (trigger == null)
			{
				trigger = host.AddComponent<NetworkTrigger>();
			}
			// Layers 0 detects nothing, silently (fishnet-networkcollider-layers-trap).
			trigger.SetLayers(TriggerLayers);

			PointOfInterestFeatureKit.AssignSceneId(networkObject, context, 0x4E61);
			EditorUtility.SetDirty(region);
			EditorUtility.SetDirty(trigger);
			return region;
		}
	}

	/// <summary>
	/// Counts the site once per character towards the "Places Discovered" exploration achievement, on its first entry
	/// into the site's region (<see cref="DiscoverSiteAction"/>).
	/// </summary>
	/// <remarks>
	/// Rides the site's region (made if no <see cref="RegionFeature"/> has), marks it with the site's unlock index
	/// (<see cref="PointOfInterestDiscovery"/>), and adds the shared "Site Discovered" trigger, which
	/// <see cref="PointOfInterestGameplayContent.EnsureDiscoveryContent"/> creates on first use.
	/// </remarks>
	[Serializable]
	public class ExplorationFeature : PointOfInterestFeature, IPointOfInterestUnlockFeature
	{
		public override void Build(PointOfInterestSiteContext context)
		{
			int index = PointOfInterestFeatureKit.UnlockIndex(context, "discovery trigger");
			if (index < 0)
			{
				return;
			}
			Trigger discovered = PointOfInterestGameplayContent.EnsureDiscoveryContent();
			if (discovered == null)
			{
				PointOfInterestFeatureKit.Note(context, "the Site Discovered trigger could not be made; no discovery.");
				return;
			}

			Region region = RegionFeature.Ensure(context);
			PointOfInterestDiscovery site = region.GetComponent<PointOfInterestDiscovery>();
			if (site == null)
			{
				site = region.gameObject.AddComponent<PointOfInterestDiscovery>();
			}
			site.SiteIndex = index;
			if (!region.OnRegionEnter.Contains(discovered))
			{
				region.OnRegionEnter.Add(discovered);
			}
			EditorUtility.SetDirty(site);
			EditorUtility.SetDirty(region);
		}
	}
}
#endif
