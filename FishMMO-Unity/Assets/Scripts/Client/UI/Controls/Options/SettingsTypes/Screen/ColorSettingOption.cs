using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// A setting option that opens a <see cref="UIColorPicker"/> for a single named color stored in UIConfiguration.
	/// Attach as a child of the Options panel alongside other <see cref="SettingOption"/> components.
	/// The <see cref="ConfigKeyPrefix"/> must match the color key used in <see cref="UITheme"/>
	/// (e.g., "TooltipTitle" reads TooltipTitleColorR/G/B/A).
	/// </summary>
	public class ColorSettingOption : SettingOption
	{
		/// <summary>
		/// The configuration key prefix for this color (e.g., "TooltipTitle").
		/// Maps to {prefix}ColorR, {prefix}ColorG, {prefix}ColorB, {prefix}ColorA in UIConfiguration.
		/// </summary>
		[Tooltip("Config key prefix (e.g., 'TooltipTitle' maps to TooltipTitleColorR/G/B/A).")]
		public string ConfigKeyPrefix = "";

		/// <summary>
		/// Reference to the shared <see cref="UIColorPicker"/> in the scene.
		/// </summary>
		public UIColorPicker ColorPicker;

		/// <summary>
		/// Button that opens the color picker for this setting.
		/// </summary>
		public Button OpenButton;

		/// <summary>
		/// Image used to preview the currently selected color.
		/// </summary>
		[Tooltip("Preview swatch that displays the current color.")]
		public Image ColorPreview;

		/// <summary>
		/// The currently configured color, kept in sync with the config file.
		/// </summary>
		private Color currentColor = Color.white;

		/// <summary>
		/// Validates required references and wires the open button to show the picker.
		/// </summary>
		public override void Initialize()
		{
			if (string.IsNullOrEmpty(ConfigKeyPrefix))
			{
				Log.Error("ColorSettingOption", $"ConfigKeyPrefix cannot be null on {gameObject.name}!");
				return;
			}

			if (ColorPicker == null)
			{
				Log.Error("ColorSettingOption", $"ColorPicker reference is missing on {gameObject.name}.");
				return;
			}

			if (OpenButton != null)
			{
				OpenButton.onClick.RemoveAllListeners();
				OpenButton.onClick.AddListener(OnOpenPicker);
			}
		}

		/// <summary>
		/// Loads the RGBA values from UIConfiguration and updates the preview swatch.
		/// </summary>
		public override void Load()
		{
			Configuration config = new Configuration(Constants.GetWorkingDirectory());
			if (!config.Load("UIConfiguration"))
			{
				return;
			}

			config.TryGetByte($"{ConfigKeyPrefix}ColorR", out byte r);
			config.TryGetByte($"{ConfigKeyPrefix}ColorG", out byte g);
			config.TryGetByte($"{ConfigKeyPrefix}ColorB", out byte b);
			config.TryGetByte($"{ConfigKeyPrefix}ColorA", out byte a, 255);

			currentColor = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
			UpdatePreview();
		}

		/// <summary>
		/// Saves the current color to UIConfiguration and reloads the theme.
		/// </summary>
		public override void Save()
		{
			Configuration config = new Configuration(Constants.GetWorkingDirectory());
			config.Load("UIConfiguration");

			byte r = (byte)Mathf.RoundToInt(currentColor.r * 255f);
			byte g = (byte)Mathf.RoundToInt(currentColor.g * 255f);
			byte b = (byte)Mathf.RoundToInt(currentColor.b * 255f);
			byte a = (byte)Mathf.RoundToInt(currentColor.a * 255f);

			config.Set($"{ConfigKeyPrefix}ColorR", r.ToString());
			config.Set($"{ConfigKeyPrefix}ColorG", g.ToString());
			config.Set($"{ConfigKeyPrefix}ColorB", b.ToString());
			config.Set($"{ConfigKeyPrefix}ColorA", a.ToString());

#if !UNITY_EDITOR
			config.Save();
#endif

			// Reload theme so all UI reflects the updated colors immediately.
			CanvasCrawler.LoadTheme();
		}

		/// <summary>
		/// Opens the shared color picker, sets it to the current color, and subscribes for changes.
		/// </summary>
		private void OnOpenPicker()
		{
			ColorPicker.OnColorChanged = OnPickerColorChanged;
			ColorPicker.SetColor(currentColor);
			ColorPicker.Show();
		}

		/// <summary>
		/// Called by the color picker whenever the user adjusts the color.
		/// Updates the local color, preview swatch, and saves to configuration.
		/// </summary>
		private void OnPickerColorChanged(Color color)
		{
			currentColor = color;
			UpdatePreview();
			Save();
		}

		/// <summary>
		/// Updates the <see cref="ColorPreview"/> image to reflect <see cref="currentColor"/>.
		/// </summary>
		private void UpdatePreview()
		{
			if (ColorPreview != null)
			{
				ColorPreview.color = currentColor;
			}
		}
	}
}