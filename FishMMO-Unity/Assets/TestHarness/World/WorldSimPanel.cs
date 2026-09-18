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
	/// on what ground — in four tabs over one live readout.
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

		private static readonly Color PanelColour = new Color(0.06f, 0.07f, 0.09f, 0.9f);
		private static readonly Color TextColour = new Color(0.9f, 0.92f, 0.95f, 1f);
		private static readonly Color MutedColour = new Color(0.62f, 0.67f, 0.74f, 1f);
		private static readonly Color AccentColour = new Color(0.5f, 0.72f, 0.97f, 1f);

		private Label readout;
		private Label bodyFacts;
		private Label skyList;
		private Label coverLabel;
		private Slider timeSlider;
		private Slider daySlider;
		private Slider transition;
		private Button playButton;
		private Toggle directToggle;
		private VisualElement cloudSection;

		private readonly List<Button> bodyButtons = new List<Button>();
		private readonly List<Button> presetButtons = new List<Button>();
		private readonly List<Button> tabButtons = new List<Button>();
		private readonly List<VisualElement> tabPages = new List<VisualElement>();
		private readonly Dictionary<WeatherChannel, Slider> channelSliders = new Dictionary<WeatherChannel, Slider>();
		private readonly Dictionary<WeatherLayerKind, Slider> layerSliders = new Dictionary<WeatherLayerKind, Slider>();
		private float refresh;
		private int activeTab;

		private void Start()
		{
			UIDocument document = GetComponent<UIDocument>();
			if (document == null || document.rootVisualElement == null || Controller == null)
			{
				return;
			}
			Build(document.rootVisualElement);
		}

		// ── Widgets ───────────────────────────────────────────────────

		private static Label Heading(string text)
		{
			var label = new Label(text);
			label.style.unityFontStyleAndWeight = FontStyle.Bold;
			label.style.color = AccentColour;
			label.style.marginTop = 8;
			label.style.marginBottom = 2;
			label.style.fontSize = 13;
			return label;
		}

		private static Label Small(string text)
		{
			var label = new Label(text);
			label.style.color = MutedColour;
			label.style.fontSize = 11;
			label.style.whiteSpace = WhiteSpace.Normal;
			return label;
		}

		private static Button SmallButton(string text, Action clicked)
		{
			var button = new Button(clicked) { text = text };
			button.style.fontSize = 11;
			button.style.marginLeft = 1;
			button.style.marginRight = 1;
			button.style.marginTop = 1;
			button.style.marginBottom = 1;
			button.style.paddingTop = 2;
			button.style.paddingBottom = 2;
			return button;
		}

		private static VisualElement Row()
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.flexWrap = Wrap.Wrap;
			return row;
		}

		private static Slider LabeledSlider(string text, float low, float high, float value, Action<float> changed)
		{
			var slider = new Slider(text, low, high) { value = Mathf.Clamp(value, low, high), showInputField = true };
			slider.style.marginTop = 1;
			slider.style.marginBottom = 1;
			slider.labelElement.style.minWidth = 96;
			slider.labelElement.style.fontSize = 11;
			slider.labelElement.style.color = TextColour;
			slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return slider;
		}

		private static Toggle LabeledToggle(string text, bool value, Action<bool> changed, string tooltip)
		{
			var toggle = new Toggle(text) { value = value, tooltip = tooltip };
			toggle.labelElement.style.minWidth = 96;
			toggle.labelElement.style.fontSize = 11;
			toggle.labelElement.style.color = TextColour;
			toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return toggle;
		}

		// ── Build ─────────────────────────────────────────────────────

		private void Build(VisualElement root)
		{
			root.Clear();
			root.style.flexDirection = FlexDirection.Row;

			var panel = new VisualElement();
			panel.name = "world-sim-panel";
			panel.style.width = 360;
			panel.style.height = Length.Percent(100);
			panel.style.backgroundColor = PanelColour;
			panel.style.paddingLeft = 8;
			panel.style.paddingRight = 8;
			panel.style.paddingTop = 6;
			panel.style.paddingBottom = 8;
			root.Add(panel);

			var title = new Label("World Sim");
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.fontSize = 16;
			title.style.color = TextColour;
			panel.Add(title);
			panel.Add(Small("Right-drag to look, scroll to zoom, WASD/QE to fly, Shift to hurry. Everything here is the real sky and the real weather model. Nothing reaches a server."));

			// The readout sits above the tabs, because what the sky is doing is worth seeing whichever
			// set of controls happens to be open.
			readout = Small(string.Empty);
			readout.style.marginTop = 6;
			readout.style.color = TextColour;
			panel.Add(readout);

			var tabBar = Row();
			tabBar.style.marginTop = 8;
			panel.Add(tabBar);

			var scroll = new ScrollView();
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
			Button button = SmallButton(name, () => ShowTab(index));
			button.style.paddingLeft = 8;
			button.style.paddingRight = 8;
			button.style.fontSize = 12;
			bar.Add(button);
			tabButtons.Add(button);

			var page = new VisualElement();
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
				tabButtons[i].style.color = i == index ? AccentColour : TextColour;
				tabButtons[i].style.unityFontStyleAndWeight = i == index ? FontStyle.Bold : FontStyle.Normal;
			}
		}

		// ── Sky ───────────────────────────────────────────────────────

		private void BuildSkyTab(VisualElement page)
		{
			page.Add(Heading("Body"));
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
			bodyFacts = Small(string.Empty);
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

			page.Add(Heading("Time"));
			timeSlider = LabeledSlider("Time of day", 0f, 1f, (float)Controller.TimeOfDay, v => Controller.TimeOfDay = v);
			page.Add(timeSlider);
			daySlider = LabeledSlider("Day of year", 0f, 364f, Controller.DayOfYear, v => Controller.DayOfYear = v);
			page.Add(daySlider);
			page.Add(LabeledSlider("Hours / second", 0f, 3f, Controller.TimeScale, v => Controller.TimeScale = v));
			var times = Row();
			playButton = SmallButton(Controller.Paused ? "▶ Run" : "❚❚ Pause", () =>
			{
				Controller.Paused = !Controller.Paused;
				playButton.text = Controller.Paused ? "▶ Run" : "❚❚ Pause";
			});
			times.Add(playButton);
			times.Add(SmallButton("Dawn", () => Jump(0.25)));
			times.Add(SmallButton("Noon", () => Jump(0.5)));
			times.Add(SmallButton("Dusk", () => Jump(0.75)));
			times.Add(SmallButton("Midnight", () => Jump(0.0)));
			page.Add(times);
			var seasons = Row();
			seasons.Add(SmallButton("Spring", () => SetDay(0.20f)));
			seasons.Add(SmallButton("Summer", () => SetDay(0.47f)));
			seasons.Add(SmallButton("Autumn", () => SetDay(0.72f)));
			seasons.Add(SmallButton("Winter", () => SetDay(0.97f)));
			page.Add(seasons);

			page.Add(Heading("Sky profile"));
			var skies = Row();
			skies.Add(SmallButton("Body's own", () => Controller.SkyOverride = null));
			foreach (SkyProfile profile in Controller.SkyProfiles)
			{
				SkyProfile captured = profile;
				skies.Add(SmallButton(profile.name, () => Controller.SkyOverride = captured));
			}
			page.Add(skies);
			page.Add(Small("A profile here is forced on whatever body you stand on, the way a region's Change Sky Profile action does."));
			page.Add(LabeledToggle("Larger than life", Controller.LargerThanLife, v => Controller.LargerThanLife = v,
				"Off (what ships): every disc is life-size. On: suns, moons and planets are drawn at the profile's scales, the way most games flatter the sky. This only changes what you see here; the assets keep their own setting."));

			page.Add(Heading("Quality"));
			var tiers = Row();
			for (int i = 0; i < QualitySettings.names.Length; i++)
			{
				int level = i;
				tiers.Add(SmallButton(QualitySettings.names[i], () => QualitySettings.SetQualityLevel(level, true)));
			}
			page.Add(tiers);

			page.Add(Heading("In the sky"));
			skyList = Small(string.Empty);
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
			var presets = Row();
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
			presets.Add(SmallButton("Clear all", () =>
			{
				Controller.ClearAll(transition.value);
				SyncChannelSliders();
				SyncDirectToggle();
			}));
			page.Add(presets);

			page.Add(Heading("Storm cells"));
			page.Add(Small("A cell starts upwind and drifts across the camera."));
			var cells = Row();
			foreach (string name in new[] { "Thunderstorm", "Blizzard", "Sandstorm", "Hailstorm", "Ashfall", "Heavy Rain" })
			{
				WeatherPreset preset = Controller.Presets.Find(p => p != null && p.ResolvedName == name);
				if (preset != null)
				{
					cells.Add(SmallButton(name, () => { Controller.SpawnCell(preset); SyncDirectToggle(); }));
				}
			}
			page.Add(cells);

			page.Add(Heading("Layers (on top of the preset)"));
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
			page.Add(Heading("Set the sky directly"));
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
				cloudSection.Add(new Label("No weather render profile in the scene, so there is nothing to tune."));
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
			page.Add(Heading("Held cover"));
			page.Add(Small("Snow, wet, ash and sand held at a depth instead of accumulating."));
			page.Add(LabeledSlider("Snow", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Snow = v)));
			page.Add(LabeledSlider("Wet", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Wet = v)));
			page.Add(LabeledSlider("Ash", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Ash = v)));
			page.Add(LabeledSlider("Sand", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Sand = v)));

			page.Add(Heading("What is on the ground"));
			coverLabel = Small(string.Empty);
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

		private void SetDay(float yearFraction)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			int days = system != null ? system.DaysPerYear : 365;
			float day = Mathf.Round(yearFraction * days);
			Controller.DayOfYear = day;
			daySlider?.SetValueWithoutNotify(day);
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
				bool selected = Controller.Bodies[i] == Controller.Body;
				bodyButtons[i].style.color = selected ? AccentColour : TextColour;
				bodyButtons[i].style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
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
			if (refresh > 0f || Controller == null || readout == null)
			{
				return;
			}
			refresh = 0.15f;
			CultureInfo culture = CultureInfo.InvariantCulture;
			CelestialState state = Controller.State;
			SkySystem sky = Controller.Sky;
			if (state == null)
			{
				readout.text = "No day/night cycle in the scene.";
				return;
			}
			if (!Controller.Paused)
			{
				timeSlider?.SetValueWithoutNotify((float)state.LocalTime01);
				daySlider?.SetValueWithoutNotify(Controller.DayOfYear);
			}
			string eclipse = state.SolarEclipse > 0.01f
				? $" · solar eclipse {(state.SolarEclipse * 100f).ToString("0", culture)}% ({state.EclipsingBody?.ResolvedName})"
				: state.LunarEclipse > 0.01f ? $" · lunar eclipse {(state.LunarEclipse * 100f).ToString("0", culture)}%" : string.Empty;
			float stars = sky != null ? sky.Current.StarVisibility : 0f;

			WeatherSample sample = Controller.LastSample;
			WeatherFrame shown = Controller.Presentation != null ? Controller.Presentation.Shown : sample.Frame;
			string falling = sample.Precipitation == PrecipitationKind.None ? "nothing" : sample.Precipitation.ToString().ToLowerInvariant();
			string driving = Controller.DriveWeatherDirectly
				? "sliders"
				: Controller.ActivePreset != null ? Controller.ActivePreset.ResolvedName : "no preset";

			readout.text =
				$"{SceneTime.Format(state.LocalTime01)}  ·  day {Mathf.FloorToInt(Controller.DayOfYear)}  ·  {(state.IsDaylight ? "day" : "night")}\n" +
				$"sun {state.SunAltitude.ToString("0.0", culture)}°  ·  stars {(stars * 100f).ToString("0", culture)}%  ·  meteors {state.MeteorRate.ToString("0", culture)}/h{eclipse}\n" +
				$"Weather: {driving} · {Controller.Timeline.Cells.Count} cell(s) · {Controller.Timeline.Layers.Count} layer(s)\n" +
				$"Falling {falling} {shown[WeatherChannel.Precipitation].ToString("0.00", culture)} · fog {WeatherFogPresenter.Amount(shown).ToString("0.00", culture)} · " +
				$"wind {(shown[WeatherChannel.WindSpeed] * 30f).ToString("0.0", culture)} m/s · temp {sample.Temperature.ToString("+0.00;-0.00", culture)} · {(sample.IsSheltered ? "sheltered" : "exposed")}\n" +
				CloudLine(sky, culture);

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
				bool active = !Controller.DriveWeatherDirectly && ReferenceEquals(button.userData, Controller.ActivePreset);
				button.style.borderBottomColor = active ? AccentColour : new StyleColor(StyleKeyword.Null);
				button.style.borderBottomWidth = active ? 2 : new StyleFloat(StyleKeyword.Null);
			}
		}

		/// <summary>
		/// What the clouds are doing on this tier: the sky is drawn by a marched volume, and how
		/// much of it runs — the steps, the shadow on the ground, the light shafts — changes with
		/// quality. Without this line, a tier that quietly turns something off looks like a bug.
		/// </summary>
		private static string CloudLine(SkySystem sky, CultureInfo culture)
		{
			if (sky == null)
			{
				return "Clouds: no sky system";
			}
			if (!SkySystem.CloudsReady)
			{
				return "Clouds: not ready — bake the cloud noise (Weather Tools → Bake cloud noise)";
			}
			CloudTierSettings tier = sky.CloudTier;
			string rays = sky.GodRayIntensity > 0.001f
				? $"shafts {sky.GodRayIntensity.ToString("0.00", culture)}{(sky.GodRayEclipse > 0.02f ? " (from the eclipsing body)" : string.Empty)}"
				: "no shafts";
			int bands = sky.CloudBands != null ? sky.CloudBands.Count : 0;
			return $"Clouds: {bands} band(s), {sky.CloudShellBottom.ToString("0", culture)}–{sky.CloudShellTop.ToString("0", culture)} m · "
				+ $"{(tier.Resolution * 100f).ToString("0", culture)}% of the screen · {tier.Steps} steps · detail {tier.Detail.ToString("0.0", culture)}"
				+ $" · {(tier.Temporal ? "steadied" : "no history")} · {rays}";
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
