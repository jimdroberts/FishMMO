using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FishMMO.Logging;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// The local, on-disk copy of the 2FA setup payload (otpauth URI + recovery codes) written
	/// during account creation so the player still has the codes if the client dies before they
	/// finish writing them down.
	///
	/// <para>The payload used to be a plaintext <c>.txt</c>. Randomising the filename and tightening
	/// the permissions — the previous pass — removed the account-name leak and the guessable path,
	/// but the contents were still readable by anything that could open the file: another local
	/// user on a machine where the chmod did not take, a backup agent, a sync client, a stolen
	/// disk. This class encrypts the payload under the account password; see
	/// <see cref="TwoFactorRecoveryCrypto"/> for why the password and not a machine-bound key.</para>
	///
	/// <para>Every method here is deliberately non-destructive on failure. The one thing this
	/// storage must never do is destroy a payload the player has not yet copied down.</para>
	/// </summary>
	public static class TwoFactorRecoveryStore
	{
		/// <summary>
		/// Subdirectory of the client's persistent data path holding recovery payloads. Its own
		/// directory so the directory permissions can be tightened as well as each file's.
		/// </summary>
		public const string DirectoryName = "2fa-recovery";

		/// <summary>Extension for an encrypted envelope.</summary>
		public const string EnvelopeExtension = ".2fa";

		/// <summary>Extension of the superseded plaintext payload, still recognised for migration.</summary>
		public const string LegacyExtension = ".txt";

		/// <summary>
		/// Creates the recovery directory if needed and tightens its permissions.
		/// </summary>
		/// <param name="persistentDataPath">The client's persistent data path.</param>
		/// <returns>The full path of the recovery directory.</returns>
		public static string EnsureDirectory(string persistentDataPath)
		{
			string directory = Path.Combine(persistentDataPath, DirectoryName);
			Directory.CreateDirectory(directory);
			TryRestrictPermissions(directory, "700");
			return directory;
		}

		/// <summary>
		/// Encrypts and stores a recovery payload.
		/// </summary>
		/// <param name="directory">The recovery directory, from <see cref="EnsureDirectory"/>.</param>
		/// <param name="password">The account password. Never written to disk and never logged.</param>
		/// <param name="payload">The text to protect.</param>
		/// <param name="path">Receives the path of the stored envelope on success.</param>
		/// <returns><c>true</c> if an envelope is on disk that has been proven readable.</returns>
		/// <remarks>
		/// The write is <b>verified before it is published</b>: the envelope goes to a temporary
		/// file, is read back off the disk, is decrypted with the same password, and is only moved
		/// into place if the round trip reproduced the payload byte for byte. A recovery-code file
		/// that cannot be decrypted is worse than no file at all, because the player believes they
		/// have a copy. If anything fails the temporary file is removed and nothing is published.
		/// </remarks>
		public static bool TrySave(string directory, string password, string payload, out string path)
		{
			path = null;

			if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(payload))
			{
				return false;
			}

			string target = Path.Combine(directory, "recovery-" + NewFileToken() + EnvelopeExtension);
			string temporary = target + ".tmp";
			byte[] envelope = null;
			try
			{
				envelope = TwoFactorRecoveryCrypto.Encrypt(password, payload);

				File.WriteAllBytes(temporary, envelope);
				TryRestrictPermissions(temporary, "600");

				// Read back from disk rather than trusting the in-memory array: this is also the
				// check that the bytes actually landed on the filesystem intact.
				byte[] written = File.ReadAllBytes(temporary);
				TwoFactorRecoveryReadResult verify = TwoFactorRecoveryCrypto.TryDecrypt(password, written, out string roundTripped);
				if (verify != TwoFactorRecoveryReadResult.Success || !string.Equals(roundTripped, payload, StringComparison.Ordinal))
				{
					TryDeleteQuietly(temporary);
					Log.Warning("TwoFactorRecoveryStore", "The recovery payload failed its read-back check and was not stored.");
					return false;
				}

				File.Move(temporary, target);
				TryRestrictPermissions(target, "600");
				path = target;
				return true;
			}
			catch (Exception ex)
			{
				TryDeleteQuietly(temporary);
				// The message, never the payload or the password.
				Log.Warning("TwoFactorRecoveryStore", $"Failed to store the recovery payload: {ex.Message}");
				return false;
			}
			finally
			{
				TwoFactorRecoveryCrypto.Zero(envelope);
			}
		}

		/// <summary>
		/// Lists the recovery files currently on disk, newest first.
		/// </summary>
		/// <param name="directory">The recovery directory. May not exist.</param>
		/// <param name="legacyPlaintext">
		/// <c>true</c> to list the superseded unencrypted payloads instead of the envelopes.
		/// </param>
		/// <returns>Full paths; empty if the directory is absent or unreadable.</returns>
		public static List<string> List(string directory, bool legacyPlaintext = false)
		{
			List<string> results = new List<string>();
			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			{
				return results;
			}
			try
			{
				string pattern = "*" + (legacyPlaintext ? LegacyExtension : EnvelopeExtension);
				string[] files = Directory.GetFiles(directory, pattern);
				Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
				results.AddRange(files);
			}
			catch (Exception ex)
			{
				Log.Warning("TwoFactorRecoveryStore", $"Failed to list the recovery folder: {ex.Message}");
			}
			return results;
		}

		/// <summary>
		/// Reads one stored recovery file.
		/// </summary>
		/// <param name="path">The file to read.</param>
		/// <param name="password">The account password.</param>
		/// <param name="payload">
		/// Receives the recovered text on <see cref="TwoFactorRecoveryReadResult.Success"/>, or the
		/// file's contents verbatim on <see cref="TwoFactorRecoveryReadResult.LegacyPlaintext"/> —
		/// an unencrypted file needs no password to read, which is the entire problem with it.
		/// </param>
		/// <returns>Why the read succeeded or failed.</returns>
		/// <remarks>
		/// <see cref="TwoFactorRecoveryReadResult.Empty"/> also covers "the file is not there",
		/// which is the distinction a caller needs: "nothing is stored" is a normal state,
		/// "something is stored and would not open" is not, and the second must never be treated
		/// as the first — that is how a good envelope gets deleted after one mistyped password.
		/// </remarks>
		public static TwoFactorRecoveryReadResult TryRead(string path, string password, out string payload)
		{
			payload = null;
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				return TwoFactorRecoveryReadResult.Empty;
			}

			byte[] blob;
			try
			{
				blob = File.ReadAllBytes(path);
			}
			catch (Exception ex)
			{
				Log.Warning("TwoFactorRecoveryStore", $"Failed to read a recovery file: {ex.Message}");
				return TwoFactorRecoveryReadResult.Empty;
			}

			if (!TwoFactorRecoveryCrypto.LooksLikeEnvelope(blob))
			{
				try
				{
					payload = System.Text.Encoding.UTF8.GetString(blob);
				}
				catch (ArgumentException)
				{
					return TwoFactorRecoveryReadResult.Malformed;
				}
				return TwoFactorRecoveryReadResult.LegacyPlaintext;
			}

			return TwoFactorRecoveryCrypto.TryDecrypt(password, blob, out payload);
		}

		/// <summary>
		/// Re-writes a superseded plaintext payload as an encrypted envelope.
		/// </summary>
		/// <param name="directory">The recovery directory.</param>
		/// <param name="legacyPath">The plaintext file to migrate.</param>
		/// <param name="password">
		/// The account password. This must be a password the caller has just had <b>confirmed by
		/// the server</b>, not merely one the player typed — see the remarks.
		/// </param>
		/// <param name="newPath">Receives the path of the new envelope on success.</param>
		/// <returns><c>true</c> if the payload is now stored encrypted and the plaintext is gone.</returns>
		/// <remarks>
		/// <para><b>Order matters and it is not negotiable.</b> The envelope is written and proven
		/// readable first (<see cref="TrySave"/> does not publish an unverified file), and only then
		/// is the plaintext deleted. Deleting first, or deleting on a partial success, would take a
		/// payload the player can definitely read and replace it with one they possibly cannot.</para>
		/// <para><b>Why a confirmed password.</b> Encrypting under a password that turns out to be
		/// wrong does not fail — it succeeds, and produces a file nobody can ever open. That would
		/// silently destroy the codes while reporting success. So migration is only ever driven from
		/// a point in the flow where the server has already accepted the password.</para>
		/// </remarks>
		public static bool TryMigrateLegacy(string directory, string legacyPath, string password, out string newPath)
		{
			newPath = null;

			if (string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath) || string.IsNullOrEmpty(password))
			{
				return false;
			}

			string contents;
			try
			{
				contents = File.ReadAllText(legacyPath);
			}
			catch (Exception ex)
			{
				Log.Warning("TwoFactorRecoveryStore", $"Failed to read a legacy recovery file for migration: {ex.Message}");
				return false;
			}

			if (string.IsNullOrWhiteSpace(contents))
			{
				// An empty file carries nothing worth keeping and nothing worth losing.
				TryDeleteQuietly(legacyPath);
				return false;
			}

			if (!TrySave(directory, password, contents, out newPath))
			{
				// The plaintext is still there and still readable. That is the correct outcome of
				// a failed migration.
				Log.Warning("TwoFactorRecoveryStore", "A legacy recovery file could not be encrypted; it was left as it was.");
				return false;
			}

			TryDeleteQuietly(legacyPath);
			Log.Debug("TwoFactorRecoveryStore", "A legacy plaintext recovery file was re-stored encrypted.");
			return true;
		}

		/// <summary>
		/// Deletes a stored recovery file, ignoring failures.
		/// </summary>
		/// <param name="path">The file to remove.</param>
		public static void Delete(string path)
		{
			TryDeleteQuietly(path);
		}

		/// <summary>
		/// Deletes a file without letting an I/O failure escape.
		/// </summary>
		private static void TryDeleteQuietly(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return;
			}
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (Exception ex)
			{
				// The path is not logged: it is inside a directory whose name is the point.
				Log.Warning("TwoFactorRecoveryStore", $"Failed to delete a recovery file: {ex.Message}");
			}
		}

		/// <summary>
		/// A random, account-independent filename token.
		/// </summary>
		/// <remarks>
		/// Cryptographic randomness rather than a GUID or a timestamp: the name must carry no
		/// information about the account and must not be predictable by another local process that
		/// knows roughly when the player registered.
		/// </remarks>
		private static string NewFileToken()
		{
			byte[] bytes = new byte[8];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(bytes);
			}
			return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
		}

		/// <summary>
		/// Best-effort tightening of a path's permissions so other local users cannot read it.
		/// </summary>
		/// <param name="path">The file or directory to restrict.</param>
		/// <param name="mode">The chmod mode, e.g. <c>600</c> for a file or <c>700</c> for a directory.</param>
		/// <remarks>
		/// POSIX only, and best effort even there — which is exactly why the contents are now
		/// encrypted rather than relying on this.
		/// </remarks>
		public static void TryRestrictPermissions(string path, string mode)
		{
#if UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || UNITY_EDITOR_LINUX || UNITY_EDITOR_OSX
			try
			{
				var psi = new System.Diagnostics.ProcessStartInfo("/bin/chmod", $"{mode} \"{path}\"")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
				};
				using var p = System.Diagnostics.Process.Start(psi);
				p?.WaitForExit(500);
			}
			catch { /* best effort */ }
#endif
		}
	}
}
