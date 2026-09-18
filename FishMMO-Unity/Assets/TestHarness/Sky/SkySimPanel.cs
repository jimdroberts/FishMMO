using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.TestHarness.Sky
{
	/// <summary>
	/// The sky test bed's controls: which body you stand on, where on it, the date and time, the
	/// sky profile, the weather the sky is drawn under, the quality tier, and a live readout of
	/// everything in the sky above you.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public sealed class SkySimPanel : MonoBehaviour
	{
		public SkySimController Controller;

		private static readonly Color PanelColour = new Color(0.06f, 0.07f, 0.09f, 0.9f);
		private static readonly Color TextColour = new Color(0.9f, 0.92f, 0.95f, 1f);
		private static readonly Color MutedColour = new Color(0.62f, 0.67f, 0.74f, 1f);
		private static readonly Color AccentColour = new Color(0.55f, 0.75f, 1f, 1f);

		private Label readout;
		private VisualElement cloudSection;
		private Label bodyFacts;
		private Label skyList;
		private Slider timeSlider;
		private Slider daySlider;
		private Button playButton;
		private readonly List<Button> bodyButtons = new List<Button>();
		private readonly Dictionary<WeatherChannel, Slider> weatherSliders = new Dictionary<WeatherChannel, Slider>();
		private float refresh;

		private void Start()
		{
			UIDocument document = GetComponent<UIDocument>();
			if (document == null || document.rootVisualElement == null || Controller == null)
			{
				return;
			}
			Build(document.rootVisualElement);
			Controller.Presented += _ => { };
		}

		// ── Widgets ──

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
			var slider = new Slider(text, low, high) { value = value, showInputField = true };
			slider.style.marginTop = 1;
			slider.style.marginBottom = 1;
			slider.labelElement.style.minWidth = 96;
			slider.labelElement.style.fontSize = 11;
			slider.labelElement.style.color = TextColour;
			slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return slider;
		}

		private void Build(VisualElement root)
		{
			root.style.flexDirection = FlexDirection.Row;

			var panel = new ScrollView();
			panel.style.width = 330;
			panel.style.maxHeight = Length.Percent(100);
			panel.style.backgroundColor = PanelColour;
			panel.style.paddingLeft = 8;
			panel.style.paddingRight = 8;
			panel.style.paddingTop = 6;
			panel.style.paddingBottom = 8;
			root.Add(panel);

			var title = new Label("Sky Sim");
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.fontSize = 16;
			title.style.color = TextColour;
			panel.Add(title);
			panel.Add(Small("Right-drag to look, scroll to zoom, WASD to move. Every sky here is the one the game draws on that body."));

			readout = Small(string.Empty);
			readout.style.marginTop = 6;
			readout.style.color = TextColour;
			panel.Add(readout);

			// ── Where you stand ──
			panel.Add(Heading("Body"));
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
			panel.Add(bodies);
			bodyFacts = Small(string.Empty);
			panel.Add(bodyFacts);

			panel.Add(LabeledSlider("Latitude", -90f, 90f, Controller.Latitude, v => { Controller.Latitude = v; RefreshFacts(); }));
			panel.Add(LabeledSlider("Longitude", -180f, 180f, Controller.Longitude, v => Controller.Longitude = v));
			panel.Add(LabeledSlider("Heading", 0f, 360f, Controller.Heading, v => Controller.Heading = v));
			var places = Row();
			places.Add(SmallButton("Equator", () => SetLatitude(0f)));
			places.Add(SmallButton("45°N", () => SetLatitude(45f)));
			places.Add(SmallButton("Arctic 70°N", () => SetLatitude(70f)));
			places.Add(SmallButton("Pole 89°N", () => SetLatitude(89f)));
			places.Add(SmallButton("45°S", () => SetLatitude(-45f)));
			panel.Add(places);

			// ── Time ──
			panel.Add(Heading("Time"));
			timeSlider = LabeledSlider("Time of day", 0f, 1f, (float)Controller.TimeOfDay, v => Controller.TimeOfDay = v);
			panel.Add(timeSlider);
			daySlider = LabeledSlider("Day of year", 0f, 364f, Controller.DayOfYear, v => Controller.DayOfYear = v);
			panel.Add(daySlider);
			panel.Add(LabeledSlider("Hours / second", 0f, 3f, Controller.TimeScale, v => Controller.TimeScale = v));
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
			panel.Add(times);
			var seasons = Row();
			seasons.Add(SmallButton("Spring", () => SetDay(0.20f)));
			seasons.Add(SmallButton("Summer", () => SetDay(0.47f)));
			seasons.Add(SmallButton("Autumn", () => SetDay(0.72f)));
			seasons.Add(SmallButton("Winter", () => SetDay(0.97f)));
			panel.Add(seasons);

			// ── Sky ──
			panel.Add(Heading("Sky profile"));
			var skies = Row();
			skies.Add(SmallButton("Body's own", () => Controller.SkyOverride = null));
			foreach (SkyProfile profile in Controller.SkyProfiles)
			{
				SkyProfile captured = profile;
				skies.Add(SmallButton(profile.name, () => Controller.SkyOverride = captured));
			}
			panel.Add(skies);
			panel.Add(Small("A profile here is forced on whatever body you stand on, the way a region's Change Sky Profile action does."));

			var larger = new Toggle("Larger than life") { value = Controller.LargerThanLife };
			larger.tooltip = "Off (what ships): every disc is life-size. On: suns, moons and planets are drawn at the profile's scales, the way most games flatter the sky. This only changes what you see here; the assets keep their own setting.";
			larger.labelElement.style.minWidth = 96;
			larger.labelElement.style.fontSize = 11;
			larger.labelElement.style.color = TextColour;
			larger.RegisterValueChangedCallback(evt => Controller.LargerThanLife = evt.newValue);
			// Every band of the sky, on live controls.
			cloudSection = new VisualElement();
			panel.Add(cloudSection);
			BuildCloudSection();
			panel.Add(larger);

			// ── Weather ──
			panel.Add(Heading("Weather over the sky"));
			WeatherSlider(panel, "Cloud cover", WeatherChannel.CloudCover);
			WeatherSlider(panel, "Cloud density", WeatherChannel.CloudDensity);
			WeatherSlider(panel, "Precipitation", WeatherChannel.Precipitation);
			WeatherSlider(panel, "Rain", WeatherChannel.RainWeight);
			WeatherSlider(panel, "Snow", WeatherChannel.SnowWeight);
			WeatherSlider(panel, "Fog", WeatherChannel.FogDensity);
			WeatherSlider(panel, "Lightning", WeatherChannel.LightningRate);
			WeatherSlider(panel, "Aurora", WeatherChannel.Aurora);
			WeatherSlider(panel, "Wind", WeatherChannel.WindSpeed);
			panel.Add(LabeledSlider("Temperature", -1f, 1f, Controller.Temperature, v => Controller.Temperature = v));
			var weathers = Row();
			weathers.Add(SmallButton("Clear", () => SetWeather(WeatherFrame.Clear, 0.2f)));
			weathers.Add(SmallButton("Overcast", () => SetWeather(Frame((WeatherChannel.CloudCover, 1f), (WeatherChannel.CloudDensity, 0.9f), (WeatherChannel.FogDensity, 0.2f)), 0.1f)));
			weathers.Add(SmallButton("Storm", () => SetWeather(Frame((WeatherChannel.CloudCover, 1f), (WeatherChannel.CloudDensity, 1f), (WeatherChannel.Precipitation, 0.9f), (WeatherChannel.RainWeight, 1f), (WeatherChannel.LightningRate, 1f), (WeatherChannel.WindSpeed, 0.7f), (WeatherChannel.FogDensity, 0.4f)), 0.2f)));
			weathers.Add(SmallButton("Aurora night", () => SetWeather(Frame((WeatherChannel.Aurora, 1f), (WeatherChannel.CloudCover, 0.1f)), -0.7f)));
			panel.Add(weathers);

			// ── Quality ──
			panel.Add(Heading("Quality"));
			var tiers = Row();
			for (int i = 0; i < QualitySettings.names.Length; i++)
			{
				int level = i;
				tiers.Add(SmallButton(QualitySettings.names[i], () => QualitySettings.SetQualityLevel(level, true)));
			}
			panel.Add(tiers);

			// ── What is up there ──
			panel.Add(Heading("In the sky"));
			skyList = Small(string.Empty);
			panel.Add(skyList);
			var looks = Row();
			looks.Add(SmallButton("Look at sun", () => LookAtBody(true)));
			looks.Add(SmallButton("Look at moon", () => LookAtBody(false)));
			looks.Add(SmallButton("Look north", () => Controller.LookAt(Vector3.forward)));
			looks.Add(SmallButton("Look up", () => Controller.LookAt(new Vector3(0f, 1f, 0.15f))));
			panel.Add(looks);

			RefreshBodyButtons();
			RefreshFacts();
		}

		private void WeatherSlider(VisualElement panel, string label, WeatherChannel channel)
		{
			Slider slider = LabeledSlider(label, 0f, 1f, Controller.Weather[channel], v =>
			{
				WeatherFrame frame = Controller.Weather;
				frame[channel] = v;
				Controller.Weather = frame;
			});
			weatherSliders[channel] = slider;
			panel.Add(slider);
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

		private void SetWeather(WeatherFrame frame, float temperature)
		{
			Controller.Weather = frame;
			Controller.Temperature = temperature;
			foreach (KeyValuePair<WeatherChannel, Slider> pair in weatherSliders)
			{
				pair.Value.SetValueWithoutNotify(frame[pair.Key]);
			}
		}

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
			daySlider.SetValueWithoutNotify(day);
		}

		private void Jump(double localTime01)
		{
			Controller.JumpTo(localTime01);
			timeSlider.SetValueWithoutNotify((float)localTime01);
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

		private void Update()
		{
			refresh -= Time.deltaTime;
			if (refresh > 0f || Controller == null || readout == null)
			{
				return;
			}
			refresh = 0.1f;
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
				timeSlider.SetValueWithoutNotify((float)state.LocalTime01);
				daySlider.SetValueWithoutNotify(Controller.DayOfYear);
			}
			string eclipse = state.SolarEclipse > 0.01f
				? $" · solar eclipse {(state.SolarEclipse * 100f).ToString("0", culture)}% ({state.EclipsingBody?.ResolvedName})"
				: state.LunarEclipse > 0.01f ? $" · lunar eclipse {(state.LunarEclipse * 100f).ToString("0", culture)}%" : string.Empty;
			float stars = sky != null ? sky.Current.StarVisibility : 0f;
			readout.text =
				$"{SceneTime.Format(state.LocalTime01)}  ·  day {Mathf.FloorToInt(Controller.DayOfYear)}  ·  {(state.IsDaylight ? "day" : "night")}\n" +
				$"sun {state.SunAltitude.ToString("0.0", culture)}°  ·  stars {(stars * 100f).ToString("0", culture)}%  ·  meteors {state.MeteorRate.ToString("0", culture)}/h{eclipse}\n" +
				CloudLine(sky, culture);

			if (skyList != null)
			{
				skyList.text = Describe(state, culture);
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

		/// <summary>Draws the cloud controls, and redraws them when the bands change.</summary>
		private void BuildCloudSection()
		{
			if (cloudSection == null)
			{
				return;
			}
			cloudSection.Clear();
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			if (profile == null)
			{
				cloudSection.Add(new Label("No weather render profile, so there is nothing to tune."));
				return;
			}
			CloudControls.Build(cloudSection, profile, BuildCloudSection);
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
