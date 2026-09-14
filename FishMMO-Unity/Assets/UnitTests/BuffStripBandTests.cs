using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEditor;
using NUnit.Framework;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The player's own buff and debuff icons sit on their 16-unit band above the resource bars,
	/// at every aspect ratio and interface scale — including with a single icon on each side.
	/// </summary>
	/// <remarks>
	/// Reported at 3440x1440: one icon dropped off the band into the resource bars. The render
	/// targets here are real SCREEN sizes rather than 1200-wide panels, because layout is snapped to
	/// device pixels and the defect only exists where pixels-per-point is fractional (1.6 at
	/// 1920 wide, 2.13 at 2560, 2.87 at 3440). At exactly one pixel per point it cannot happen, which
	/// is why every 1200-wide probe measured the strip as perfect. The icons are built by the
	/// container's own CreateGroup so the element tree is the shipped one.
	/// </remarks>
	[TestFixture]
	public class BuffStripBandTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string WorldRoot = "Assets/Scripts/Client/GUI/World/";
		private const string TemplatePath = "Assets/Templates/Entity/Buffs/Minor Increase Armor.asset";

		/// <summary>The strip band's bottom offset and height, from UIBuffContainer.uss.</summary>
		private const float BandBottom = 106f;
		private const float IconSize = 16f;

		private readonly List<GameObject> hosts = new List<GameObject>();
		private readonly Dictionary<string, UIDocument> documents = new Dictionary<string, UIDocument>();
		private PanelSettings settings;
		private RenderTexture texture;

		[TearDown]
		public void TearDown()
		{
			Unmount();
			UITKPanelScale.Apply(1.0f);
		}

		private void Mount(int width, int height, float scale, int icons)
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			BaseBuffTemplate template = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(TemplatePath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(template, $"a buff template must exist at {TemplatePath}");

			texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			settings = Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			MountPanel("UIHealthBar", WorldRoot + "ResourceBar/UIResourceBar.uxml", typeof(UITKHealthBar));
			MountPanel("UIManaBar", WorldRoot + "ResourceBar/UIResourceBar.uxml", typeof(UITKManaBar));
			MountPanel("UIStaminaBar", WorldRoot + "ResourceBar/UIResourceBar.uxml", typeof(UITKStaminaBar));
			MountPanel("UIBuff", WorldRoot + "Buff/UIBuff.uxml", typeof(UITKBuff));
			MountPanel("UIDebuff", WorldRoot + "Buff/UIDebuff.uxml", typeof(UITKDebuff));

			MethodInfo create = typeof(UITKBuffContainer).GetMethod("CreateGroup", BindingFlags.NonPublic | BindingFlags.Instance);
			LogAssert.IsNotNull(create, "UITKBuffContainer.CreateGroup must exist; this test builds icons through it");

			foreach (string name in new[] { "UIBuff", "UIDebuff" })
			{
				UIDocument document = documents[name];
				UITKBuffContainer container = document.GetComponent<UITKBuffContainer>();
				VisualElement list = document.rootVisualElement.Q("buff-list");
				LogAssert.IsNotNull(list, $"{name} must have a buff-list");

				for (int i = 0; i < icons; ++i)
				{
					object view = create.Invoke(container, new object[] { template });
					list.Add((VisualElement)view.GetType().GetField("Root").GetValue(view));
				}
			}

			UITKPanelScale.Register(settings);
			UITKPanelScale.Apply(scale);
		}

		private void MountPanel(string name, string uxml, System.Type type)
		{
			GameObject host = new GameObject(name);
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);

			UITKControl control = (UITKControl)host.AddComponent(type);
			control.Document = document;
			control.OnStarting();

			hosts.Add(host);
			documents[name] = document;
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
		/// With one, two and six icons per side, every icon lies inside the band and clear of the
		/// bars, at panel heights 675 (1920x1080), 800 (1920x1280) and 506 (2560x1080), plus the
		/// reported 3440x1440, each at interface scale 1, 0.8 and 1.25.
		/// </summary>
		/// <remarks>
		/// One icon is the case as reported, but a lone icon cannot wrap and so passes even with the
		/// defect present; the icon that fell was the second one in a fit-content row that the
		/// device-pixel snap made a few hundredths too narrow (see .buff-list in
		/// UIBuffContainer.uss). Two and six are what actually fail without the fix.
		/// </remarks>
		[UnityTest]
		public IEnumerator IconsSitOnTheBand()
		{
			(int w, int h)[] screens = { (1920, 1080), (1920, 1280), (2560, 1080), (3440, 1440) };
			float[] scales = { 1.0f, 0.8f, 1.25f };
			int[] counts = { 1, 2, 6 };

			foreach ((int w, int h) in screens)
			foreach (float scale in scales)
			foreach (int count in counts)
			{
				{
					Mount(w, h, scale, count);

					for (int frame = 0; frame < 10; ++frame)
					{
						yield return null;
					}

					string where = $"at {w}x{h}, interface scale {scale}, {count} icon(s) per side,";
					float panelHeight = documents["UIBuff"].rootVisualElement.layout.height;
					LogAssert.IsTrue(panelHeight > 1f, $"{where} the panel must be laid out; height {panelHeight}");

					float bandTop = panelHeight - BandBottom - IconSize;
					float bandBottom = panelHeight - BandBottom;

					Rect bars = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
					foreach (string name in new[] { "UIHealthBar", "UIManaBar", "UIStaminaBar" })
					{
						Rect r = documents[name].rootVisualElement.Q("bar-root").worldBound;
						bars = Rect.MinMaxRect(Mathf.Min(bars.xMin, r.xMin), Mathf.Min(bars.yMin, r.yMin),
							Mathf.Max(bars.xMax, r.xMax), Mathf.Max(bars.yMax, r.yMax));
					}

					foreach (string name in new[] { "UIBuff", "UIDebuff" })
					{
						VisualElement list = documents[name].rootVisualElement.Q("buff-list");
						LogAssert.AreEqual(count, list.childCount, $"{where} {name} must hold exactly the icons added");

						for (int i = 0; i < list.childCount; ++i)
						{
							Rect icon = list[i].worldBound;
							LogAssert.IsTrue(icon.width > 1f && icon.height > 1f, $"{where} {name} icon {i} must be laid out; got {icon}");
							LogAssert.IsTrue(icon.yMin >= bandTop - 0.5f && icon.yMax <= bandBottom + 0.5f,
								$"{where} {name} icon {i} (y {icon.yMin}..{icon.yMax}) must lie inside the strip band " +
								$"(y {bandTop}..{bandBottom}, bottom:{BandBottom} in a {panelHeight}-unit panel)");
							LogAssert.IsFalse(icon.Overlaps(bars),
								$"{where} {name} icon {i} {icon} must not intersect the resource bar group {bars}");
						}
					}

					Unmount();
				}
			}
		}
	}
}
