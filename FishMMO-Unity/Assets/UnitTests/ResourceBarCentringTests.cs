using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEditor;
using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The health, mana and stamina bars sit centred on the screen by default, at every aspect
	/// ratio and interface scale.
	/// </summary>
	/// <remarks>
	/// They are three separate UIDocuments, so the group cannot be centred by a shared container.
	/// They used to be placed at fixed lefts (x294..906) that are only centred in a panel exactly
	/// 1200 units wide — which the panel stops being as soon as the Interface Scale setting moves
	/// the reference resolution (1500 wide at 0.8, 960 at 1.25). The default placement is now
	/// derived from the panel's live width; a position the player dragged to still wins, which is
	/// why the check is made with no stored position.
	/// </remarks>
	[TestFixture]
	public class ResourceBarCentringTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/ResourceBar/UIResourceBar.uxml";

		private static readonly (string name, System.Type type)[] Bars =
		{
			("UIHealthBar", typeof(UITKHealthBar)),
			("UIManaBar", typeof(UITKManaBar)),
			("UIStaminaBar", typeof(UITKStaminaBar)),
		};

		private readonly List<GameObject> hosts = new List<GameObject>();
		private readonly List<UIDocument> documents = new List<UIDocument>();
		private PanelSettings settings;
		private RenderTexture texture;

		[TearDown]
		public void TearDown()
		{
			Unmount();
			UITKPanelScale.Apply(1.0f);
		}

		private void Mount(int width, int height, float scale)
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the resource bar UXML must exist at {UxmlPath}");

			/* A render target 1200 wide, so at scale 1 the panel is the 1200-unit space every HUD
			 * offset is authored in and only the height varies with the aspect ratio. */
			texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			settings = Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			// Through the setting's own API, the way the Options slider applies it.
			UITKPanelScale.Register(settings);
			UITKPanelScale.Apply(scale);
			LogAssert.AreEqual(
				new Vector2Int(Mathf.RoundToInt(1200 / scale), Mathf.RoundToInt(800 / scale)),
				settings.referenceResolution,
				$"UITKPanelScale.Apply({scale}) must rewrite the reference resolution of the registered panel");

			foreach ((string name, System.Type type) in Bars)
			{
				LogAssert.IsFalse(UITKPanelPositions.TryLoad(name, out _),
					$"{name} must have no stored position: this checks the default placement, which a dragged position overrides");

				GameObject host = new GameObject(name);
				UIDocument document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = uxml;

				UITKControl control = (UITKControl)host.AddComponent(type);
				control.Document = document;
				control.OnStarting();

				hosts.Add(host);
				documents.Add(document);
			}
		}

		private void Unmount()
		{
			foreach (GameObject host in hosts)
			{
				if (host != null)
				{
					Object.DestroyImmediate(host);
				}
			}
			hosts.Clear();
			documents.Clear();

			if (settings != null)
			{
				Object.DestroyImmediate(settings);
				settings = null;
			}
			if (texture != null)
			{
				texture.Release();
				Object.DestroyImmediate(texture);
				texture = null;
			}
		}

		/// <summary>
		/// The group's centre is the panel's centre, at 16:9, 3:2 and 21:9 and at interface scale
		/// 0.8, 1 and 1.25.
		/// </summary>
		[UnityTest]
		public IEnumerator BarGroupIsCentredOnThePanel()
		{
			/* The three authored heights at one pixel per unit, plus two real screens whose
			 * pixels-per-point is fractional (2.13 and 2.87). Those are where the snapped bar
			 * width fed back into its own centring and looped; at 1200 wide that cannot happen. */
			(int width, int height)[] screens = { (1200, 675), (1200, 800), (1200, 506), (2560, 1080), (3440, 1440) };
			float[] scales = { 1.0f, 0.8f, 1.25f };

			foreach ((int width, int height) in screens)
			{
				foreach (float scale in scales)
				{
					Mount(width, height, scale);

					for (int frame = 0; frame < 10; ++frame)
					{
						yield return null;
					}

					float panelWidth = documents[0].rootVisualElement.layout.width;
					LogAssert.IsTrue(Mathf.Abs(panelWidth - 1200f / scale) < 1f,
						$"at scale {scale} the panel must be {1200f / scale} units wide; it resolved to {panelWidth}");

					float minX = float.MaxValue;
					float maxX = float.MinValue;
					for (int i = 0; i < Bars.Length; ++i)
					{
						Rect bar = documents[i].rootVisualElement.Q("bar-root").worldBound;
						LogAssert.IsTrue(bar.width > 1f && bar.height > 1f,
							$"{Bars[i].name} must be laid out before its position means anything; it resolved to {bar}");
						minX = Mathf.Min(minX, bar.xMin);
						maxX = Mathf.Max(maxX, bar.xMax);
					}

					float centre = (minX + maxX) * 0.5f;
					LogAssert.IsTrue(Mathf.Abs(centre - panelWidth * 0.5f) <= 1f,
						$"at {width}x{height}, interface scale {scale}, the resource bar group (x {minX}..{maxX}) " +
						$"must be centred on the panel ({panelWidth * 0.5f}); its centre is {centre}");

					Unmount();
				}
			}
		}
	}
}
