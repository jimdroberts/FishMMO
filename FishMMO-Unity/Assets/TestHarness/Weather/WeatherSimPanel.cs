using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared.Weather;

namespace FishMMO.TestHarness.Weather
{
	/// <summary>
	/// The weather test bed's control panel: presets, per-kind layers, storm cells, temperature,
	/// sun, quality level and a live readout of the weather at the camera.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public sealed class WeatherSimPanel : MonoBehaviour
	{
		public WeatherSimController Controller;

		private static readonly Color Panel = new Color(0.07f, 0.08f, 0.1f, 0.88f);
		private static readonly Color Text = new Color(0.9f, 0.92f, 0.95f, 1f);
		private static readonly Color Muted = new Color(0.62f, 0.67f, 0.74f, 1f);
		private static readonly Color Accent = new Color(0.45f, 0.7f, 0.95f, 1f);

		private Label readout;
		private Label coverLabel;
		private Slider transition;
		private VisualElement cloudSection;
		private readonly Dictionary<WeatherLayerKind, Slider> layerSliders = new Dictionary<WeatherLayerKind, Slider>();
		private readonly List<Button> presetButtons = new List<Button>();
		private float refresh;

		private void Start()
		{
			UIDocument document = GetComponent<UIDocument>();
			if (document == null || document.rootVisualElement == null || Controller == null)
			{
				return;
			}
			Build(document.rootVisualElement);
		}

		private static Label Heading(string text)
		{
			var label = new Label(text);
			label.style.unityFontStyleAndWeight = FontStyle.Bold;
			label.style.color = Accent;
			label.style.marginTop = 8;
			label.style.marginBottom = 2;
			label.style.fontSize = 13;
			return label;
		}

		private static Label Small(string text)
		{
			var label = new Label(text);
			label.style.color = Muted;
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

		private static Slider LabeledSlider(string text, float low, float high, float value, Action<float> changed)
		{
			var slider = new Slider(text, low, high) { value = value, showInputField = true };
			slider.style.marginTop = 1;
			slider.style.marginBottom = 1;
			slider.labelElement.style.minWidth = 90;
			slider.labelElement.style.color = Text;
			slider.labelElement.style.fontSize = 11;
			slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
			return slider;
		}

		private void Build(VisualElement root)
		{
			root.Clear();
			var panel = new ScrollView();
			panel.name = "weather-sim-panel";
			panel.style.position = Position.Absolute;
			panel.style.left = 10;
			panel.style.top = 10;
			panel.style.bottom = 10;
			panel.style.width = 360;
			panel.style.backgroundColor = Panel;
			panel.style.paddingLeft = 10;
			panel.style.paddingRight = 10;
			panel.style.paddingTop = 8;
			panel.style.borderTopLeftRadius = 6;
			panel.style.borderTopRightRadius = 6;
			panel.style.borderBottomLeftRadius = 6;
			panel.style.borderBottomRightRadius = 6;
			root.Add(panel);

			var title = new Label("Weather Sim");
			title.style.fontSize = 18;
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.color = Text;
			panel.Add(title);
			panel.Add(Small("Local preview through the real weather model. Right mouse to look, WASD/QE to fly, Shift to hurry. Nothing here reaches a server."));

			panel.Add(Heading("Now"));
			readout = new Label();
			readout.style.color = Text;
			readout.style.fontSize = 11;
			readout.style.whiteSpace = WhiteSpace.Normal;
			panel.Add(readout);
			coverLabel = new Label();
			coverLabel.style.color = Muted;
			coverLabel.style.fontSize = 11;
			panel.Add(coverLabel);

			panel.Add(Heading("Presets"));
			transition = LabeledSlider("Transition s", 0f, 60f, 5f, _ => { });
			panel.Add(transition);
			var presets = new VisualElement();
			presets.style.flexDirection = FlexDirection.Row;
			presets.style.flexWrap = Wrap.Wrap;
			foreach (WeatherPreset preset in Controller.Presets)
			{
				if (preset == null)
				{
					continue;
				}
				WeatherPreset captured = preset;
				Button button = SmallButton(preset.ResolvedName, () => Controller.ApplyPreset(captured, transition.value));
				button.userData = preset;
				presetButtons.Add(button);
				presets.Add(button);
			}
			presets.Add(SmallButton("Clear all", () => Controller.ClearAll(transition.value)));
			panel.Add(presets);

			panel.Add(Heading("Storm cells"));
			panel.Add(Small("A cell starts upwind and drifts across the camera."));
			var cells = new VisualElement();
			cells.style.flexDirection = FlexDirection.Row;
			cells.style.flexWrap = Wrap.Wrap;
			foreach (string name in new[] { "Thunderstorm", "Blizzard", "Sandstorm", "Hailstorm", "Ashfall", "Heavy Rain" })
			{
				WeatherPreset preset = Controller.Presets.Find(p => p != null && p.ResolvedName == name);
				if (preset != null)
				{
					cells.Add(SmallButton(name, () => Controller.SpawnCell(preset)));
				}
			}
			panel.Add(cells);

			panel.Add(Heading("Layers (on top of the preset)"));
			foreach (WeatherLayerKind kind in (WeatherLayerKind[])Enum.GetValues(typeof(WeatherLayerKind)))
			{
				WeatherLayerKind captured = kind;
				Slider slider = LabeledSlider(kind.ToString(), 0f, 1f, 0f, v => Controller.SetLayer(captured, v, 0.5f));
				layerSliders[kind] = slider;
				panel.Add(slider);
			}

			// Everything about the clouds, on live controls. Rebuilt in place when a band is added
			// or removed, because the section's shape changes with the list.
			cloudSection = new VisualElement();
			panel.Add(cloudSection);
			BuildCloudSection();

			panel.Add(Heading("World"));
			panel.Add(LabeledSlider("Temperature", -1f, 1f, Controller.Temperature, v => Controller.Temperature = v));
			panel.Add(LabeledSlider("Time of day", 0f, 1f, (float)Controller.TimeOfDay, v => Controller.TimeOfDay = v));
			panel.Add(LabeledSlider("Day of year", 0f, 364f, Controller.DayOfYear, v => Controller.DayOfYear = v));
			panel.Add(LabeledSlider("Latitude", -85f, 85f, Controller.Latitude, v => Controller.Latitude = v));
			var quality = new DropdownField("Quality", new List<string>(QualitySettings.names), QualitySettings.GetQualityLevel());
			quality.labelElement.style.color = Text;
			quality.RegisterValueChangedCallback(evt => QualitySettings.SetQualityLevel(Array.IndexOf(QualitySettings.names, evt.newValue), true));
			panel.Add(quality);
			// ── Ground ──
			// Cover takes a quarter of an hour of weather to build, which is no way to look at a
			// wet street or a snowed-in courtyard. These hold it at a depth instead, and the
			// surfaces show it at once. "Let it settle" hands the ground back to the weather.
			panel.Add(Heading("Ground (held)"));
			panel.Add(LabeledSlider("Snow", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Snow = v)));
			panel.Add(LabeledSlider("Wet", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Wet = v)));
			panel.Add(LabeledSlider("Ash", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Ash = v)));
			panel.Add(LabeledSlider("Sand", 0f, 1f, 0f, v => HoldCover((ref WeatherCover c) => c.Sand = v)));

			var match = new Toggle("Match temperature to preset") { value = Controller.MatchTemperature };
			match.labelElement.style.color = Text;
			match.tooltip = "Clicking a snow preset in a warm scene otherwise gives rain: falling snow turns to rain above freezing, and a snow layer does not apply outside its own range at all.";
			match.RegisterValueChangedCallback(evt => Controller.MatchTemperature = evt.newValue);
			panel.Add(match);

			var toolRow = new VisualElement();
			toolRow.style.flexDirection = FlexDirection.Row;
			toolRow.Add(SmallButton("Reset cover", Controller.ResetCover));
			toolRow.Add(SmallButton("Let it settle", () => Controller.CoverOverride = null));
			toolRow.Add(SmallButton("+15 min", () => Controller.AdvanceCover(900f)));
			panel.Add(toolRow);

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

		private static string SkyLine(CultureInfo culture)
		{
			SkySystem sky = SkySystem.Instance;
			FishMMO.Shared.Celestial.CelestialState state = sky != null ? sky.State : null;
			if (state == null)
			{
				return "Sky: no day/night cycle";
			}
			string moon = state.Moon >= 0 ? $"moon {(state.Bodies[state.Moon].Illumination * 100f).ToString("0", culture)}% at {state.Bodies[state.Moon].AltitudeDegrees.ToString("0", culture)}°" : "no moon";
			return $"Sky: {FishMMO.Shared.Celestial.SceneTime.Format(state.LocalTime01)} · sun {state.SunAltitude.ToString("0.0", culture)}° · {moon} · latitude {state.Latitude.ToString("0", culture)}° · meteors {state.MeteorRate.ToString("0", culture)}/h · strikes {(sky.Lightning != null ? sky.Lightning.TotalStrikes : 0)}";
		}

		private void Update()
		{
			refresh -= Time.deltaTime;
			if (refresh > 0f || readout == null || Controller == null)
			{
				return;
			}
			refresh = 0.25f;
			WeatherSample sample = Controller.LastSample;
			WeatherFrame shown = Controller.Presentation != null ? Controller.Presentation.Shown : sample.Frame;
			var culture = CultureInfo.InvariantCulture;
			string falling = sample.Precipitation == PrecipitationKind.None ? "nothing" : sample.Precipitation.ToString().ToLowerInvariant();
			int drawn = Controller.Presentation != null ? Controller.Presentation.Precipitation.Drawn.Count : 0;
			bool occlusion = Controller.Presentation != null && Controller.Presentation.Occlusion.IsValid;
			readout.text =
				$"Preset {(Controller.ActivePreset != null ? Controller.ActivePreset.ResolvedName : "none")} · {Controller.Timeline.Cells.Count} cell(s) · {Controller.Timeline.Layers.Count} layer(s)\n" +
				$"Falling: {falling} {shown[WeatherChannel.Precipitation].ToString("0.00", culture)} (drop {shown[WeatherChannel.DropSize].ToString("0.00", culture)}), {drawn} kind(s) drawn\n" +
				$"Clouds {shown[WeatherChannel.CloudCover].ToString("0.00", culture)} · fog {WeatherFogPresenter.Amount(shown).ToString("0.00", culture)} · wind {(shown[WeatherChannel.WindSpeed] * 30f).ToString("0.0", culture)} m/s toward {shown[WeatherChannel.WindHeading].ToString("0", culture)}°\n" +
				$"Lightning {shown[WeatherChannel.LightningRate].ToString("0.00", culture)} · aurora {shown[WeatherChannel.Aurora].ToString("0.00", culture)} · storm {shown.StormSeverity.ToString("0.00", culture)}\n" +
				$"Temperature {sample.Temperature.ToString("+0.00;-0.00", culture)} · {(sample.IsSheltered ? "sheltered" : "exposed")} · sky map {(occlusion ? "ready" : "building")} · {QualitySettings.names[QualitySettings.GetQualityLevel()]}\n" +
				SkyLine(culture);
			WeatherCover cover = Controller.Timeline.Cover;
			coverLabel.text = (Controller.PresetTemperature.HasValue
				? $"The preset moved the scene to {Controller.PresetTemperature.Value.ToString("0.00", culture)}°.\n"
				: string.Empty) + $"Ground: snow {cover.Snow.ToString("0.00", culture)} · wet {cover.Wet.ToString("0.00", culture)} · ash {cover.Ash.ToString("0.00", culture)} · sand {cover.Sand.ToString("0.00", culture)}";
			foreach (Button button in presetButtons)
			{
				bool active = ReferenceEquals(button.userData, Controller.ActivePreset);
				button.style.borderBottomColor = active ? Accent : new StyleColor(StyleKeyword.Null);
				button.style.borderBottomWidth = active ? 2 : new StyleFloat(StyleKeyword.Null);
			}
		}
	}
}
