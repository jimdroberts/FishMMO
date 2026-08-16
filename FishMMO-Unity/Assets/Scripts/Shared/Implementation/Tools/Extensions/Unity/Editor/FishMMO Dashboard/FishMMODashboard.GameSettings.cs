#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Partial class handling the Game Settings inspector panel for the FishMMO Dashboard.
	/// Provides editable Host Configuration, Certificate Pins, Client Secret, and a
	/// read-only Constants view — all in one unified location.
	/// </summary>
	public partial class FishMMODashboard
	{
		// ══════════════════════════════════════════════════════════════════
		//  CONSTANTS
		// ══════════════════════════════════════════════════════════════════

		private const string sentinelMarker = "FISHMMO_SENTINEL_PLACEHOLDER";
		private const int sslTimeoutMs = 10000;

		// ══════════════════════════════════════════════════════════════════
		//  HOST CONFIGURATION STATE
		// ══════════════════════════════════════════════════════════════════

		private string gs_apiHost = "";
		private string gs_gameHost = "";
		private string gs_playHost = "";
		private string gs_rootDomain = "";
		private Label gs_hostStatusLabel;

		// ══════════════════════════════════════════════════════════════════
		//  CERTIFICATE PINS STATE
		// ══════════════════════════════════════════════════════════════════

		private bool gs_pinApiHostEnabled = true;
		// Game hosts (loginserver/world/scene) often only expose UDP/QUIC — not TCP:443 —
		// so pin fetch to Game Host fails by default. Prefer API / custom HTTPS hosts.
		private bool gs_pinGameHostEnabled = false;
		private string gs_pinCustomHost = "";
		private readonly List<string> gs_discoveredPins = new List<string>();
		private readonly List<string> gs_pinErrors = new List<string>();
		private Label gs_pinStatusLabel;
		private VisualElement gs_pinListContainer;
		private VisualElement gs_pinErrorContainer;
		private Button gs_writePinsButton;
		private Button gs_fetchPinsButton;

		/// <summary>
		/// True while a pin fetch is running on a background thread. Guards against a second
		/// fetch being started on top of the first. Instance state, so a domain reload during a
		/// fetch clears it along with the rebuilt panel.
		/// </summary>
		private bool gs_pinFetchInProgress;

		// ══════════════════════════════════════════════════════════════════
		//  CLIENT SECRET STATE
		// ══════════════════════════════════════════════════════════════════

		private string gs_secretInput = "";
		private Label gs_secretStatusLabel;

		// ══════════════════════════════════════════════════════════════════
		//  DRAFT STATE (survives panel rebuild + domain reload)
		// ══════════════════════════════════════════════════════════════════

		// Unsaved typed/fetched values used to be wiped because ShowGameSettingsInspector
		// always reloaded from disk (sentinels → empty fields), and AssetDatabase.Refresh
		// after a Write triggers a domain reload that resets every instance field.
		//
		// Drafts live in SessionState, NOT EditorPrefs:
		//   • SessionState is in-memory and dies with the Editor process, so the client
		//     gate secret never reaches a plaintext file on disk (EditorPrefs writes to
		//     ~/.local/share/unity3d/prefs, %APPDATA% registry, or a macOS plist — all
		//     world-readable, all shared by every Unity project on the machine).
		//   • It still survives assembly reloads, which is the whole requirement here.
		//
		// Each section owns its own dirty flag so writing one section never discards
		// another section's unsaved work.
		private const string DraftHostsDirtyKey = "FishMMODashboard.GameSettings.HostsDirty";
		private const string DraftApiHostKey = "FishMMODashboard.GameSettings.ApiHost";
		private const string DraftGameHostKey = "FishMMODashboard.GameSettings.GameHost";
		private const string DraftPlayHostKey = "FishMMODashboard.GameSettings.PlayHost";
		private const string DraftRootDomainKey = "FishMMODashboard.GameSettings.RootDomain";

		private const string DraftPinsDirtyKey = "FishMMODashboard.GameSettings.PinsDirty";
		private const string DraftPinsKey = "FishMMODashboard.GameSettings.Pins";

		// Pin fetch inputs are UI-only (they are never written to a generated file), so
		// they are kept for the session independently of the fetched-pin draft.
		private const string DraftPinInputsSetKey = "FishMMODashboard.GameSettings.PinInputsSet";
		private const string DraftPinApiEnabledKey = "FishMMODashboard.GameSettings.PinApiEnabled";
		private const string DraftPinGameEnabledKey = "FishMMODashboard.GameSettings.PinGameEnabled";
		private const string DraftPinCustomHostKey = "FishMMODashboard.GameSettings.PinCustomHost";

		private const string DraftSecretDirtyKey = "FishMMODashboard.GameSettings.SecretDirty";
		private const string DraftSecretKey = "FishMMODashboard.GameSettings.Secret";

		// ══════════════════════════════════════════════════════════════════
		//  ENTRY POINT
		// ══════════════════════════════════════════════════════════════════

		/// <summary>
		/// Shows the Game Settings panel with Host Configuration, Certificate Pins,
		/// Client Secret, and Constants sections.
		/// </summary>
		/// <param name="reloadFromDisk">
		/// True (the Reload from File buttons only) discards every unsaved draft and
		/// loads the generated files. False keeps unsaved drafts so that reopening the
		/// panel — or a script recompile — does not wipe typed or fetched values.
		/// </param>
		private void ShowGameSettingsInspector(bool reloadFromDisk = false)
		{
			ClearInspector();

			if (inspectorHeader != null)
			{
				inspectorHeader.text = "Game Settings";
			}

			LoadGameSettingsState(reloadFromDisk);

			BuildHostConfigSection();
			BuildCertificatePinsSection();
			BuildClientSecretSection();
			BuildConstantsSection();

			SetStatus(reloadFromDisk ? "Game Settings (reloaded from disk)" : "Game Settings");
		}

		/// <summary>
		/// Loads UI state from the generated files, preferring each section's unsaved
		/// session draft when one exists.
		/// </summary>
		/// <param name="forceFromFile">
		/// True discards all drafts and reads the generated files (explicit Reload).
		/// </param>
		private void LoadGameSettingsState(bool forceFromFile = false)
		{
			if (forceFromFile)
				ClearAllDrafts();

			// Order matters: the pin host toggles derive from the host config values.
			LoadHostConfigState(forceFromFile);
			LoadCertificatePinState(forceFromFile);
			LoadClientSecretState(forceFromFile);
		}

		private void LoadHostConfigState(bool forceFromFile)
		{
			if (!forceFromFile && SessionState.GetBool(DraftHostsDirtyKey, false))
			{
				gs_apiHost = SessionState.GetString(DraftApiHostKey, gs_apiHost ?? "");
				gs_gameHost = SessionState.GetString(DraftGameHostKey, gs_gameHost ?? "");
				gs_playHost = SessionState.GetString(DraftPlayHostKey, gs_playHost ?? "");
				gs_rootDomain = SessionState.GetString(DraftRootDomainKey, gs_rootDomain ?? "");
				return;
			}

			string hostPath = GetHostConfigFilePath();
			if (!File.Exists(hostPath))
				return;

			try
			{
				string content = File.ReadAllText(hostPath);
				string api = ParseConstantFromFile(content, "ApiHost");
				string game = ParseConstantFromFile(content, "GameHost");
				string play = ParseConstantFromFile(content, "PlayHost");
				string root = ParseConstantFromFile(content, "RootDomain");

				gs_apiHost = IsSentinelOrEmpty(api) ? "" : api;
				gs_gameHost = IsSentinelOrEmpty(game) ? "" : game;
				gs_playHost = IsSentinelOrEmpty(play) ? "" : play;
				gs_rootDomain = IsSentinelOrEmpty(root) ? "" : root;
			}
			catch { /* leave fields blank on error */ }
		}

		private void LoadCertificatePinState(bool forceFromFile)
		{
			// Fetch errors are transient — they always belong to the last fetch attempt.
			gs_pinErrors.Clear();

			LoadPinFetchInputs(forceFromFile);

			gs_discoveredPins.Clear();

			if (!forceFromFile && SessionState.GetBool(DraftPinsDirtyKey, false))
			{
				string pinsBlob = SessionState.GetString(DraftPinsKey, "");
				if (!string.IsNullOrEmpty(pinsBlob))
				{
					foreach (string line in pinsBlob.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
					{
						string pin = line.Trim();
						if (!string.IsNullOrEmpty(pin) && !pin.Contains(sentinelMarker))
							gs_discoveredPins.Add(pin);
					}
				}
				return;
			}

			string pinPath = GetCertificatePinsFilePath();
			if (!File.Exists(pinPath))
				return;

			try
			{
				string content = File.ReadAllText(pinPath);
				var parsed = ParsePinsFromFile(content);
				if (parsed != null)
					gs_discoveredPins.AddRange(parsed);
			}
			catch { /* leave empty on error */ }
		}

		/// <summary>
		/// Restores the pin fetch inputs (host toggles + custom host) for this session,
		/// or derives them from the host configuration the first time around.
		/// </summary>
		private void LoadPinFetchInputs(bool forceFromFile)
		{
			if (!forceFromFile && SessionState.GetBool(DraftPinInputsSetKey, false))
			{
				gs_pinApiHostEnabled = SessionState.GetBool(DraftPinApiEnabledKey, gs_pinApiHostEnabled);
				gs_pinGameHostEnabled = SessionState.GetBool(DraftPinGameEnabledKey, gs_pinGameHostEnabled);
				gs_pinCustomHost = SessionState.GetString(DraftPinCustomHostKey, gs_pinCustomHost ?? "");
				return;
			}

			try
			{
				string apiUrl = string.IsNullOrEmpty(gs_apiHost)
					? Constants.Configuration.APIHost : gs_apiHost;
				string gameHost = string.IsNullOrEmpty(gs_gameHost)
					? Constants.Configuration.GameHost : gs_gameHost;

				gs_pinApiHostEnabled = !IsSentinelOrEmpty(apiUrl);
				// Do not auto-enable Game Host for pin fetch: many deploys have no HTTPS on that name.
				// Operators can still tick Game Host manually if TCP:443 is available.
				if (IsSentinelOrEmpty(gameHost))
					gs_pinGameHostEnabled = false;
			}
			catch { /* use defaults */ }
		}

		private void LoadClientSecretState(bool forceFromFile)
		{
			if (!forceFromFile && SessionState.GetBool(DraftSecretDirtyKey, false))
			{
				gs_secretInput = SessionState.GetString(DraftSecretKey, gs_secretInput ?? "");
				return;
			}

			string secretPath = GetClientSecretFilePath();
			if (!File.Exists(secretPath))
				return;

			try
			{
				string content = File.ReadAllText(secretPath);
				string parsed = ParseConstantFromFile(content, "Secret");
				if (!string.IsNullOrEmpty(parsed) && !parsed.Contains(sentinelMarker))
					gs_secretInput = parsed;
				else
					gs_secretInput = "";
			}
			catch { gs_secretInput = ""; }
		}

		// ══════════════════════════════════════════════════════════════════
		//  DRAFT PERSISTENCE
		// ══════════════════════════════════════════════════════════════════

		private void SaveHostDraft()
		{
			SessionState.SetBool(DraftHostsDirtyKey, true);
			SessionState.SetString(DraftApiHostKey, gs_apiHost ?? "");
			SessionState.SetString(DraftGameHostKey, gs_gameHost ?? "");
			SessionState.SetString(DraftPlayHostKey, gs_playHost ?? "");
			SessionState.SetString(DraftRootDomainKey, gs_rootDomain ?? "");
		}

		private static void ClearHostDraft()
		{
			SessionState.EraseBool(DraftHostsDirtyKey);
			SessionState.EraseString(DraftApiHostKey);
			SessionState.EraseString(DraftGameHostKey);
			SessionState.EraseString(DraftPlayHostKey);
			SessionState.EraseString(DraftRootDomainKey);
		}

		private void SavePinDraft()
		{
			SessionState.SetBool(DraftPinsDirtyKey, true);
			SessionState.SetString(DraftPinsKey, string.Join("\n", gs_discoveredPins));
		}

		private static void ClearPinDraft()
		{
			SessionState.EraseBool(DraftPinsDirtyKey);
			SessionState.EraseString(DraftPinsKey);
		}

		private void SavePinFetchInputs()
		{
			SessionState.SetBool(DraftPinInputsSetKey, true);
			SessionState.SetBool(DraftPinApiEnabledKey, gs_pinApiHostEnabled);
			SessionState.SetBool(DraftPinGameEnabledKey, gs_pinGameHostEnabled);
			SessionState.SetString(DraftPinCustomHostKey, gs_pinCustomHost ?? "");
		}

		private static void ClearPinFetchInputs()
		{
			SessionState.EraseBool(DraftPinInputsSetKey);
			SessionState.EraseBool(DraftPinApiEnabledKey);
			SessionState.EraseBool(DraftPinGameEnabledKey);
			SessionState.EraseString(DraftPinCustomHostKey);
		}

		/// <summary>
		/// Holds the typed secret for this Editor session only. Never written to disk —
		/// the secret's only durable home is ClientApiSecret.generated.cs.
		/// </summary>
		private void SaveSecretDraft()
		{
			SessionState.SetBool(DraftSecretDirtyKey, true);
			SessionState.SetString(DraftSecretKey, gs_secretInput ?? "");
		}

		private static void ClearSecretDraft()
		{
			SessionState.EraseBool(DraftSecretDirtyKey);
			SessionState.EraseString(DraftSecretKey);
		}

		private static void ClearAllDrafts()
		{
			ClearHostDraft();
			ClearPinDraft();
			ClearPinFetchInputs();
			ClearSecretDraft();
		}

		/// <summary>
		/// Adds the "unsaved draft" hint shown above a section's button row.
		/// </summary>
		private static void AddDraftHint(VisualElement section, string message)
		{
			Label hint = new Label(message);
			hint.style.fontSize = 10;
			hint.style.color = new Color(0.95f, 0.75f, 0.35f, 1f);
			hint.style.whiteSpace = WhiteSpace.Normal;
			hint.style.marginTop = 6;
			section.Add(hint);
		}

		// ══════════════════════════════════════════════════════════════════
		//  HOST CONFIGURATION SECTION
		// ══════════════════════════════════════════════════════════════════

		private void BuildHostConfigSection()
		{
			VisualElement section = CreateConstantsSection("Host Configuration");

			// Info
			Label info = new Label(
				"These values are IL-embedded at compile time via HostConfig.generated.cs.\n" +
				"CI can override them via env vars: FISHMMO_API_HOST, FISHMMO_GAME_HOST,\n" +
				"FISHMMO_PLAY_HOST, FISHMMO_ROOT_DOMAIN.");
			info.style.fontSize = 10;
			info.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			info.style.whiteSpace = WhiteSpace.Normal;
			info.style.marginBottom = 8;
			section.Add(info);

			// API Host
			TextField apiField = new TextField("API Host (e.g. https://api.fishmmo.com/)");
			apiField.value = gs_apiHost;
			apiField.RegisterValueChangedCallback(evt =>
			{
				gs_apiHost = evt.newValue ?? "";
				SaveHostDraft();
			});
			apiField.style.marginBottom = 4;
			section.Add(apiField);

			// Game Host
			TextField gameField = new TextField("Game Host (e.g. game.fishmmo.com)");
			gameField.value = gs_gameHost;
			gameField.RegisterValueChangedCallback(evt =>
			{
				gs_gameHost = evt.newValue ?? "";
				SaveHostDraft();
			});
			gameField.style.marginBottom = 4;
			section.Add(gameField);

			// Play Host
			TextField playField = new TextField("Play Host — WebGL client (e.g. play.fishmmo.com)");
			playField.value = gs_playHost;
			playField.RegisterValueChangedCallback(evt =>
			{
				gs_playHost = evt.newValue ?? "";
				SaveHostDraft();
			});
			playField.style.marginBottom = 4;
			section.Add(playField);

			// Root Domain
			TextField rootField = new TextField("Root Domain (e.g. fishmmo.com)");
			rootField.value = gs_rootDomain;
			rootField.RegisterValueChangedCallback(evt =>
			{
				gs_rootDomain = evt.newValue ?? "";
				SaveHostDraft();
			});
			rootField.style.marginBottom = 8;
			section.Add(rootField);

			// Derived preview
			Label derivedLabel = new Label("Derived Values");
			derivedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
			derivedLabel.style.fontSize = 11;
			derivedLabel.style.marginBottom = 2;
			section.Add(derivedLabel);

			string smtpPreview = IsSentinelOrEmpty(gs_rootDomain)
				? "noreply@<root-domain>" : "noreply@" + gs_rootDomain.Trim();
			string launcherPreview = IsSentinelOrEmpty(gs_rootDomain)
				? "https://www.<root-domain>/docs/introduction.html"
				: "https://www." + gs_rootDomain.Trim() + "/docs/introduction.html";

			AddConstantRow(section, "SMTP From Address", smtpPreview);
			AddConstantRow(section, "Launcher HTML URL", launcherPreview);

			// Status
			gs_hostStatusLabel = new Label("");
			gs_hostStatusLabel.style.marginTop = 6;
			gs_hostStatusLabel.style.fontSize = 11;
			gs_hostStatusLabel.style.whiteSpace = WhiteSpace.Normal;
			section.Add(gs_hostStatusLabel);

			if (SessionState.GetBool(DraftHostsDirtyKey, false))
			{
				AddDraftHint(section,
					"Unsaved host draft — kept across panel rebuilds and script recompiles " +
					"for this Editor session. Write Host Config to persist it.");
			}

			// Buttons
			VisualElement buttonRow = new VisualElement();
			buttonRow.style.flexDirection = FlexDirection.Row;
			buttonRow.style.marginTop = 8;

			Button writeButton = new Button(() => WriteHostConfig());
			writeButton.text = "Write Host Config";
			writeButton.style.height = 28;
			writeButton.style.marginRight = 4;
			writeButton.style.backgroundColor = new Color(0.24f, 0.43f, 0.24f, 1f);
			writeButton.style.color = new Color(0.75f, 0.95f, 0.75f, 1f);
			writeButton.style.borderTopLeftRadius = 4;
			writeButton.style.borderTopRightRadius = 4;
			writeButton.style.borderBottomLeftRadius = 4;
			writeButton.style.borderBottomRightRadius = 4;
			buttonRow.Add(writeButton);

			Button loadButton = new Button(() => ShowGameSettingsInspector(reloadFromDisk: true));
			loadButton.text = "Reload from File";
			loadButton.tooltip = "Discards every unsaved draft (hosts, pins, secret) and reloads " +
				"the generated files from disk.";
			loadButton.style.height = 28;
			loadButton.style.borderTopLeftRadius = 4;
			loadButton.style.borderTopRightRadius = 4;
			loadButton.style.borderBottomLeftRadius = 4;
			loadButton.style.borderBottomRightRadius = 4;
			buttonRow.Add(loadButton);

			section.Add(buttonRow);
			inspectorContent.Add(section);
		}

		private void WriteHostConfig()
		{
			try
			{
				var errors = new List<string>();
				if (IsSentinelOrEmpty(gs_apiHost))
					errors.Add("API Host is required.");
				if (IsSentinelOrEmpty(gs_gameHost))
					errors.Add("Game Host is required.");
				if (IsSentinelOrEmpty(gs_playHost))
					errors.Add("Play Host is required.");
				if (IsSentinelOrEmpty(gs_rootDomain))
					errors.Add("Root Domain is required.");

				if (errors.Count > 0)
				{
					gs_hostStatusLabel.text = string.Join("\n", errors);
					gs_hostStatusLabel.style.color = new Color(0.95f, 0.4f, 0.4f, 1f);
					return;
				}

				string apiHost = gs_apiHost.Trim();
				string gameHost = gs_gameHost.Trim();
				string playHost = gs_playHost.Trim();
				string rootDomain = gs_rootDomain.Trim();
				string smtpFrom = "noreply@" + rootDomain;
				string launcherUrl = "https://www." + rootDomain + "/docs/introduction.html";

				string path = GetHostConfigFilePath();
				string dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var sb = new StringBuilder();
				sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
				sb.AppendLine("// AUTO-GENERATED — written by FishMMO Dashboard > Game Settings.");
				sb.AppendLine("// Do NOT edit manually.  Use the FishMMO-Installer or set env vars in CI.");
				sb.AppendLine("// Generated at: " + DateTime.UtcNow.ToString("O"));
				sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
				sb.AppendLine("//");
				sb.AppendLine("// Sentinel check: the build validator (ClientSecurityBuildValidator) blocks");
				sb.AppendLine("// non-development builds that contain the FISHMMO_SENTINEL_PLACEHOLDER marker.");
				sb.AppendLine("// CI must replace every occurrence before invoking Unity.");
				sb.AppendLine("//");
				sb.AppendLine("// CI env vars:");
				sb.AppendLine("//   FISHMMO_API_HOST   — API gateway URL  (e.g. \"https://api.fishmmo.com\")");
				sb.AppendLine("//   FISHMMO_GAME_HOST  — Game server host  (e.g. \"game.fishmmo.com\")");
				sb.AppendLine("//   FISHMMO_PLAY_HOST  — WebGL client host (e.g. \"play.fishmmo.com\")");
				sb.AppendLine("//   FISHMMO_ROOT_DOMAIN — Root domain for certs/smtp (e.g. \"fishmmo.com\")");
				sb.AppendLine();
				sb.AppendLine("namespace FishMMO.Shared");
				sb.AppendLine("{");
				sb.AppendLine("\t/// <summary>");
				sb.AppendLine("\t/// IL-embedded host configuration. The real values are substituted at");
				sb.AppendLine("\t/// build time by CI or the FishMMO-Installer. The committed sentinel");
				sb.AppendLine("\t/// values are intentionally invalid.");
				sb.AppendLine("\t/// </summary>");
				sb.AppendLine("\tpublic static class GeneratedHostConfig");
				sb.AppendLine("\t{");
				sb.AppendLine("\t\tpublic const string SentinelMarker = \"" + sentinelMarker + "\";");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>API gateway URL with trailing slash (e.g. \"https://api.fishmmo.com/\").</summary>");
				sb.AppendLine("\t\tpublic const string ApiHost =");
				sb.AppendLine("\t\t\t\"" + apiHost + "\";");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>Game server hostname for WebTransport/QUIC connections.</summary>");
				sb.AppendLine("\t\tpublic const string GameHost =");
				sb.AppendLine("\t\t\t\"" + gameHost + "\";");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>WebGL client / player-facing hostname.</summary>");
				sb.AppendLine("\t\tpublic const string PlayHost =");
				sb.AppendLine("\t\t\t\"" + playHost + "\";");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>Root domain (no subdomain) for TLS certs and email.</summary>");
				sb.AppendLine("\t\tpublic const string RootDomain =");
				sb.AppendLine("\t\t\t\"" + rootDomain + "\";");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>SMTP from address for verification emails.</summary>");
				sb.AppendLine("\t\tpublic const string SmtpFromAddress =");
				sb.AppendLine("\t\t\t\"" + smtpFrom + "\";");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>SMTP from display name.</summary>");
				sb.AppendLine("\t\tpublic const string SmtpFromName = \"FishMMO\";");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>Launcher HTML/news page URL.</summary>");
				sb.AppendLine("\t\tpublic const string LauncherHtmlUrl =");
				sb.AppendLine("\t\t\t\"" + launcherUrl + "\";");
				sb.AppendLine("\t}");
				sb.AppendLine("}");

				File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
				// The file is now the source of truth for these fields; drop only the host
				// draft so any unwritten pin/secret work survives the Refresh domain reload.
				ClearHostDraft();
				AssetDatabase.Refresh();

				gs_hostStatusLabel.text = "Wrote host configuration to HostConfig.generated.cs";
				gs_hostStatusLabel.style.color = new Color(0.4f, 0.85f, 0.4f, 1f);
				SetStatus("Host config written.");
			}
			catch (Exception ex)
			{
				gs_hostStatusLabel.text = "Failed to write: " + ex.Message;
				gs_hostStatusLabel.style.color = new Color(0.95f, 0.4f, 0.4f, 1f);
			}
		}

		// ══════════════════════════════════════════════════════════════════
		//  CERTIFICATE PINS SECTION
		// ══════════════════════════════════════════════════════════════════

		private void BuildCertificatePinsSection()
		{
			VisualElement section = CreateConstantsSection("Certificate Pins");

			// Info
			Label info = new Label(
				"Fetch TLS certificate SPKI SHA-256 pins from hosts that speak HTTPS on port 443.\n" +
				"Game/QUIC-only hostnames (loginserver/world/scene) usually fail — use API/IPFetch/play.\n" +
				"After Fetch succeeds, click Write Pins to File (saves CertificatePins.generated.cs).\n" +
				"Prefer at least two pins (active + backup) for release builds.");
			info.style.fontSize = 10;
			info.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			info.style.whiteSpace = WhiteSpace.Normal;
			info.style.marginBottom = 8;
			section.Add(info);

			// Host toggles
			string apiHost = string.IsNullOrEmpty(gs_apiHost)
				? Constants.Configuration.APIHost : gs_apiHost;
			string gameHost = string.IsNullOrEmpty(gs_gameHost)
				? Constants.Configuration.GameHost : gs_gameHost;

			Toggle apiToggle = new Toggle("API Host:  " + ExtractHost(apiHost));
			apiToggle.value = gs_pinApiHostEnabled;
			apiToggle.RegisterValueChangedCallback(evt =>
			{
				gs_pinApiHostEnabled = evt.newValue;
				SavePinFetchInputs();
			});
			apiToggle.style.marginBottom = 2;
			section.Add(apiToggle);

			Toggle gameToggle = new Toggle("Game Host: " + gameHost);
			gameToggle.value = gs_pinGameHostEnabled;
			gameToggle.RegisterValueChangedCallback(evt =>
			{
				gs_pinGameHostEnabled = evt.newValue;
				SavePinFetchInputs();
			});
			gameToggle.style.marginBottom = 4;
			section.Add(gameToggle);

			TextField customField = new TextField("Custom Host:");
			customField.value = gs_pinCustomHost;
			customField.RegisterValueChangedCallback(evt =>
			{
				gs_pinCustomHost = evt.newValue ?? "";
				SavePinFetchInputs();
			});
			customField.style.marginBottom = 8;
			section.Add(customField);

			// Existing pins display
			gs_pinListContainer = new VisualElement();
			gs_pinListContainer.style.marginBottom = 8;
			section.Add(gs_pinListContainer);
			RefreshPinListDisplay();

			// Status
			gs_pinStatusLabel = new Label(
				"Step 1: Fetch Pins (HTTPS :443 hosts only). Step 2: Write Pins to File.");
			gs_pinStatusLabel.style.marginTop = 4;
			gs_pinStatusLabel.style.fontSize = 11;
			gs_pinStatusLabel.style.whiteSpace = WhiteSpace.Normal;
			gs_pinStatusLabel.style.color = new Color(0.65f, 0.65f, 0.65f, 1f);
			section.Add(gs_pinStatusLabel);

			// Errors (updated after each fetch)
			gs_pinErrorContainer = new VisualElement();
			gs_pinErrorContainer.style.marginTop = 4;
			section.Add(gs_pinErrorContainer);
			RefreshPinErrorDisplay();

			if (SessionState.GetBool(DraftPinsDirtyKey, false))
			{
				AddDraftHint(section,
					"Fetched pins are not written yet — kept for this Editor session only. " +
					"Click Write Pins to File to persist them.");
			}

			// Buttons — Write is always visible (was only created when pins already
			// existed at panel build time, so after Fetch the button never appeared).
			VisualElement buttonRow = new VisualElement();
			buttonRow.style.flexDirection = FlexDirection.Row;
			buttonRow.style.marginTop = 8;

			gs_fetchPinsButton = new Button(() => FetchCertificatePins());
			gs_fetchPinsButton.text = "Fetch Pins";
			gs_fetchPinsButton.style.height = 28;
			gs_fetchPinsButton.style.marginRight = 4;
			gs_fetchPinsButton.style.backgroundColor = new Color(0.25f, 0.35f, 0.55f, 1f);
			gs_fetchPinsButton.style.color = new Color(0.75f, 0.85f, 1f, 1f);
			gs_fetchPinsButton.style.borderTopLeftRadius = 4;
			gs_fetchPinsButton.style.borderTopRightRadius = 4;
			gs_fetchPinsButton.style.borderBottomLeftRadius = 4;
			gs_fetchPinsButton.style.borderBottomRightRadius = 4;
			gs_fetchPinsButton.SetEnabled(!gs_pinFetchInProgress);
			buttonRow.Add(gs_fetchPinsButton);

			gs_writePinsButton = new Button(() => WriteCertificatePins());
			gs_writePinsButton.text = "Write Pins to File";
			gs_writePinsButton.style.height = 28;
			gs_writePinsButton.style.marginRight = 4;
			gs_writePinsButton.style.backgroundColor = new Color(0.24f, 0.43f, 0.24f, 1f);
			gs_writePinsButton.style.color = new Color(0.75f, 0.95f, 0.75f, 1f);
			gs_writePinsButton.style.borderTopLeftRadius = 4;
			gs_writePinsButton.style.borderTopRightRadius = 4;
			gs_writePinsButton.style.borderBottomLeftRadius = 4;
			gs_writePinsButton.style.borderBottomRightRadius = 4;
			gs_writePinsButton.SetEnabled(gs_discoveredPins.Count > 0);
			gs_writePinsButton.tooltip = gs_discoveredPins.Count > 0
				? "Writes CertificatePins.generated.cs and refreshes assets."
				: "Fetch at least one pin first (or load existing non-sentinel pins).";
			buttonRow.Add(gs_writePinsButton);

			section.Add(buttonRow);
			inspectorContent.Add(section);
		}

		private void RefreshPinListDisplay()
		{
			if (gs_pinListContainer == null) return;
			gs_pinListContainer.Clear();

			if (gs_discoveredPins.Count == 0)
			{
				Label emptyLabel = new Label(
					"No pins loaded. Enable API Host and/or Custom Host (HTTPS :443), " +
					"click Fetch Pins, then Write Pins to File.");
				emptyLabel.style.fontSize = 10;
				emptyLabel.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
				emptyLabel.style.whiteSpace = WhiteSpace.Normal;
				gs_pinListContainer.Add(emptyLabel);
				UpdateWritePinsButtonState();
				return;
			}

			Label header = new Label($"Current Pins ({gs_discoveredPins.Count})");
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.fontSize = 11;
			header.style.marginBottom = 2;
			gs_pinListContainer.Add(header);

			foreach (var pin in gs_discoveredPins)
			{
				Label pinLabel = new Label(pin);
				pinLabel.style.fontSize = 10;
				pinLabel.style.marginLeft = 8;
				pinLabel.style.color = new Color(0.7f, 0.85f, 0.7f, 1f);
				pinLabel.AddToClassList("constants-value"); // monospace-ish
				gs_pinListContainer.Add(pinLabel);
			}

			UpdateWritePinsButtonState();
		}

		private void RefreshPinErrorDisplay()
		{
			if (gs_pinErrorContainer == null) return;
			gs_pinErrorContainer.Clear();

			if (gs_pinErrors.Count == 0) return;

			Label errHeader = new Label("Fetch errors:");
			errHeader.style.marginTop = 2;
			errHeader.style.fontSize = 11;
			errHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
			errHeader.style.color = new Color(0.95f, 0.6f, 0.3f, 1f);
			gs_pinErrorContainer.Add(errHeader);

			foreach (var err in gs_pinErrors)
			{
				Label errLabel = new Label(err);
				errLabel.style.fontSize = 10;
				errLabel.style.color = new Color(0.95f, 0.6f, 0.3f, 1f);
				errLabel.style.whiteSpace = WhiteSpace.Normal;
				errLabel.style.marginLeft = 8;
				gs_pinErrorContainer.Add(errLabel);
			}
		}

		private void UpdateWritePinsButtonState()
		{
			if (gs_writePinsButton == null) return;
			bool canWrite = gs_discoveredPins.Count > 0;
			gs_writePinsButton.SetEnabled(canWrite);
			gs_writePinsButton.tooltip = canWrite
				? "Writes CertificatePins.generated.cs and refreshes assets."
				: "Fetch at least one pin first (or load existing non-sentinel pins).";
		}

		/// <summary>
		/// Starts a certificate pin fetch. Each host involves a TCP connect plus a TLS handshake
		/// with a 10s timeout, so the work runs on a background thread and the result is applied
		/// from <see cref="EditorApplication.update"/>. Doing it inline would freeze the whole
		/// Editor UI for up to 10 seconds per host.
		/// </summary>
		private void FetchCertificatePins()
		{
			if (gs_pinFetchInProgress)
			{
				return;
			}

			gs_discoveredPins.Clear();
			gs_pinErrors.Clear();
			RefreshPinErrorDisplay();
			UpdateWritePinsButtonState();

			// Resolve hosts on the main thread — these read Unity-side configuration.
			string apiHost = string.IsNullOrEmpty(gs_apiHost)
				? Constants.Configuration.APIHost : gs_apiHost;
			string gameHost = string.IsNullOrEmpty(gs_gameHost)
				? Constants.Configuration.GameHost : gs_gameHost;

			var hostsToFetch = new List<string>();
			if (gs_pinApiHostEnabled && !string.IsNullOrWhiteSpace(apiHost))
				hostsToFetch.Add(ExtractHost(apiHost));
			if (gs_pinGameHostEnabled && !string.IsNullOrWhiteSpace(gameHost))
				hostsToFetch.Add(gameHost);
			if (!string.IsNullOrWhiteSpace(gs_pinCustomHost))
				hostsToFetch.Add(gs_pinCustomHost.Trim());

			if (hostsToFetch.Count == 0)
			{
				SetPinStatus("No hosts selected. Enable API Host and/or enter a Custom Host with HTTPS :443.",
					new Color(0.95f, 0.6f, 0.3f, 1f));
				RefreshPinListDisplay();
				RefreshPinErrorDisplay();
				return;
			}

			gs_pinFetchInProgress = true;
			SetPinFetchControlsEnabled(false);
			SetPinStatus($"Fetching HTTPS SPKI pins from {hostsToFetch.Count} host(s)…",
				new Color(0.7f, 0.7f, 0.3f, 1f));

			Task<PinFetchOutcome> fetchTask = Task.Run(() => FetchPinsOffThread(hostsToFetch));

			EditorApplication.CallbackFunction poll = null;
			poll = () =>
			{
				if (!fetchTask.IsCompleted)
				{
					return;
				}

				EditorApplication.update -= poll;

				// The dashboard window may have been closed while the fetch was in flight;
				// Unity's overloaded == reports a destroyed object as null.
				if (this == null)
				{
					return;
				}

				gs_pinFetchInProgress = false;
				ApplyPinFetchOutcome(fetchTask, hostsToFetch.Count);
			};
			EditorApplication.update += poll;
		}

		/// <summary>
		/// Applies a completed fetch to the UI. Runs on the Editor main thread.
		/// </summary>
		private void ApplyPinFetchOutcome(Task<PinFetchOutcome> fetchTask, int hostCount)
		{
			gs_discoveredPins.Clear();
			gs_pinErrors.Clear();

			if (fetchTask.IsFaulted)
			{
				gs_pinErrors.Add("Pin fetch failed: " + fetchTask.Exception?.GetBaseException().Message);
			}
			else if (fetchTask.IsCompletedSuccessfully)
			{
				PinFetchOutcome outcome = fetchTask.Result;
				gs_discoveredPins.AddRange(outcome.Pins);
				gs_pinErrors.AddRange(outcome.Errors);
			}

			if (gs_discoveredPins.Count > 0)
			{
				string msg = $"Fetched {gs_discoveredPins.Count} unique pin(s) from {hostCount} host(s). " +
					"Click Write Pins to File to save CertificatePins.generated.cs.";
				if (gs_pinErrors.Count > 0)
					msg += $" ({gs_pinErrors.Count} host error(s) — see below.)";
				SetPinStatus(msg, new Color(0.4f, 0.85f, 0.4f, 1f));
				// Survive a panel rebuild / recompile before the user gets to Write.
				SavePinDraft();
			}
			else
			{
				SetPinStatus(
					"No pins fetched. Hosts need HTTPS on TCP :443 (game QUIC-only hosts will fail). " +
					"Use API Host / Custom Host (e.g. api.fishmmo.com, ipfetch.fishmmo.com).",
					new Color(0.95f, 0.4f, 0.4f, 1f));
			}

			SetPinFetchControlsEnabled(true);
			RefreshPinListDisplay();
			RefreshPinErrorDisplay();
			UpdateWritePinsButtonState();
		}

		/// <summary>
		/// Performs the blocking TLS handshakes. Runs on a thread-pool thread — it must not touch
		/// Unity APIs or any <c>gs_</c> UI state.
		/// </summary>
		private static PinFetchOutcome FetchPinsOffThread(List<string> hostsToFetch)
		{
			var pins = new List<string>();
			var errors = new List<string>();
			var seen = new HashSet<string>(StringComparer.Ordinal);

			foreach (var host in hostsToFetch)
			{
				string hostClean = host.Trim();
				if (string.IsNullOrEmpty(hostClean)) continue;

				try
				{
					string pin = FetchSpkiPin(hostClean, 443);
					if (!string.IsNullOrEmpty(pin) && seen.Add(pin))
						pins.Add(pin);
				}
				catch (Exception ex)
				{
					errors.Add(hostClean + ": " + ex.Message);
				}
			}

			return new PinFetchOutcome(pins, errors);
		}

		/// <summary>Result of an off-thread pin fetch.</summary>
		private readonly struct PinFetchOutcome
		{
			/// <summary>Unique SPKI pins discovered.</summary>
			public List<string> Pins { get; }

			/// <summary>Per-host failures.</summary>
			public List<string> Errors { get; }

			public PinFetchOutcome(List<string> pins, List<string> errors)
			{
				Pins = pins;
				Errors = errors;
			}
		}

		/// <summary>
		/// Disables the fetch controls while a fetch is running so a second one cannot be started
		/// on top of the first.
		/// </summary>
		private void SetPinFetchControlsEnabled(bool enabled)
		{
			gs_fetchPinsButton?.SetEnabled(enabled);
		}

		private void SetPinStatus(string message, Color color)
		{
			if (gs_pinStatusLabel != null)
			{
				gs_pinStatusLabel.text = message;
				gs_pinStatusLabel.style.color = color;
			}
		}

		private static string FetchSpkiPin(string host, int port)
		{
			using (var client = new TcpClient())
			{
				client.SendTimeout = sslTimeoutMs;
				client.ReceiveTimeout = sslTimeoutMs;

				var result = client.BeginConnect(host, port, null, null);
				if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(sslTimeoutMs)))
				{
					client.Close();
					throw new TimeoutException($"Connection to {host}:{port} timed out after {sslTimeoutMs}ms.");
				}
				client.EndConnect(result);

				byte[] certDer = null;

				using (var sslStream = new SslStream(
					client.GetStream(),
					false,
					(sender, certificate, chain, errors) =>
					{
						if (certificate != null)
							certDer = certificate.GetRawCertData();

						if (errors == SslPolicyErrors.None)
							return true;

						Debug.LogWarning(
							$"[FishMMODashboard] TLS validation warning for {host}: " +
							$"{errors}. The certificate chain could not be verified against " +
							$"the system trust store. If you are on an untrusted network, " +
							$"MITM interception is possible. Verify the SPKI " +
							$"fingerprint via an out-of-band channel before saving.");
						return true;
					},
					null))
				{
					sslStream.AuthenticateAsClient(host);
				}

				if (certDer == null || certDer.Length == 0)
					throw new InvalidOperationException("No certificate data received from " + host);

				return ComputeSpkiSha256Base64(certDer);
			}
		}


		private static string ComputeSpkiSha256Base64(byte[] certificateDer)
		{
			if (certificateDer == null || certificateDer.Length == 0)
				throw new ArgumentException("Certificate DER is empty.", nameof(certificateDer));

			var cert = new X509CertificateParser().ReadCertificate(certificateDer);
			var spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(cert.GetPublicKey());
			byte[] der = spki.GetDerEncoded();
			byte[] hash;
			using (var sha = System.Security.Cryptography.SHA256.Create())
				hash = sha.ComputeHash(der);
			return Convert.ToBase64String(hash);
		}
		private void WriteCertificatePins()
		{
			try
			{
				string path = GetCertificatePinsFilePath();
				string dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var sb = new StringBuilder();
				sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
				sb.AppendLine("// AUTO-GENERATED — written by FishMMO Dashboard > Game Settings.");
				sb.AppendLine("// Generated at: " + DateTime.UtcNow.ToString("O"));
				sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
				sb.AppendLine("//");
				sb.AppendLine("// Sentinel check: the build validator (ClientSecurityBuildValidator) blocks");
				sb.AppendLine("// non-development builds that contain the FISHMMO_SENTINEL_PLACEHOLDER marker.");
				sb.AppendLine();
				sb.AppendLine("namespace FishMMO.Client.Security");
				sb.AppendLine("{");
				sb.AppendLine("\t/// <summary>");
				sb.AppendLine("\t/// IL-embedded certificate pin set.");
				sb.AppendLine("\t/// </summary>");
				sb.AppendLine("\tpublic static class GeneratedPinSet");
				sb.AppendLine("\t{");
				sb.AppendLine("\t\tpublic const string SentinelMarker = \"FISHMMO_SENTINEL_PLACEHOLDER\";");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>");
				sb.AppendLine("\t\t/// SHA-256 SPKI pins (base64). Minimum 2 entries required for");
				sb.AppendLine("\t\t/// release builds.");
				sb.AppendLine("\t\t/// </summary>");
				sb.AppendLine("\t\tpublic static readonly string[] Pins =");
				sb.AppendLine("\t\t{");

				for (int i = 0; i < gs_discoveredPins.Count; i++)
				{
					string comma = i < gs_discoveredPins.Count - 1 ? "," : "";
					sb.AppendLine($"\t\t\t\"{gs_discoveredPins[i]}\"{comma}");
				}

				sb.AppendLine("\t\t};");
				sb.AppendLine();
				sb.AppendLine("\t\t/// <summary>");
				sb.AppendLine("\t\t/// Ed25519 public key (base64) for verifying signed pin update");
				sb.AppendLine("\t\t/// manifests from the API. Empty string disables runtime updates.");
				sb.AppendLine("\t\t/// </summary>");
				sb.AppendLine("\t\tpublic const string ManifestPublicKeyBase64 = \"\";");
				sb.AppendLine("\t}");
				sb.AppendLine("}");

				File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
				// Pins are on disk now; the fetch inputs stay so the panel comes back the
				// way the operator left it. Other sections keep their own drafts.
				ClearPinDraft();
				AssetDatabase.Refresh();

				SetPinStatus($"Wrote {gs_discoveredPins.Count} pin(s) to CertificatePins.generated.cs",
					new Color(0.4f, 0.85f, 0.4f, 1f));
				SetStatus("Certificate pins written.");
			}
			catch (Exception ex)
			{
				SetPinStatus("Failed to write: " + ex.Message,
					new Color(0.95f, 0.4f, 0.4f, 1f));
			}
		}

		// ══════════════════════════════════════════════════════════════════
		//  CLIENT SECRET SECTION
		// ══════════════════════════════════════════════════════════════════

		private void BuildClientSecretSection()
		{
			VisualElement section = CreateConstantsSection("Client Secret");

			// Info
			Label info = new Label(
				"Paste the client gate secret (base64) obtained from the FishMMO Installer\n" +
				"output or directly from the deployment_secrets database.\n" +
				"The secret must be valid base64 and decode to at least 32 bytes.");
			info.style.fontSize = 10;
			info.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			info.style.whiteSpace = WhiteSpace.Normal;
			info.style.marginBottom = 8;
			section.Add(info);

			// Secret input — masked on screen; the plaintext value never leaves this
			// session (SessionState) or ClientApiSecret.generated.cs.
			TextField secretField = new TextField("Gate Secret (base64)");
			secretField.isPasswordField = true;
			secretField.value = gs_secretInput;
			secretField.RegisterValueChangedCallback(evt =>
			{
				gs_secretInput = evt.newValue ?? "";
				SaveSecretDraft();
			});
			secretField.style.marginBottom = 4;
			section.Add(secretField);

			// Masked preview
			if (!string.IsNullOrEmpty(gs_secretInput))
			{
				string masked = MaskSecret(gs_secretInput);
				Label previewLabel = new Label("Preview: " + masked);
				previewLabel.style.fontSize = 10;
				previewLabel.style.color = new Color(0.6f, 0.6f, 0.6f, 1f);
				previewLabel.style.marginBottom = 4;
				section.Add(previewLabel);
			}

			// Status
			gs_secretStatusLabel = new Label("");
			gs_secretStatusLabel.style.marginTop = 4;
			gs_secretStatusLabel.style.fontSize = 11;
			gs_secretStatusLabel.style.whiteSpace = WhiteSpace.Normal;
			section.Add(gs_secretStatusLabel);

			if (SessionState.GetBool(DraftSecretDirtyKey, false))
			{
				AddDraftHint(section,
					"Unsaved secret — held in memory for this Editor session only and lost " +
					"when the Editor closes. Write Secret to File to persist it.");
			}

			// Buttons
			VisualElement buttonRow = new VisualElement();
			buttonRow.style.flexDirection = FlexDirection.Row;
			buttonRow.style.marginTop = 8;

			Button writeButton = new Button(() => WriteClientSecret());
			writeButton.text = "Write Secret to File";
			writeButton.style.height = 28;
			writeButton.style.marginRight = 4;
			writeButton.style.backgroundColor = new Color(0.24f, 0.43f, 0.24f, 1f);
			writeButton.style.color = new Color(0.75f, 0.95f, 0.75f, 1f);
			writeButton.style.borderTopLeftRadius = 4;
			writeButton.style.borderTopRightRadius = 4;
			writeButton.style.borderBottomLeftRadius = 4;
			writeButton.style.borderBottomRightRadius = 4;
			buttonRow.Add(writeButton);

			Button loadButton = new Button(() => ShowGameSettingsInspector(reloadFromDisk: true));
			loadButton.text = "Reload from File";
			loadButton.tooltip = "Discards every unsaved draft (hosts, pins, secret) and reloads " +
				"the generated files from disk.";
			loadButton.style.height = 28;
			loadButton.style.borderTopLeftRadius = 4;
			loadButton.style.borderTopRightRadius = 4;
			loadButton.style.borderBottomLeftRadius = 4;
			loadButton.style.borderBottomRightRadius = 4;
			buttonRow.Add(loadButton);

			section.Add(buttonRow);
			inspectorContent.Add(section);
		}

		private void WriteClientSecret()
		{
			try
			{
				if (!IsValidBase64Secret(gs_secretInput))
				{
					gs_secretStatusLabel.text = "Invalid secret. Must be valid base64 and decode to at least 32 bytes.";
					gs_secretStatusLabel.style.color = new Color(0.95f, 0.4f, 0.4f, 1f);
					return;
				}

				string secret = gs_secretInput.Trim();
				string path = GetClientSecretFilePath();
				string dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var sb = new StringBuilder();
				sb.AppendLine("// AUTO-GENERATED by FishMMO Dashboard > Game Settings.");
				sb.AppendLine("// Generated at: " + DateTime.UtcNow.ToString("O"));
				sb.AppendLine();
				sb.AppendLine("namespace FishMMO.Client.Security");
				sb.AppendLine("{");
				sb.AppendLine("\tpublic static class GeneratedClientSecret");
				sb.AppendLine("\t{");
				sb.AppendLine("\t\tpublic const string Secret = \"" + secret + "\";");
				sb.AppendLine("\t}");
				sb.AppendLine("}");

				File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
				// Drop the in-memory copy as soon as the file owns the value.
				ClearSecretDraft();
				AssetDatabase.Refresh();

				gs_secretStatusLabel.text = "Wrote secret to ClientApiSecret.generated.cs";
				gs_secretStatusLabel.style.color = new Color(0.4f, 0.85f, 0.4f, 1f);
				SetStatus("Client secret written.");
			}
			catch (Exception ex)
			{
				gs_secretStatusLabel.text = "Failed to write: " + ex.Message;
				gs_secretStatusLabel.style.color = new Color(0.95f, 0.4f, 0.4f, 1f);
			}
		}

		private static bool IsValidBase64Secret(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
				return false;

			try
			{
				byte[] decoded = Convert.FromBase64String(input.Trim());
				return decoded.Length >= 32;
			}
			catch
			{
				return false;
			}
		}

		private static string MaskSecret(string secret)
		{
			if (string.IsNullOrEmpty(secret))
				return "";

			if (secret.Length <= 8)
				return new string('*', secret.Length);

			return secret.Substring(0, 4) + new string('*', secret.Length - 8) + secret.Substring(secret.Length - 4);
		}

		// ══════════════════════════════════════════════════════════════════
		//  CONSTANTS SECTION
		// ══════════════════════════════════════════════════════════════════

		private void BuildConstantsSection()
		{
			Foldout foldout = new Foldout();
			foldout.text = "Constants (read-only)";
			foldout.value = false; // collapsed by default
			foldout.style.marginTop = 8;

			// ── Configuration ──
			VisualElement configSection = CreateConstantsSection("Configuration");
			AddConstantRow(configSection, "Project Name", Constants.Configuration.ProjectName);
			AddConstantRow(configSection, "Client Executable", Constants.Configuration.ClientExecutable);
			AddConstantRow(configSection, "Updater Executable", Constants.Configuration.UpdaterExecutable);
			AddConstantRow(configSection, "Setup Directory", Constants.Configuration.SetupDirectory);
			AddConstantRow(configSection, "API Host", Constants.Configuration.APIHost);
			AddConstantRow(configSection, "Game Host", Constants.Configuration.GameHost);
			AddConstantRow(configSection, "Scene Path", Constants.Configuration.ScenePath);
			AddConstantRow(configSection, "Bootstrap Scene Path", Constants.Configuration.BootstrapScenePath);
			AddConstantRow(configSection, "Client Bootstrap Scene Path", Constants.Configuration.ClientBootstrapScenePath);
			AddConstantRow(configSection, "Server Bootstrap Scene Path", Constants.Configuration.ServerBootstrapScenePath);
			AddConstantRow(configSection, "World Scene Path", Constants.Configuration.WorldScenePath);
			AddConstantRow(configSection, "Local Scene Path", Constants.Configuration.LocalScenePath);
			AddConstantRow(configSection, "Maximum Player Hotkeys", Constants.Configuration.MaximumPlayerHotkeys.ToString());
			foldout.Add(configSection);

			// ── Character ──
			VisualElement charSection = CreateConstantsSection("Character");
			AddConstantRow(charSection, "Walk Speed", Constants.Character.WalkSpeed.ToString("F1"));
			AddConstantRow(charSection, "Run Speed", Constants.Character.RunSpeed.ToString("F1"));
			AddConstantRow(charSection, "Sprint Speed", Constants.Character.SprintSpeed.ToString("F1"));
			AddConstantRow(charSection, "Sprint Stamina Cost", Constants.Character.SprintStaminaCost.ToString("F1"));
			AddConstantRow(charSection, "Crouch Speed", Constants.Character.CrouchSpeed.ToString("F1"));
			AddConstantRow(charSection, "Jump Up Speed", Constants.Character.JumpUpSpeed.ToString("F1"));
			AddConstantRow(charSection, "Jump Stamina Cost", Constants.Character.JumpStaminaCost.ToString("F1"));
			AddConstantRow(charSection, "Gravity", Constants.Character.Gravity.ToString());
			foldout.Add(charSection);

			// ── Layers ──
			VisualElement layerSection = CreateConstantsSection("Layers");
			AddConstantRow(layerSection, "Default", Constants.Layers.DefaultLayer.ToString());
			AddConstantRow(layerSection, "Ignore Raycast", Constants.Layers.IgnoreRaycast.ToString());
			AddConstantRow(layerSection, "Ground", Constants.Layers.Ground.ToString());
			AddConstantRow(layerSection, "Obstruction", Constants.Layers.Obstruction.ToString());
			AddConstantRow(layerSection, "Player", Constants.Layers.Player.ToString());
			foldout.Add(layerSection);

			// ── Misc ──
			VisualElement miscSection = CreateConstantsSection("Misc");
			AddConstantRow(miscSection, "Shared Static Label", Constants.SharedStaticLabel);
			foldout.Add(miscSection);

			// Footer note
			Label footerLabel = new Label(
				"These values are defined in Constants.cs and are read-only.\nEdit Constants.cs directly to change them.");
			footerLabel.style.marginTop = 8;
			footerLabel.style.fontSize = 10;
			footerLabel.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			footerLabel.style.whiteSpace = WhiteSpace.Normal;
			foldout.Add(footerLabel);

			inspectorContent.Add(foldout);
		}

		// ══════════════════════════════════════════════════════════════════
		//  SHARED UTILITY METHODS
		// ══════════════════════════════════════════════════════════════════

		private static bool IsSentinelOrEmpty(string value)
		{
			return string.IsNullOrWhiteSpace(value) || value.Contains(sentinelMarker);
		}

		/// <summary>
		/// Extracts a const string value from a generated C# file.
		/// Looks for <c>const string Name = "...";</c> (value may span multiple lines).
		/// </summary>
		private static string ParseConstantFromFile(string content, string constantName)
		{
			string search = "const string " + constantName + " =";
			int start = content.IndexOf(search, StringComparison.Ordinal);
			if (start < 0) return null;

			start = content.IndexOf('"', start + search.Length);
			if (start < 0) return null;

			int end = content.IndexOf("\";", start + 1, StringComparison.Ordinal);
			if (end < 0) return null;

			return content.Substring(start + 1, end - start - 1);
		}

		/// <summary>
		/// Extracts pin strings from a generated C# file's Pins array initializer.
		/// </summary>
		private static List<string> ParsePinsFromFile(string content)
		{
			var result = new List<string>();
			int start = content.IndexOf("Pins =", StringComparison.Ordinal);
			if (start < 0) return result;
			start = content.IndexOf('{', start);
			if (start < 0) return result;
			int end = content.IndexOf("};", start, StringComparison.Ordinal);
			if (end < 0) return result;

			string arrayContent = content.Substring(start + 1, end - start - 1);
			foreach (string line in arrayContent.Split('\n'))
			{
				string trimmed = line.Trim();
				if (trimmed.StartsWith("\"", StringComparison.Ordinal))
				{
					int firstQuote = trimmed.IndexOf('"');
					int lastQuote = trimmed.LastIndexOf('"');
					if (firstQuote >= 0 && lastQuote > firstQuote)
					{
						string pin = trimmed.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
						if (!string.IsNullOrWhiteSpace(pin) && !pin.Contains(sentinelMarker))
							result.Add(pin);
					}
				}
			}
			return result;
		}

		/// <summary>
		/// Extracts the hostname from a URL string.
		/// </summary>
		private static string ExtractHost(string url)
		{
			if (string.IsNullOrWhiteSpace(url)) return url;
			if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
				return uri.Host;
			return url.Trim();
		}

		// ── File paths ──────────────────────────────────────────────────

		private static string GetHostConfigFilePath()
		{
			return Path.Combine(Application.dataPath,
				"Scripts", "Shared", "Implementation", "HostConfig.generated.cs");
		}

		private static string GetCertificatePinsFilePath()
		{
			return Path.Combine(Application.dataPath,
				"Scripts", "Client", "Security", "CertificatePins.generated.cs");
		}

		private static string GetClientSecretFilePath()
		{
			return Path.Combine(Application.dataPath,
				"Scripts", "Client", "Security", "ClientApiSecret.generated.cs");
		}
	}
}
#endif
