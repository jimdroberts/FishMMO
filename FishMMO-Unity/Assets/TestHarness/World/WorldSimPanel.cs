using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// The world test bed's controls: where you stand, when, under what weather, with what clouds,
	/// on what ground — in four tabs under the live figures.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Tabs rather than one long column, because this panel is the two old ones put together and
	/// stacking them would have made something nobody could find anything in. The readout stays
	/// above the tabs: what the sky and the weather are actually doing is the one thing worth
	/// seeing whichever set of controls is in front of you.
	/// </para>
	/// <para>
	/// The two panels this replaces had their own private copies of the same four widgets, in
	/// slightly different colours and label widths, which is why the beds never quite looked alike.
	/// There is one copy here.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(UIDocument))]
	public sealed class WorldSimPanel : MonoBehaviour
	{
		public WorldSimController Controller;

		[Tooltip("FishMMO-Theme.uss: the game's own theme, so the bed looks like the game it is a bed for.")]
		public StyleSheet Theme;
		[Tooltip("WorldSimPanel.uss: this panel's layout, in the theme's tokens.")]
		public StyleSheet Layout;

		private const string ThemePath = "Assets/Scripts/Client/GUI/FishMMO-Theme.uss";
		private const string LayoutPath = "Assets/TestHarness/World/WorldSimPanel.uss";
		private const string ActiveClass = "ws-button--active";

		private Label bodyFacts;
		private Label skyList;
		private Label coverLabel;
		private Slider timeSlider;
		private Slider daySlider;
		private Slider yearSlider;
		private Slider transition;
		private Button playButton;
		private Toggle directToggle;
		private VisualElement cloudSection;
		private VisualElement stats;

		private readonly List<Button> bodyButtons = new List<Button>();
		private readonly List<Button> presetButtons = new List<Button>();
		private readonly List<Button> tierButtons = new List<Button>();
		private readonly List<Button> tabButtons = new List<Button>();
		private readonly List<VisualElement> tabPages = new List<VisualElement>();
		private readonly Dictionary<WeatherChannel, Slider> channelSliders = new Dictionary<WeatherChannel, Slider>();
		private readonly Dictionary<WeatherLayerKind, Slider> layerSliders = new Dictionary<WeatherLayerKind, Slider>();
		/// <summary>The figures at the top, by name, so the refresh sets a value and never rebuilds a row.</summary>
		private readonly Dictionary<string, Label> statValues = new Dictionary<string, Label>();
		private float refresh;
		private int activeTab;

		/// <summary>The pointer is over the panel: a scroll is for the panel's list, not the camera's zoom.</summary>
		public static bool PointerOverPanel { get; private set; }

		/// <summary>A value is being typed into one of the panel's boxes: the keys are text, not flight.</summary>
		public static bool TypingInPanel { get; private set; }

		private void OnDisable()
		{
			PointerOverPanel = false;
			TypingInPanel = false;
		}

		private void Start()
		{
			UIDocument document = GetComponent<UIDocument>();
			if (document == null || document.rootVisualElement == null || Controller == null)
			{
				return;
			}
			Build(document.rootVisualElement);
		}

		/// <summary>
		/// The theme and then the layout, in that order: the layout is written in the theme's tokens.
		/// </summary>
		/// <remarks>
		/// The generator assigns both. A scene generated before they existed has neither, and in the
		/// editor — the only place this bed runs — they are found by path, so an old scene is themed
		/// without being regenerated.
		/// </remarks>
		private void ApplyStyleSheets(VisualElement root)
		{
#if UNITY_EDITOR
			if (Theme == null)
			{
				Theme = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemePath);
			}
			if (Layout == null)
			{
				Layout = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(LayoutPath);
			}
#endif
			if (Theme != null && !root.styleSheets.Contains(Theme))
			{
				root.styleSheets.Add(Theme);
			}
			if (Layout != null && !root.styleSheets.Contains(Layout))
			{
				root.styleSheets.Add(Layout);
			}
		}

		// ── Widgets ───────────────────────────────────────────────────
		// Classes and no colours: what anything looks like is the theme's business.

		private static Label Heading(string text, string tooltip = null)
		{
			var label = new Label(text.ToUpperInvariant()) { tooltip = tooltip };
			label.AddToClassList("fish-section");
			label.AddToClassList("ws-section");
			return label;
		}

		private static Label Small(string text)
		{
			var label = new Label(text);
			label.AddToClassList("ws-note");
			return label;
		}

		private static Button SmallButton(string text, Action clicked)
		{
			var button = new Button(clicked) { text = text };
			button.AddToClassList("fish-button");
			button.AddToClassList("fish-button--ghost");
			button.AddToClassList("ws-button");
			return button;
		}

		/// <summary>A row of buttons of one width each, so rows of them line up into a grid.</summary>
		private static VisualElement Row(bool wide = false)
		{
			var row = new VisualElement();
			row.AddToClassList("ws-button-row");
			if (wide)
			{
				row.AddToClassList("ws-button-row--wide");
			}
			return row;
		}

		private static Slider LabeledSlider(string text, float low, float high, float value, Action<float> changed)
		{
			var slider = new Slider(text, low, high) { value = Mathf.Clamp(value, low, high), showInputField = true };
			slider.AddToClassList("fish-slider");
			slider.AddToClassList("ws-slider");
			slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return slider;
		}

		private static Toggle LabeledToggle(string text, bool value, Action<bool> changed, string tooltip)
		{
			var toggle = new Toggle(text) { value = value, tooltip = tooltip };
			toggle.AddToClassList("fish-toggle");
			toggle.AddToClassList("ws-toggle");
			toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return toggle;
		}

		/// <summary>A framed block for text that is read rather than clicked.</summary>
		private static Label Well(string text)
		{
			var label = new Label(text);
			label.AddToClassList("fish-well");
			label.AddToClassList("ws-readout");
			return label;
		}

		/// <summary>Whether an element is the editable part of a value box — a slider's, or a text field's.</summary>
		private static bool IsTextInput(VisualElement element)
		{
			for (VisualElement at = element; at != null; at = at.parent)
			{
				if (at is TextField || at is FloatField || at is IntegerField)
				{
					return true;
				}
			}
			return false;
		}

		// ── Statistics ────────────────────────────────────────────────

		/// <summary>One titled card of figures. Wide cards run their rows two abreast.</summary>
		private VisualElement Card(string title, bool wide, params (string key, string label)[] rows)
		{
			var card = new VisualElement();
			card.AddToClassList("ws-card");
			if (wide)
			{
				card.AddToClassList("ws-card--wide");
			}
			var body = new VisualElement();
			body.AddToClassList("fish-well");
			body.AddToClassList("ws-card__body");
			card.Add(body);

			var heading = new Label(title.ToUpperInvariant());
			heading.AddToClassList("fish-label--caption");
			heading.AddToClassList("ws-card__title");
			body.Add(heading);

			var grid = new VisualElement();
			grid.AddToClassList("ws-card__grid");
			body.Add(grid);
			foreach ((string key, string label) in rows)
			{
				var row = new VisualElement();
				row.AddToClassList("ws-stat");
				var name = new Label(label);
				name.AddToClassList("ws-stat__key");
				var value = new Label("—");
				value.AddToClassList("ws-stat__value");
				row.Add(name);
				row.Add(value);
				grid.Add(row);
				statValues[key] = value;
			}
			return card;
		}

		/// <summary>The season in words, for the hemisphere stood in. The figure itself is the northern one.</summary>
		private static string SeasonName(float season01, float latitude)
		{
			float local = Mathf.Repeat(season01 + (latitude < 0f ? 0.5f : 0f), 1f);
			string[] names = { "midwinter", "late winter", "spring", "late spring", "midsummer", "late summer", "autumn", "late autumn" };
			return names[Mathf.RoundToInt(local * 8f) % 8];
		}

		/// <summary>Sets one figure, and how it should read: plain, good, a warning, or not applicable.</summary>
		private void Stat(string key, string text, string tone = null)
		{
			if (!statValues.TryGetValue(key, out Label label))
			{
				return;
			}
			label.text = text;
			label.EnableInClassList("ws-stat__value--good", tone == "good");
			label.EnableInClassList("ws-stat__value--warn", tone == "warn");
			label.EnableInClassList("ws-stat__value--dim", tone == "dim");
		}

		// ── Build ─────────────────────────────────────────────────────

		private void Build(VisualElement root)
		{
			root.Clear();
			root.style.flexDirection = FlexDirection.Row;
			ApplyStyleSheets(root);

			var panel = new VisualElement { name = "world-sim-panel" };
			panel.AddToClassList("fish-panel");
			panel.AddToClassList("ws-panel");
			root.Add(panel);

			// The keys that fly the camera are the keys UI Toolkit navigates with: W and S move the
			// focus from control to control, A and D change the slider it lands on. So after one
			// click anywhere on the panel, flying the camera also walked the focus up out of the Run
			// button into the clock's own sliders and scrubbed them — the rate, the year, the day,
			// the hour — and on the weather tab dragged the channel sliders about, which turns the
			// time-driven weather off altogether. The panel is a mouse panel. Navigation is refused
			// on the way down, before any control sees it; typing in a value box is not navigation
			// and is untouched.
			panel.RegisterCallback<NavigationMoveEvent>(evt =>
			{
				evt.StopPropagation();
				panel.focusController?.IgnoreEvent(evt);
			}, TrickleDown.TrickleDown);
			panel.RegisterCallback<NavigationSubmitEvent>(evt =>
			{
				evt.StopPropagation();
				panel.focusController?.IgnoreEvent(evt);
			}, TrickleDown.TrickleDown);
			// What the camera needs to know to keep out of the panel's way.
			panel.RegisterCallback<PointerEnterEvent>(_ => PointerOverPanel = true);
			panel.RegisterCallback<PointerLeaveEvent>(_ => PointerOverPanel = false);
			panel.RegisterCallback<FocusInEvent>(evt => TypingInPanel = IsTextInput(evt.target as VisualElement));
			panel.RegisterCallback<FocusOutEvent>(_ => TypingInPanel = false);

			var header = new VisualElement();
			header.AddToClassList("ws-header");
			var title = new Label("WORLD SIM");
			title.AddToClassList("fish-label--title");
			header.Add(title);
			Button collapse = null;
			collapse = SmallButton("Hide stats", () =>
			{
				bool hidden = stats.style.display == DisplayStyle.None;
				stats.style.display = hidden ? DisplayStyle.Flex : DisplayStyle.None;
				collapse.text = hidden ? "Hide stats" : "Show stats";
			});
			collapse.style.flexGrow = 0;
			collapse.style.flexBasis = StyleKeyword.Auto;
			header.Add(collapse);
			panel.Add(header);

			var help = new Label("Right-drag to look · scroll to zoom · WASD/QE to fly · Shift to hurry. The real sky and the real weather model; nothing reaches a server.");
			help.AddToClassList("ws-help");
			panel.Add(help);

			// The figures sit above the tabs, because what the sky is doing is worth seeing whichever
			// set of controls happens to be open. One card to a subject, so a number is found by
			// where it is and not by reading a paragraph for it.
			stats = new VisualElement();
			stats.AddToClassList("ws-stats");
			stats.Add(Card("Clock", false,
				("clock.time", "Time"), ("clock.date", "Date"), ("clock.season", "Season"), ("clock.hours", "World hours"), ("clock.rate", "Rate")));
			stats.Add(Card("Sun & sky", false,
				("sky.sun", "Sun"), ("sky.stars", "Stars"), ("sky.meteors", "Meteors"), ("sky.eclipse", "Eclipse"), ("sky.aurora", "Aurora")));
			stats.Add(Card("Air — what drives the weather", true,
				("air.pressure", "Pressure"), ("air.humidity", "Humidity"), ("air.instability", "Instability"), ("air.driver", "Driver")));
			stats.Add(Card("Weather — what is shown", true,
				("wx.source", "Source"), ("wx.timeline", "Timeline"), ("wx.falling", "Falling"), ("wx.fog", "Fog"),
				("wx.wind", "Wind"), ("wx.temp", "Temperature"), ("wx.exposure", "Exposure")));
			stats.Add(Card("Clouds — what is drawn", true,
				("cl.bands", "Bands"), ("cl.shell", "Shell"), ("cl.resolution", "Resolution"), ("cl.steps", "Steps"),
				("cl.history", "History"), ("cl.shafts", "Shafts")));
			panel.Add(stats);

			var tabBar = new VisualElement();
			tabBar.AddToClassList("ws-tabs");
			panel.Add(tabBar);

			var scroll = new ScrollView();
			scroll.AddToClassList("fish-scroll");
			scroll.style.flexGrow = 1;
			panel.Add(scroll);

			AddTab(tabBar, scroll, "Sky", BuildSkyTab);
			AddTab(tabBar, scroll, "Weather", BuildWeatherTab);
			AddTab(tabBar, scroll, "Clouds", BuildCloudTab);
			AddTab(tabBar, scroll, "Ground", BuildGroundTab);
			ShowTab(0);

			RefreshBodyButtons();
			RefreshFacts();
		}

		private void AddTab(VisualElement bar, VisualElement host, string name, Action<VisualElement> build)
		{
			int index = tabPages.Count;
			var button = new Button(() => ShowTab(index)) { text = name.ToUpperInvariant() };
			button.AddToClassList("fish-tab");
			button.AddToClassList("ws-tab");
			bar.Add(button);
			tabButtons.Add(button);

			var page = new VisualElement();
			page.AddToClassList("ws-page");
			build(page);
			host.Add(page);
			tabPages.Add(page);
		}

		private void ShowTab(int index)
		{
			activeTab = index;
			for (int i = 0; i < tabPages.Count; i++)
			{
				tabPages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
				tabButtons[i].EnableInClassList("fish-tab--active", i == index);
			}
		}

		// ── Sky ───────────────────────────────────────────────────────

		private void BuildSkyTab(VisualElement page)
		{
			page.Add(Heading("Where", "The body you stand on and where on it. Latitude sets the sun's path and the wind belt; longitude sets the local hour."));
			var bodies = Row();
			foreach (WorldBody body in Controller.Bodies)
			{
				WorldBody captured = body;
				Button button = SmallButton(body.ResolvedName, () =>
				{
					Controller.Body = captured;
					RefreshBodyButtons();
					RefreshFacts();
				});
				bodyButtons.Add(button);
				bodies.Add(button);
			}
			if (Controller.Bodies.Count == 0)
			{
				bodies.Add(Small("No solar system is loaded. Create the example system on the Solar System page."));
			}
			page.Add(bodies);
			bodyFacts = Well(string.Empty);
			page.Add(bodyFacts);

			page.Add(LabeledSlider("Latitude", -90f, 90f, Controller.Latitude, v => { Controller.Latitude = v; RefreshFacts(); }));
			page.Add(LabeledSlider("Longitude", -180f, 180f, Controller.Longitude, v => Controller.Longitude = v));
			page.Add(LabeledSlider("Heading", 0f, 360f, Controller.Heading, v => Controller.Heading = v));
			var places = Row();
			places.Add(SmallButton("Equator", () => SetLatitude(0f)));
			places.Add(SmallButton("45°N", () => SetLatitude(45f)));
			places.Add(SmallButton("Arctic 70°N", () => SetLatitude(70f)));
			places.Add(SmallButton("Pole 89°N", () => SetLatitude(89f)));
			places.Add(SmallButton("45°S", () => SetLatitude(-45f)));
			page.Add(places);

			page.Add(Heading("When", "Year, day and time together are the whole clock. The weather and every orbit are worked out from it, so all three are needed to have a moment back."));
			timeSlider = LabeledSlider("Time of day", 0f, 1f, (float)Controller.TimeOfDay, v => Controller.ScrubTo(v));
			page.Add(timeSlider);
			int daysPerYear = SolarSystemProfile.Active != null ? SolarSystemProfile.Active.DaysPerYear : 365;
			daySlider = LabeledSlider("Day of year", 0f, daysPerYear - 1f, Controller.DayOfYear, v => Controller.DayOfYear = v);
			page.Add(daySlider);
			yearSlider = LabeledSlider("Year", 0f, 100f, Controller.Year, v => Controller.Year = Mathf.RoundToInt(v));
			yearSlider.tooltip = "Which year of the world clock. The weather and every orbit are worked out from the whole date, so the same day of another year is a different sky: this, the day and the time together are what reproduce a moment. The box takes years past the slider's end.";
			page.Add(yearSlider);
			page.Add(LabeledSlider("Hours / second", 0f, 3f, Controller.TimeScale, v => Controller.TimeScale = v));
			var times = Row();
			playButton = SmallButton(Controller.Paused ? "▶ Run" : "❚❚ Pause", () =>
			{
				Controller.Paused = !Controller.Paused;
				playButton.text = Controller.Paused ? "▶ Run" : "❚❚ Pause";
			});
			// The one button on the panel that starts and stops everything else.
			playButton.RemoveFromClassList("fish-button--ghost");
			playButton.AddToClassList("fish-button--primary");
			times.Add(playButton);
			times.Add(SmallButton("Dawn", () => Jump(0.25)));
			times.Add(SmallButton("Noon", () => Jump(0.5)));
			times.Add(SmallButton("Dusk", () => Jump(0.75)));
			times.Add(SmallButton("Midnight", () => Jump(0.0)));
			page.Add(times);
			var eclipses = Row();
			eclipses.Add(SmallButton("Next solar eclipse", () => JumpToEclipse(true)));
			eclipses.Add(SmallButton("Next lunar eclipse", () => JumpToEclipse(false)));
			page.Add(eclipses);
			page.Add(Small("Jumps to a minute before first contact and slows the clock to a hundredth of an hour a second, so the phases can be watched: the whole of a solar eclipse here is about twenty real minutes at the bed's usual rate, and its totality a few seconds."));
			var seasons = Row();
			// The season here, on this body: found from where its sun stands, not read off the calendar.
			seasons.Add(SmallButton("Spring", () => SetSeason(0.25f)));
			seasons.Add(SmallButton("Summer", () => SetSeason(0.5f)));
			seasons.Add(SmallButton("Autumn", () => SetSeason(0.75f)));
			seasons.Add(SmallButton("Winter", () => SetSeason(0f)));
			page.Add(seasons);

			// The sun, in the three parts it is actually made of. On the panel because judging a sky
			// by eye and changing a constant in a shader are not the same afternoon's work.
			SkyProfile sunProfile = Controller.Sky != null ? Controller.Sky.ActiveSky : null;
			if (sunProfile != null)
			{
				page.Add(Heading("Sun", "Halo is the broad one: the light the air scatters forward at you, tens of degrees wide. Disc is the third of a degree the sun itself covers. Glow only shows toward a low sun near the horizon."));
				page.Add(LabeledSlider("Halo", 0f, 2f, sunProfile.SunHalo, v => sunProfile.SunHalo = v));
				page.Add(LabeledSlider("Disc", 0f, 40f, sunProfile.SunDisc, v => sunProfile.SunDisc = v));
				page.Add(LabeledSlider("Horizon glow", 0f, 1f, sunProfile.SunGlow, v => sunProfile.SunGlow = v));
			}
			page.Add(LabeledToggle("Light shafts", SkySystem.DrawGodRays, v => SkySystem.DrawGodRays = v,
				"Off: the frame without the shaft pass at all, neither its light nor the shadowed lanes beside it. Anything still wrong with the sun's glow with this off is the sky's, not the shafts'."));

			page.Add(Heading("Sky profile", "A profile here is forced on whatever body you stand on, the way a region's Change Sky Profile action does."));
			var skies = Row(true);
			skies.Add(SmallButton("Body's own", () => Controller.SkyOverride = null));
			foreach (SkyProfile profile in Controller.SkyProfiles)
			{
				SkyProfile captured = profile;
				skies.Add(SmallButton(profile.name, () => Controller.SkyOverride = captured));
			}
			page.Add(skies);
			SkyProfile painted = Controller.Sky != null ? Controller.Sky.ActiveSky : null;
			if (painted != null)
			{
				page.Add(LabeledToggle("Painted colours", painted.UseAuthoredColours, v => painted.UseAuthoredColours = v,
					"Off (what ships): the sky's colours are worked out from the air of the body stood on — how much of it there is and how dusty. On: this profile's painted gradients, as they were. Flip it to compare the two on any body; on an airless one the sky is black either way."));
			}
			page.Add(LabeledToggle("Larger than life", Controller.LargerThanLife, v => Controller.LargerThanLife = v,
				"Off (what ships): every disc is life-size. On: suns, moons and planets are drawn at the profile's scales, the way most games flatter the sky. This only changes what you see here; the assets keep their own setting."));

			page.Add(Heading("Quality"));
			var tiers = Row(true);
			for (int i = 0; i < QualitySettings.names.Length; i++)
			{
				int level = i;
				Button tier = SmallButton(QualitySettings.names[i], () => QualitySettings.SetQualityLevel(level, true));
				tierButtons.Add(tier);
				tiers.Add(tier);
			}
			page.Add(tiers);

			page.Add(Heading("In the sky"));
			skyList = Well(string.Empty);
			page.Add(skyList);
			var looks = Row();
			looks.Add(SmallButton("Look at sun", () => LookAtBody(true)));
			looks.Add(SmallButton("Look at moon", () => LookAtBody(false)));
			looks.Add(SmallButton("Look north", () => Controller.LookAt(Vector3.forward)));
			looks.Add(SmallButton("Look up", () => Controller.LookAt(new Vector3(0f, 1f, 0.15f))));
			page.Add(looks);
		}

		// ── Weather ───────────────────────────────────────────────────

		private void BuildWeatherTab(VisualElement page)
		{
			page.Add(Heading("Presets"));
			transition = LabeledSlider("Transition s", 0f, 60f, 5f, _ => { });
			page.Add(transition);
			var presets = Row(true);
			foreach (WeatherPreset preset in Controller.Presets)
			{
				if (preset == null)
				{
					continue;
				}
				WeatherPreset captured = preset;
				Button button = SmallButton(preset.ResolvedName, () =>
				{
					Controller.ApplyPreset(captured, transition.value);
					SyncDirectToggle();
				});
				button.userData = preset;
				presetButtons.Add(button);
				presets.Add(button);
			}
			// Reset, not clear: every preset and hand-set layer fades out and the drifting field
			// takes the sky back. A clear sky is the Clear preset, which overrides the field.
			presets.Add(SmallButton("Reset to field", () =>
			{
				Controller.ClearAll(transition.value);
				SyncChannelSliders();
				SyncDirectToggle();
			}));
			page.Add(presets);

			page.Add(Heading("Storm cells", "A cell starts upwind and drifts across the camera."));
			var cells = Row(true);
			foreach (string name in new[] { "Thunderstorm", "Blizzard", "Sandstorm", "Hailstorm", "Ashfall", "Heavy Rain" })
			{
				WeatherPreset preset = Controller.Presets.Find(p => p != null && p.ResolvedName == name);
				if (preset != null)
				{
					cells.Add(SmallButton(name, () => { Controller.SpawnCell(preset); SyncDirectToggle(); }));
				}
			}
			page.Add(cells);

			page.Add(Heading("Layers", "Laid on top of whatever preset is running."));
			foreach (WeatherLayerKind kind in (WeatherLayerKind[])Enum.GetValues(typeof(WeatherLayerKind)))
			{
				WeatherLayerKind captured = kind;
				Slider slider = LabeledSlider(kind.ToString(), 0f, 1f, 0f, v =>
				{
					Controller.SetLayer(captured, v, 0.5f);
					SyncDirectToggle();
				});
				layerSliders[kind] = slider;
				page.Add(slider);
			}

			page.Add(Heading("Climate"));
			page.Add(LabeledSlider("Temperature", -1f, 1f, Controller.Temperature, v => Controller.Temperature = v));
			page.Add(LabeledToggle("Match to preset", Controller.MatchTemperature, v => Controller.MatchTemperature = v,
				"Clicking a snow preset in a warm scene otherwise gives rain: falling snow turns to rain above freezing, and a snow layer does not apply outside its own range at all."));

			// ── Driving the sky directly ──
			// The model cannot be asked for "exactly this much cloud and this much aurora" — no
			// preset lands on those numbers — and that is a thing worth being able to ask for. So
			// the channels can be set straight, and the toggle says which of the two is running.
			page.Add(Heading("Direct", "The model cannot be asked for exactly this much cloud and this much aurora, because no preset lands on those numbers. These set the channels straight. Applying a preset, a layer or a cell turns them back off."));
			directToggle = LabeledToggle("Use these sliders", Controller.DriveWeatherDirectly,
				v => Controller.DriveWeatherDirectly = v,
				"On, these channels are the whole weather and the timeline above is ignored. Applying a preset, a layer or a cell turns it back off.");
			page.Add(directToggle);
			ChannelSlider(page, "Cloud cover", WeatherChannel.CloudCover);
			ChannelSlider(page, "Cloud density", WeatherChannel.CloudDensity);
			ChannelSlider(page, "Precipitation", WeatherChannel.Precipitation);
			ChannelSlider(page, "Rain", WeatherChannel.RainWeight);
			ChannelSlider(page, "Snow", WeatherChannel.SnowWeight);
			ChannelSlider(page, "Fog", WeatherChannel.FogDensity);
			ChannelSlider(page, "Lightning", WeatherChannel.LightningRate);
			ChannelSlider(page, "Aurora", WeatherChannel.Aurora);
			ChannelSlider(page, "Wind", WeatherChannel.WindSpeed);
			var quick = Row();
			quick.Add(SmallButton("Clear", () => SetDirect(WeatherFrame.Clear, 0.2f)));
			quick.Add(SmallButton("Overcast", () => SetDirect(Frame((WeatherChannel.CloudCover, 1f), (WeatherChannel.CloudDensity, 0.9f), (WeatherChannel.FogDensity, 0.2f)), 0.1f)));
			quick.Add(SmallButton("Storm", () => SetDirect(Frame((WeatherChannel.CloudCover, 1f), (WeatherChannel.CloudDensity, 1f), (WeatherChannel.Precipitation, 0.9f), (WeatherChannel.RainWeight, 1f), (WeatherChannel.LightningRate, 1f), (WeatherChannel.WindSpeed, 0.7f), (WeatherChannel.FogDensity, 0.4f)), 0.2f)));
			quick.Add(SmallButton("Aurora night", () => SetDirect(Frame((WeatherChannel.Aurora, 1f), (WeatherChannel.CloudCover, 0.1f)), -0.7f)));
			page.Add(quick);
		}

		private void ChannelSlider(VisualElement page, string label, WeatherChannel channel)
		{
			Slider slider = LabeledSlider(label, 0f, 1f, Controller.SkyWeather[channel], v =>
			{
				WeatherFrame frame = Controller.SkyWeather;
				frame[channel] = v;
				// The setter turns direct mode on: moving one of these is a request to use them.
				Controller.SkyWeather = frame;
				SyncDirectToggle();
			});
			channelSliders[channel] = slider;
			page.Add(slider);
		}

		private static WeatherFrame Frame(params (WeatherChannel channel, float value)[] values)
		{
			var frame = new WeatherFrame();
			foreach ((WeatherChannel channel, float value) in values)
			{
				frame[channel] = value;
			}
			return frame;
		}

		private void SetDirect(WeatherFrame frame, float temperature)
		{
			Controller.SkyWeather = frame;
			Controller.Temperature = temperature;
			SyncChannelSliders();
			SyncDirectToggle();
		}

		private void SyncChannelSliders()
		{
			WeatherFrame frame = Controller.SkyWeather;
			foreach (KeyValuePair<WeatherChannel, Slider> pair in channelSliders)
			{
				pair.Value.SetValueWithoutNotify(frame[pair.Key]);
			}
		}

		private void SyncDirectToggle()
		{
			directToggle?.SetValueWithoutNotify(Controller.DriveWeatherDirectly);
		}

		// ── Clouds ────────────────────────────────────────────────────

		private void BuildCloudTab(VisualElement page)
		{
			cloudSection = new VisualElement();
			page.Add(cloudSection);
			BuildCloudSection();
		}

		/// <summary>Draws the cloud controls, and redraws them when the bands change.</summary>
		private void BuildCloudSection()
		{
			if (cloudSection == null)
			{
				return;
			}
			cloudSection.Clear();
			WeatherRenderProfile profile = Controller != null && Controller.Presentation != null ? Controller.Presentation.Profile : null;
			profile = profile != null ? profile : WeatherRenderProfile.Active;
			if (profile == null)
			{
				cloudSection.Add(Small("No weather render profile in the scene, so there is nothing to tune."));
				return;
			}
			CloudControls.Build(cloudSection, profile, BuildCloudSection);
		}

		// ── Ground ────────────────────────────────────────────────────

		private void BuildGroundTab(VisualElement page)
		{
			// Cover takes a quarter of an hour of weather to build, which is no way to look at a wet
			// street or a snowed-in courtyard. These hold it at a depth instead, and the surfaces show
			// it at once. "Let it settle" hands the ground back to the weather.
			page.Add(Heading("Ground clock", "How fast the ground wets and dries, against real time. The sky runs far faster than this by default; at the sky's own rate the ground is wet and dry again inside a second."));
			page.Add(LabeledSlider("Ground clock ×", 1f, 200f, Controller.GroundTimeScale, v => Controller.GroundTimeScale = v));
			page.Add(Heading("Held cover", "Snow, wet, ash and sand held at a depth instead of accumulating, so a wet street can be looked at without a quarter of an hour of rain."));
			page.Add(LabeledSlider("Snow", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Snow = v)));
			page.Add(LabeledSlider("Wet", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Wet = v)));
			page.Add(LabeledSlider("Ash", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Ash = v)));
			page.Add(LabeledSlider("Sand", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Sand = v)));

			page.Add(Heading("On the ground"));
			coverLabel = Well(string.Empty);
			page.Add(coverLabel);

			var toolRow = Row();
			toolRow.Add(SmallButton("Reset cover", Controller.ResetCover));
			toolRow.Add(SmallButton("Let it settle", () => Controller.CoverOverride = null));
			toolRow.Add(SmallButton("+15 min", () => Controller.AdvanceCover(900f)));
			page.Add(toolRow);
		}

		/// <summary>
		/// Holds the ground's cover where the sliders put it. Changing one keeps the others, so a
		/// wet, ash-dusted street is one drag away from a dry one.
		/// </summary>
		private void HoldCover(RefAction<WeatherCover> change)
		{
			WeatherCover cover = Controller.CoverOverride ?? Controller.Timeline.Cover;
			change(ref cover);
			Controller.CoverOverride = cover;
		}

		private delegate void RefAction<T>(ref T value);

		// ── Small actions ─────────────────────────────────────────────

		private void SetLatitude(float value)
		{
			Controller.Latitude = value;
			RefreshFacts();
		}

		private void SetSeason(float season01)
		{
			float day = Controller.DayOfSeason(season01);
			Controller.DayOfYear = day;
			daySlider?.SetValueWithoutNotify(day);
		}

		/// <summary>The next eclipse of the chosen kind from now, from here, with the clock set to watch it.</summary>
		private void JumpToEclipse(bool solar)
		{
			if (!Controller.FindNextEclipse(solar, out double hoursAtFirstContact))
			{
				Debug.LogWarning($"[World Sim] No {(solar ? "solar" : "lunar")} eclipse found in the next few years from this place.");
				return;
			}
			Controller.Paused = true;
			Controller.JumpToHours(hoursAtFirstContact - 1.0 / 60.0);
			Controller.TimeScale = 0.01f;
			timeSlider?.SetValueWithoutNotify((float)Controller.TimeOfDay);
			daySlider?.SetValueWithoutNotify(Controller.DayOfYear);
			yearSlider?.SetValueWithoutNotify(Controller.Year);
		}

		private void Jump(double localTime01)
		{
			Controller.JumpTo(localTime01);
			timeSlider?.SetValueWithoutNotify((float)localTime01);
		}

		private void LookAtBody(bool sun)
		{
			CelestialState state = Controller.State;
			if (state == null)
			{
				return;
			}
			int index = sun ? state.Sun : state.Moon;
			if (index >= 0)
			{
				Controller.LookAt(state.Bodies[index].Direction);
			}
		}

		private void RefreshBodyButtons()
		{
			for (int i = 0; i < bodyButtons.Count && i < Controller.Bodies.Count; i++)
			{
				bodyButtons[i].EnableInClassList(ActiveClass, Controller.Bodies[i] == Controller.Body);
			}
		}

		private void RefreshFacts()
		{
			if (bodyFacts == null)
			{
				return;
			}
			WorldBody body = Controller.Body;
			if (body == null)
			{
				bodyFacts.text = "No body selected.";
				return;
			}
			CultureInfo culture = CultureInfo.InvariantCulture;
			SolarSystemProfile system = SolarSystemProfile.Active;
			string day = $"{Controller.BodyDayHours.ToString("0.##", culture)} h";
			string air = body.HasWeather ? body.Atmosphere.ToString().ToLowerInvariant() + " air" : "airless (no weather, black sky)";
			string sky = body.Sky != null ? body.Sky.name : "default sky";
			string starlight = system != null
				? $", starlight {(CelestialMath.Insolation(system, body, Controller.Hours) / Math.Max(1e-6, CelestialMath.MeanHomeInsolation(system))).ToString("0.##", culture)}×"
				: string.Empty;
			bodyFacts.text = $"{body.ResolvedName}: day {day}, {air}, {sky}{starlight}{(body.TidallyLocked ? ", tidally locked" : string.Empty)}";
		}

		// ── Readout ───────────────────────────────────────────────────

		private void Update()
		{
			refresh -= Time.deltaTime;
			if (refresh > 0f || Controller == null || stats == null)
			{
				return;
			}
			refresh = 0.15f;
			CultureInfo culture = CultureInfo.InvariantCulture;
			CelestialState state = Controller.State;
			SkySystem sky = Controller.Sky;
			if (state == null)
			{
				Stat("clock.time", "no day/night cycle", "warn");
				return;
			}
			if (!Controller.Paused)
			{
				timeSlider?.SetValueWithoutNotify((float)state.LocalTime01);
				daySlider?.SetValueWithoutNotify(Controller.DayOfYear);
				yearSlider?.SetValueWithoutNotify(Controller.Year);
			}

			// Clock.
			Stat("clock.time", $"{SceneTime.Format(state.LocalTime01)} · {(state.IsDaylight ? "day" : "night")}");
			Stat("clock.date", $"year {Controller.Year}, day {Mathf.FloorToInt(Controller.DayOfYear)}");
			// An upright axis has no seasons, and the figure for "none" is the equinox's: said in words,
			// or a moon reads as stuck in spring.
			bool upright = Controller.Body != null && Controller.Body.AxialTiltDegrees < 3f;
			Stat("clock.season", upright
				? "none (upright axis)"
				: SeasonName(CelestialMath.Season01(SolarSystemProfile.Active, Controller.Body, Controller.Hours), Controller.Latitude),
				upright ? "dim" : null);
			Stat("clock.hours", Controller.Hours.ToString("0.00", culture));
			Stat("clock.rate", Controller.Paused ? "paused" : $"{Controller.TimeScale.ToString("0.###", culture)} h/s", Controller.Paused ? "dim" : null);

			// Sun and sky.
			float stars = sky != null ? sky.Current.StarVisibility : 0f;
			Stat("sky.sun", $"{state.SunAltitude.ToString("0.0", culture)}° up");
			Stat("sky.stars", $"{(stars * 100f).ToString("0", culture)}%", stars <= 0.001f ? "dim" : null);
			Stat("sky.meteors", $"{state.MeteorRate.ToString("0", culture)} / h");
			SolarEclipseInfo drawnEclipse = sky != null ? sky.DrawnEclipse : state.Solar;
			if (drawnEclipse.Phase != SolarEclipsePhase.None)
			{
				string phase = drawnEclipse.Phase.ToString().ToLowerInvariant();
				Stat("sky.eclipse", $"solar {phase} · {(drawnEclipse.Obscuration * 100f).ToString("0", culture)}% covered · looks {(drawnEclipse.Darkness * 100f).ToString("0", culture)}% dark"
					+ (drawnEclipse.Totality > 0.01f ? $" · totality {(drawnEclipse.Totality * 100f).ToString("0", culture)}%" : string.Empty), "warn");
			}
			else if (state.LunarPhase != LunarEclipsePhase.None)
			{
				Stat("sky.eclipse", $"lunar {state.LunarPhase.ToString().ToLowerInvariant()} · {(state.LunarEclipse * 100f).ToString("0", culture)}% in the umbra", "warn");
			}
			else
			{
				Stat("sky.eclipse", "none", "dim");
			}

			// The aurora, and why: how disturbed the field is, and whether this latitude is under the ring.
			{
				SolarSystemProfile solar = SolarSystemProfile.Active;
				WorldBody standing = Controller.Body;
				float season = CelestialMath.Season01(solar, standing, Controller.Hours);
				float activity = WeatherDriver.GeomagneticActivity(WeatherDriver.WorldSeed, Controller.Hours * 3600.0, season);
				string storm = activity < 0.05f ? "quiet" : activity < 0.4f ? "unsettled" : activity < 0.75f ? "storm" : "great storm";
				if (standing != null && (standing.MagneticField <= 0.001f || !standing.HasWeather))
				{
					Stat("sky.aurora", standing.HasWeather ? "none (no magnetic field)" : "none (no air)", "dim");
				}
				else
				{
					// The same wind the weather field uses, so this reads what is drawn.
					float wind = solar != null && standing != null
						? (float)(CelestialMath.Insolation(solar, standing, Controller.Hours) / Math.Max(1e-6, CelestialMath.MeanHomeInsolation(solar)))
						: 1f;
					float overhead = WeatherDriver.Aurora(WeatherDriver.WorldSeed, Controller.Hours * 3600.0, Controller.Latitude, season, standing != null ? standing.MagneticField : 1f, wind);
					Stat("sky.aurora", $"{storm} · {(overhead * 100f).ToString("0", culture)}% here", overhead > 0.5f ? "good" : overhead < 0.05f ? "dim" : null);
				}
			}

			// The air: what the driver says it is doing. Without this the weather just changes and
			// there is no telling whether a front is arriving or a slider was nudged.
			WeatherSample sample = Controller.LastSample;
			WeatherFrame shown = Controller.Presentation != null ? Controller.Presentation.Shown : sample.Frame;
			WeatherDriver.Synoptic air = sample.Air;
			if (Controller.DriveWeatherDirectly)
			{
				Stat("air.pressure", "—", "dim");
				Stat("air.humidity", "—", "dim");
				Stat("air.instability", "—", "dim");
				Stat("air.driver", "off (sliders)", "warn");
			}
			else
			{
				string system = air.Pressure < -0.15f ? "low" : air.Pressure > 0.15f ? "high" : "slack";
				Stat("air.pressure", $"{system} {air.Pressure.ToString("+0.00;-0.00", culture)}");
				Stat("air.humidity", $"{(air.Humidity * 100f).ToString("0", culture)}%");
				Stat("air.instability", $"{(air.Instability * 100f).ToString("0", culture)}%");
				bool overridden = sample.DriverWeight < 0.999f;
				Stat("air.driver", overridden ? $"{((1f - sample.DriverWeight) * 100f).ToString("0", culture)}% overridden" : "running",
					overridden ? "warn" : "good");
			}

			// The weather that is shown.
			string falling = sample.Precipitation == PrecipitationKind.None ? "nothing" : sample.Precipitation.ToString().ToLowerInvariant();
			float amount = shown[WeatherChannel.Precipitation];
			Stat("wx.source", Controller.DriveWeatherDirectly ? "sliders" : Controller.ActivePreset != null ? Controller.ActivePreset.ResolvedName : "the field");
			Stat("wx.timeline", $"{Controller.Timeline.Cells.Count} cell(s) · {Controller.Timeline.Layers.Count} layer(s)");
			Stat("wx.falling", sample.Precipitation == PrecipitationKind.None ? "nothing" : $"{falling} {amount.ToString("0.00", culture)}",
				sample.Precipitation == PrecipitationKind.None ? "dim" : null);
			float fog = WeatherFogPresenter.Amount(shown);
			Stat("wx.fog", fog.ToString("0.00", culture), fog <= 0.005f ? "dim" : null);
			Stat("wx.wind", $"{(shown[WeatherChannel.WindSpeed] * 30f).ToString("0.0", culture)} m/s");
			// The whole, and the part of it that is the body's: how far this world is from its suns,
			// its air, this latitude and the season. Frozen and scorched are said so, since the scale
			// stops at one either way and a world can be well past it.
			string body = Controller.BodyTemperature <= -0.999f ? "frozen" : Controller.BodyTemperature >= 0.999f ? "scorched" : Controller.BodyTemperature.ToString("+0.00;-0.00", culture);
			Stat("wx.temp", $"{sample.Temperature.ToString("+0.00;-0.00", culture)} (body {body})",
				Mathf.Abs(Controller.BodyTemperature) >= 0.999f ? "warn" : null);
			Stat("wx.exposure", sample.IsSheltered ? "sheltered" : "exposed");

			RefreshCloudStats(sky, culture);

			if (skyList != null && activeTab == 0)
			{
				skyList.text = Describe(state, culture);
			}
			if (coverLabel != null && activeTab == 3)
			{
				WeatherCover cover = Controller.Timeline.Cover;
				coverLabel.text = (Controller.PresetTemperature.HasValue
					? $"The preset moved the scene to {Controller.PresetTemperature.Value.ToString("0.00", culture)}°.\n"
					: string.Empty)
					+ $"snow {cover.Snow.ToString("0.00", culture)} · wet {cover.Wet.ToString("0.00", culture)} · "
					+ $"ash {cover.Ash.ToString("0.00", culture)} · sand {cover.Sand.ToString("0.00", culture)}";
			}
			foreach (Button button in presetButtons)
			{
				button.EnableInClassList(ActiveClass, !Controller.DriveWeatherDirectly && ReferenceEquals(button.userData, Controller.ActivePreset));
			}
			int quality = QualitySettings.GetQualityLevel();
			for (int i = 0; i < tierButtons.Count; i++)
			{
				tierButtons[i].EnableInClassList(ActiveClass, i == quality);
			}
		}

		/// <summary>
		/// What the clouds are doing on this tier: the sky is drawn by a marched volume, and how
		/// much of it runs — the steps, the shadow on the ground, the light shafts — changes with
		/// quality. Without these, a tier that quietly turns something off looks like a bug.
		/// </summary>
		private void RefreshCloudStats(SkySystem sky, CultureInfo culture)
		{
			if (sky == null || !SkySystem.CloudsReady)
			{
				// Said where it will be seen, and said once: the rest of the card has nothing to show.
				Stat("cl.bands", sky == null ? "no sky system" : "not baked — Weather Tools → Bake cloud noise", "warn");
				Stat("cl.shell", "—", "dim");
				Stat("cl.resolution", "—", "dim");
				Stat("cl.steps", "—", "dim");
				Stat("cl.history", "—", "dim");
				Stat("cl.shafts", "—", "dim");
				return;
			}
			CloudTierSettings tier = sky.CloudTier;
			int bands = sky.CloudBands != null ? sky.CloudBands.Count : 0;
			Stat("cl.bands", bands.ToString(culture));
			Stat("cl.shell", $"{sky.CloudShellBottom.ToString("0", culture)}–{sky.CloudShellTop.ToString("0", culture)} m");
			Stat("cl.resolution", $"{(tier.Resolution * 100f).ToString("0", culture)}% of screen");
			Stat("cl.steps", $"{tier.Steps} · detail {tier.Detail.ToString("0.0", culture)}");
			Stat("cl.history", tier.Temporal ? "steadied" : "off", tier.Temporal ? null : "warn");
			if (!SkySystem.DrawGodRays)
			{
				Stat("cl.shafts", "switched off", "warn");
			}
			else if (sky.GodRayIntensity > 0.001f)
			{
				Stat("cl.shafts", sky.GodRayIntensity.ToString("0.00", culture) + (sky.GodRayEclipse > 0.02f ? " (eclipse)" : string.Empty));
			}
			else
			{
				Stat("cl.shafts", "none", "dim");
			}
		}

		private static string Describe(CelestialState state, CultureInfo culture)
		{
			var lines = new List<string>();
			for (int i = 0; i < state.Bodies.Count && lines.Count < 10; i++)
			{
				SkyBodyState body = state.Bodies[i];
				if (body.Body == null || !body.AboveHorizon)
				{
					continue;
				}
				string phase = body.Kind == SkyBodyKind.Star ? string.Empty : $", {(body.Illumination * 100f).ToString("0", culture)}% lit";
				string shadow = body.Shadowed > 0.01f ? $", {(body.Shadowed * 100f).ToString("0", culture)}% shadowed" : string.Empty;
				string size = $"{(body.AngularRadius * 2f * Mathf.Rad2Deg).ToString("0.##", culture)}° wide";
				lines.Add($"{body.Body.ResolvedName} ({body.Kind}) {body.AltitudeDegrees.ToString("0", culture)}° up, {size}{phase}{shadow}");
			}
			return lines.Count > 0 ? string.Join("\n", lines) : "Nothing above the horizon.";
		}
	}
}
