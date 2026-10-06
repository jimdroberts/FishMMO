using System;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Additions to a place's air: a little more cold, a little more damp, a higher pressure. Never a
	/// replacement for it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The weather is worked out from the air — how warm it is, how damp, how the pressure lies, how
	/// ready it is to overturn and how hard it blows — on the world it is over. The only way to ask
	/// for different weather is to change that air, and the only way to change it is by adding to
	/// it. A scene authored colder, an admin raising the humidity, a quest that makes the air
	/// unstable, a valley a volume keeps cool: each is a number added to what physics already says,
	/// and physics then decides what that air does. Zero changes nothing.
	/// </para>
	/// <para>
	/// There were presets before this — named weathers that wrote the answer straight in, rain or
	/// fog or clear sky, whatever the air was doing. They are gone: a preset could ask for rain from
	/// dry air, clear skies in a hurricane, snow in the desert, and the sky drawn from the air and
	/// the rain written by the preset disagreed. Additions to the air cannot: the rain, the cloud and
	/// the fog all come out of the same air, and the air is the only thing anyone touches.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct AirOffsets
	{
		[Tooltip("Kelvin added to the air's temperature. Negative for extra cold, positive for extra heat.")]
		public float Temperature;

		[Tooltip("Added to the air's humidity, on its 0 (parched) … 1 (saturated) scale.")]
		[Range(-1f, 1f)] public float Humidity;

		[Tooltip("Added to the pressure pattern, on its −1 (the heart of a deep low) … +1 (a settled high) scale. Down brings rising air, cloud and rain; up brings sinking air and a lid on the sky.")]
		[Range(-2f, 2f)] public float Pressure;

		[Tooltip("Added to how ready the air is to overturn, on its 0 (settled) … 1 (explosive) scale. Up builds heaps and towers; down flattens the sky into sheets.")]
		[Range(-1f, 1f)] public float Instability;

		[Tooltip("Metres per second added to the wind, along the way it is already blowing. Negative calms it.")]
		public float Wind;

		[Tooltip("m/s² added to the pull the air feels. More pull makes the air thinner with height and its clouds lower and squatter; less makes them tall.")]
		public float Gravity;

		/// <summary>Nothing added.</summary>
		public static AirOffsets None => default;

		/// <summary>True when nothing is added at all.</summary>
		public bool IsZero =>
			Temperature == 0f && Humidity == 0f && Pressure == 0f && Instability == 0f && Wind == 0f && Gravity == 0f;

		/// <summary>The temperature addition on the game's climate scale.</summary>
		public float TemperatureScale => (float)(Temperature / FishMMO.Shared.Biomes.ClimateModel.KelvinPerUnit);

		public static AirOffsets operator +(AirOffsets a, AirOffsets b) => new AirOffsets
		{
			Temperature = a.Temperature + b.Temperature,
			Humidity = a.Humidity + b.Humidity,
			Pressure = a.Pressure + b.Pressure,
			Instability = a.Instability + b.Instability,
			Wind = a.Wind + b.Wind,
			Gravity = a.Gravity + b.Gravity,
		};

		public static AirOffsets operator *(AirOffsets a, float k) => new AirOffsets
		{
			Temperature = a.Temperature * k,
			Humidity = a.Humidity * k,
			Pressure = a.Pressure * k,
			Instability = a.Instability * k,
			Wind = a.Wind * k,
			Gravity = a.Gravity * k,
		};

		public static AirOffsets Lerp(in AirOffsets a, in AirOffsets b, float t)
		{
			return a * (1f - t) + b * t;
		}

		/// <summary>
		/// The same air with these added. The temperature is not touched here: it belongs to the
		/// climate, and reaches the air through that.
		/// </summary>
		public WeatherDriver.Synoptic Apply(in WeatherDriver.Synoptic air)
		{
			if (Humidity == 0f && Pressure == 0f && Instability == 0f && Wind == 0f)
			{
				return air;
			}
			WeatherDriver.Synoptic local = air;
			local.Humidity = Mathf.Clamp01(air.Humidity + Humidity);
			local.Pressure = Mathf.Clamp(air.Pressure + Pressure, -1f, 1f);
			local.Instability = Mathf.Clamp01(air.Instability + Instability);
			local.ColumnType = Mathf.Clamp01(WeatherDriver.BaseColumnType(local.Instability) + local.Tower * WeatherDriver.TowerGain(local.Instability));
			if (Wind != 0f)
			{
				float speed = air.Wind.magnitude;
				Vector2 direction = speed > 1e-4f ? air.Wind / speed : Vector2.up;
				local.Wind = direction * Mathf.Max(0f, speed + Wind);
			}
			return local;
		}

		/// <summary>A world's air with the pull changed.</summary>
		public PlanetAir Apply(in PlanetAir planet)
		{
			if (Gravity == 0f)
			{
				return planet;
			}
			PlanetAir local = planet;
			local.Gravity = Mathf.Max(0.05f, planet.Gravity + Gravity);
			return local;
		}

		public override string ToString()
		{
			return $"T {Temperature:+0.0;-0.0;0} K, H {Humidity:+0.00;-0.00;0}, P {Pressure:+0.00;-0.00;0}, instability {Instability:+0.00;-0.00;0}, wind {Wind:+0.0;-0.0;0} m/s, g {Gravity:+0.00;-0.00;0}";
		}
	}

	/// <summary>A scene-wide addition to the air, moving from one value to another between two ticks.</summary>
	[Serializable]
	public struct AirOffsetEntry
	{
		public AirOffsets From;
		public AirOffsets To;
		/// <summary>World seconds the move from <see cref="From"/> to <see cref="To"/> starts and ends: it holds with the world.</summary>
		public double StartSeconds, EndSeconds;

		/// <summary>What is added at a moment of world time, eased.</summary>
		public AirOffsets AtSeconds(double worldSeconds)
		{
			float t = EndSeconds <= StartSeconds
				? (worldSeconds >= EndSeconds ? 1f : 0f)
				: Mathf.Clamp01((float)((worldSeconds - StartSeconds) / (EndSeconds - StartSeconds)));
			if (worldSeconds < StartSeconds)
			{
				t = 0f;
			}
			t = t * t * (3f - 2f * t);
			return AirOffsets.Lerp(From, To, t);
		}
	}
}
