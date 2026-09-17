using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>One named month.</summary>
	[Serializable]
	public class CalendarMonth
	{
		public string Name;
		[Min(1)] public int Days = 30;
	}

	/// <summary>
	/// The one world calendar. It is the HOME world's calendar: a day is one home solar day and a
	/// year is one home orbit. Scenes on every body show this date alongside their own local time.
	/// </summary>
	[CreateAssetMenu(fileName = "World Calendar", menuName = "FishMMO/World/Calendar", order = 10)]
	public class CalendarProfile : CachedScriptableObject<CalendarProfile>, ICachedObject
	{
		/// <summary>2026-01-01T00:00:00Z.</summary>
		public const long DefaultEpochUnixSeconds = 1767225600L;

		[Tooltip("The body whose days and years this calendar counts. Empty: the solar system's home world.")]
		public WorldBody CalendarBody;
		[Tooltip("Home solar days per home orbit. Sets the home world's orbital period.")]
		[Min(1)] public int DaysPerYear = 365;
		[Tooltip("Must add up to Days Per Year. Neutral defaults; rename freely.")]
		public List<CalendarMonth> Months = DefaultMonths();
		public List<string> Weekdays = new List<string> { "Day 1", "Day 2", "Day 3", "Day 4", "Day 5", "Day 6", "Day 7" };
		public int EpochYear = 1;
		public string EraName = string.Empty;
		[Tooltip("The real instant that is year 1, day 1, 00:00, as Unix seconds (UTC). Changing it needs a server restart.")]
		public long WorldEpochUnixSeconds = DefaultEpochUnixSeconds;

		public static List<CalendarMonth> DefaultMonths()
		{
			var months = new List<CalendarMonth>(12);
			int[] lengths = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
			for (int i = 0; i < lengths.Length; i++)
			{
				months.Add(new CalendarMonth { Name = "Month " + (i + 1), Days = lengths[i] });
			}
			return months;
		}

		/// <summary>True when the months add up to the year.</summary>
		public bool MonthsMatchYear()
		{
			int total = 0;
			foreach (CalendarMonth m in Months)
			{
				total += m != null ? m.Days : 0;
			}
			return total == DaysPerYear;
		}

		/// <summary>Year, month and day (all 1-based) for a whole number of home days since the epoch.</summary>
		public void ToDate(long daysSinceEpoch, out long year, out int month, out int day)
		{
			int perYear = Mathf.Max(1, DaysPerYear);
			long y = daysSinceEpoch >= 0 ? daysSinceEpoch / perYear : (daysSinceEpoch - perYear + 1) / perYear;
			int dayOfYear = (int)(daysSinceEpoch - y * perYear);
			year = EpochYear + y;
			month = 1;
			day = dayOfYear + 1;
			if (!MonthsMatchYear())
			{
				return;
			}
			for (int i = 0; i < Months.Count; i++)
			{
				if (day <= Months[i].Days)
				{
					month = i + 1;
					return;
				}
				day -= Months[i].Days;
			}
		}
	}
}
