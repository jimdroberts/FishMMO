#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// FishMMO Dashboard → World → Solar System: the body list, a live orrery, the sky seen from
	/// any body at any place and time, the bodies in that sky, the cost against the sky limits,
	/// and problems.
	/// </summary>
	public class SolarSystemPage : VisualElement
	{
		private static readonly (string label, double hoursPerSecond)[] Speeds =
		{
			("Paused", 0.0),
			("1 real minute / s", 1.0 / 60.0),
			("10 real minutes / s", 10.0 / 60.0),
			("1 real hour / s", 1.0),
			("1 real day / s", 24.0),
			("1 home year / s", double.NaN),
		};

		private SolarSystemProfile profile;
		private UnityEngine.Object selected;
		private double hours;
		private double speed;
		private bool yearSpeed;
		private double lastTick;
		private int dirtyStamp = -1;

		private readonly ObjectField profileField;
		private readonly VisualElement bodyList;
		private readonly OrreryView orrery;
		private readonly SkyPreviewView sky;
		private readonly VisualElement inspectorHost;
		private readonly VisualElement facts;
		private readonly VisualElement skyTable;
		private readonly VisualElement costPanel;
		private readonly VisualElement problemsPanel;
		private readonly Slider daySlider;
		private readonly Slider timeSlider;
		private readonly Label dateLabel;
		private readonly PopupField<WorldBody> observerField;
		private readonly Slider latitudeSlider;
		private readonly Slider longitudeSlider;
		private readonly DropdownField speedField;
		private InspectorElement inspector;
		private IVisualElementScheduledItem ticker;

		/// <summary>Sets the preview time and place, for render probes and deep links.</summary>
		public void ShowSky(WorldBody observer, double worldHours, float latitude, float longitude)
		{
			hours = worldHours;
			if (observer != null)
			{
				RefreshObservers();
				if (observerField.choices.Contains(observer))
				{
					observerField.SetValueWithoutNotify(observer);
				}
			}
			latitudeSlider.SetValueWithoutNotify(latitude);
			longitudeSlider.SetValueWithoutNotify(longitude);
			SyncSliders();
			RefreshViews();
		}

		public SolarSystemPage()
		{
			style.flexGrow = 1f;
			style.flexDirection = FlexDirection.Column;

			// ── Toolbar ──
			var toolbar = new Toolbar();
			profileField = new ObjectField { objectType = typeof(SolarSystemProfile), allowSceneObjects = false };
			profileField.style.minWidth = 220f;
			profileField.RegisterValueChangedCallback(evt => SetProfile(evt.newValue as SolarSystemProfile));
			toolbar.Add(profileField);
			toolbar.Add(new ToolbarButton(CreateExample) { text = "Create example system", tooltip = "Creates Sun, Home, Moon 1, the calendar and the world atlas under " + WorldEditorAssets.Root + ". Reuses a system that already exists." });
			var addMenu = new ToolbarMenu { text = "Add body" };
			addMenu.menu.AppendAction("Star", _ => AddBody<StarBody>("Star"));
			addMenu.menu.AppendAction("Planet", _ => AddBody<WorldBody>("Planet"));
			addMenu.menu.AppendAction("Moon of the selected planet", _ => AddBody<WorldBody>("Moon"), _ => SelectedPlanet() != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
			addMenu.menu.AppendAction("Comet", _ => AddBody<CometBody>("Comet"));
			addMenu.menu.AppendSeparator();
			addMenu.menu.AppendAction("Meteor shower", _ => AddShower());
			addMenu.menu.AppendAction("Asteroid belt", _ => AddBelt());
			toolbar.Add(addMenu);
			toolbar.Add(new ToolbarButton(RemoveSelected) { text = "Remove", tooltip = "Removes the selected body from the system; you are then asked whether to delete its asset." });
			toolbar.Add(new ToolbarButton(() => { if (selected != null) EditorGUIUtility.PingObject(selected); }) { text = "Ping" });
			toolbar.Add(new ToolbarSpacer { flex = true });
			toolbar.Add(new ToolbarButton(() => { Selection.activeObject = WorldEditorAssets.FindFirst<Atlas.WorldAtlas>(); }) { text = "Select atlas" });
			Add(toolbar);

			var columns = new VisualElement();
			columns.style.flexDirection = FlexDirection.Row;
			columns.style.flexGrow = 1f;
			Add(columns);

			// ── Left: bodies ──
			var left = new ScrollView();
			left.style.width = 210f;
			left.style.minWidth = 160f;
			left.style.borderRightWidth = 1f;
			left.style.borderRightColor = new Color(0f, 0f, 0f, 0.3f);
			bodyList = new VisualElement();
			left.Add(bodyList);
			columns.Add(left);

			// ── Centre: time, orrery, sky ──
			var centre = new VisualElement();
			centre.style.flexGrow = 1f;
			centre.style.flexBasis = 0f;
			centre.style.paddingLeft = 6f;
			centre.style.paddingRight = 6f;
			columns.Add(centre);

			var timeRow = Row();
			daySlider = new Slider("Day", 0f, 364f) { showInputField = true };
			daySlider.style.flexGrow = 1f;
			daySlider.RegisterValueChangedCallback(evt => SetHoursFromSliders());
			timeSlider = new Slider("Time of day", 0f, 1f) { showInputField = true };
			timeSlider.style.flexGrow = 1f;
			timeSlider.RegisterValueChangedCallback(evt => SetHoursFromSliders());
			timeRow.Add(daySlider);
			timeRow.Add(timeSlider);
			centre.Add(timeRow);

			var playRow = Row();
			var names = new List<string>();
			foreach (var s in Speeds)
			{
				names.Add(s.label);
			}
			speedField = new DropdownField("Playback", names, 0);
			speedField.RegisterValueChangedCallback(evt => SetSpeed(names.IndexOf(evt.newValue)));
			playRow.Add(speedField);
			playRow.Add(new Button(() => { hours = WorldClock.WorldSecondsFromUtc(DateTime.UtcNow, profile != null ? profile.EpochUnixSeconds : CalendarProfile.DefaultEpochUnixSeconds) / 3600.0; SyncSliders(); RefreshViews(); }) { text = "Now", tooltip = "Jump to the world time this instant, from this machine's clock." });
			dateLabel = new Label();
			dateLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
			dateLabel.style.marginLeft = 8f;
			dateLabel.style.flexGrow = 1f;
			playRow.Add(dateLabel);
			centre.Add(playRow);

			var views = new VisualElement();
			views.style.flexDirection = FlexDirection.Row;
			views.style.flexGrow = 1f;
			views.style.minHeight = 300f;
			centre.Add(views);

			var orreryColumn = new VisualElement();
			orreryColumn.style.flexGrow = 1f;
			orreryColumn.style.flexBasis = 0f;
			orreryColumn.style.marginRight = 4f;
			orreryColumn.Add(Caption("Orrery — top-down, √AU scale, moon orbits enlarged. Scroll to zoom, click a body to select it."));
			orrery = new OrreryView();
			orrery.BodyClicked += body => Select(body);
			orreryColumn.Add(orrery);
			views.Add(orreryColumn);

			var skyColumn = new VisualElement();
			skyColumn.style.flexGrow = 1f;
			skyColumn.style.flexBasis = 0f;
			skyColumn.Add(Caption("Sky — looking up: zenith centre, north up, east left."));
			var skyRow = Row();
			observerField = new PopupField<WorldBody>("Sky from", new List<WorldBody> { null }, 0, b => b != null ? b.ResolvedName : "(none)", b => b != null ? b.ResolvedName : "(none)");
			observerField.RegisterValueChangedCallback(_ => RefreshViews());
			skyRow.Add(observerField);
			skyColumn.Add(skyRow);
			var placeRow = Row();
			latitudeSlider = new Slider("Latitude", -90f, 90f) { showInputField = true, value = 30f };
			latitudeSlider.style.flexGrow = 1f;
			latitudeSlider.RegisterValueChangedCallback(_ => RefreshViews());
			longitudeSlider = new Slider("Longitude", -180f, 180f) { showInputField = true };
			longitudeSlider.style.flexGrow = 1f;
			longitudeSlider.RegisterValueChangedCallback(_ => RefreshViews());
			placeRow.Add(latitudeSlider);
			placeRow.Add(longitudeSlider);
			skyColumn.Add(placeRow);
			sky = new SkyPreviewView();
			skyColumn.Add(sky);
			views.Add(skyColumn);

			var lower = new ScrollView();
			lower.style.maxHeight = 260f;
			lower.style.minHeight = 120f;
			skyTable = Section(lower, "Bodies in this sky");
			costPanel = Section(lower, "Cost");
			centre.Add(lower);

			// ── Right: inspector, facts, problems ──
			var right = new ScrollView();
			right.style.width = 360f;
			right.style.minWidth = 260f;
			right.style.borderLeftWidth = 1f;
			right.style.borderLeftColor = new Color(0f, 0f, 0f, 0.3f);
			right.style.paddingLeft = 6f;
			problemsPanel = Section(right, "Problems");
			facts = Section(right, "Facts");
			inspectorHost = Section(right, "Settings");
			columns.Add(right);

			RegisterCallback<AttachToPanelEvent>(_ => Attach());
			RegisterCallback<DetachFromPanelEvent>(_ => Detach());

			SetProfile(WorldEditorAssets.FindFirst<SolarSystemProfile>());
		}

		// ── Layout helpers ──

		private static VisualElement Row()
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;
			row.style.marginTop = 2f;
			row.style.marginBottom = 2f;
			return row;
		}

		private static Label Caption(string text)
		{
			var label = new Label(text);
			label.style.fontSize = 10;
			label.style.opacity = 0.7f;
			label.style.whiteSpace = WhiteSpace.Normal;
			label.style.marginBottom = 2f;
			return label;
		}

		private static VisualElement Section(VisualElement parent, string title)
		{
			var header = new Label(title);
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.marginTop = 8f;
			header.style.marginBottom = 2f;
			parent.Add(header);
			var body = new VisualElement();
			parent.Add(body);
			return body;
		}

		private static void AddLine(VisualElement parent, string text, Color? color = null)
		{
			var label = new Label(text);
			label.style.whiteSpace = WhiteSpace.Normal;
			label.style.marginLeft = 0f;
			label.style.paddingLeft = 0f;
			if (color.HasValue)
			{
				label.style.color = color.Value;
			}
			parent.Add(label);
		}

		private static void AddPair(VisualElement parent, string key, string value)
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			var k = new Label(key);
			k.style.width = 150f;
			k.style.opacity = 0.75f;
			var v = new Label(value);
			v.style.flexGrow = 1f;
			v.style.whiteSpace = WhiteSpace.Normal;
			row.Add(k);
			row.Add(v);
			parent.Add(row);
		}

		// ── Lifecycle ──

		private void Attach()
		{
			lastTick = EditorApplication.timeSinceStartup;
			ticker = schedule.Execute(Tick).Every(50);
		}

		private void Detach()
		{
			ticker?.Pause();
			ticker = null;
			DisposeInspector();
		}

		private void Tick()
		{
			double now = EditorApplication.timeSinceStartup;
			double dt = Math.Min(0.25, now - lastTick);
			lastTick = now;
			bool changed = false;
			if (profile != null && (speed > 0.0 || yearSpeed))
			{
				hours += dt * (yearSpeed ? CelestialMath.YearHours(profile) : speed);
				SyncSliders();
				changed = true;
			}
			int stamp = DirtyStamp();
			if (stamp != dirtyStamp)
			{
				dirtyStamp = stamp;
				RebuildBodyList();
				RefreshObservers();
				changed = true;
			}
			if (changed)
			{
				RefreshViews();
			}
		}

		/// <summary>A number that changes whenever the profile or any of its bodies is edited.</summary>
		private int DirtyStamp()
		{
			if (profile == null)
			{
				return 0;
			}
			unchecked
			{
				int stamp = EditorUtility.GetDirtyCount(profile) * 31 + profile.Bodies.Count;
				foreach (CelestialBody body in profile.Bodies)
				{
					stamp = stamp * 31 + (body != null ? EditorUtility.GetDirtyCount(body) + body.GetHashCode() : 7);
				}
				if (profile.Calendar != null)
				{
					stamp = stamp * 31 + EditorUtility.GetDirtyCount(profile.Calendar);
				}
				return stamp;
			}
		}

		private void SetProfile(SolarSystemProfile value)
		{
			profile = value;
			profileField.SetValueWithoutNotify(value);
			selected = value;
			dirtyStamp = -1;
			RebuildBodyList();
			RefreshObservers();
			SyncSliders();
			ShowInspector();
			RefreshViews();
		}

		private void SetSpeed(int index)
		{
			if (index < 0 || index >= Speeds.Length)
			{
				return;
			}
			yearSpeed = double.IsNaN(Speeds[index].hoursPerSecond);
			speed = yearSpeed ? 0.0 : Speeds[index].hoursPerSecond;
		}

		private double DayHours => CelestialMath.HomeSolarDayHours(profile);

		private void SetHoursFromSliders()
		{
			hours = (Math.Floor(daySlider.value) + timeSlider.value) * DayHours;
			RefreshViews();
		}

		private void SyncSliders()
		{
			int perYear = profile != null ? profile.DaysPerYear : 365;
			daySlider.highValue = perYear - 1;
			double days = hours / DayHours;
			double dayOfYear = days - Math.Floor(days / perYear) * perYear;
			daySlider.SetValueWithoutNotify((float)Math.Floor(dayOfYear));
			timeSlider.SetValueWithoutNotify((float)(dayOfYear - Math.Floor(dayOfYear)));
		}

		private void RefreshObservers()
		{
			var bodies = new List<WorldBody>();
			if (profile != null)
			{
				foreach (CelestialBody body in profile.Bodies)
				{
					if (body is WorldBody world && !bodies.Contains(world))
					{
						bodies.Add(world);
					}
				}
			}
			if (bodies.Count == 0)
			{
				bodies.Add(null);
			}
			WorldBody current = observerField.value;
			observerField.choices = bodies;
			if (current == null || !bodies.Contains(current))
			{
				current = profile != null && profile.HomeWorld != null && bodies.Contains(profile.HomeWorld) ? profile.HomeWorld : bodies[0];
			}
			observerField.SetValueWithoutNotify(current);
		}

		// ── Body list ──

		private void RebuildBodyList()
		{
			bodyList.Clear();
			if (profile == null)
			{
				AddLine(bodyList, "No solar system yet. Pick one above or press Create example system.");
				return;
			}
			bodyList.Add(ListItem(profile, "Solar System", 0, profile == selected));
			if (profile.Calendar != null)
			{
				bodyList.Add(ListItem(profile.Calendar, "Calendar", 0, profile.Calendar == selected));
			}
			var shown = new HashSet<CelestialBody>();
			foreach (CelestialBody body in profile.Bodies)
			{
				if (body != null && (body.Parent == null || !profile.Bodies.Contains(body.Parent)))
				{
					AddTree(body, 0, shown);
				}
			}
			foreach (CelestialBody body in profile.Bodies)
			{
				if (body != null && !shown.Contains(body))
				{
					AddTree(body, 0, shown);
				}
			}
		}

		private void AddTree(CelestialBody body, int depth, HashSet<CelestialBody> shown)
		{
			if (!shown.Add(body))
			{
				return;
			}
			string kind = body is StarBody ? "★" : body is CometBody ? "☄" : body is WorldBody w && w.Kind == WorldBodyKind.Moon ? "◐" : "●";
			string suffix = profile.HomeWorld == body ? "  (home)" : string.Empty;
			bodyList.Add(ListItem(body, $"{kind} {body.ResolvedName}{suffix}", depth, body == selected));
			foreach (CelestialBody child in profile.Bodies)
			{
				if (child != null && child.Parent == body)
				{
					AddTree(child, depth + 1, shown);
				}
			}
		}

		private VisualElement ListItem(UnityEngine.Object asset, string text, int depth, bool isSelected)
		{
			var label = new Label(text);
			label.style.paddingLeft = 6f + depth * 14f;
			label.style.paddingTop = 2f;
			label.style.paddingBottom = 2f;
			label.style.marginLeft = 0f;
			if (isSelected)
			{
				label.style.backgroundColor = new Color(0.24f, 0.37f, 0.59f, 0.6f);
			}
			label.RegisterCallback<ClickEvent>(_ => Select(asset));
			return label;
		}

		private void Select(UnityEngine.Object asset)
		{
			selected = asset;
			RebuildBodyList();
			ShowInspector();
			RefreshViews();
		}

		private WorldBody SelectedPlanet()
		{
			if (selected is WorldBody world)
			{
				return world.Kind == WorldBodyKind.Moon && world.Parent is WorldBody parent ? parent : world;
			}
			return profile != null ? profile.HomeWorld : null;
		}

		private void ShowInspector()
		{
			DisposeInspector();
			inspectorHost.Clear();
			if (selected == null)
			{
				return;
			}
			inspector = new InspectorElement(selected);
			inspectorHost.Add(inspector);
		}

		private void DisposeInspector()
		{
			if (inspector != null)
			{
				inspector.RemoveFromHierarchy();
				inspector = null;
			}
		}

		// ── Editing ──

		private void CreateExample()
		{
			SolarSystemProfile system = WorldEditorAssets.CreateExampleSystem();
			SetProfile(system);
		}

		private void AddBody<T>(string role) where T : CelestialBody
		{
			if (profile == null)
			{
				EditorUtility.DisplayDialog("No solar system", "Create or pick a solar system first.", "OK");
				return;
			}
			StarBody star = profile.PrimaryStar;
			WorldBody planet = SelectedPlanet();
			double farthest = 0.0;
			foreach (CelestialBody body in profile.Bodies)
			{
				if (body is WorldBody w && w.Kind != WorldBodyKind.Moon)
				{
					farthest = Math.Max(farthest, w.Orbit.Distance);
				}
			}
			int count = profile.Bodies.Count + 1;
			T created = WorldEditorAssets.Create<T>(WorldEditorAssets.BodiesFolder, $"{role} {count}", asset =>
			{
				asset.DisplayName = $"{role} {count}";
				switch (asset)
				{
					case StarBody s:
						s.SkyRadiusKm = 500000f;
						s.Orbit = new OrbitSettings { Distance = 1f, PeriodDays = 27.3f };
						s.Tint = new Color(1f, 0.8f, 0.6f, 1f);
						break;
					case CometBody c:
						c.Parent = star;
						c.SkyRadiusKm = 10f;
						c.Orbit = new OrbitSettings { Distance = 12f, Eccentricity = 0.95f, InclinationDegrees = 20f, OffsetDegrees = 180f, PeriodMode = OrbitPeriodMode.Kepler, PeriodDays = 27.3f };
						c.Tint = new Color(1f, 0.95f, 0.85f, 1f);
						break;
					case WorldBody w when role == "Moon":
						w.Kind = WorldBodyKind.Moon;
						w.Parent = planet;
						w.TidallyLocked = true;
						w.SkyRadiusKm = 1200f;
						w.Atmosphere = AtmosphereKind.None;
						w.Water = 0f;
						w.MinimumRadiusKm = 10f;
						w.CurrentRadiusKm = 10f;
						w.Orbit = new OrbitSettings { Distance = 250f + 150f * MoonCount(planet), PeriodMode = OrbitPeriodMode.Authored, PeriodDays = 5f + 4f * MoonCount(planet) };
						w.Tint = new Color(0.7f, 0.7f, 0.72f, 1f);
						break;
					case WorldBody w:
						w.Kind = WorldBodyKind.Planet;
						w.Parent = star;
						w.Orbit = new OrbitSettings { Distance = (float)(farthest > 0 ? farthest * 1.6 : 1.0), PeriodMode = OrbitPeriodMode.Kepler, PeriodDays = 365f, OffsetDegrees = (count * 67) % 360 };
						w.Tint = new Color(0.8f, 0.6f, 0.45f, 1f);
						break;
				}
			});
			Undo.RecordObject(profile, "Add body");
			profile.Bodies.Add(created);
			if (created is StarBody && profile.PrimaryStar == created)
			{
				foreach (CelestialBody body in profile.Bodies)
				{
					if (body is WorldBody w && w.Kind != WorldBodyKind.Moon && w.Parent == null)
					{
						Undo.RecordObject(w, "Assign star");
						w.Parent = created;
						EditorUtility.SetDirty(w);
					}
				}
			}
			if (profile.HomeWorld == null && created is WorldBody first && first.Kind == WorldBodyKind.Planet)
			{
				profile.HomeWorld = first;
			}
			EditorUtility.SetDirty(profile);
			AssetDatabase.SaveAssets();
			Select(created);
		}

		private int MoonCount(WorldBody planet)
		{
			int n = 0;
			foreach (CelestialBody body in profile.Bodies)
			{
				if (body != null && body.Parent == planet)
				{
					n++;
				}
			}
			return n;
		}

		private void AddShower()
		{
			if (profile == null)
			{
				return;
			}
			Undo.RecordObject(profile, "Add meteor shower");
			profile.MeteorShowers.Add(new MeteorShower { Name = "Shower " + (profile.MeteorShowers.Count + 1), PeakDayOfYear = Mathf.Clamp(Mathf.FloorToInt(daySlider.value) + 1, 1, profile.DaysPerYear) });
			EditorUtility.SetDirty(profile);
			Select(profile);
		}

		private void AddBelt()
		{
			if (profile == null)
			{
				return;
			}
			Undo.RecordObject(profile, "Add asteroid belt");
			profile.AsteroidBelts.Add(new AsteroidBelt { Name = "Belt " + (profile.AsteroidBelts.Count + 1), Seed = profile.AsteroidBelts.Count + 1 });
			EditorUtility.SetDirty(profile);
			Select(profile);
		}

		private void RemoveSelected()
		{
			if (profile == null || !(selected is CelestialBody body))
			{
				return;
			}
			Undo.RecordObject(profile, "Remove body");
			profile.Bodies.Remove(body);
			if (profile.HomeWorld == body)
			{
				profile.HomeWorld = null;
			}
			EditorUtility.SetDirty(profile);
			foreach (CelestialBody other in profile.Bodies)
			{
				if (other != null && other.Parent == body)
				{
					Debug.LogWarning($"[Solar System] {other.ResolvedName} orbited {body.ResolvedName}; give it a new parent.");
				}
			}
			AssetDatabase.SaveAssets();
			if (EditorUtility.DisplayDialog("Body removed", $"{body.ResolvedName} is no longer in the system. Delete its asset too?", "Delete asset", "Keep asset"))
			{
				WorldEditorAssets.DeleteWithConfirm(body);
			}
			Select(profile);
		}

		// ── Views and panels ──

		private void RefreshViews()
		{
			WorldBody observer = observerField.value;
			orrery.System = profile;
			orrery.Hours = hours;
			orrery.Selected = selected as CelestialBody;
			orrery.Observer = observer;
			orrery.ObserverLongitude = longitudeSlider != null ? longitudeSlider.value : 0f;
			orrery.Refresh();

			sky.System = profile;
			sky.Observer = observer;
			sky.Hours = hours;
			sky.Latitude = latitudeSlider.value;
			sky.Longitude = longitudeSlider.value;
			sky.Refresh();

			RefreshDate(observer);
			RefreshFacts(observer);
			RefreshSkyTable();
			RefreshCost();
			RefreshProblems();
		}

		private void RefreshDate(WorldBody observer)
		{
			if (profile == null)
			{
				dateLabel.text = string.Empty;
				return;
			}
			long day = CelestialMath.HomeDay(profile, hours);
			string date = $"Home day {day}";
			if (profile.Calendar != null)
			{
				profile.Calendar.ToDate(day, out long year, out int month, out int dayOfMonth);
				string monthName = month - 1 < profile.Calendar.Months.Count && profile.Calendar.Months[month - 1] != null ? profile.Calendar.Months[month - 1].Name : "Month " + month;
				date = $"{dayOfMonth} {monthName}, year {year} {profile.Calendar.EraName}".TrimEnd();
			}
			string local = string.Empty;
			if (observer != null)
			{
				double t = CelestialMath.LocalTime01(profile, observer, hours, longitudeSlider.value);
				bool day01 = CelestialMath.IsDaylight(profile, observer, hours, latitudeSlider.value, longitudeSlider.value);
				local = $"   ·   {SceneTime.Format(t)} {(day01 ? "☀" : "☾")} on {observer.ResolvedName}";
			}
			dateLabel.text = $"{date}{local}   ·   world hour {hours.ToString("0.00", CultureInfo.InvariantCulture)}";
		}

		private void RefreshFacts(WorldBody observer)
		{
			facts.Clear();
			if (profile == null)
			{
				return;
			}
			switch (selected)
			{
				case WorldBody world:
				{
					double rotation = CelestialMath.RotationHours(profile, world);
					double solarDay = CelestialMath.SolarDayHours(profile, world);
					AddPair(facts, "Turns in", Hours(rotation) + (world.TidallyLocked ? " (locked to its orbit)" : string.Empty));
					AddPair(facts, "Solar day", double.IsInfinity(solarDay) ? "never (the sun stands still)" : Hours(solarDay));
					AddPair(facts, "Orbit", Hours(CelestialMath.OrbitHours(profile, world)) + $" ({CelestialMath.OrbitHours(profile, world) / CelestialMath.HomeSolarDayHours(profile):0.#} home days)");
					AddPair(facts, "Daylight here today", Hours(CelestialMath.DaylightHours(profile, world, hours, latitudeSlider.value)) + $" at {latitudeSlider.value:0}°");
					CelestialMath.ClimateOffsets(profile, world, hours, out float t, out float h);
					AddPair(facts, "Climate offset", $"temperature {t:+0.00;-0.00}, humidity {h:+0.00;-0.00}{(world.HasWeather ? string.Empty : " — no air: no weather")}");
					AddPair(facts, "Starlight", $"{CelestialMath.Insolation(profile, world, hours):0.###} × home average {CelestialMath.MeanHomeInsolation(profile):0.###}");
					AddPair(facts, "Atlas radius", $"{world.AtlasRadiusKm:0.#} km ({world.RadiusMode})");
					break;
				}
				case CometBody comet:
				{
					double period = CelestialMath.OrbitHours(profile, comet);
					double perihelion = comet.Orbit.Distance * (1f - comet.Orbit.Eccentricity);
					AddPair(facts, "Orbit", $"{period / CelestialMath.YearHours(profile):0.##} home years");
					AddPair(facts, "Closest to the star", $"{perihelion:0.###} AU");
					AddPair(facts, "Brightness now", $"{CelestialSky.CometBrightness(profile, comet, hours):0.###} (1 = bright naked-eye)");
					break;
				}
				case StarBody star:
					AddPair(facts, "Luminosity", $"{star.Luminosity:0.##} × the default sun");
					AddPair(facts, "Colour temperature", $"{star.TemperatureK:0} K");
					break;
				case SolarSystemProfile _:
				case CalendarProfile _:
				{
					AddPair(facts, "Home solar day", Hours(CelestialMath.HomeSolarDayHours(profile)));
					AddPair(facts, "Year", $"{profile.DaysPerYear} days = {CelestialMath.YearHours(profile) / 24.0:0.#} real days");
					double day = hours / CelestialMath.HomeSolarDayHours(profile);
					double dayOfYear = day - Math.Floor(day / profile.DaysPerYear) * profile.DaysPerYear;
					AddPair(facts, "Meteors tonight", $"{CelestialSky.MeteorRate(profile, dayOfYear):0} per hour");
					break;
				}
			}
		}

		private static string Hours(double hours)
		{
			if (double.IsInfinity(hours) || double.IsNaN(hours))
			{
				return "∞";
			}
			int h = (int)Math.Floor(hours);
			int m = (int)Math.Round((hours - h) * 60.0);
			if (m == 60)
			{
				h++;
				m = 0;
			}
			return m == 0 ? $"{h} h" : $"{h} h {m:00} m";
		}

		private void RefreshSkyTable()
		{
			skyTable.Clear();
			if (sky.Observer == null)
			{
				AddLine(skyTable, "Pick a planet or moon under Sky from.");
				return;
			}
			var rows = new List<SkyBodyView>(sky.Views);
			rows.Sort((a, b) => b.Altitude.CompareTo(a.Altitude));
			var header = new Label("Body · altitude · azimuth · size · sky share · lit · distance");
			header.style.opacity = 0.7f;
			skyTable.Add(header);
			foreach (SkyBodyView row in rows)
			{
				string size = row.AngularDiameterDegrees >= 0.1 ? $"{row.AngularDiameterDegrees:0.##}°" : $"{row.AngularDiameterDegrees * 60.0:0.##}′";
				string distance = row.DistanceKm >= 1e7 ? $"{row.DistanceKm / CelestialMath.AuKm:0.###} AU" : $"{row.DistanceKm:N0} km";
				string text = $"{row.Body.ResolvedName} · {row.Altitude:0.0}° · {row.Azimuth:0}° · {size} · {row.SkyPercent:0.####}% · {row.Illumination * 100.0:0}% · {distance}{(row.Textured ? " · textured" : string.Empty)}";
				AddLine(skyTable, text, row.AboveHorizon ? (Color?)null : new Color(0.6f, 0.6f, 0.6f, 1f));
			}
		}

		private void RefreshCost()
		{
			costPanel.Clear();
			if (profile == null)
			{
				return;
			}
			int suns = 0, moons = 0, planets = 0, comets = 0;
			foreach (CelestialBody body in profile.Bodies)
			{
				switch (body)
				{
					case StarBody _: suns++; break;
					case CometBody _: comets++; break;
					case WorldBody w when w.Kind == WorldBodyKind.Moon: moons++; break;
					case WorldBody _: planets++; break;
				}
			}
			int asteroids = CelestialSky.VisibleAsteroidCount(profile);
			int totalAsteroids = 0;
			foreach (AsteroidBelt belt in profile.AsteroidBelts)
			{
				totalAsteroids += belt != null ? belt.Count : 0;
			}
			double day = hours / CelestialMath.HomeSolarDayHours(profile);
			double dayOfYear = day - Math.Floor(day / profile.DaysPerYear) * profile.DaysPerYear;
			float meteorRate = CelestialSky.MeteorRate(profile, dayOfYear);
			// A meteor lasts about a second; the sky holds a buffer of this many at the busiest.
			int meteorBuffer = Mathf.CeilToInt(meteorRate / 3600f * 60f) + 8;
			SkyLimits limits = profile.Limits ?? new SkyLimits();

			CostLine("Suns", suns, limits.Suns);
			CostLine("Moons", moons, limits.Moons);
			CostLine("Planets", planets, limits.Planets);
			CostLine("Comets", comets, limits.Comets);
			CostLine("Visible asteroids", totalAsteroids, limits.VisibleAsteroids);
			CostLine("Meteor buffer", meteorBuffer, limits.Meteors);

			double texturedShare = 0.0;
			int texturedCount = 0, discs = 0;
			foreach (SkyBodyView view in sky.Views)
			{
				if (!view.AboveHorizon)
				{
					continue;
				}
				if (view.Textured)
				{
					texturedCount++;
					texturedShare += view.SkyPercent;
				}
				else
				{
					discs++;
				}
			}

			// A rough model, calibrated to nothing yet: fixed sky cost, a small per-disc cost,
			// fill cost for textured bodies by the share of the sky they cover, and points.
			double ms = 0.25 + discs * 0.004 + comets * 0.03 + texturedCount * 0.02 + texturedShare * 0.035 + asteroids * 0.00008 + meteorBuffer * 0.0004;
			AddPair(costPanel, "In this sky", $"{discs} disc(s), {texturedCount} textured body(ies) covering {texturedShare:0.##}% of the sky");
			AddPair(costPanel, "Estimated sky pass", $"Performant {ms * 1.8:0.00} ms · Balanced {ms:0.00} ms · High {ms * 0.8:0.00} ms (budget 0.50 / 0.75 / 1.00)");
			AddLine(costPanel, "Estimate only. The measured cost per tier comes from the render probe once the sky shader exists (phase P2).", new Color(0.7f, 0.7f, 0.7f, 1f));
		}

		private void CostLine(string label, int count, int limit)
		{
			bool over = count > limit;
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			var k = new Label(label);
			k.style.width = 150f;
			k.style.opacity = 0.75f;
			var bar = new VisualElement();
			bar.style.width = 120f;
			bar.style.height = 8f;
			bar.style.marginTop = 5f;
			bar.style.backgroundColor = new Color(0f, 0f, 0f, 0.3f);
			var fill = new VisualElement();
			fill.style.height = 8f;
			fill.style.width = Length.Percent(Mathf.Clamp01(limit > 0 ? count / (float)limit : 1f) * 100f);
			fill.style.backgroundColor = over ? new Color(0.9f, 0.35f, 0.3f, 1f) : new Color(0.35f, 0.7f, 0.45f, 1f);
			bar.Add(fill);
			var v = new Label($"{count} / {limit}{(over ? " — over the limit; the sky draws only the first " + limit : string.Empty)}");
			v.style.marginLeft = 6f;
			row.Add(k);
			row.Add(bar);
			row.Add(v);
			costPanel.Add(row);
		}

		private void RefreshProblems()
		{
			problemsPanel.Clear();
			List<string> errors = SolarSystemChecks.Problems(profile);
			if (errors.Count == 0)
			{
				AddLine(problemsPanel, "None.", new Color(0.45f, 0.8f, 0.5f, 1f));
				return;
			}
			foreach (string error in errors)
			{
				AddLine(problemsPanel, "⚠ " + error, new Color(1f, 0.72f, 0.4f, 1f));
			}
		}
	}

	/// <summary>Checks a solar system for authoring mistakes. Used by the page and the Validate tool.</summary>
	public static class SolarSystemChecks
	{
		public static List<string> Problems(SolarSystemProfile profile)
		{
			var problems = new List<string>();
			if (profile == null)
			{
				problems.Add("There is no solar system. Scenes use the default 6-hour day until one exists.");
				return problems;
			}
			if (profile.PrimaryStar == null)
			{
				problems.Add("The system has no star: nothing lights the sky or sets the climate.");
			}
			if (profile.HomeWorld == null)
			{
				problems.Add("No home world is set. The calendar and every climate offset are measured from it.");
			}
			else if (!profile.Bodies.Contains(profile.HomeWorld))
			{
				problems.Add($"The home world {profile.HomeWorld.ResolvedName} is not in the body list.");
			}
			if (profile.Calendar == null)
			{
				problems.Add("No calendar is set: the default 365-day year is used.");
			}
			else if (!profile.Calendar.MonthsMatchYear())
			{
				problems.Add($"The calendar's months do not add up to {profile.Calendar.DaysPerYear} days.");
			}
			var seen = new HashSet<CelestialBody>();
			foreach (CelestialBody body in profile.Bodies)
			{
				if (body == null)
				{
					problems.Add("The body list has an empty entry.");
					continue;
				}
				if (!seen.Add(body))
				{
					problems.Add($"{body.ResolvedName} is listed twice.");
				}
				if (body.Parent != null && !profile.Bodies.Contains(body.Parent))
				{
					problems.Add($"{body.ResolvedName} orbits {body.Parent.ResolvedName}, which is not in the system.");
				}
				for (CelestialBody p = body.Parent; p != null; p = p.Parent)
				{
					if (p == body)
					{
						problems.Add($"{body.ResolvedName} orbits itself through its parents.");
						break;
					}
				}
				switch (body)
				{
					case WorldBody w when w.Kind == WorldBodyKind.Moon && !(w.Parent is WorldBody):
						problems.Add($"Moon {w.ResolvedName} needs a planet as its parent.");
						break;
					case WorldBody w when w.Kind != WorldBodyKind.Moon && !(w.Parent is StarBody):
						problems.Add($"Planet {w.ResolvedName} needs a star as its parent.");
						break;
					case CometBody c when !(c.Parent is StarBody):
						problems.Add($"Comet {c.ResolvedName} needs a star as its parent.");
						break;
				}
				if (body is WorldBody world && world.Kind != WorldBodyKind.Moon && world.Orbit.PeriodMode == OrbitPeriodMode.Kepler && profile.HomeWorld != null && world != profile.HomeWorld && Mathf.Abs(world.Orbit.Distance - profile.HomeWorld.Orbit.Distance) < 1e-4f && !world.Retrograde && Mathf.Abs(world.Orbit.OffsetDegrees - profile.HomeWorld.Orbit.OffsetDegrees) < 1f)
				{
					problems.Add($"{world.ResolvedName} shares the home world's orbit and position.");
				}
			}
			SkyLimits limits = profile.Limits ?? new SkyLimits();
			int stars = 0;
			foreach (CelestialBody body in profile.Bodies)
			{
				if (body is StarBody)
				{
					stars++;
				}
			}
			if (stars > limits.Suns)
			{
				problems.Add($"{stars} stars, but the sky draws at most {limits.Suns}.");
			}
			foreach (MeteorShower shower in profile.MeteorShowers)
			{
				if (shower != null && shower.PeakDayOfYear > profile.DaysPerYear)
				{
					problems.Add($"Meteor shower {shower.Name} peaks on day {shower.PeakDayOfYear}, after the year ends.");
				}
			}
			foreach (AsteroidBelt belt in profile.AsteroidBelts)
			{
				if (belt != null && belt.InnerAU >= belt.OuterAU)
				{
					problems.Add($"Asteroid belt {belt.Name}: the inner edge must be inside the outer edge.");
				}
			}
			return problems;
		}
	}
}
#endif
