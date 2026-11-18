#if UNITY_EDITOR
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A ship adrift on the open sea (2026-10-25, phase I2p, decision 42): a weed-locked derelict, or the whaler towing her prize,
	/// laid as one hull moored on the water with no floor (never aground) through <see cref="FloatingBoats"/>, exactly as a harbour
	/// moors its boats: the root and its collider rise and fall only with the tide (the server keeps it, so she can be boarded), the
	/// drawn hull under it bobs on the swell on the client. Sails furled, or a longship's oars shipped: nobody is sailing her.
	/// </summary>
	/// <remarks>
	/// The sites are hosted by no biome until a round names them (PointOfInterestRules: <c>.Adrift(50, 1000).Only()</c>), so this
	/// builds nothing in any scene today. The whale fall, the barnacle log and the longline have no pieces yet (with the I7 vessels and
	/// props): their sites are named and discoverable, with nothing laid.
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestAdrift : IPointOfInterestSiteBuilder
	{
		static PointOfInterestAdrift() => PointOfInterestSiteBuilders.Register(new PointOfInterestAdrift());

		public bool Handles(PointOfInterestRecord record) => record.Kind == POIType.AdriftDerelict || record.Kind == POIType.WhaleTow;

		public void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			float sea = PropsFeature.SeaLevelOf(context);
			if (float.IsNegativeInfinity(sea) || float.IsInfinity(sea))
			{
				return;
			}
			// A derelict is now and then an old longship; a whaler is always a cog.
			bool longship = record.Kind == POIType.AdriftDerelict && context.Random.NextFloat() < 0.3f;
			string piece = longship ? "Longship" : "Cog";
			int variant = longship ? 1 : 0;
			GameObject prefab = StructureKit.Prefab(piece, variant);
			if (prefab == null || !StructurePieces.TryHull(piece, out float length, out _, out _))
			{
				context.Notes?.Add($"{record.Name}: no {piece} hull in the kit yet (Generate biome art); nothing laid.");
				return;
			}
			GameObject holder = context.AddChild(PointOfInterestHarbour.ObjectName);
			var at = new Vector3(record.Position.x, sea, record.Position.z);
			var berth = new FloatingBoats.Moored
			{
				Position = at,
				Yaw = record.Yaw,
				Floor = float.NegativeInfinity,
				Heel = 0f,
				Length = length,
				Phase = context.Random.NextFloat(),
			};
			berth.Boat = PointOfInterestHarbour.Moor(prefab, piece, variant, holder.transform, at, Quaternion.Euler(0f, berth.Yaw, 0f), out berth.Visual, context.Palette);
			holder.AddComponent<FloatingBoats>().Configure(new[] { berth }, null, null);
			context.Notes?.Add($"{record.Name}: a {piece} adrift.");
		}
	}
}
#endif
