using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Shared;
using FishNet.Transporting.WebTransport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FishMMO.Client.Editor
{
	/// <summary>
	/// Renders the network statistics overlay, driven by realistic fabricated traffic, to PNGs
	/// outside the project: a desktop client in a busy scene, WebGL clients on Chrome (estimated
	/// transport layer) and Firefox (browser-reported), a desktop client that has not connected
	/// yet, and two row tooltips.
	/// </summary>
	/// <remarks>
	/// <para><b>Invocation.</b> Batch mode with a graphics device and without <c>-quit</c>: the
	/// capture needs editor frames to elapse, so this runs as an <c>EditorApplication.update</c>
	/// state machine and exits the editor itself (see <see cref="UITKPanelValidator.RenderPreviews"/>
	/// for why a plain loop renders blank images):</para>
	/// <code>
	/// xvfb-run -a -s "-screen 0 1600x1000x24" &lt;Unity&gt; -batchmode -projectPath &lt;FishMMO-Unity&gt; \
	///   -executeMethod FishMMO.Client.Editor.NetworkStatsRender.Render \
	///   -netstatsOut /home/jim/Dev/FishMMO-Dev/PanelRenders/NetworkStats -logFile /tmp/netstats-render.log
	/// </code>
	/// <para><b>Output.</b> The directory comes from <c>-netstatsOut &lt;dir&gt;</c>, else the
	/// <c>FISHMMO_NETSTATS_RENDER_DIR</c> environment variable, else
	/// <c>&lt;repo&gt;/PanelRenders/NetworkStats</c>. A directory inside the project's Assets folder
	/// is refused: Unity would import every PNG and write a .meta beside it. Each capture writes a
	/// full 16:9 frame (<c>&lt;name&gt;.png</c>) and a crop of the drawn content
	/// (<c>&lt;name&gt;-crop.png</c>), both at twice the reference resolution.</para>
	/// <para><b>What is real and what is fabricated.</b> The panel, its stylesheet, the sampler,
	/// every formatting rule and the tooltip renderer are the shipped ones. Only the snapshots are
	/// made up, shaped like the traffic of each client: the browser ones go through the
	/// transport's own <see cref="TransportTrafficMath.ApplyBrowserStats"/>, so the estimated
	/// Chrome figures are exactly what the estimate model produces for that application traffic.
	/// The player's configuration is not touched: a scratch store is swapped in for the run and
	/// the original put back.</para>
	/// </remarks>
	public static class NetworkStatsRender
	{
		/// <summary>Command-line switch naming the output directory.</summary>
		public const string OutputArgument = "-netstatsOut";

		/// <summary>Environment variable naming the output directory.</summary>
		public const string OutputEnvironmentVariable = "FISHMMO_NETSTATS_RENDER_DIR";

		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string PanelUxmlPath = "Assets/Scripts/Client/GUI/Shared/NetworkStats/UINetworkStats.uxml";
		private const string TooltipUxmlPath = "Assets/Scripts/Client/GUI/Shared/Tooltip/UITooltip.uxml";

		/// <summary>Twice the 1200-unit reference width, at 16:9, so text is legible when cropped.</summary>
		private const int CaptureWidth = 2400;
		private const int CaptureHeight = 1350;

		/// <summary>
		/// A muted ground colour standing in for the world behind the overlay, so its translucency
		/// shows. The crop reads it back from the texture rather than trusting this value.
		/// </summary>
		private static readonly Color Ground = new Color(0.20f, 0.25f, 0.21f, 1.0f);

		/// <summary>Editor frames to let a mounted capture lay out and repaint before reading it.</summary>
		private const int SettleFrames = 12;

		/// <summary>Pixels of ground kept around the drawn content in a crop.</summary>
		private const int CropPadding = 16;

		private enum Scenario
		{
			DesktopBusy,
			WebGlChrome,
			WebGlFirefox,
			DesktopNotConnected,
		}

		private sealed class Capture
		{
			public string Name;
			public Scenario Scenario;
			/// <summary>When set, the tooltip for this row is drawn beside the overlay.</summary>
			public NetworkStatsRow? Tooltip;
		}

		private sealed class Mounted
		{
			public Capture Capture;
			public GameObject Host;
			public UITKNetworkStats Panel;
			public GameObject TooltipHost;
			public PanelSettings Settings;
			public RenderTexture Texture;
		}

		private static readonly Capture[] Captures =
		{
			new Capture { Name = "netstats-desktop-busy", Scenario = Scenario.DesktopBusy },
			new Capture { Name = "netstats-webgl-chrome", Scenario = Scenario.WebGlChrome },
			new Capture { Name = "netstats-webgl-firefox", Scenario = Scenario.WebGlFirefox },
			new Capture { Name = "netstats-desktop-not-connected", Scenario = Scenario.DesktopNotConnected },
			new Capture { Name = "netstats-desktop-wire-tooltip", Scenario = Scenario.DesktopBusy, Tooltip = NetworkStatsRow.Wire },
			new Capture { Name = "netstats-webgl-chrome-quic-tooltip", Scenario = Scenario.WebGlChrome, Tooltip = NetworkStatsRow.Quic },
		};

		private static Queue<Capture> queue;
		private static Mounted pending;
		private static int framesWaited;
		private static string outputDirectory;
		private static PanelSettings panelSettingsSource;
		private static Configuration previousConfiguration;
		/// <summary>True while the scratch store is in place and the original is owed back.</summary>
		private static bool configurationSwapped;
		private static readonly List<string> problems = new List<string>();
		private static readonly List<string> written = new List<string>();

		/// <summary>Renders every capture. Batch mode: do not pass -quit; this exits the editor.</summary>
		public static void Render()
		{
			problems.Clear();
			written.Clear();

			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
			{
				Finish("no graphics device; run without -nographics");
				return;
			}

			outputDirectory = ResolveOutputDirectory(out string refusal);
			if (outputDirectory == null)
			{
				Finish(refusal);
				return;
			}
			Directory.CreateDirectory(outputDirectory);
			Log($"writing to {outputDirectory}");

			panelSettingsSource = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			if (panelSettingsSource == null)
			{
				Finish($"PanelSettings not found at {PanelSettingsPath}");
				return;
			}

			/* A scratch store with the overlay switched on. The panel decides its own visibility
			 * from this setting, and the developer's configuration must not be where that is
			 * written. Restored in Finish. */
			previousConfiguration = Configuration.GlobalSettings;
			configurationSwapped = true;
			Configuration.SetGlobalSettings(new Configuration(Path.Combine(Path.GetTempPath(), "FishMMO-NetworkStatsRender")));
			ClientSettings.Set(ClientSettings.NetworkStatsEnabledKey, true);
			ClientSettings.Set(ClientSettings.NetworkStatsGraphKey, true);

			queue = new Queue<Capture>(Captures);
			pending = null;
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		/// <summary>
		/// The output directory, or null with the reason: the switch, then the environment variable,
		/// then the default beside the project. Never inside Assets.
		/// </summary>
		private static string ResolveOutputDirectory(out string refusal)
		{
			refusal = null;
			string chosen = null;
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; ++i)
			{
				if (args[i] == OutputArgument)
				{
					chosen = args[i + 1];
				}
			}
			if (string.IsNullOrWhiteSpace(chosen))
			{
				chosen = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
			}
			string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			if (string.IsNullOrWhiteSpace(chosen))
			{
				chosen = Path.Combine(project, "..", "PanelRenders", "NetworkStats");
			}

			string full = Path.GetFullPath(chosen);
			string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if ((full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(assets, StringComparison.Ordinal))
			{
				refusal = $"refusing to write renders inside Assets ({full}); Unity would import them";
				return null;
			}
			return full;
		}

		private static void Pump()
		{
			try
			{
				if (pending != null)
				{
					++framesWaited;
					if (framesWaited < SettleFrames)
					{
						pending.Panel?.Document?.rootVisualElement?.MarkDirtyRepaint();
						return;
					}
					Write(pending);
					Teardown(pending);
					pending = null;
					return;
				}

				if (queue == null || queue.Count == 0)
				{
					Finish(null);
					return;
				}

				pending = Mount(queue.Dequeue());
				framesWaited = 0;
			}
			catch (Exception ex)
			{
				problems.Add($"pump: {ex}");
				if (pending != null)
				{
					Teardown(pending);
					pending = null;
				}
				Finish(null);
			}
		}

		private static Mounted Mount(Capture capture)
		{
			VisualTreeAsset panelTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PanelUxmlPath);
			if (panelTree == null)
			{
				throw new InvalidOperationException($"{PanelUxmlPath} did not load");
			}

			RenderTexture texture = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32)
			{
				name = "NetworkStatsRender_" + capture.Name,
			};
			texture.Create();

			// A clone: pointing the shared asset at a texture would redirect every live panel.
			PanelSettings settings = Object.Instantiate(panelSettingsSource);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.clearColor = true;
			settings.colorClearValue = Ground;

			GameObject host = new GameObject(UITKNetworkStats.PanelName) { hideFlags = HideFlags.HideAndDontSave };
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = panelTree;
			document.enabled = false;

			UITKNetworkStats panel = host.AddComponent<UITKNetworkStats>();
			panel.Document = document;
			panel.StartOpen = false;
			panel.CloseOnQuitToMenu = false;

			/* The real lifecycle: Awake registers and hides, Show enables the document, which
			 * clones the tree and runs OnStarting against it. Awake never fires in edit mode on
			 * its own. */
			Invoke(panel, "Awake");
			panel.Show();
			if (!panel.Visible)
			{
				throw new InvalidOperationException("the overlay did not come up; its setting should have been on");
			}

			foreach (TransportTrafficSnapshot snapshot in Traffic(capture.Scenario))
			{
				panel.Ingest(in snapshot);
			}
			panel.Refresh();

			GameObject tooltipHost = null;
			if (capture.Tooltip.HasValue)
			{
				tooltipHost = MountTooltip(settings, panel, capture.Tooltip.Value);
			}

			return new Mounted
			{
				Capture = capture,
				Host = host,
				Panel = panel,
				TooltipHost = tooltipHost,
				Settings = settings,
				Texture = texture,
			};
		}

		/// <summary>
		/// Draws one row's tooltip with the shipped tooltip markup and renderer, placed to the left
		/// of the overlay where a pointer on that row would open it.
		/// </summary>
		private static GameObject MountTooltip(PanelSettings settings, UITKNetworkStats panel, NetworkStatsRow row)
		{
			VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TooltipUxmlPath);
			if (tree == null)
			{
				throw new InvalidOperationException($"{TooltipUxmlPath} did not load");
			}

			GameObject host = new GameObject("NetworkStatsRender_Tooltip") { hideFlags = HideFlags.HideAndDontSave };
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = tree;
			document.sortingOrder = (float)UITKPanelLayer.Tooltip;

			VisualElement root = document.rootVisualElement;
			VisualElement box = root?.Q<VisualElement>("tooltip-box");
			VisualElement content = root?.Q<VisualElement>("tooltip-content");
			if (box == null || content == null)
			{
				throw new InvalidOperationException("the tooltip markup has no tooltip-box or tooltip-content");
			}

			TooltipContent tooltip = new TooltipContent();
			NetworkStatsReadout readout = panel.CurrentReadout;
			NetworkStatsPresentation.BuildTooltip(tooltip, row, in readout);
			UITKTooltipView.Render(content, tooltip);

			// Bottom-anchored beside the overlay's right-hand corner: 8 of margin, 280 of overlay.
			box.style.position = Position.Absolute;
			box.style.right = 8 + 280 + 10;
			box.style.bottom = 40;
			box.style.left = StyleKeyword.Auto;
			box.style.top = StyleKeyword.Auto;
			return host;
		}

		private static void Write(Mounted mounted)
		{
			Texture2D shot = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = mounted.Texture;
			shot.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
			shot.Apply();
			RenderTexture.active = previous;

			string full = Path.Combine(outputDirectory, mounted.Capture.Name + ".png");
			File.WriteAllBytes(full, shot.EncodeToPNG());
			written.Add(full);

			/* The crop is the bounding box of everything that is not ground, rather than the
			 * panel's worldBound turned into pixels: that would depend on which way up this
			 * graphics device's render textures are, and the drawn pixels do not. */
			if (TryFindContent(shot, out RectInt content))
			{
				Texture2D crop = new Texture2D(content.width, content.height, TextureFormat.RGBA32, false);
				crop.SetPixels(shot.GetPixels(content.x, content.y, content.width, content.height));
				crop.Apply();
				string cropped = Path.Combine(outputDirectory, mounted.Capture.Name + "-crop.png");
				File.WriteAllBytes(cropped, crop.EncodeToPNG());
				written.Add(cropped);
				Object.DestroyImmediate(crop);
			}
			else
			{
				problems.Add($"{mounted.Capture.Name}: rendered nothing but the ground colour");
			}

			ReportGeometry(mounted);
			Object.DestroyImmediate(shot);
		}

		/// <summary>Logs where the overlay laid out and whether its rows fit, for reading alongside the images.</summary>
		private static void ReportGeometry(Mounted mounted)
		{
			VisualElement root = mounted.Panel.Document.rootVisualElement;
			VisualElement box = root.Q<VisualElement>("netstats-panel");
			if (box == null)
			{
				problems.Add($"{mounted.Capture.Name}: netstats-panel missing");
				return;
			}
			Rect panelRect = box.worldBound;
			Log($"{mounted.Capture.Name}: overlay {panelRect.x:0}x{panelRect.y:0} {panelRect.width:0}x{panelRect.height:0} (panel units)");

			/* A badge or value pushed past the box's right edge is clipped silently, so every
			 * label is checked against the box it lives in. */
			root.Query<Label>().ForEach(label =>
			{
				Rect r = label.worldBound;
				if (r.width <= 0 || float.IsNaN(r.width) || string.IsNullOrEmpty(label.text))
				{
					return;
				}
				if (r.xMax > panelRect.xMax + 0.5f || r.yMax > panelRect.yMax + 0.5f)
				{
					problems.Add($"{mounted.Capture.Name}: '{label.name}' ({label.text}) spills outside the overlay");
				}
			});
		}

		private static bool TryFindContent(Texture2D shot, out RectInt rect)
		{
			Color32[] pixels = shot.GetPixels32();

			/* The ground as it came out of the texture, not as it was asked for: a linear-space
			 * project stores the clear colour converted, so comparing against Ground itself would
			 * count every pixel as content. The left-hand corner is always ground; the overlay and
			 * its tooltip sit on the right. */
			Color32 ground = pixels[0];
			int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
			for (int y = 0; y < CaptureHeight; ++y)
			{
				int row = y * CaptureWidth;
				for (int x = 0; x < CaptureWidth; ++x)
				{
					Color32 p = pixels[row + x];
					if (Math.Abs(p.r - ground.r) > 3 || Math.Abs(p.g - ground.g) > 3 || Math.Abs(p.b - ground.b) > 3)
					{
						if (x < minX) minX = x;
						if (x > maxX) maxX = x;
						if (y < minY) minY = y;
						if (y > maxY) maxY = y;
					}
				}
			}
			if (maxX < 0)
			{
				rect = default;
				return false;
			}
			minX = Math.Max(0, minX - CropPadding);
			minY = Math.Max(0, minY - CropPadding);
			maxX = Math.Min(CaptureWidth - 1, maxX + CropPadding);
			maxY = Math.Min(CaptureHeight - 1, maxY + CropPadding);
			rect = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
			return true;
		}

		private static void Teardown(Mounted mounted)
		{
			if (mounted.Panel != null)
			{
				// Unregisters from UIManager and the settings event, as a real destroy would.
				Invoke(mounted.Panel, "OnDestroy");
			}
			if (mounted.TooltipHost != null) Object.DestroyImmediate(mounted.TooltipHost);
			if (mounted.Host != null) Object.DestroyImmediate(mounted.Host);
			if (mounted.Settings != null) Object.DestroyImmediate(mounted.Settings);
			if (mounted.Texture != null)
			{
				mounted.Texture.Release();
				Object.DestroyImmediate(mounted.Texture);
			}
		}

		private static void Invoke(UITKControl control, string method)
		{
			MethodInfo info = typeof(UITKControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
			if (info == null)
			{
				throw new MissingMethodException(nameof(UITKControl), method);
			}
			info.Invoke(control, null);
		}

		private static void Finish(string failure)
		{
			EditorApplication.update -= Pump;
			if (failure != null)
			{
				problems.Add(failure);
			}

			/* Put the developer's store back exactly, including "there was none": SetGlobalSettings
			 * refuses null, so the field is reached directly. Only if it was swapped: a run that
			 * failed before the swap must not null out a live store. */
			if (configurationSwapped)
			{
				FieldInfo field = typeof(Configuration).GetField("globalSettings", BindingFlags.NonPublic | BindingFlags.Static);
				if (field != null)
				{
					field.SetValue(null, previousConfiguration);
				}
				else if (previousConfiguration != null)
				{
					Configuration.SetGlobalSettings(previousConfiguration);
				}
				previousConfiguration = null;
				configurationSwapped = false;
			}

			foreach (string path in written)
			{
				Log("wrote " + path);
			}
			if (problems.Count == 0)
			{
				Log($"RESULT: OK, {written.Count} images");
			}
			else
			{
				foreach (string problem in problems)
				{
					Log("PROBLEM " + problem);
				}
				Log($"RESULT: {problems.Count} problem(s), {written.Count} images");
			}

			if (Application.isBatchMode)
			{
				EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
			}
		}

		private static void Log(string message)
		{
			Debug.Log("[NetworkStatsRender] " + message);
			Console.WriteLine("[NetworkStatsRender] " + message);
		}

		// ── Fabricated traffic ──────────────────────────────────────────────

		/// <summary>Seventy seconds of one client's snapshots, oldest first.</summary>
		private static IEnumerable<TransportTrafficSnapshot> Traffic(Scenario scenario)
		{
			switch (scenario)
			{
				case Scenario.DesktopBusy: return DesktopBusy();
				case Scenario.WebGlChrome: return Browser(firefox: false);
				case Scenario.WebGlFirefox: return Browser(firefox: true);
				default: return DesktopNotConnected();
			}
		}

		/// <summary>
		/// Application traffic of a player in a crowded town about twenty-five minutes into a
		/// session: server-to-client state dominates, with a zone-change burst of spawn messages
		/// forty seconds in. Deterministic, so a re-render is comparable with the last one.
		/// </summary>
		private struct AppTraffic
		{
			public long RecvReliableBytes, RecvReliableMessages, RecvUnreliableBytes, RecvUnreliableMessages;
			public long SentReliableBytes, SentReliableMessages, SentUnreliableBytes, SentUnreliableMessages;
			public long FramingRecv, FramingSent;

			public void Advance(int second, double scale)
			{
				double wave = Math.Sin(second / 6.5);
				bool burst = second >= 40 && second < 44;

				long recvUnreliableMsgs = (long)((58 + 9 * wave) * scale);
				long recvUnreliable = (long)((31000 + 7000 * wave) * scale);
				long recvReliableMsgs = (long)((22 + 4 * wave + (burst ? 160 : 0)) * scale);
				long recvReliable = (long)((9500 + 2200 * wave + (burst ? 74000 : 0)) * scale);

				long sentUnreliableMsgs = (long)(31 * scale);
				long sentUnreliable = (long)((5600 + 500 * wave) * scale);
				long sentReliableMsgs = (long)((5 + (burst ? 6 : 0)) * scale);
				long sentReliable = (long)((900 + (burst ? 1400 : 0)) * scale);

				RecvUnreliableMessages += recvUnreliableMsgs;
				RecvUnreliableBytes += recvUnreliable;
				RecvReliableMessages += recvReliableMsgs;
				RecvReliableBytes += recvReliable;
				SentUnreliableMessages += sentUnreliableMsgs;
				SentUnreliableBytes += sentUnreliable;
				SentReliableMessages += sentReliableMsgs;
				SentReliableBytes += sentReliable;

				// The length prefix is 1 byte below 64 and 2 below 16384: most reliable messages here take 2.
				FramingRecv += recvReliableMsgs * 2;
				FramingSent += sentReliableMsgs * 2;
			}

			public void WriteTo(ref TransportTrafficSnapshot s)
			{
				s.AppRecvReliableBytes = RecvReliableBytes;
				s.AppRecvReliableMessages = RecvReliableMessages;
				s.AppRecvUnreliableBytes = RecvUnreliableBytes;
				s.AppRecvUnreliableMessages = RecvUnreliableMessages;
				s.AppSentReliableBytes = SentReliableBytes;
				s.AppSentReliableMessages = SentReliableMessages;
				s.AppSentUnreliableBytes = SentUnreliableBytes;
				s.AppSentUnreliableMessages = SentUnreliableMessages;
				s.FramingRecvBytes = FramingRecv;
				s.FramingSentBytes = FramingSent;
			}

			/// <summary>A session already twenty-five minutes old, so the totals read like one.</summary>
			public static AppTraffic Warm(double scale)
			{
				AppTraffic t = default;
				for (int second = -1500; second < 0; ++second)
				{
					t.Advance(second, scale * 0.8);
				}
				return t;
			}
		}

		private static IEnumerable<TransportTrafficSnapshot> DesktopBusy()
		{
			AppTraffic app = AppTraffic.Warm(1.0);
			long udpRecv, udpSent, dgramRecv, dgramSent;
			long sentPackets = 96000, lostPackets = 205;
			double time = 5000.0;

			/* The QUIC layer of the same traffic as msquic would count it: one datagram per
			 * unreliable message, reliable data packed into full packets, about 34 bytes of short
			 * header, frame header and AEAD tag per datagram, and acknowledgements riding on data
			 * (or alone, every other packet received). Three handshakes' worth up front for the
			 * login, world and scene connections. */
			udpRecv = (long)(app.RecvReliableBytes + app.RecvUnreliableBytes + app.FramingRecv) + 3 * 3100;
			udpSent = (long)(app.SentReliableBytes + app.SentUnreliableBytes + app.FramingSent) + 3 * 1400;
			dgramRecv = app.RecvUnreliableMessages + app.RecvReliableMessages / 2 + 3 * 3;
			dgramSent = app.SentUnreliableMessages + app.SentReliableMessages + dgramRecv / 3 + 3 * 3;
			udpRecv += dgramRecv * 34;
			udpSent += dgramSent * 34 + (dgramRecv / 2) * 6;

			for (int second = 0; second < 70; ++second)
			{
				AppTraffic before = app;
				app.Advance(second, 1.0);

				long recvApp = (app.RecvReliableBytes - before.RecvReliableBytes) + (app.RecvUnreliableBytes - before.RecvUnreliableBytes) + (app.FramingRecv - before.FramingRecv);
				long sentApp = (app.SentReliableBytes - before.SentReliableBytes) + (app.SentUnreliableBytes - before.SentUnreliableBytes) + (app.FramingSent - before.FramingSent);
				long recvDgrams = (app.RecvUnreliableMessages - before.RecvUnreliableMessages) + Math.Max(1, (app.RecvReliableBytes - before.RecvReliableBytes) / 1200);
				long sentDgrams = (app.SentUnreliableMessages - before.SentUnreliableMessages) + (app.SentReliableMessages - before.SentReliableMessages) + recvDgrams / 4;

				dgramRecv += recvDgrams;
				dgramSent += sentDgrams;
				udpRecv += recvApp + recvDgrams * 34;
				udpSent += sentApp + sentDgrams * 34 + (recvDgrams / 2) * 6;
				sentPackets += sentDgrams;
				if (second == 41 || second == 42)
				{
					lostPackets += 3;
				}

				time += 1.0 + (second % 3) * 0.004;

				TransportTrafficSnapshot s = default;
				s.TimestampSeconds = time;
				s.Backend = TransportTrafficBackend.Native;
				app.WriteTo(ref s);
				s.QuicMeasure = TrafficMeasure.Measured;
				s.QuicRecvBytes = udpRecv;
				s.QuicSentBytes = udpSent;
				s.DatagramMeasure = TrafficMeasure.Measured;
				s.UdpRecvDatagrams = dgramRecv;
				s.UdpSentDatagrams = dgramSent;
				s.SessionsOpened = 3;
				s.SessionsActive = 1;
				s.Process = TransportProcessCounters.Unknown;
				s.Connection = new TransportConnectionStats
				{
					IsValid = true,
					Measure = TrafficMeasure.Measured,
					SampledAtSeconds = time - 0.4,
					RttMs = 38.4 + 3.0 * Math.Sin(second / 4.0),
					MinRttMs = 31.2,
					MaxRttMs = 112.0,
					RttVarianceMs = 4.1,
					PathMtu = 1472,
					CongestionWindowBytes = 61440,
					CongestionEvents = 2,
					SentPackets = sentPackets,
					RecvPackets = dgramRecv,
					SentBytes = udpSent,
					RecvBytes = udpRecv,
					LostPackets = lostPackets,
					SpuriousLostPackets = 11,
					RecvDroppedPackets = 40,
					RecvReorderedPackets = 18,
				};
				yield return s;
			}
		}

		private static IEnumerable<TransportTrafficSnapshot> Browser(bool firefox)
		{
			// A quieter scene than the desktop capture: a browser player in a small group.
			AppTraffic app = AppTraffic.Warm(0.55);
			double time = 800.0;
			double[] values = new double[TransportTrafficMath.BrowserStats.Count];

			for (int second = 0; second < 70; ++second)
			{
				app.Advance(second, 0.55);
				time += 1.0 + (second % 2) * 0.01;

				TransportTrafficSnapshot s = default;
				s.TimestampSeconds = time;
				s.Backend = TransportTrafficBackend.Browser;
				app.WriteTo(ref s);
				s.SessionsOpened = 3;
				s.SessionsActive = 1;

				int mask = 0;
				if (firefox)
				{
					/* Firefox's getStats(): wire bytes in bytesSent (no overhead field), packets
					 * and the RTT figures, summed over the page's sessions. Shaped as the same
					 * traffic plus QUIC framing, so the browser-reported figure sits where a
					 * measured one would. */
					TransportTrafficMath.EstimateBrowserQuic(in s, out long sentBytes, out long recvBytes, out long sentPackets, out long recvPackets);
					values[TransportTrafficMath.BrowserStats.BytesSent] = sentBytes * 1.03;
					values[TransportTrafficMath.BrowserStats.BytesReceived] = recvBytes * 1.02;
					values[TransportTrafficMath.BrowserStats.PacketsSent] = sentPackets;
					values[TransportTrafficMath.BrowserStats.PacketsReceived] = recvPackets;
					values[TransportTrafficMath.BrowserStats.SmoothedRttMs] = 44.0 + 4.0 * Math.Sin(second / 5.0);
					values[TransportTrafficMath.BrowserStats.MinRttMs] = 36.5;
					mask = (1 << TransportTrafficMath.BrowserStats.BytesSent)
						| (1 << TransportTrafficMath.BrowserStats.BytesReceived)
						| (1 << TransportTrafficMath.BrowserStats.PacketsSent)
						| (1 << TransportTrafficMath.BrowserStats.PacketsReceived)
						| (1 << TransportTrafficMath.BrowserStats.SmoothedRttMs)
						| (1 << TransportTrafficMath.BrowserStats.MinRttMs)
						| TransportTrafficMath.BrowserStats.ApiPresentBit
						| TransportTrafficMath.BrowserStats.LiveSampleBit;
				}

				// The transport's own mapping: estimated where the browser gave nothing (Chrome).
				TransportTrafficMath.ApplyBrowserStats(ref s, firefox ? values : null, mask, time);
				yield return s;
			}
		}

		/// <summary>
		/// A desktop client at the login screen before its first connection: the native library
		/// has not started, so everything below the game's own (still empty) messages is unknown.
		/// </summary>
		private static IEnumerable<TransportTrafficSnapshot> DesktopNotConnected()
		{
			double time = 12.0;
			for (int second = 0; second < 4; ++second)
			{
				time += 1.0;
				TransportTrafficSnapshot s = default;
				s.TimestampSeconds = time;
				s.Backend = TransportTrafficBackend.Native;
				s.QuicMeasure = TrafficMeasure.Unavailable;
				s.QuicRecvBytes = -1;
				s.QuicSentBytes = -1;
				s.DatagramMeasure = TrafficMeasure.Unavailable;
				s.UdpRecvDatagrams = -1;
				s.UdpSentDatagrams = -1;
				s.Process = TransportProcessCounters.Unknown;
				s.Connection = TransportConnectionStats.Unknown;
				yield return s;
			}
		}
	}
}
