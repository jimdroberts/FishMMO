using System.Collections.Generic;
using FishNet.Broadcast;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server → client: the whole weather timeline of the scene the character is in. Sent on
	/// arrival and in answer to <see cref="WeatherResyncRequestBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// Lists, not arrays: see the wire array serializer gap. Every client in a scene receives it,
	/// not only those near a storm — a storm 3 km away has no network object for interest
	/// management to track, yet it is on the horizon.
	/// </remarks>
	public struct WeatherTimelineBroadcast : IBroadcast
	{
		public string SceneName;
		public uint Revision;
		public uint Seed;
		public byte SceneMode;
		public int FixedPresetID;
		public float FixedIntensity;
		public List<WeatherLayerEntry> Layers;
		public List<StormCell> Cells;
		public WeatherClimateEntry Climate;
		public WeatherCover Cover;
		/// <summary>The server tick <see cref="Cover"/> was measured at.</summary>
		public uint CoverTick;
	}

	/// <summary>
	/// Server → client: what changed since the previous revision. A client that sees a gap asks for
	/// the whole timeline instead of guessing.
	/// </summary>
	public struct WeatherDeltaBroadcast : IBroadcast
	{
		public string SceneName;
		/// <summary>Must be exactly one more than the revision the client holds.</summary>
		public uint Revision;
		/// <summary>Added or replaced layers, matched by handle.</summary>
		public List<WeatherLayerEntry> Layers;
		public List<ushort> RemovedLayers;
		/// <summary>Added or replaced cells, matched by id.</summary>
		public List<StormCell> Cells;
		public List<ushort> RemovedCells;
		public bool HasClimate;
		public WeatherClimateEntry Climate;
		public bool HasCover;
		public WeatherCover Cover;
		public uint CoverTick;
	}

	/// <summary>Client → server: my weather timeline has a gap; send it whole.</summary>
	public struct WeatherResyncRequestBroadcast : IBroadcast
	{
		public uint HaveRevision;
	}

	/// <summary>
	/// Server → client: the world clock's anchor, and the one it is easing away from, so a client
	/// that arrives mid-correction computes the same time as everyone else.
	/// </summary>
	public struct WorldClockBroadcast : IBroadcast
	{
		public WorldClockAnchor Anchor;
		public WorldClockAnchor Previous;
		public bool HasPrevious;
		public uint SlewTicks;
	}
}
