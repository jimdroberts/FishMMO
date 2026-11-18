using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>What the air is doing at a place and a moment: the synoptic state the weather driver computes.</summary>
	/// <remarks>
	/// Core vocabulary (Phase 4 of the core and modules plan, 2026-11-17): predicted exposure reads it, so it cannot belong
	/// to the weather module. It was the nested <c>WeatherAir</c>.
	/// </remarks>
	public struct WeatherAir
	{
		/// <summary>0..1: how strongly a tower stands here. The rain and the lightning come out of these.</summary>
		public float Tower;
		/// <summary>0 a flat deck, 0.5 a heap, 1 a tower through the whole depth: how tall the column here grows.</summary>
		public float ColumnType;
		/// <summary>−1..1: how much the local formation adds to or takes from the cover here.</summary>
		public float Mesoscale;
		/// <summary>−1 the middle of a deep low, +1 a settled high.</summary>
		public float Pressure;
		/// <summary>0 dry, 1 saturated.</summary>
		public float Humidity;
		/// <summary>The temperature anomaly this air carries, −1..1, before the climate.</summary>
		public float Temperature;
		/// <summary>0..1: how willing this air is to build a storm.</summary>
		public float Instability;
		/// <summary>The wind here, in metres per second.</summary>
		public Vector2 Wind;
		/// <summary>The hour this air was sampled at, 0.5 noon. Fog and the daily swing need it.</summary>
		public float LocalTime01;
		/// <summary>
		/// How far the sun has stopped setting here (0 it sets as usual, 1 it does not set at all):
		/// the midnight sun, inside a polar circle in its summer.
		/// </summary>
		public float PolarDay;
		/// <summary>How far the sun has stopped rising here: the polar night, in its winter.</summary>
		public float PolarNight;
		/// <summary>
		/// How much of a rotating storm's updraught is here, 0..1: a supercell's mesocyclone,
		/// whose spin lowers the pressure inside it and pulls the rising air up faster than its
		/// buoyancy alone would.
		/// </summary>
		public float Mesocyclone;
	}
}
