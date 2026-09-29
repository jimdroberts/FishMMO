using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What a storm looks like from outside it: its cloud, where its rain falls, and where it hangs
	/// lower — worked out from the air it grows in, like everything else about it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A storm cell (<see cref="StormCell"/>) is where the storm's WEATHER is: the rain, the wind,
	/// the hail a player stands in. That is not the storm's cloud. A thunderstorm's rain falls from
	/// a small part of a cumulonimbus several kilometres across, whose anvil spreads tens of
	/// kilometres downwind at the tropopause; a supercell's tornado is a few hundred metres of a
	/// storm twenty kilometres wide, and hangs from a wall cloud under the storm's rotating
	/// updraught on its rear flank, while its rain and hail fall ahead of it; a squall line's rain is
	/// led by a shelf cloud along its gust front. Drawn from the cell alone, a storm was a rain
	/// shaft hanging from an ordinary sky and a tornado a funnel hanging from nothing.
	/// </para>
	/// <para>
	/// Everything here is relative to the cell's centre, in metres, in the world's x and z: the
	/// same for every client and the server, from the same air.
	/// </para>
	/// <list type="bullet">
	/// <item><b>Base</b>: the storm's own, lower than the open air's — its inflow is the moist air
	/// the storm feeds on, whose condensation level is lower (and a low base is what tornadoes
	/// want).</item>
	/// <item><b>Top</b>: as far as the air carries a parcel, overshooting the tropopause a little.</item>
	/// <item><b>Core</b>: the cumulonimbus tower; about a third as wide as it is deep.</item>
	/// <item><b>Anvil</b>: the updraught's outflow, spread along the tropopause downwind.</item>
	/// <item><b>Rain core</b>: on the forward flank, downwind of the updraught.</item>
	/// <item><b>Mesocyclone and wall cloud</b> (a supercell): the rotating updraught on the storm's
	/// rear flank — to the right of its motion in the northern hemisphere — two to six miles
	/// across, with a lowering under it a fraction of a mile to a few miles across, a few hundred
	/// metres below the rain-free base, which the tornado hangs from (the storm spotter's
	/// anatomy, NWS).</item>
	/// <item><b>Shelf cloud</b> (a squall line): a lowering wedge along the gust front's leading
	/// edge, where the cold outflow lifts the warm air it undercuts.</item>
	/// </list>
	/// </remarks>
	public struct StormAnatomy
	{
		/// <summary>False for a storm with no cloud of its own: a haboob, a dust devil, an eruption.</summary>
		public bool Valid;
		/// <summary>The storm's cloud base above the ground, m.</summary>
		public float BaseMetres;
		/// <summary>The tower's top above the ground, overshoot included, m.</summary>
		public float TopMetres;
		/// <summary>The cumulonimbus tower's radius, m.</summary>
		public float CoreRadius;
		/// <summary>The storm body's centre relative to the cell's, m. Zero but for a supercell, whose cell is its tornado.</summary>
		public Vector2 BodyOffset;
		/// <summary>The anvil's bottom and top above the ground, m.</summary>
		public float AnvilBase, AnvilTop;
		/// <summary>The anvil's radius, m; zero where the storm does not reach the tropopause.</summary>
		public float AnvilRadius;
		/// <summary>The anvil's centre relative to the body's, m: downwind.</summary>
		public Vector2 AnvilOffset;
		/// <summary>The rain core's centre relative to the body's, m.</summary>
		public Vector2 RainOffset;
		/// <summary>The rain core's radius, m.</summary>
		public float RainRadius;
		/// <summary>A supercell's rotating updraught's radius, m, centred on the cell (its tornado). Zero for every other kind.</summary>
		public float MesoRadius;
		/// <summary>The wall cloud's radius, m, centred on the cell.</summary>
		public float WallCloudRadius;
		/// <summary>How far below the base the wall cloud hangs, m.</summary>
		public float WallCloudDrop;
		/// <summary>A squall line's shelf cloud: how far below the base it hangs at the gust front, m.</summary>
		public float ShelfDrop;
		/// <summary>How far ahead of the rain the shelf reaches, m.</summary>
		public float ShelfWidth;
		/// <summary>The way the storm is going, a unit vector: its motion, or the wind's where it stands still.</summary>
		public Vector2 Forward;

		/// <summary>The wall cloud's underside above the ground, m: where a tornado's funnel hangs from.</summary>
		public float WallCloudBase => Mathf.Max(0f, BaseMetres - WallCloudDrop);

		/// <summary>
		/// The anatomy of a storm of this kind in the air around a place.
		/// </summary>
		/// <param name="kind">What kind of storm.</param>
		/// <param name="around">The weather where the storm stands: its open air and column, and the planet.</param>
		/// <param name="motion">How the storm is moving, m/s; its forward is the wind's where it is still.</param>
		/// <param name="latitudeDegrees">Where on the world: which flank a supercell rotates on.</param>
		/// <param name="cellRadius">The cell's own radius, m: its rain area, or a front's depth.</param>
		public static StormAnatomy Of(StormKind kind, in WeatherSample around, Vector2 motion, float latitudeDegrees, float cellRadius)
		{
			var anatomy = new StormAnatomy();
			if (!around.Planet.HasAir || around.Planet.Gravity <= 0f)
			{
				return anatomy;
			}
			switch (kind)
			{
				case StormKind.Thunderstorm:
				case StormKind.Supercell:
				case StormKind.SquallLine:
				case StormKind.TropicalCyclone:
					break;
				default:
					return anatomy;
			}

			AirColumn open = around.OpenColumn;
			WeatherDriver.Synoptic air = StormPhysics.Perturb(around.OpenAir, kind, 1f);
			AirColumn storm = AirColumn.Of(around.Planet, open.SurfaceKelvin, air.Humidity, air.Pressure, air.Instability);

			Vector2 wind = around.OpenAir.Wind;
			Vector2 forward = motion.sqrMagnitude > 0.01f ? motion.normalized
				: wind.sqrMagnitude > 1e-4f ? wind.normalized : Vector2.right;
			// To the right of the motion in the northern hemisphere, the left in the southern: the
			// flank a supercell's mesocyclone turns on, and the side its inflow comes from.
			float hemisphere = latitudeDegrees >= 0f ? 1f : -1f;
			Vector2 right = new Vector2(forward.y, -forward.x) * hemisphere;

			anatomy.Valid = true;
			anatomy.Forward = forward;
			// The base is where the storm's INFLOW condenses: the boundary-layer air it feeds on, which
			// is the open air near the ground, a little lowered by the damp its own rain and outflow
			// put back into it. Not the rain-soaked air inside the storm, which is saturated and would
			// put the base on the ground.
			anatomy.BaseMetres = Mathf.Max(150f, Mathf.Lerp(open.Base, Mathf.Min(open.Base, storm.Base), 0.3f));
			anatomy.TopMetres = Mathf.Max(anatomy.BaseMetres + 500f, storm.Deep ? storm.TowerCeiling : storm.Top);
			float depth = anatomy.TopMetres - anatomy.BaseMetres;

			// A cumulonimbus is about a third as wide as it is deep; a supercell's is broader, its one
			// updraught feeding the whole of it.
			float core = Mathf.Clamp(0.3f * depth, 2000f, 8000f);
			if (kind == StormKind.Supercell)
			{
				core = Mathf.Clamp(0.4f * depth, 3000f, 12000f);
			}
			else if (kind == StormKind.SquallLine || kind == StormKind.TropicalCyclone)
			{
				core = Mathf.Max(core, cellRadius);
			}
			anatomy.CoreRadius = core;

			// The anvil: what the updraught carries up spreads along the tropopause and streams away
			// downwind, several times the tower's width. Only a storm that reaches the tropopause.
			if (storm.Deep)
			{
				// At the tropopause, or where the tower stops if it does not quite reach it: an anvil
				// is never above the tower that feeds it.
				anatomy.AnvilTop = Mathf.Clamp(storm.Tropopause, anatomy.BaseMetres + 1000f, anatomy.TopMetres);
				anatomy.AnvilBase = Mathf.Clamp(Mathf.Max(storm.IceLevel, 0.75f * storm.Tropopause), anatomy.BaseMetres + 500f, anatomy.AnvilTop - 300f);
				anatomy.AnvilRadius = Mathf.Clamp(3f * core, 8000f, 40000f);
				Vector2 downwind = wind.sqrMagnitude > 1e-4f ? wind.normalized : forward;
				anatomy.AnvilOffset = downwind * (0.6f * anatomy.AnvilRadius);
			}

			switch (kind)
			{
				case StormKind.Supercell:
				{
					// The cell is the tornado, on the rear flank under the mesocyclone; the body stands
					// ahead of it and to its left, and its rain and hail ahead of that — the forward
					// flank. The wall cloud hangs under the rotating updraught a few hundred metres
					// below the rain-free base.
					anatomy.MesoRadius = Mathf.Clamp(0.35f * core, 1500f, 5000f);
					anatomy.BodyOffset = forward * (0.55f * core) - right * (0.35f * core);
					anatomy.RainOffset = forward * (0.5f * core) - right * (0.15f * core);
					anatomy.RainRadius = 0.55f * core;
					anatomy.WallCloudRadius = Mathf.Clamp(0.5f * anatomy.MesoRadius, 700f, 3500f);
					// Never more than most of the way down: a wall cloud is a lowering, not a fog.
					anatomy.WallCloudDrop = Mathf.Min(Mathf.Clamp(0.35f * anatomy.BaseMetres, 100f, 900f), 0.6f * anatomy.BaseMetres);
					break;
				}
				case StormKind.SquallLine:
				{
					// The shelf leads the rain along the gust front: the cold outflow lifts the warm air
					// it undercuts into a lowering wedge ahead of the line.
					anatomy.RainOffset = Vector2.zero;
					anatomy.RainRadius = cellRadius;
					anatomy.ShelfDrop = Mathf.Min(Mathf.Clamp(0.4f * anatomy.BaseMetres, 150f, 1000f), 0.6f * anatomy.BaseMetres);
					anatomy.ShelfWidth = Mathf.Clamp(1.5f * anatomy.BaseMetres, 1000f, 4000f);
					break;
				}
				case StormKind.TropicalCyclone:
				{
					// The central dense overcast: the eyewall's outflow spread over the whole storm.
					anatomy.RainRadius = cellRadius;
					anatomy.AnvilRadius = Mathf.Max(anatomy.AnvilRadius, 1.5f * cellRadius);
					anatomy.AnvilOffset = Vector2.zero;
					break;
				}
				default:
				{
					// An ordinary thunderstorm rains from the downwind side of its tower.
					anatomy.RainRadius = Mathf.Max(cellRadius, 0.6f * core);
					anatomy.RainOffset = (wind.sqrMagnitude > 1e-4f ? wind.normalized : forward) * (0.25f * core);
					break;
				}
			}
			return anatomy;
		}
	}
}
