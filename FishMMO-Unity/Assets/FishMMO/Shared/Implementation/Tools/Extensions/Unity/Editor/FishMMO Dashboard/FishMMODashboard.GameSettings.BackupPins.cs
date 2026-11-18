#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Multiplier;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Backup certificate pins for the Game Settings panel.
	/// </summary>
	/// <remarks>
	/// A backup pin is the SPKI hash of a spare private key that is not on the server. Release
	/// clients carry it from the start, so if the live key is lost or compromised the certificate
	/// can be reissued for the spare key and every shipped client keeps connecting. Fetch Pins can
	/// only see the key a server presents, so the backup is generated or read from a file here.
	/// Backup pins are written to CertificatePins.generated.cs with <see cref="BackupPinMarker"/>
	/// so they load back into this list; the client treats every pin the same.
	/// </remarks>
	public partial class FishMMODashboard
	{
		private const string BackupPinMarker = "// backup";
		private const string DraftBackupPinsDirtyKey = "FishMMODashboard.GameSettings.BackupPinsDirty";
		private const string DraftBackupPinsKey = "FishMMODashboard.GameSettings.BackupPins";

		private readonly List<string> gs_backupPins = new List<string>();
		private VisualElement gs_backupPinListContainer;
		private string gs_backupPinInput = "";

		private void SaveBackupPinDraft()
		{
			SessionState.SetBool(DraftBackupPinsDirtyKey, true);
			SessionState.SetString(DraftBackupPinsKey, string.Join("\n", gs_backupPins));
		}

		private static void ClearBackupPinDraft()
		{
			SessionState.EraseBool(DraftBackupPinsDirtyKey);
			SessionState.EraseString(DraftBackupPinsKey);
		}

		private void BuildBackupPinsSubsection(VisualElement section)
		{
			Label header = new Label("Backup Pins (offline keys)");
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.fontSize = 11;
			header.style.marginTop = 10;
			header.style.marginBottom = 2;
			section.Add(header);

			Label info = new Label(
				"The pin of a spare private key that is NOT on the server. If the live key is ever lost or " +
				"compromised, the certificate is reissued for the spare key and every shipped client keeps " +
				"connecting. Generate Backup Key creates one (EC P-256, like the live Let's Encrypt key) and saves " +
				"it where you choose, outside the project: keep that file offline (a password manager or USB " +
				"drive), never in the repository or on the server. A key or certificate made elsewhere " +
				"(e.g. openssl genpkey) can be added from its file, or its pin pasted.");
			info.style.fontSize = 10;
			info.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			info.style.whiteSpace = WhiteSpace.Normal;
			info.style.marginBottom = 4;
			section.Add(info);

			gs_backupPinListContainer = new VisualElement();
			gs_backupPinListContainer.style.marginBottom = 4;
			section.Add(gs_backupPinListContainer);
			RefreshBackupPinListDisplay();

			VisualElement pasteRow = new VisualElement();
			pasteRow.style.flexDirection = FlexDirection.Row;
			TextField pasteField = new TextField("Paste Pin:");
			pasteField.value = gs_backupPinInput;
			pasteField.style.flexGrow = 1;
			pasteField.RegisterValueChangedCallback(evt => gs_backupPinInput = evt.newValue ?? "");
			pasteRow.Add(pasteField);
			Button addButton = new Button(() =>
			{
				if (AddBackupPin(gs_backupPinInput, "pasted"))
				{
					gs_backupPinInput = "";
					pasteField.SetValueWithoutNotify("");
				}
			});
			addButton.text = "Add";
			pasteRow.Add(addButton);
			section.Add(pasteRow);

			VisualElement buttonRow = new VisualElement();
			buttonRow.style.flexDirection = FlexDirection.Row;
			buttonRow.style.marginTop = 4;
			Button generateButton = new Button(GenerateBackupKey);
			generateButton.text = "Generate Backup Key…";
			generateButton.tooltip = "Creates an EC P-256 private key, saves it outside the project, and adds its pin.";
			generateButton.style.height = 24;
			generateButton.style.marginRight = 4;
			buttonRow.Add(generateButton);
			Button fileButton = new Button(AddBackupPinFromFile);
			fileButton.text = "Add from Key/Certificate File…";
			fileButton.tooltip = "Reads a PEM private key, public key or certificate and adds its pin. Encrypted keys are not read.";
			fileButton.style.height = 24;
			buttonRow.Add(fileButton);
			section.Add(buttonRow);
		}

		private void RefreshBackupPinListDisplay()
		{
			if (gs_backupPinListContainer == null) return;
			gs_backupPinListContainer.Clear();

			if (gs_backupPins.Count == 0)
			{
				Label empty = new Label("No backup pin. Release builds need one (live + backup = 2).");
				empty.style.fontSize = 10;
				empty.style.color = new Color(0.95f, 0.75f, 0.3f, 1f);
				empty.style.whiteSpace = WhiteSpace.Normal;
				gs_backupPinListContainer.Add(empty);
				return;
			}

			foreach (string pin in gs_backupPins)
			{
				VisualElement row = new VisualElement();
				row.style.flexDirection = FlexDirection.Row;
				row.style.alignItems = Align.Center;
				Label pinLabel = new Label(pin);
				pinLabel.style.fontSize = 10;
				pinLabel.style.marginLeft = 8;
				pinLabel.style.flexGrow = 1;
				pinLabel.style.color = new Color(0.7f, 0.8f, 0.95f, 1f);
				pinLabel.AddToClassList("constants-value");
				row.Add(pinLabel);
				string captured = pin;
				Button remove = new Button(() =>
				{
					gs_backupPins.Remove(captured);
					SaveBackupPinDraft();
					RefreshBackupPinListDisplay();
					UpdateWritePinsButtonState();
				});
				remove.text = "Remove";
				row.Add(remove);
				gs_backupPinListContainer.Add(row);
			}
		}

		/// <summary>Validates and adds a backup pin. Returns true when it was added.</summary>
		private bool AddBackupPin(string pin, string source)
		{
			pin = (pin ?? "").Trim().Trim('"', ',');
			if (!IsSpkiPin(pin))
			{
				SetPinStatus("Not a pin: expected the base64 SHA-256 of a public key (44 characters ending in '=').",
					new Color(0.95f, 0.4f, 0.4f, 1f));
				return false;
			}
			if (gs_discoveredPins.Contains(pin))
			{
				SetPinStatus("That is the live key's pin. A backup must be a different key, kept off the server.",
					new Color(0.95f, 0.4f, 0.4f, 1f));
				return false;
			}
			if (gs_backupPins.Contains(pin))
			{
				SetPinStatus("That backup pin is already listed.", new Color(0.95f, 0.75f, 0.3f, 1f));
				return false;
			}
			gs_backupPins.Add(pin);
			SaveBackupPinDraft();
			RefreshBackupPinListDisplay();
			UpdateWritePinsButtonState();
			SetPinStatus($"Added the {source} backup pin. Click Write Pins to File to save it.",
				new Color(0.4f, 0.85f, 0.4f, 1f));
			return true;
		}

		private static bool IsSpkiPin(string pin)
		{
			if (string.IsNullOrEmpty(pin) || pin.Length != 44) return false;
			try
			{
				return Convert.FromBase64String(pin).Length == 32;
			}
			catch (FormatException)
			{
				return false;
			}
		}

		/// <summary>Reads the live pin from the server's certificate file.</summary>
		private void AddLivePinFromCertificateFile()
		{
			string start = Directory.Exists("/etc/fishmmo/certs") ? "/etc/fishmmo/certs" : HomeDirectory();
			string path = EditorUtility.OpenFilePanel("Server certificate (fullchain.pem)", start, "");
			if (string.IsNullOrEmpty(path)) return;
			try
			{
				AsymmetricKeyParameter key = ReadPublicKeyFromPem(File.ReadAllText(path), out bool isPrivateKey);
				if (isPrivateKey)
				{
					SetPinStatus("That is a private key. Choose the certificate (fullchain.pem) — a server's private key " +
						"should not be copied to the editor.", new Color(0.95f, 0.6f, 0.3f, 1f));
					return;
				}
				string pin = SpkiPin(key);
				if (gs_backupPins.Contains(pin))
				{
					SetPinStatus("That certificate uses the backup key; it is already listed as a backup pin.",
						new Color(0.95f, 0.75f, 0.3f, 1f));
					return;
				}
				if (!gs_discoveredPins.Contains(pin))
					gs_discoveredPins.Add(pin);
				SavePinDraft();
				RefreshPinListDisplay();
				SetPinStatus($"Live pin read from {Path.GetFileName(path)}. Click Write Pins to File to save it.",
					new Color(0.4f, 0.85f, 0.4f, 1f));
			}
			catch (Exception ex)
			{
				SetPinStatus("Could not read a certificate from that file: " + ex.Message, new Color(0.95f, 0.4f, 0.4f, 1f));
			}
		}

		private void AddBackupPinFromFile()
		{
			string path = EditorUtility.OpenFilePanel("Backup key or certificate (PEM)", HomeDirectory(), "");
			if (string.IsNullOrEmpty(path)) return;
			try
			{
				AddBackupPin(SpkiPin(ReadPublicKeyFromPem(File.ReadAllText(path), out _)), Path.GetFileName(path));
			}
			catch (Exception ex)
			{
				SetPinStatus("Could not read a key or certificate from that file: " + ex.Message +
					" (an encrypted key needs 'openssl pkey -in KEY -pubout' first).", new Color(0.95f, 0.4f, 0.4f, 1f));
			}
		}

		/// <summary>
		/// Generates an EC P-256 key, writes it as an unencrypted PKCS#8 PEM (the form certbot and
		/// openssl read) readable only by the current user, and adds its pin.
		/// </summary>
		private void GenerateBackupKey()
		{
			string path = EditorUtility.SaveFilePanel("Save the backup private key — keep it offline",
				HomeDirectory(), "fishmmo-backup-key.pem", "pem");
			if (string.IsNullOrEmpty(path)) return;
			path = Path.GetFullPath(path);

			// Never inside the repository: one careless commit would publish the key.
			string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
			if (IsInsideDirectory(path, repositoryRoot))
			{
				EditorUtility.DisplayDialog("Backup key",
					$"Save the key outside {repositoryRoot}: anything inside the repository can end up committed.", "OK");
				return;
			}
			if (File.Exists(path))
			{
				EditorUtility.DisplayDialog("Backup key",
					"That file already exists. A backup key is never overwritten; choose a new file name.", "OK");
				return;
			}

			try
			{
				var generator = new ECKeyPairGenerator();
				generator.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256r1, new SecureRandom()));
				AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();
				byte[] pkcs8 = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();

				// Create the file empty and private first, so the key never exists with the
				// umask's default (usually world-readable) permissions.
				using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
				RestrictToOwner(path);
				File.WriteAllText(path, ToPem("PRIVATE KEY", pkcs8), Encoding.ASCII);

				string pin = SpkiPin(pair.Public);
				AddBackupPin(pin, "generated");
				EditorUtility.DisplayDialog("Backup key saved",
					$"Saved {path}\n\nPin: {pin}\n\n" +
					"Move this file somewhere offline (password manager, USB drive) and delete it from this " +
					"machine afterwards. Do not put it on the server: a backup only helps if it was not exposed " +
					"with the live key.\n\nClick Write Pins to File to save the pin.", "OK");
			}
			catch (Exception ex)
			{
				SetPinStatus("Generating the backup key failed: " + ex.Message, new Color(0.95f, 0.4f, 0.4f, 1f));
			}
		}

		/// <summary>
		/// Returns the public key in a PEM certificate, public key or private key (PKCS#8 or
		/// SEC1/PKCS#1). <paramref name="isPrivateKey"/> reports which kind the file held.
		/// </summary>
		private static AsymmetricKeyParameter ReadPublicKeyFromPem(string pemText, out bool isPrivateKey)
		{
			isPrivateKey = false;
			object item;
			using (var reader = new StringReader(pemText))
				item = new PemReader(reader).ReadObject();
			switch (item)
			{
				case Org.BouncyCastle.X509.X509Certificate certificate:
					return certificate.GetPublicKey();
				case AsymmetricCipherKeyPair pair:
					isPrivateKey = true;
					return pair.Public;
				case ECPrivateKeyParameters ec:
					isPrivateKey = true;
					return new ECPublicKeyParameters(
						new FixedPointCombMultiplier().Multiply(ec.Parameters.G, ec.D).Normalize(), ec.Parameters);
				case RsaPrivateCrtKeyParameters rsa:
					isPrivateKey = true;
					return new RsaKeyParameters(false, rsa.Modulus, rsa.PublicExponent);
				case AsymmetricKeyParameter key when !key.IsPrivate:
					return key;
				case null:
					throw new InvalidDataException("no PEM block found");
				default:
					throw new InvalidDataException("unsupported PEM content (" + item.GetType().Name + ")");
			}
		}

		/// <summary>Base64 SHA-256 of the DER SubjectPublicKeyInfo — the pin the client checks.</summary>
		private static string SpkiPin(AsymmetricKeyParameter publicKey)
		{
			byte[] spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded();
			using (var sha = System.Security.Cryptography.SHA256.Create())
				return Convert.ToBase64String(sha.ComputeHash(spki));
		}

		private static string ToPem(string label, byte[] der)
		{
			string base64 = Convert.ToBase64String(der);
			var sb = new StringBuilder();
			sb.Append("-----BEGIN ").Append(label).Append("-----\n");
			for (int i = 0; i < base64.Length; i += 64)
				sb.Append(base64, i, Math.Min(64, base64.Length - i)).Append('\n');
			sb.Append("-----END ").Append(label).Append("-----\n");
			return sb.ToString();
		}

		private static void RestrictToOwner(string path)
		{
			if (Application.platform == RuntimePlatform.WindowsEditor)
				return; // The user profile is already private to its owner on Windows.
			// ArgumentList is not available on Unity's runtime, so quote the path by hand.
			var startInfo = new ProcessStartInfo("chmod",
				"600 \"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			using (Process chmod = Process.Start(startInfo))
			{
				chmod.WaitForExit(5000);
				if (!chmod.HasExited || chmod.ExitCode != 0)
					throw new IOException("could not restrict the key file's permissions (chmod 600 failed)");
			}
		}

		private static bool IsInsideDirectory(string path, string directory)
		{
			string root = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(path, directory, StringComparison.OrdinalIgnoreCase);
		}

		private static string HomeDirectory() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
	}
}
#endif
