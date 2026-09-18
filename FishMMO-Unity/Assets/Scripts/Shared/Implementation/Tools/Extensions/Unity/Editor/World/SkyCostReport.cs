#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What the sky actually cost when it was last measured, per quality tier.
	/// </summary>
	/// <remarks>
	/// The Solar System page can only estimate the sky's cost from the counts a designer authors.
	/// The Sky Sim render probe measures the real thing and writes it here; the page then shows both,
	/// and says which machine and renderer the measurement came from, because a software renderer on
	/// a build machine says nothing about a player's GPU. Kept in <c>Library/</c>: it is a
	/// measurement of this machine, not project source.
	/// </remarks>
	[Serializable]
	public class SkyCostReport
	{
		public const string Path = "Library/FishMMO/SkyCost.json";

		[Serializable]
		public class Entry
		{
			public string Tier;
			/// <summary>Milliseconds per frame, averaged over the measured frames.</summary>
			public float Milliseconds;
			/// <summary>Sky body quads drawn (suns, moons, planets, comets, asteroids, meteors).</summary>
			public int Quads;
			/// <summary>Bodies drawn with their own surface texture, each its own draw.</summary>
			public int Textured;
			public int StarCubemapSize;
			public int Frames;
		}

		public string Machine;
		public string Renderer;
		public string MeasuredUtc;
		public List<Entry> Entries = new List<Entry>();

		public Entry For(string tier)
		{
			return Entries.Find(e => string.Equals(e.Tier, tier, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>The last measurement, or null when nothing has been measured on this machine.</summary>
		public static SkyCostReport Load()
		{
			try
			{
				if (!File.Exists(Path))
				{
					return null;
				}
				SkyCostReport report = JsonUtility.FromJson<SkyCostReport>(File.ReadAllText(Path));
				return report != null && report.Entries != null && report.Entries.Count > 0 ? report : null;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[Sky cost] {Path} could not be read: {ex.Message}");
				return null;
			}
		}

		public void Save()
		{
			Machine = SystemInfo.deviceName;
			Renderer = SystemInfo.graphicsDeviceName;
			MeasuredUtc = DateTime.UtcNow.ToString("u");
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
			File.WriteAllText(Path, JsonUtility.ToJson(this, true));
			Debug.Log($"[Sky cost] Wrote {Path} ({Entries.Count} tier(s), measured on {Renderer}).");
		}

		/// <summary>How old the measurement is, in words, for the designer's panel.</summary>
		public string Age()
		{
			if (!DateTime.TryParse(MeasuredUtc, out DateTime when))
			{
				return "at an unknown time";
			}
			TimeSpan since = DateTime.UtcNow - when.ToUniversalTime();
			if (since.TotalHours < 1.0)
			{
				return $"{Mathf.Max(1, Mathf.RoundToInt((float)since.TotalMinutes))} minute(s) ago";
			}
			return since.TotalDays < 1.0
				? $"{Mathf.RoundToInt((float)since.TotalHours)} hour(s) ago"
				: $"{Mathf.RoundToInt((float)since.TotalDays)} day(s) ago";
		}
	}
}
#endif
