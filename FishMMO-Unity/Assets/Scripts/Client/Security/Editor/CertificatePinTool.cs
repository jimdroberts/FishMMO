using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using FishMMO.Shared;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Client.Security.Editor
{
	/// <summary>
	/// Editor tool that downloads SSL certificates from configured hosts,
	/// computes SPKI SHA-256 pins via <see cref="ClientCertificatePinning.ComputeSpkiSha256Base64"/>,
	/// and writes them to <c>StreamingAssets/client-security.json</c>.
	///
	/// <para>Open via <b>FishMMO &gt; Security &gt; Fetch Certificate Pins</b>.</para>
	/// </summary>
	public sealed class CertificatePinTool : EditorWindow
	{
		private const string configFileName = "client-security.json";
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
			window.minSize = new Vector2(450, 380);
			window.Show();
		}

		private void OnEnable()
		{
			// Default hosts from Constants.  Fall back to well-known FQDNs if
			// Constants is not yet initialised (e.g. first domain reload).
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
				"result to StreamingAssets/" + configFileName + ".\n\n" +
				"Always configure at least two pins (active + backup key) so certificate " +
				"renewals don't require a client patch.",
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
				if (GUILayout.Button("Write to " + configFileName, GUILayout.Height(30)))
					WritePinsToFile();
			}
			EditorGUILayout.EndHorizontal();

			if (GUILayout.Button("Load Existing from " + configFileName, GUILayout.Height(20)))
				LoadExistingPins();

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

		/// <summary>
		/// Opens a TLS connection to <paramref name="host"/>:<paramref name="port"/>,
		/// captures the leaf certificate, and returns its SPKI SHA-256 pin.
		/// </summary>
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
						// Capture the leaf certificate DER bytes during validation.
						// We pin the SPKI, which survives key-reuse across renewals.
						if (certificate != null)
						{
							certDer = certificate.GetRawCertData();
						}

						if (errors == System.Net.Security.SslPolicyErrors.None)
						{
							return true;
						}

						// Log a clear warning so the operator can verify the
						// fingerprint via an out-of-band channel.  We still return
						// true (allowing the handshake to proceed) because staging /
						// self-signed certs are common during development, but the
						// operator must confirm the printed SPKI before saving pins.
						UnityEngine.Debug.LogWarning(
							$"[CertificatePinTool] TLS validation warning for {host}: " +
							$"{errors}. The certificate chain could not be verified against " +
							$"the system trust store. If you are on an untrusted network, " +
							$"man-in-the-middle interception is possible. Verify the SPKI " +
							$"fingerprint shown in the output window via an out-of-band " +
							$"channel (e.g. SSH to the server and run: " +
							$"openssl x509 -in /path/to/cert.pem -pubkey -noout | " +
							$"openssl pkey -pubin -outform DER | " +
							$"openssl dgst -sha256 -binary | base64) before saving.");
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

		private void WritePinsToFile()
		{
			try
			{
				EnsureStreamingAssetsExists();

				string path = GetConfigFilePath();
				var payload = new PinConfigPayload
				{
					pins = discoveredPins.ToArray(),
					allowOnEmpty = false
				};
				string json = JsonUtility.ToJson(payload, prettyPrint: true);
				File.WriteAllText(path, json);
				AssetDatabase.Refresh();

				statusMessage = $"Wrote {discoveredPins.Count} pin(s) to {configFileName}";
				statusType = MessageType.Info;
			}
			catch (Exception ex)
			{
				statusMessage = "Failed to write config: " + ex.Message;
				statusType = MessageType.Error;
			}
			Repaint();
		}

		private void LoadExistingPins()
		{
			discoveredPins.Clear();
			fetchErrors.Clear();

			string path = GetConfigFilePath();
			if (!File.Exists(path))
			{
				statusMessage = configFileName + " does not exist yet. Fetch pins from your hosts, then write the file.";
				statusType = MessageType.Info;
				return;
			}

			try
			{
				string json = File.ReadAllText(path);
				var parsed = JsonUtility.FromJson<PinConfigPayload>(json);
				if (parsed?.pins != null)
					discoveredPins.AddRange(parsed.pins);

				statusMessage = $"Loaded {discoveredPins.Count} existing pin(s).";
				statusType = MessageType.Info;
			}
			catch (Exception ex)
			{
				statusMessage = "Failed to parse " + configFileName + ": " + ex.Message;
				statusType = MessageType.Error;
			}
			Repaint();
		}

		// ── Helpers ───────────────────────────────────────────────────

		private static string GetConfigFilePath()
		{
			return Path.Combine(Application.streamingAssetsPath, configFileName);
		}

		private static void EnsureStreamingAssetsExists()
		{
			string dir = Application.streamingAssetsPath;
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);
		}

		/// <summary>
		/// Extracts the hostname from a URL (e.g. "https://api.fishmmo.com/" → "api.fishmmo.com").
		/// </summary>
		private static string ExtractHost(string url)
		{
			if (string.IsNullOrWhiteSpace(url)) return url;
			if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
				return uri.Host;
			return url.Trim();
		}

		[Serializable]
		private sealed class PinConfigPayload
		{
			public string[] pins;
			public bool allowOnEmpty;
		}
	}
}
