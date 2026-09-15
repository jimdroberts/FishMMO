using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEditor;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The client launcher's settings open as an overlay on the news, and close without taking the
	/// panel out of the displayed hierarchy.
	/// </summary>
	/// <remarks>
	/// <para>Two separate properties, pinned together because both live in the same toggle.
	/// The overlay: settings is positioned absolutely over the bottom of the body, so opening it
	/// covers part of the news instead of resizing it, and the footer never moves.</para>
	/// <para>The closing switch: repeated clicks on Settings made memory jump, and the cost was not
	/// the news reflowing. Measured with 2,400 opens and closes, closing with <c>display: none</c>
	/// (the old <c>fish-hidden</c> class) allocated about 12 KB more per frame than an idle launcher
	/// whether or not settings overlaid the news, because showing a displayed-none subtree rebuilds
	/// every element's visuals and re-meshes every label. Closing with <c>visibility: hidden</c>
	/// cut that to about 1.6 KB. A hidden panel is still laid out, so the test also proves nothing
	/// inside it can be clicked while closed.</para>
	/// <para>Measured against the real markup on the shared panel settings at the reference
	/// resolution, the way the other layout fixtures measure their panels.</para>
	/// </remarks>
	[TestFixture]
	public class LauncherSettingsOverlayTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string LauncherUxmlPath = "Assets/Scripts/Client/GUI/Launcher/UILauncher.uxml";
		private const string ClosedClass = "launcher-settings--closed";
		private const int PanelWidth = 1200;
		private const int PanelHeight = 800;

		private PanelSettings settings;
		private RenderTexture texture;
		private GameObject host;
		private UIDocument document;

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LauncherUxmlPath);
			LogAssert.IsNotNull(uxml, $"the launcher UXML must exist at {LauncherUxmlPath}");

			// A target of the reference size, so the panel has the units the stylesheet was written for.
			texture = new RenderTexture(PanelWidth, PanelHeight, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			settings = Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			host = new GameObject("LauncherSettingsOverlayTests");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			if (settings != null)
			{
				Object.DestroyImmediate(settings);
			}
			if (texture != null)
			{
				texture.Release();
				Object.DestroyImmediate(texture);
			}
			host = null;
			document = null;
			settings = null;
			texture = null;
		}

		private VisualElement Named(string name)
		{
			VisualElement element = document.rootVisualElement.Q(name);
			LogAssert.IsNotNull(element, $"the launcher markup must contain #{name}");
			return element;
		}

		/// <summary>Waits for layout, and refuses to measure a tree that was never laid out.</summary>
		private IEnumerator Settle(string what)
		{
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			Rect news = Named("launcher-news-scroll").worldBound;
			LogAssert.IsTrue(news.width > 1f && news.height > 1f,
				$"the news pane must be laid out {what} before its geometry means anything; it resolved to {news.width}x{news.height}");
		}

		[UnityTest]
		public IEnumerator SettingsOpensOverTheNewsWithoutResizingIt()
		{
			VisualElement settingsPanel = Named("launcher-settings");
			VisualElement newsPane = Named("launcher-news-scroll");
			VisualElement body = Named("launcher-body");
			VisualElement footer = Named("launcher-footer");

			LogAssert.IsTrue(settingsPanel.ClassListContains(ClosedClass), "settings starts closed, as the launcher shows it");
			LogAssert.IsFalse(settingsPanel.ClassListContains("fish-hidden"),
				"settings must not close with display: none, which rebuilds and re-meshes the whole panel on every open");

			yield return Settle("with settings closed");
			Rect newsClosed = newsPane.worldBound;
			Rect bodyClosed = body.worldBound;
			Rect footerClosed = footer.worldBound;

			/* Closed by visibility, the panel is still laid out; nothing inside it may take a click.
			 * Picked at the centre of where it sits, the hit must land outside it. */
			Rect settingsClosed = settingsPanel.worldBound;
			LogAssert.IsTrue(settingsPanel.resolvedStyle.visibility == Visibility.Hidden, "a closed settings panel resolves as hidden");
			LogAssert.IsTrue(settingsClosed.height > 1f, $"a closed settings panel is still laid out; it resolved to {settingsClosed.height} tall");
			VisualElement picked = document.rootVisualElement.panel.Pick(settingsClosed.center);
			LogAssert.IsTrue(picked == null || !settingsPanel.Contains(picked) && picked != settingsPanel,
				$"a click where the closed settings panel sits must not reach it; it reached {(picked == null ? "nothing" : picked.name + " " + picked.GetType().Name)}");

			// Exactly what UITKClientLauncher.ToggleSettings does.
			settingsPanel.RemoveFromClassList(ClosedClass);

			yield return Settle("with settings open");
			Rect newsOpen = newsPane.worldBound;
			Rect settingsOpen = settingsPanel.worldBound;
			Rect bodyOpen = body.worldBound;

			LogAssert.IsTrue(settingsPanel.resolvedStyle.position == Position.Absolute,
				"settings is positioned absolutely, over the body, not as a flex item beside the news");
			LogAssert.IsTrue(settingsOpen.height > 1f && settingsOpen.width > 1f,
				$"settings has real size when open; it resolved to {settingsOpen.width}x{settingsOpen.height}");

			LogAssert.IsTrue(Mathf.Abs(newsOpen.height - newsClosed.height) < 0.5f && Mathf.Abs(newsOpen.yMin - newsClosed.yMin) < 0.5f,
				$"opening settings must not resize or move the news pane (closed {newsClosed}, open {newsOpen}); a change here is the re-layout of every news label on each toggle");
			LogAssert.IsTrue(Mathf.Abs(footer.worldBound.yMin - footerClosed.yMin) < 0.5f,
				$"opening settings must not move the footer (closed {footerClosed.yMin}, open {footer.worldBound.yMin})");

			LogAssert.IsTrue(settingsOpen.yMin < newsOpen.yMax && settingsOpen.yMax > newsOpen.yMin,
				$"settings overlaps the news it covers (settings {settingsOpen}, news {newsOpen})");
			LogAssert.IsTrue(Mathf.Abs(settingsOpen.yMax - bodyOpen.yMax) < 1f,
				$"settings sits against the bottom of the body, just above the footer (settings bottom {settingsOpen.yMax}, body bottom {bodyOpen.yMax})");
			LogAssert.IsTrue(settingsOpen.height <= bodyClosed.height * 0.38f + 1f,
				$"settings stays within its 38% cap of the body, leaving most of the news visible (settings {settingsOpen.height}, body {bodyClosed.height})");
		}
	}
}
