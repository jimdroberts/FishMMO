using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using TMPro;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	public class CanvasCrawler : MonoBehaviour
	{
		public static Configuration Configuration;

		public static Dictionary<Type, BaseCanvasTypeSettings> CanvasSettingsMap = new Dictionary<Type, BaseCanvasTypeSettings>()
		{
			{ typeof(UnityEngine.UI.Button), new ButtonCanvasTypeSettings() },
			{ typeof(UnityEngine.UI.Image), new ImageCanvasTypeSettings() },
			{ typeof(RawImage), new RawImageCanvasTypeSettings() },
			{ typeof(Text), new TextCanvasTypeSettings() },
			{ typeof(UnityEngine.UI.Toggle), new ToggleCanvasTypeSettings() },
			{ typeof(UnityEngine.UI.Slider), new SliderCanvasTypeSettings() },
			{ typeof(Scrollbar), new ScrollbarCanvasTypeSettings() },
			{ typeof(Dropdown), new DropdownCanvasTypeSettings() },
			{ typeof(InputField), new InputFieldCanvasTypeSettings() },
			{ typeof(Mask), new MaskCanvasTypeSettings() },
			{ typeof(VerticalLayoutGroup), new VerticalLayoutGroupCanvasTypeSettings() },
			{ typeof(HorizontalLayoutGroup), new HorizontalLayoutGroupCanvasTypeSettings() },
			{ typeof(TMP_InputField), new TMP_InputFieldCanvasTypeSettings() },
			{ typeof(TMP_Text),	new TMP_TextCanvasTypeSettings() },
		};

		public Canvas Canvas;

		void Awake()
		{
			if (Canvas == null)
			{
				Canvas = GetComponent<Canvas>();

				if (Canvas == null)
				{
					throw new UnityException("Unable to find a Canvas on the gameObject.");
				}
			}

			if (Configuration == null)
			{
				Configuration = new Configuration(Client.GetWorkingDirectory());
				if (!Configuration.Load("UIConfiguration" + Configuration.EXTENSION))
				{
					// if we failed to load the file.. save a new one
					Configuration.Set("PrimaryColorR", "44");
					Configuration.Set("PrimaryColorG", "44");
					Configuration.Set("PrimaryColorB", "44");
					Configuration.Set("PrimaryColorA", "235");

					Configuration.Set("SecondaryColorR", "80");
					Configuration.Set("SecondaryColorG", "80");
					Configuration.Set("SecondaryColorB", "80");
					Configuration.Set("SecondaryColorA", "235");

					Configuration.Set("HighlightColorR", "128");
					Configuration.Set("HighlightColorG", "128");
					Configuration.Set("HighlightColorB", "128");
					Configuration.Set("HighlightColorA", "250");

					Configuration.Set("BackgroundColorR", "128");
					Configuration.Set("BackgroundColorG", "128");
					Configuration.Set("BackgroundColorB", "128");
					Configuration.Set("BackgroundColorA", "235");

					Configuration.Set("TextColorR", "136");
					Configuration.Set("TextColorG", "136");
					Configuration.Set("TextColorB", "136");
					Configuration.Set("TextColorA", "255");

					Configuration.Set("HealthColorR", "207");
					Configuration.Set("HealthColorG", "76");
					Configuration.Set("HealthColorB", "76");
					Configuration.Set("HealthColorA", "255");

					Configuration.Set("ManaColorR", "87");
					Configuration.Set("ManaColorG", "119");
					Configuration.Set("ManaColorB", "222");
					Configuration.Set("ManaColorA", "255");

					Configuration.Set("StaminaColorR", "83");
					Configuration.Set("StaminaColorG", "176");
					Configuration.Set("StaminaColorB", "59");
					Configuration.Set("StaminaColorA", "255");

					Configuration.Set("CrosshairColorR", "255");
					Configuration.Set("CrosshairColorG", "255");
					Configuration.Set("CrosshairColorB", "255");
					Configuration.Set("CrosshairColorA", "255");

#if !UNITY_EDITOR
					Configuration.Save();
#endif
				}
			}
		}

		void Start()
		{
			Crawl(Canvas);
		}

		public static void Crawl(Canvas canvas)
		{
			List<GameObject> gobs = canvas.transform.FindAllChildGameObjects();

			foreach (GameObject go in gobs)
			{
				if (go.name.Contains("Ignore"))
				{
					continue;
				}
				foreach (KeyValuePair<Type, BaseCanvasTypeSettings> pair in CanvasSettingsMap)
				{
					var type = go.GetComponent(pair.Key);
					if (type == null) continue;
					
					pair.Value.ApplySettings(type, Configuration);
				}
			}
		}
	}
}