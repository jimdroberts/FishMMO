using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FishMMO.Shared;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Client.Security.Editor
{
	/// <summary>
	/// Editor tool that downloads SSL certificates from configured hosts,
	/// computes SPKI SHA-256 pins via <see cref="ClientCertificatePinning.ComputeSpkiSha256Base64"/>,
	/// and writes them to <c>Assets/Scripts/Client/Security/CertificatePins.generated.cs</c>.
	///
	/// <para>Open via <b>FishMMO &gt; Security &gt; Fetch Certificate Pins</b>.</para>
	/// </summary>
	public sealed class CertificatePinTool : EditorWindow
	{
		private const string generatedFileName = "CertificatePins.generated.cs";
		private const int sslTimeoutMs = 10000;

		private string apiHost;
		private string gameHost;
		private string customHost = "";
		private bool apiHostEnabled = true;
		private bool gameHostEnabled = true;

		private Vector2 scrollPosition;
		private string statusMessage = "";
		private MessageType statusType = MessageType.None;

		private readonly List<string> discoveredPins = new List<string>();
		private readonly List<string> fetchErrors = new List<string>();

		[MenuItem("FishMMO/Security/Fetch Certificate Pins", priority = 100)]
		public static void ShowWindow()
		{
			var window = GetWindow<CertificatePinTool>(true, "Certificate Pins", true);
			window.minSize = new Vector2(450, 480);
			window.Show();
		}

		private void OnEnable()
		{
			try { apiHost = ExtractHost(Constants.Configuration.APIHost); }
			catch { apiHost = "api.fishmmo.com"; }

			try { gameHost = Constants.Configuration.GameHost; }
			catch { gameHost = "game.fishmmo.com"; }

			LoadExistingPins();
		}

		private void OnGUI()
		{
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

			EditorGUILayout.LabelField("Certificate Pin Generator", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			EditorGUILayout.HelpBox(
				"Connect to each host via TLS, download the leaf certificate, " +
				"extract the SubjectPublicKeyInfo (SPKI) SHA-256 pin, and write the " +
				"result to Assets/Scripts/Client/Security/" + generatedFileName + ".\n\n" +
				"Always configure at least two pins (active + backup key) so certificate " +
				"renewals don't require a client patch.\n\n" +
				"Pins are IL-embedded at compile time — no StreamingAssets file.",
				MessageType.Info);

			EditorGUILayout.Space();

			// ── Host list ─────────────────────────────────────────────

			EditorGUILayout.LabelField("Hosts", EditorStyles.boldLabel);
			apiHostEnabled = EditorGUILayout.ToggleLeft("API Host:  " + (apiHost ?? "(unknown)"), apiHostEnabled);
			gameHostEnabled = EditorGUILayout.ToggleLeft("Game Host: " + (gameHost ?? "(unknown)"), gameHostEnabled);

			EditorGUILayout.BeginHorizontal();
			customHost = EditorGUILayout.TextField("Custom Host:", customHost);
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			// ── Existing pins ────────────────────────────────────────

			if (discoveredPins.Count > 0)
			{
				EditorGUILayout.LabelField("Current Pins (" + discoveredPins.Count + ")", EditorStyles.boldLabel);
				EditorGUI.indentLevel++;
				foreach (var pin in discoveredPins)
					EditorGUILayout.SelectableLabel(pin, EditorStyles.textField, GUILayout.Height(16));
				EditorGUI.indentLevel--;
				EditorGUILayout.Space();
			}

			// ── Buttons ───────────────────────────────────────────────

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Fetch Pins", GUILayout.Height(30)))
				FetchPins();

			if (discoveredPins.Count > 0)
			{
				if (GUILayout.Button("Write to " + generatedFileName, GUILayout.Height(30)))
					WritePinsToGeneratedFile();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Load Existing from " + generatedFileName, GUILayout.Height(20)))
				LoadExistingPins();

			if (GUILayout.Button("Validate Build Config", GUILayout.Height(20)))
				ValidateBuildConfig();
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			// ── Build-time embedding note ────────────────────────────

			EditorGUILayout.HelpBox(
				"Pins are written directly to CertificatePins.generated.cs and IL-embedded " +
				"at compile time. No CI environment variables or substitution scripts are needed.",
				MessageType.Info);

			EditorGUILayout.Space();

			// ── Status ────────────────────────────────────────────────

			if (!string.IsNullOrEmpty(statusMessage))
				EditorGUILayout.HelpBox(statusMessage, statusType);

			// ── Errors ────────────────────────────────────────────────

			if (fetchErrors.Count > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Errors", EditorStyles.boldLabel);
				foreach (var err in fetchErrors)
					EditorGUILayout.HelpBox(err, MessageType.Warning);
			}

			EditorGUILayout.EndScrollView();
		}

		// ── Fetch ─────────────────────────────────────────────────────

		private void FetchPins()
		{
			discoveredPins.Clear();
			fetchErrors.Clear();
			statusMessage = "Fetching...";
			statusType = MessageType.Info;
			Repaint();

			var hostsToFetch = new List<string>();
			if (apiHostEnabled && !string.IsNullOrWhiteSpace(apiHost))
				hostsToFetch.Add(apiHost);
			if (gameHostEnabled && !string.IsNullOrWhiteSpace(gameHost))
				hostsToFetch.Add(gameHost);
			if (!string.IsNullOrWhiteSpace(customHost))
				hostsToFetch.Add(customHost.Trim());

			if (hostsToFetch.Count == 0)
			{
				statusMessage = "No hosts selected.";
				statusType = MessageType.Warning;
				return;
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);

			foreach (var host in hostsToFetch)
			{
				string hostClean = host.Trim();
				if (string.IsNullOrEmpty(hostClean)) continue;

				try
				{
					string pin = FetchSpkiPin(hostClean, 443);
					if (!string.IsNullOrEmpty(pin) && seen.Add(pin))
						discoveredPins.Add(pin);
				}
				catch (Exception ex)
				{
					fetchErrors.Add(hostClean + ": " + ex.Message);
				}
			}

			if (discoveredPins.Count > 0)
			{
				statusMessage = $"Fetched {discoveredPins.Count} unique pin(s) from {hostsToFetch.Count} host(s).";
				statusType = MessageType.Info;
			}
			else
			{
				statusMessage = "No pins fetched. Check errors below.";
				statusType = MessageType.Error;
			}

			Repaint();
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
							$"[CertificatePinTool] TLS validation warning for {host}: " +
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

				return ClientCertificatePinning.ComputeSpkiSha256Base64(certDer);
			}
		}

		// ── File I/O ──────────────────────────────────────────────────

		private void WritePinsToGeneratedFile()
		{
			try
			{
				string path = GetGeneratedFilePath();
				string dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					Directory.CreateDirectory(dir);

				var sb = new StringBuilder();
				sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
				sb.AppendLine("// AUTO-GENERATED — CI substitutes real values before release builds.");
				sb.AppendLine("// Generated by FishMMO > Security > Fetch Certificate Pins");
				sb.AppendLine("// Generated at: " + DateTime.UtcNow.ToString("O"));
				sb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
				sb.AppendLine("//");
				sb.AppendLine("// Sentinel check: the build validator (ClientSecurityBuildValidator) blocks");
				sb.AppendLine("// non-development builds that contain the FISHMMO_SENTINEL_PLACEHOLDER marker.");
				sb.AppendLine("// CI must replace every occurrence before invoking Unity.");
				sb.AppendLine();
				sb.AppendLine("namespace FishMMO.Client.Security");
				sb.AppendLine("{");
				sb.AppendLine("\t/// <summary>");
				sb.AppendLine("\t/// IL-embedded certificate pin set. The real values are substituted at");
				sb.AppendLine("\t/// build time by CI. The committed sentinel values are intentionally");
				sb.AppendLine("\t/// invalid so pinning cannot accidentally ship with empty values.");
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

				for (int i = 0; i < discoveredPins.Count; i++)
				{
					string comma = i < discoveredPins.Count - 1 ? "," : "";
					sb.AppendLine($"\t\t\t\"{discoveredPins[i]}\"{comma}");
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
				AssetDatabase.Refresh();

				statusMessage = $"Wrote {discoveredPins.Count} pin(s) to {generatedFileName}";
				statusType = MessageType.Info;
			}
			catch (Exception ex)
			{
				statusMessage = "Failed to write generated file: " + ex.Message;
				statusType = MessageType.Error;
			}
			Repaint();
		}

		private void LoadExistingPins()
		{
			discoveredPins.Clear();
			fetchErrors.Clear();

			string path = GetGeneratedFilePath();
			if (!File.Exists(path))
			{
				statusMessage = generatedFileName + " does not exist yet. Fetch pins from your hosts, then write the file.";
				statusType = MessageType.Info;
				return;
			}

			try
			{
				// Parse pin strings from the generated C# file.
				string content = File.ReadAllText(path);
				var parsed = ParsePinsFromGeneratedFile(content);
				if (parsed != null)
					discoveredPins.AddRange(parsed);

				statusMessage = $"Loaded {discoveredPins.Count} existing pin(s).";
				statusType = MessageType.Info;
			}
			catch (Exception ex)
			{
				statusMessage = "Failed to parse " + generatedFileName + ": " + ex.Message;
				statusType = MessageType.Error;
			}
			Repaint();
		}

		/// <summary>
		/// Extracts pin strings from the generated C# file by looking for
		/// quoted strings inside the Pins array initializer.
		/// </summary>
		private static List<string> ParsePinsFromGeneratedFile(string content)
		{
			var result = new List<string>();
			// Find the Pins array: everything between "Pins =\n\t\t{" and the matching "};"
			int start = content.IndexOf("Pins =", StringComparison.Ordinal);
			if (start < 0) return result;
			start = content.IndexOf('{', start);
			if (start < 0) return result;
			int end = content.IndexOf("};", start, StringComparison.Ordinal);
			if (end < 0) return result;

			string arrayContent = content.Substring(start + 1, end - start - 1);
			// Extract quoted strings.
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
						if (!string.IsNullOrWhiteSpace(pin) && !pin.Contains(GeneratedPinSet.SentinelMarker))
							result.Add(pin);
					}
				}
			}
			return result;
		}

		// ── Build validation ──────────────────────────────────────────

		private void ValidateBuildConfig()
		{
			statusMessage = "";
			statusType = MessageType.None;
			fetchErrors.Clear();

			string path = GetGeneratedFilePath();
			if (!File.Exists(path))
			{
				fetchErrors.Add(generatedFileName + " does not exist. Fetch pins and write the file first.");
			}
			else
			{
				string content = File.ReadAllText(path);
				if (content.Contains(GeneratedPinSet.SentinelMarker))
					fetchErrors.Add("Sentinel placeholder values still present in " + generatedFileName +
						". Fetch real pins before making a release build.");

				var pins = ParsePinsFromGeneratedFile(content);
				if (pins.Count < 2)
					fetchErrors.Add("Less than 2 real pins configured. At least 2 (active + backup) are required for release builds.");
			}

			// Check no StreamingAssets pin file exists.
			string oldPath = Path.Combine(Application.streamingAssetsPath, "client-security.json");
			if (File.Exists(oldPath))
				fetchErrors.Add("Obsolete StreamingAssets/client-security.json still exists. Delete it — pins are now IL-embedded.");

			if (fetchErrors.Count == 0)
			{
				statusMessage = "Build config is valid for release.";
				statusType = MessageType.Info;
			}
			else
			{
				statusMessage = fetchErrors.Count + " issue(s) found.";
				statusType = MessageType.Error;
			}
			Repaint();
		}

		// ── Helpers ───────────────────────────────────────────────────

		private static string GetGeneratedFilePath()
		{
			// Resolve relative to the project Assets folder.
			return Path.Combine(Application.dataPath,
				"Scripts", "Client", "Security", generatedFileName);
		}

		private static string ExtractHost(string url)
		{
			if (string.IsNullOrWhiteSpace(url)) return url;
			if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
				return uri.Host;
			return url.Trim();
		}
	}
}
