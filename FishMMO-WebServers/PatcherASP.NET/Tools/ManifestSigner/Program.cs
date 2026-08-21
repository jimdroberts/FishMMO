using System.Text;
using FishMMO.WebServers.Signing;

namespace FishMMO.WebServers.Tools
{
	/// <summary>
	/// Operator tool for the FishMMO release signing key.
	/// </summary>
	/// <remarks>
	/// Three verbs, deliberately no more:
	/// <list type="bullet">
	///   <item><description><c>keygen</c> — produce the Ed25519 keypair.</description></item>
	///   <item><description><c>sign</c> — sign an offline JSON manifest (a static
	///     <c>latest_version.json</c> served from a CDN, or the pin manifest).</description></item>
	///   <item><description><c>verify</c> — check a signed document against a public key, so an
	///     operator can confirm what is actually deployed rather than what they believe is
	///     deployed.</description></item>
	/// </list>
	/// The signing rules come from the shipping server source by compile-time link, so this tool
	/// cannot drift from the server's canonical form.
	/// </remarks>
	public static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length == 0)
			{
				PrintUsage();
				return 2;
			}

			try
			{
				switch (args[0].ToLowerInvariant())
				{
					case "keygen": return KeyGen(args);
					case "sign": return Sign(args);
					case "verify": return Verify(args);
					case "-h":
					case "--help":
					case "help": PrintUsage(); return 0;
					default:
						Console.Error.WriteLine($"Unknown command '{args[0]}'.");
						PrintUsage();
						return 2;
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("ERROR: " + ex.Message);
				return 1;
			}
		}

		private static void PrintUsage()
		{
			Console.WriteLine(@"ManifestSigner — FishMMO release signing key tool

  keygen [--out-dir DIR] [--force]
      Generates an Ed25519 keypair.
      Writes DIR/version-manifest-signing.key (base64 private seed, mode 600) and
      DIR/version-manifest-signing.pub (base64 public key). With no --out-dir the
      keys are printed to stdout and NOTHING is written.

  sign --key FILE|--key-base64 B64 --in MANIFEST.json [--out FILE]
      Signs a JSON object. Any existing 'signature' field is discarded and re-emitted
      last. NOTE: the OUTPUT is the signed artifact — it is re-serialised into the
      canonical spacing, so deploy the output file, not the input.

  verify --public B64|--public-file FILE --in SIGNED.json
      Verifies a signed document. Exit code 0 = valid, 1 = invalid.

Key placement after keygen:
  SERVER  private key -> Signing:VersionManifestPrivateKeyFile (preferred),
                         FISHMMO_VERSION_MANIFEST_SIGNING_KEY, or
                         Signing:VersionManifestPrivateKeyBase64
  CLIENT  public key  -> GeneratedPinSet.VersionManifestPublicKeyBase64
                         (Assets/Scripts/Client/Security/CertificatePins.generated.cs)

The private key must never be committed, never be logged, and never leave the release host.");
		}

		private static int KeyGen(string[] args)
		{
			string? outDir = GetOption(args, "--out-dir");
			bool force = HasFlag(args, "--force");

			var (seed, publicKey) = ManifestSigning.GenerateKeyPair();
			string privateBase64 = Convert.ToBase64String(seed);
			string publicBase64 = Convert.ToBase64String(publicKey);

			try
			{
				if (string.IsNullOrEmpty(outDir))
				{
					// No directory given: print and write nothing. Writing a private key to a
					// guessed location is worse than making the operator name one.
					Console.WriteLine("PRIVATE KEY (server, keep secret):");
					Console.WriteLine(privateBase64);
					Console.WriteLine();
					Console.WriteLine("PUBLIC KEY  (client, GeneratedPinSet.VersionManifestPublicKeyBase64):");
					Console.WriteLine(publicBase64);
					return 0;
				}

				Directory.CreateDirectory(outDir);
				string privatePath = Path.Combine(outDir, "version-manifest-signing.key");
				string publicPath = Path.Combine(outDir, "version-manifest-signing.pub");

				// Never silently overwrite a release key: doing so invalidates every client
				// already carrying the matching public key, and the old key is unrecoverable.
				if (!force && (File.Exists(privatePath) || File.Exists(publicPath)))
				{
					Console.Error.WriteLine(
						$"Refusing to overwrite an existing key in '{outDir}'. Pass --force only if you are certain: " +
						"replacing the key invalidates every client build that embeds the old public key.");
					return 1;
				}

				// Create the file with restrictive permissions BEFORE writing the key into it,
				// so there is no window where the seed is on disk world-readable.
				using (var stream = new FileStream(privatePath, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					TryRestrictPermissions(privatePath);
					byte[] bytes = Encoding.ASCII.GetBytes(privateBase64 + "\n");
					stream.Write(bytes, 0, bytes.Length);
					Array.Clear(bytes, 0, bytes.Length);
				}
				TryRestrictPermissions(privatePath);
				File.WriteAllText(publicPath, publicBase64 + "\n");

				Console.WriteLine($"Private key written to {privatePath} (mode 600 where supported).");
				Console.WriteLine($"Public  key written to {publicPath}");
				Console.WriteLine();
				Console.WriteLine("PUBLIC KEY (client, GeneratedPinSet.VersionManifestPublicKeyBase64):");
				Console.WriteLine(publicBase64);
				return 0;
			}
			finally
			{
				Array.Clear(seed, 0, seed.Length);
			}
		}

		private static int Sign(string[] args)
		{
			byte[] seed = LoadPrivateKey(args);
			try
			{
				string inPath = RequireOption(args, "--in");
				string json = File.ReadAllText(inPath);
				string signed = ManifestSigning.SignJsonObject(json, seed);

				string? outPath = GetOption(args, "--out");
				if (string.IsNullOrEmpty(outPath))
				{
					Console.WriteLine(signed);
				}
				else
				{
					File.WriteAllText(outPath, signed);
					Console.WriteLine($"Signed manifest written to {outPath}");
				}

				Console.Error.WriteLine(
					"NOTE: the output is the signed artifact. It is re-serialised into the canonical spacing, " +
					"so deploy the output byte-for-byte — reformatting it invalidates the signature.");
				return 0;
			}
			finally
			{
				Array.Clear(seed, 0, seed.Length);
			}
		}

		private static int Verify(string[] args)
		{
			string publicBase64 = GetOption(args, "--public")
				?? File.ReadAllText(RequireOption(args, "--public-file")).Trim();
			byte[] publicKey = Convert.FromBase64String(publicBase64.Trim());
			if (publicKey.Length != ManifestSigning.PublicKeyLength)
			{
				Console.Error.WriteLine($"Public key is {publicKey.Length} bytes; expected {ManifestSigning.PublicKeyLength}.");
				return 1;
			}

			string json = File.ReadAllText(RequireOption(args, "--in"));

			// Pull the signature value out of the document the same way the client does: read the
			// field, then blank it. Parsing with a JSON reader is fine for EXTRACTING the value —
			// what must not be re-serialised is the message being hashed.
			string? signatureBase64 = ExtractSignatureField(json);
			if (string.IsNullOrEmpty(signatureBase64))
			{
				Console.Error.WriteLine("INVALID: document has no non-empty 'signature' field.");
				return 1;
			}

			string? canonical = ManifestSigning.BuildCanonicalSignedMessage(json, signatureBase64!);
			if (canonical == null)
			{
				Console.Error.WriteLine(
					"INVALID: the signature field could not be located textually. The document was probably " +
					"reformatted after signing (a pretty-printer, or a proxy re-serialising it).");
				return 1;
			}

			bool ok = ManifestSigning.VerifyMessage(publicKey, canonical, signatureBase64!);
			Console.WriteLine(ok ? "VALID" : "INVALID: signature does not verify against this key.");
			return ok ? 0 : 1;
		}

		private static string? ExtractSignatureField(string json)
		{
			try
			{
				var node = System.Text.Json.Nodes.JsonNode.Parse(json);
				return node?[ManifestSigning.SignatureFieldName]?.GetValue<string>();
			}
			catch
			{
				return null;
			}
		}

		private static byte[] LoadPrivateKey(string[] args)
		{
			string? keyBase64 = GetOption(args, "--key-base64");
			if (string.IsNullOrEmpty(keyBase64))
			{
				keyBase64 = File.ReadAllText(RequireOption(args, "--key")).Trim();
			}
			if (!ManifestSigning.TryDecodePrivateKey(keyBase64, out byte[]? seed, out string? error))
			{
				throw new InvalidOperationException(error ?? "Unusable signing key.");
			}
			return seed!;
		}

		private static void TryRestrictPermissions(string path)
		{
			try
			{
				if (!OperatingSystem.IsWindows())
				{
					File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				}
			}
			catch
			{
				// Best effort. The console message below says "where supported" for this reason:
				// silently claiming 600 on a platform that cannot deliver it would be worse than
				// saying nothing.
			}
		}

		private static string? GetOption(string[] args, string name)
		{
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (string.Equals(args[i], name, StringComparison.Ordinal))
				{
					return args[i + 1];
				}
			}
			return null;
		}

		private static string RequireOption(string[] args, string name)
		{
			return GetOption(args, name) ?? throw new InvalidOperationException($"Missing required option {name}.");
		}

		private static bool HasFlag(string[] args, string name)
		{
			return Array.IndexOf(args, name) >= 0;
		}
	}
}
