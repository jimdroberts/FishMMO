using System;
using UnityEngine;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Titles and places shared by every race in one or more categories, drawn on top of each
	/// race's own <see cref="RaceNamingData.Titles"/>. A pool for "Undead" gives every undead race
	/// grave-wardens and barrow-lords without repeating them on each template; a pool with no
	/// categories serves every race. Registered in <see cref="TitlePoolRegistry"/> on load.
	/// </summary>
	[CreateAssetMenu(fileName = "New Title Pool", menuName = "FishMMO/Naming/Title Pool", order = 4)]
	public class TitlePoolTemplate : CachedScriptableObject<TitlePoolTemplate>, ICachedObject
	{
		[Tooltip("Race categories this pool serves (RaceTemplate.Category, case-insensitive). Empty means every race.")]
		public string[] Categories;

		[Tooltip("Titles every race in these categories can draw, merged after the race's own.")]
		public SerializableRaceTitles Titles = new SerializableRaceTitles();

		[Tooltip("Places shared by these categories, merged after the race's own places.")]
		public string[] Places;

		private RaceTitles runtimeTitles;

		/// <summary>True when the pool serves every race.</summary>
		public bool AppliesToAll => Categories == null || Categories.Length == 0;

		/// <summary>True when a race of <paramref name="category"/> may draw from this pool.</summary>
		public bool AppliesTo(string category)
		{
			if (AppliesToAll)
			{
				return true;
			}
			if (string.IsNullOrWhiteSpace(category))
			{
				return false;
			}
			for (int i = 0; i < Categories.Length; i++)
			{
				if (string.Equals(Categories[i]?.Trim(), category.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The pool's titles as the builder consumes them; rebuilt after <see cref="InvalidateRuntime"/>.</summary>
		public RaceTitles RuntimeTitles => runtimeTitles ??= Titles?.ToRuntime() ?? new RaceTitles();

		/// <summary>Drops the cached runtime titles so edits to <see cref="Titles"/> are seen.</summary>
		public void InvalidateRuntime()
		{
			runtimeTitles = null;
		}

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);
			TitlePoolRegistry.Register(this);
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			TitlePoolRegistry.Unregister(this);
			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}
