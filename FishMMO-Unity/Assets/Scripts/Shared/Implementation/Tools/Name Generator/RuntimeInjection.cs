using System;
using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Runtime injection API for the Name Generator. Game code (mods,
	/// live-ops, content DLC) can push pre-authored city / dungeon / POI
	/// names, titles, and lore entities into the generator's working set
	/// at any point; subsequent <c>Generate*</c> calls will return the
	/// injected content before falling back to procedural generation.
	///
	/// <para>This class is intentionally runtime-only (no Unity editor
	/// dependencies) so the same code path works in the editor, in
	/// play-mode, and in shipped builds.</para>
	///
	/// <para>Injected names are consumed FIFO — each
	/// <c>GenerateCityName</c> / <c>GenerateDungeonName</c> /
	/// <c>GeneratePOIName</c> call drains one entry from the matching
	/// pool. Titles (<see cref="TitleBuilder"/>) are sampled without
	/// being drained so a small set of curated titles can cover many
	/// characters.</para>
	/// </summary>
	public static class RuntimeInjection
	{
		// ── City pool ─────────────────────────────────────────────────
		// Keyed by (normalized race, cityType). Empty race matches any
		// race; CityType.Any matches any city type.

		private static readonly Dictionary<CityKey, Queue<CityNameEntry>>
			_cityPool = new Dictionary<CityKey, Queue<CityNameEntry>>();

		// ── Dungeon pool ──────────────────────────────────────────────
		// Keyed by normalized biome; empty biome matches any.

		private static readonly Dictionary<string, Queue<DungeonNameEntry>>
			_dungeonPool = new Dictionary<string, Queue<DungeonNameEntry>>(StringComparer.OrdinalIgnoreCase);

		// ── POI pool ──────────────────────────────────────────────────

		private static readonly Dictionary<POIKey, Queue<POINameEntry>>
			_poiPool = new Dictionary<POIKey, Queue<POINameEntry>>();

		// ── Title pool ────────────────────────────────────────────────
		// Sampled, not drained. Categories: "honorific","epithet",
		// "rank","legend". Empty race matches any race.

		private static readonly Dictionary<TitleKey, List<string>>
			_titlePool = new Dictionary<TitleKey, List<string>>();

		// Fires whenever a pool changes — useful for editor UIs wanting
		// to refresh their display.
		public static event Action PoolChanged;

		// ── City ──────────────────────────────────────────────────────

		public static void InjectCityName(string race, CityType cityType, CityNameEntry entry)
		{
			if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) return;
			var key = new CityKey(NormalizeRace(race), cityType);
			if (!_cityPool.TryGetValue(key, out var q))
				_cityPool[key] = q = new Queue<CityNameEntry>();
			q.Enqueue(entry);
			PoolChanged?.Invoke();
		}

		public static void InjectCityName(string race, string name,
			CityType cityType = CityType.Any, string meaning = null)
		{
			InjectCityName(race, cityType, new CityNameEntry
			{
				Name = name,
				Meaning = meaning ?? "",
				Race = race ?? "",
				CityType = cityType == CityType.Any ? "mixed" : cityType.ToString().ToLower(),
				NameFragments = new List<string> { name },
			});
		}

		internal static CityNameEntry TryPopCity(string race, CityType cityType)
		{
			// Try exact match (race, type), then (race, Any), (any, type),
			// then (any, Any). This lets designers inject either broad or
			// narrow pools and still have them consumed.
			string r = NormalizeRace(race);
			if (TryDrain(_cityPool, new CityKey(r, cityType), out var hit)) return hit;
			if (cityType != CityType.Any && TryDrain(_cityPool, new CityKey(r, CityType.Any), out hit)) return hit;
			if (!string.IsNullOrEmpty(r) && TryDrain(_cityPool, new CityKey("", cityType), out hit)) return hit;
			if (!string.IsNullOrEmpty(r) && cityType != CityType.Any && TryDrain(_cityPool, new CityKey("", CityType.Any), out hit)) return hit;
			return null;
		}

		// ── Dungeon ───────────────────────────────────────────────────

		public static void InjectDungeonName(string biome, DungeonNameEntry entry)
		{
			if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) return;
			string key = (biome ?? "").Trim().ToLowerInvariant();
			if (!_dungeonPool.TryGetValue(key, out var q))
				_dungeonPool[key] = q = new Queue<DungeonNameEntry>();
			q.Enqueue(entry);
			PoolChanged?.Invoke();
		}

		public static void InjectDungeonName(string biome, string name, string meaning = null)
		{
			InjectDungeonName(biome, new DungeonNameEntry
			{
				Name = name,
				Meaning = meaning ?? "",
				Biome = biome ?? "",
				NameFragments = new List<string> { name },
			});
		}

		internal static DungeonNameEntry TryPopDungeon(string biome)
		{
			string key = (biome ?? "").Trim().ToLowerInvariant();
			if (_dungeonPool.TryGetValue(key, out var q) && q.Count > 0) { var e = q.Dequeue(); PoolChanged?.Invoke(); return e; }
			if (!string.IsNullOrEmpty(key) && _dungeonPool.TryGetValue("", out q) && q.Count > 0) { var e = q.Dequeue(); PoolChanged?.Invoke(); return e; }
			return null;
		}

		// ── POI ───────────────────────────────────────────────────────

		public static void InjectPOIName(string biome, POIType type, POINameEntry entry)
		{
			if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) return;
			var key = new POIKey((biome ?? "").Trim().ToLowerInvariant(), type);
			if (!_poiPool.TryGetValue(key, out var q))
				_poiPool[key] = q = new Queue<POINameEntry>();
			q.Enqueue(entry);
			PoolChanged?.Invoke();
		}

		public static void InjectPOIName(string biome, string name,
			POIType type = POIType.Any, string meaning = null)
		{
			InjectPOIName(biome, type, new POINameEntry
			{
				Name = name,
				Meaning = meaning ?? "",
				Biome = biome ?? "",
				POIType = type == POIType.Any ? "mixed" : type.ToString().ToLower(),
				NameFragments = new List<string> { name },
			});
		}

		internal static POINameEntry TryPopPOI(string biome, POIType type)
		{
			string b = (biome ?? "").Trim().ToLowerInvariant();
			if (TryDrain(_poiPool, new POIKey(b, type), out var hit)) return hit;
			if (type != POIType.Any && TryDrain(_poiPool, new POIKey(b, POIType.Any), out hit)) return hit;
			if (!string.IsNullOrEmpty(b) && TryDrain(_poiPool, new POIKey("", type), out hit)) return hit;
			if (!string.IsNullOrEmpty(b) && type != POIType.Any && TryDrain(_poiPool, new POIKey("", POIType.Any), out hit)) return hit;
			return null;
		}

		// ── Titles ────────────────────────────────────────────────────

		public static void InjectTitle(string race, string category, string title)
		{
			if (string.IsNullOrWhiteSpace(title)) return;
			var key = new TitleKey(NormalizeRace(race), (category ?? "").Trim().ToLowerInvariant());
			if (!_titlePool.TryGetValue(key, out var list))
				_titlePool[key] = list = new List<string>();
			if (!list.Contains(title)) list.Add(title);
			PoolChanged?.Invoke();
		}

		public static void InjectTitles(string race, string category, IEnumerable<string> titles)
		{
			if (titles == null) return;
			foreach (var t in titles) InjectTitle(race, category, t);
		}

		/// <summary>Deterministically sample an injected title for (race,
		/// category) via the provided RNG, or return null if the pool
		/// for that key is empty. Falls back from (race,cat) →
		/// (any,cat) to let designers inject race-agnostic titles.
		/// </summary>
		public static string TryPickTitle(string race, string category, DeterministicRNG rng)
		{
			string r = NormalizeRace(race);
			string c = (category ?? "").Trim().ToLowerInvariant();
			if (_titlePool.TryGetValue(new TitleKey(r, c), out var list) && list.Count > 0)
				return list[rng.Next(list.Count)];
			if (!string.IsNullOrEmpty(r) &&
				_titlePool.TryGetValue(new TitleKey("", c), out list) && list.Count > 0)
				return list[rng.Next(list.Count)];
			return null;
		}

		// ── Admin ─────────────────────────────────────────────────────

		public static void ClearCityPool()    { _cityPool.Clear();    PoolChanged?.Invoke(); }
		public static void ClearDungeonPool() { _dungeonPool.Clear(); PoolChanged?.Invoke(); }
		public static void ClearPOIPool()     { _poiPool.Clear();     PoolChanged?.Invoke(); }
		public static void ClearTitlePool()   { _titlePool.Clear();   PoolChanged?.Invoke(); }

		public static void ClearAll()
		{
			_cityPool.Clear(); _dungeonPool.Clear();
			_poiPool.Clear();  _titlePool.Clear();
			PoolChanged?.Invoke();
		}

		/// <summary>Counts for debugging / editor UIs.</summary>
		public static int CityPoolCount     { get { int n = 0; foreach (var q in _cityPool.Values)    n += q.Count; return n; } }
		public static int DungeonPoolCount  { get { int n = 0; foreach (var q in _dungeonPool.Values) n += q.Count; return n; } }
		public static int POIPoolCount      { get { int n = 0; foreach (var q in _poiPool.Values)    n += q.Count; return n; } }
		public static int TitlePoolCount    { get { int n = 0; foreach (var l in _titlePool.Values)   n += l.Count; return n; } }

		// ── Internals ─────────────────────────────────────────────────

		private static string NormalizeRace(string race)
		{
			if (string.IsNullOrWhiteSpace(race)) return "";
			return race.Trim().ToLowerInvariant();
		}

		private static bool TryDrain<TK, TV>(Dictionary<TK, Queue<TV>> map, TK key, out TV hit)
		{
			if (map.TryGetValue(key, out var q) && q.Count > 0)
			{
				hit = q.Dequeue();
				PoolChanged?.Invoke();
				return true;
			}
			hit = default;
			return false;
		}

		private readonly struct CityKey : IEquatable<CityKey>
		{
			public readonly string Race;
			public readonly CityType Type;
			public CityKey(string race, CityType type) { Race = race; Type = type; }
			public bool Equals(CityKey o) => Race == o.Race && Type == o.Type;
			public override bool Equals(object o) => o is CityKey k && Equals(k);
			public override int GetHashCode() => (Race?.GetHashCode() ?? 0) * 397 ^ (int)Type;
		}

		private readonly struct POIKey : IEquatable<POIKey>
		{
			public readonly string Biome;
			public readonly POIType Type;
			public POIKey(string biome, POIType type) { Biome = biome; Type = type; }
			public bool Equals(POIKey o) => Biome == o.Biome && Type == o.Type;
			public override bool Equals(object o) => o is POIKey k && Equals(k);
			public override int GetHashCode() => (Biome?.GetHashCode() ?? 0) * 397 ^ (int)Type;
		}

		private readonly struct TitleKey : IEquatable<TitleKey>
		{
			public readonly string Race;
			public readonly string Category;
			public TitleKey(string race, string category) { Race = race; Category = category; }
			public bool Equals(TitleKey o) => Race == o.Race && Category == o.Category;
			public override bool Equals(object o) => o is TitleKey k && Equals(k);
			public override int GetHashCode() => (Race?.GetHashCode() ?? 0) * 397 ^ (Category?.GetHashCode() ?? 0);
		}
	}
}
