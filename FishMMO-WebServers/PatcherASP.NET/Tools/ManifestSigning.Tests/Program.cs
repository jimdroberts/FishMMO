using System.Text;
using FishMMO.Client.Security;
using FishMMO.WebServers.Signing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FishMMO.WebServers.Tools.Tests
{
	/// <summary>
	/// End-to-end proof that the patch server's signer and the Unity client's verifier agree.
	/// </summary>
	/// <remarks>
	/// Every "verifies" assertion below runs the REAL
	/// <c>FishMMO.Client.Security.Ed25519ManifestVerifier</c>, compiled from
	/// <c>Assets/Scripts/Client/Security/Ed25519ManifestVerifier.cs</c> by source link. Nothing
	/// here re-implements the client side.
	/// </remarks>
	public static class Program
	{
		private static int passed;
		private static int failed;

		public static int Main()
		{
			Console.WriteLine("ManifestSigning round-trip harness");
			Console.WriteLine("  server signer : Patcher/Signing/ManifestSigning.cs (linked)");
			Console.WriteLine("  client verifier: Assets/Scripts/Client/Security/Ed25519ManifestVerifier.cs (linked)");
			Console.WriteLine();

			Section("Ed25519 primitive");
			Rfc8032Vectors();

			Section("Key handling");
			KeyHandling();

			Section("Canonical form agreement (server bytes == client bytes)");
			CanonicalAgreement();

			Section("Valid signatures verify — every response shape");
			ValidShapes();

			Section("Tampering is detected");
			Tampering();

			Section("Missing / malformed signature field");
			MissingSignature();

			Section("Wrong key");
			WrongKey();

			Section("Spacing, escaping and determinism");
			SpacingAndEscaping();

			Section("Offline signing tool path (SignJsonObject)");
			OfflinePath();

			Section("VersionManifestSigner fail-closed policy");
			FailClosedPolicy();

			Section("Regression: the old self-referential canonical form");
			SelfReferentialRegression();

			Console.WriteLine();
			Console.WriteLine($"==== {passed} passed, {failed} failed ====");
			return failed == 0 ? 0 : 1;
		}

		// ---------------------------------------------------------------- infrastructure

		private static void Section(string name)
		{
			Console.WriteLine($"-- {name}");
		}

		private static void Check(string name, bool condition)
		{
			if (condition)
			{
				passed++;
				Console.WriteLine($"   PASS  {name}");
			}
			else
			{
				failed++;
				Console.WriteLine($"   FAIL  {name}");
			}
		}

		/// <summary>Signs a body and hands the finished document to the CLIENT verifier.</summary>
		private static bool ClientVerifies(string document, byte[] publicKey)
		{
			string signature = ReadSignatureField(document);
			return Ed25519ManifestVerifier.Verify(publicKey, document, signature);
		}

		/// <summary>
		/// Pulls the signature value out of a document with a JSON reader. Extraction may parse;
		/// only the message being hashed must never be re-serialised.
		/// </summary>
		private static string ReadSignatureField(string document)
		{
			try
			{
				return System.Text.Json.Nodes.JsonNode.Parse(document)?["signature"]?.GetValue<string>() ?? "";
			}
			catch
			{
				return "";
			}
		}

		private static (byte[] seed, byte[] pub) NewKey() => ManifestSigning.GenerateKeyPair();

		private static string LatestVersionBody(string version) =>
			new ManifestJsonWriter().AddString("latest_version", version).Build();

		private static string PatchAvailableBody() =>
			new ManifestJsonWriter()
				.AddString("latest_version", "1.4.2")
				.AddBool("patch_available", true)
				.AddString("sha256", "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")
				.AddNumber("size", 734003200)
				.Build();

		// ---------------------------------------------------------------- tests

		/// <summary>
		/// RFC 8032 §7.1 vectors. If the primitive itself were wrong every other test would still
		/// pass — signer and verifier would agree on the same wrong thing.
		/// </summary>
		private static void Rfc8032Vectors()
		{
			// TEST 1: empty message.
			byte[] seed1 = FromHex("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
			byte[] pub1 = FromHex("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
			string expected1 = "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";
			Check("RFC 8032 test 1: derived public key", Convert.ToHexString(ManifestSigning.DerivePublicKey(seed1)).ToLowerInvariant() == Convert.ToHexString(pub1).ToLowerInvariant());
			Check("RFC 8032 test 1: signature over empty message", Convert.ToHexString(SignRaw(seed1, Array.Empty<byte>())).ToLowerInvariant() == expected1);

			// TEST 2: one-byte message 0x72.
			byte[] seed2 = FromHex("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb");
			string expected2 = "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00";
			Check("RFC 8032 test 2: signature over 0x72", Convert.ToHexString(SignRaw(seed2, new byte[] { 0x72 })).ToLowerInvariant() == expected2);

			// The client's own decoder must accept a real key and reject the near-misses.
			Check("client TryDecodePublicKey accepts a valid key", Ed25519ManifestVerifier.TryDecodePublicKey(Convert.ToBase64String(pub1), out _));
			Check("client TryDecodePublicKey rejects empty", !Ed25519ManifestVerifier.TryDecodePublicKey("", out _));
			Check("client TryDecodePublicKey rejects non-base64", !Ed25519ManifestVerifier.TryDecodePublicKey("not base64 !!", out _));
			Check("client TryDecodePublicKey rejects a 31-byte key", !Ed25519ManifestVerifier.TryDecodePublicKey(Convert.ToBase64String(new byte[31]), out _));
			Check("client TryDecodePublicKey rejects a 64-byte key", !Ed25519ManifestVerifier.TryDecodePublicKey(Convert.ToBase64String(new byte[64]), out _));
		}

		private static byte[] SignRaw(byte[] seed, byte[] message)
		{
			var signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
			signer.Init(true, new Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters(seed, 0));
			signer.BlockUpdate(message, 0, message.Length);
			return signer.GenerateSignature();
		}

		private static byte[] FromHex(string hex) => Convert.FromHexString(hex);

		private static void KeyHandling()
		{
			var (seed, pub) = NewKey();
			Check("generated seed is 32 bytes", seed.Length == 32);
			Check("generated public key is 32 bytes", pub.Length == 32);
			Check("two keygens differ", !Convert.ToBase64String(NewKey().pub).Equals(Convert.ToBase64String(pub), StringComparison.Ordinal));

			Check("decode 32-byte seed", ManifestSigning.TryDecodePrivateKey(Convert.ToBase64String(seed), out byte[] a, out _) && a.Length == 32);

			byte[] combined = new byte[64];
			Buffer.BlockCopy(seed, 0, combined, 0, 32);
			Buffer.BlockCopy(pub, 0, combined, 32, 32);
			Check("decode 64-byte seed||public", ManifestSigning.TryDecodePrivateKey(Convert.ToBase64String(combined), out byte[] b, out _) && b.SequenceEqual(seed));

			byte[] spliced = (byte[])combined.Clone();
			spliced[40] ^= 0xFF;
			Check("reject 64-byte blob whose public half does not match", !ManifestSigning.TryDecodePrivateKey(Convert.ToBase64String(spliced), out _, out _));

			Check("reject 31-byte key", !ManifestSigning.TryDecodePrivateKey(Convert.ToBase64String(new byte[31]), out _, out _));
			Check("reject non-base64 key", !ManifestSigning.TryDecodePrivateKey("@@@ not base64 @@@", out _, out _));
			Check("reject empty key", !ManifestSigning.TryDecodePrivateKey("", out _, out _));
			Check("reject null key", !ManifestSigning.TryDecodePrivateKey(null, out _, out _));

			ManifestSigning.TryDecodePrivateKey("@@@", out _, out string err);
			Check("decode error text carries no key material", err != null && !err.Contains("@@@"));
		}

		/// <summary>
		/// The load-bearing test. The bytes the server signed must be exactly the bytes the client
		/// reconstructs. If these ever diverge, everything else in this file is theatre.
		/// </summary>
		private static void CanonicalAgreement()
		{
			var (seed, pub) = NewKey();
			string body = PatchAvailableBody();
			string document = ManifestSigning.SignDocument(body, seed);
			string signature = ReadSignatureField(document);

			string serverCanonical = "{" + body + ", " + ManifestSigning.BlankSignatureField + "}";
			string clientCanonical = Ed25519ManifestVerifier.BuildCanonicalSignedMessage(document, signature);

			Check("client reconstructs a canonical message at all", clientCanonical != null);
			Check("client canonical == server canonical (ordinal)", string.Equals(clientCanonical, serverCanonical, StringComparison.Ordinal));
			Check("client canonical == server canonical (UTF-8 bytes)",
				clientCanonical != null &&
				Encoding.UTF8.GetBytes(clientCanonical).SequenceEqual(Encoding.UTF8.GetBytes(serverCanonical)));
			Check("canonical message does NOT contain the signature (the old, unsatisfiable form)",
				clientCanonical != null && !clientCanonical.Contains(signature, StringComparison.Ordinal));
			Check("server and client agree on the blank-field placeholder",
				ManifestSigning.BlankSignatureField == Ed25519ManifestVerifier.BlankSignatureField);
			Check("signature field is emitted last", document.TrimEnd().EndsWith("\"}", StringComparison.Ordinal) && document.LastIndexOf("\"signature\"", StringComparison.Ordinal) > document.LastIndexOf("\"sha256\"", StringComparison.Ordinal));
			Check("document parses as JSON", System.Text.Json.Nodes.JsonNode.Parse(document) != null);
			_ = pub;
		}

		private static void ValidShapes()
		{
			var (seed, pub) = NewKey();

			// Shape 1: latest_version only (no ?from= supplied).
			string s1 = ManifestSigning.SignDocument(LatestVersionBody("1.4.2"), seed);
			Check("shape 'latest_version' verifies", ClientVerifies(s1, pub));

			// Shape 2: up_to_date.
			string s2 = ManifestSigning.SignDocument(
				new ManifestJsonWriter().AddString("latest_version", "1.4.2").AddBool("up_to_date", true).Build(), seed);
			Check("shape 'up_to_date' verifies", ClientVerifies(s2, pub));

			// Shape 3: patch_available = false.
			string s3 = ManifestSigning.SignDocument(
				new ManifestJsonWriter().AddString("latest_version", "1.4.2").AddBool("patch_available", false).Build(), seed);
			Check("shape 'patch_available:false' verifies", ClientVerifies(s3, pub));

			// Shape 4: the full patch descriptor.
			string s4 = ManifestSigning.SignDocument(PatchAvailableBody(), seed);
			Check("shape 'patch descriptor' verifies", ClientVerifies(s4, pub));

			// An empty body must still produce a well-formed, verifiable document.
			string s5 = ManifestSigning.SignDocument("", seed);
			Check("empty body verifies", ClientVerifies(s5, pub));
			Check("empty body is valid JSON", System.Text.Json.Nodes.JsonNode.Parse(s5) != null);

			// 100 random keys, so a pass cannot be an artefact of one lucky keypair.
			int ok = 0;
			for (int i = 0; i < 100; i++)
			{
				var (s, p) = NewKey();
				string doc = ManifestSigning.SignDocument(PatchAvailableBody(), s);
				if (ClientVerifies(doc, p)) ok++;
			}
			Check($"100 independent keypairs all verify (got {ok}/100)", ok == 100);
		}

		private static void Tampering()
		{
			var (seed, pub) = NewKey();
			string document = ManifestSigning.SignDocument(PatchAvailableBody(), seed);
			Check("baseline document verifies", ClientVerifies(document, pub));

			Check("tampered latest_version fails",
				!ClientVerifies(document.Replace("1.4.2", "9.9.9", StringComparison.Ordinal), pub));

			Check("tampered sha256 (one nibble) fails",
				!ClientVerifies(document.Replace("9f86d081", "9f86d082", StringComparison.Ordinal), pub));

			Check("tampered size fails",
				!ClientVerifies(document.Replace("734003200", "734003201", StringComparison.Ordinal), pub));

			Check("flipped patch_available fails",
				!ClientVerifies(document.Replace("\"patch_available\": true", "\"patch_available\": false", StringComparison.Ordinal), pub));

			Check("an added field fails",
				!ClientVerifies(document.Replace("{\"latest_version\"", "{\"evil\": 1, \"latest_version\"", StringComparison.Ordinal), pub));

			Check("a removed field fails",
				!ClientVerifies(document.Replace("\"size\": 734003200, ", "", StringComparison.Ordinal), pub));

			Check("whitespace reformatting fails (spacing is part of the signed bytes)",
				!ClientVerifies(document.Replace("\": ", "\":", StringComparison.Ordinal), pub));

			// Signature tampering.
			string signature = ReadSignatureField(document);
			byte[] sigBytes = Convert.FromBase64String(signature);

			for (int i = 0; i < 64; i += 21)
			{
				byte[] flipped = (byte[])sigBytes.Clone();
				flipped[i] ^= 0x01;
				string bad = Convert.ToBase64String(flipped);
				string doc = document.Replace(signature, bad, StringComparison.Ordinal);
				Check($"signature with byte {i} flipped fails", !Ed25519ManifestVerifier.Verify(pub, doc, bad));
			}

			Check("signature replaced by another valid signature over different content fails",
				!Ed25519ManifestVerifier.Verify(pub, document, Convert.ToBase64String(SignRaw(seed, Encoding.UTF8.GetBytes("something else")))));

			Check("truncated (63-byte) signature fails",
				!Ed25519ManifestVerifier.Verify(pub, document, Convert.ToBase64String(sigBytes.Take(63).ToArray())));

			Check("oversized (65-byte) signature fails",
				!Ed25519ManifestVerifier.Verify(pub, document, Convert.ToBase64String(sigBytes.Concat(new byte[] { 0 }).ToArray())));

			Check("non-base64 signature fails",
				!Ed25519ManifestVerifier.Verify(pub, document, "!!!! not base64 !!!!"));

			// A signature that is valid for a DIFFERENT document must not verify against this one.
			string other = ManifestSigning.SignDocument(LatestVersionBody("2.0.0"), seed);
			string otherSig = ReadSignatureField(other);
			Check("valid signature from a different document fails",
				!Ed25519ManifestVerifier.Verify(pub, document.Replace(signature, otherSig, StringComparison.Ordinal), otherSig));
		}

		private static void MissingSignature()
		{
			var (seed, pub) = NewKey();
			string document = ManifestSigning.SignDocument(PatchAvailableBody(), seed);
			string signature = ReadSignatureField(document);

			// The field removed entirely — the shape an unsigned legacy server would produce.
			string stripped = "{" + PatchAvailableBody() + "}";
			Check("document with no signature field fails", !Ed25519ManifestVerifier.Verify(pub, stripped, signature));
			Check("BuildCanonicalSignedMessage returns null when the field is absent",
				Ed25519ManifestVerifier.BuildCanonicalSignedMessage(stripped, signature) == null);

			// The field present but empty — what an unconfigured non-Production server emits.
			string emptySig = "{" + PatchAvailableBody() + ", " + ManifestSigning.BlankSignatureField + "}";
			Check("document with an EMPTY signature field fails", !Ed25519ManifestVerifier.Verify(pub, emptySig, ""));
			Check("null signature fails", !Ed25519ManifestVerifier.Verify(pub, document, null));

			// Renamed field: the verifier searches for the literal name, so this is a miss.
			Check("renamed signature field fails",
				!ClientVerifies(document.Replace("\"signature\"", "\"sig\"", StringComparison.Ordinal), pub));

			Check("null public key fails", !Ed25519ManifestVerifier.Verify(null, document, signature));
			Check("empty document fails", !Ed25519ManifestVerifier.Verify(pub, "", signature));
		}

		private static void WrongKey()
		{
			var (seed, pub) = NewKey();
			var (_, otherPub) = NewKey();
			string document = ManifestSigning.SignDocument(PatchAvailableBody(), seed);

			Check("correct key verifies", ClientVerifies(document, pub));
			Check("a different public key fails", !ClientVerifies(document, otherPub));

			// One bit wrong in the embedded public key must also fail — catches a transcription
			// slip when the operator copies the key into CertificatePins.generated.cs.
			byte[] nearly = (byte[])pub.Clone();
			nearly[0] ^= 0x01;
			Check("public key with one bit flipped fails", !ClientVerifies(document, nearly));

			int wrongKeyPasses = 0;
			for (int i = 0; i < 50; i++)
			{
				var (_, p) = NewKey();
				if (ClientVerifies(document, p)) wrongKeyPasses++;
			}
			Check($"50 unrelated keys all fail (passes: {wrongKeyPasses})", wrongKeyPasses == 0);
		}

		private static void SpacingAndEscaping()
		{
			var (seed, pub) = NewKey();

			// Determinism: Ed25519 is deterministic, so an unchanged payload must produce a
			// byte-identical document. The ETag on the endpoint depends on this being true.
			string a = ManifestSigning.SignDocument(PatchAvailableBody(), seed);
			string b = ManifestSigning.SignDocument(PatchAvailableBody(), seed);
			Check("signing is deterministic (identical bytes)", string.Equals(a, b, StringComparison.Ordinal));

			// The verifier accepts ":" spacing too. Build such a document by hand and prove it.
			string body = LatestVersionBody("1.4.2");
			string canonical = "{" + body + ", " + ManifestSigning.BlankSignatureField + "}";
			string sig = Convert.ToBase64String(ManifestSigning.SignMessage(seed, canonical));
			string tightDocument = "{" + body + ", \"signature\":\"" + sig + "\"}";
			Check("':' (no space) signature spacing on the wire still verifies",
				Ed25519ManifestVerifier.Verify(pub, tightDocument, sig));
			Check("':' spacing normalises to the same canonical message",
				Ed25519ManifestVerifier.BuildCanonicalSignedMessage(tightDocument, sig) == canonical);

			// Escaping. A version string cannot legally contain these (VersionConfig's grammar
			// forbids them), but the writer is the thing that guarantees the document stays
			// parseable and unambiguous, so it is tested directly.
			foreach (string awkward in new[] { "a\"b", "a\\b", "a\nb", "a\tb", "ab", "héllo-ünïcode", "ab" })
			{
				string doc = ManifestSigning.SignDocument(LatestVersionBody(awkward), seed);
				bool parses = false;
				string roundTripped = null;
				try
				{
					roundTripped = System.Text.Json.Nodes.JsonNode.Parse(doc)?["latest_version"]?.GetValue<string>();
					parses = true;
				}
				catch { }
				Check($"escaped value {System.Text.Json.JsonSerializer.Serialize(awkward)} verifies and round-trips",
					ClientVerifies(doc, pub) && parses && roundTripped == awkward);
			}

			// A field value that contains the literal text of a signature field. LastIndexOf must
			// still land on the real one (it is emitted last), and the self-check inside
			// SignDocument must be satisfied.
			string sneaky = ManifestSigning.SignDocument(
				new ManifestJsonWriter().AddString("latest_version", "1.0.0").AddString("note", "\"signature\": \"AAAA\"").Build(), seed);
			Check("a field value imitating a signature field does not confuse the verifier", ClientVerifies(sneaky, pub));

			// Long / large values.
			string big = ManifestSigning.SignDocument(
				new ManifestJsonWriter().AddString("latest_version", new string('x', 4096)).AddNumber("size", long.MaxValue).Build(), seed);
			Check("4 KiB value and long.MaxValue verify", ClientVerifies(big, pub));
		}

		private static void OfflinePath()
		{
			var (seed, pub) = NewKey();

			string input = "{ \"latest_version\" : \"3.1.4\" , \"patch_available\" : true , \"sha256\" : \"abc123\" , \"size\" : 42 }";
			string signed = ManifestSigning.SignJsonObject(input, seed);
			Check("offline-signed document verifies with the client verifier", ClientVerifies(signed, pub));
			Check("offline signing preserves field order",
				signed.IndexOf("latest_version", StringComparison.Ordinal) < signed.IndexOf("patch_available", StringComparison.Ordinal));

			// An existing signature field must be discarded, not signed over.
			string reSigned = ManifestSigning.SignJsonObject(signed, seed);
			Check("re-signing an already-signed document verifies", ClientVerifies(reSigned, pub));
			Check("re-signing does not leave two signature fields",
				reSigned.Split("\"signature\"").Length - 1 == 1);

			// Nested objects and arrays (the pin manifest shape).
			string nested = "{\"pins\":[\"sha256/AAA\",\"sha256/BBB\"],\"meta\":{\"issued\":\"2026-08-21\",\"count\":2},\"signature\":\"old\"}";
			string nestedSigned = ManifestSigning.SignJsonObject(nested, seed);
			Check("nested objects and arrays verify", ClientVerifies(nestedSigned, pub));
			Check("nested document still parses", System.Text.Json.Nodes.JsonNode.Parse(nestedSigned) != null);

			// Pretty-printing the output must break it — proving the signature really is over the
			// literal bytes, which is the property the whole scheme rests on.
			string pretty = System.Text.Json.JsonSerializer.Serialize(
				System.Text.Json.Nodes.JsonNode.Parse(signed),
				new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
			Check("reformatting a signed document breaks it", !ClientVerifies(pretty, pub));

			bool threw = false;
			try { ManifestSigning.SignJsonObject("[1,2,3]", seed); } catch (InvalidOperationException) { threw = true; }
			Check("signing a non-object top level is refused", threw);
		}

		private static void FailClosedPolicy()
		{
			// Production + no key => refuse to start.
			bool threw = false;
			try
			{
				_ = new VersionManifestSigner(new TestEnvironment("Production"), EmptyConfig());
			}
			catch (InvalidOperationException)
			{
				threw = true;
			}
			Check("Production with no key refuses to start", threw);

			// Development + no key => starts, unconfigured, emits an empty signature field.
			var dev = new VersionManifestSigner(new TestEnvironment("Development"), EmptyConfig());
			Check("Development with no key starts", !dev.IsConfigured);
			string unsigned = dev.BuildDocument(new ManifestJsonWriter().AddString("latest_version", "1.0.0"));
			Check("unconfigured document still carries a signature field", unsigned.Contains("\"signature\"", StringComparison.Ordinal));
			Check("unconfigured document's signature is empty", ReadSignatureField(unsigned) == "");
			Check("unconfigured document is valid JSON", System.Text.Json.Nodes.JsonNode.Parse(unsigned) != null);

			var (seed, pub) = NewKey();
			Check("a client WITH a key refuses the unconfigured document",
				!Ed25519ManifestVerifier.Verify(pub, unsigned, ReadSignatureField(unsigned)));

			// Key from inline configuration.
			string seedBase64 = Convert.ToBase64String(seed);
			var inline = new VersionManifestSigner(new TestEnvironment("Production"),
				Config(new Dictionary<string, string> { [VersionManifestSigner.PrivateKeyInlineSetting] = seedBase64 }));
			Check("inline configuration key loads", inline.IsConfigured);
			Check("reported public key matches the private key", inline.PublicKeyBase64 == Convert.ToBase64String(pub));
			Check("inline-configured signer produces a client-verifiable document",
				ClientVerifies(inline.BuildDocument(new ManifestJsonWriter().AddString("latest_version", "1.0.0")), pub));

			// Key from a file.
			string keyPath = Path.Combine(Path.GetTempPath(), "fishmmo-manifest-test-" + Guid.NewGuid().ToString("N") + ".key");
			File.WriteAllText(keyPath, seedBase64 + "\n");
			try
			{
				var fromFile = new VersionManifestSigner(new TestEnvironment("Production"),
					Config(new Dictionary<string, string> { [VersionManifestSigner.PrivateKeyFileSetting] = keyPath }));
				Check("key file loads (trailing newline tolerated)", fromFile.IsConfigured && fromFile.PublicKeyBase64 == Convert.ToBase64String(pub));
				Check("file-configured signer produces a client-verifiable document",
					ClientVerifies(fromFile.BuildDocument(new ManifestJsonWriter().AddString("latest_version", "1.0.0")), pub));
				fromFile.Dispose();
			}
			finally
			{
				File.Delete(keyPath);
			}

			// A key file that does not exist must be a hard error, NOT a fall-through to the next
			// source — a typo in the path must not silently change which key signs releases.
			bool fileThrew = false;
			try
			{
				_ = new VersionManifestSigner(new TestEnvironment("Development"),
					Config(new Dictionary<string, string>
					{
						[VersionManifestSigner.PrivateKeyFileSetting] = "/nonexistent/fishmmo/key",
						[VersionManifestSigner.PrivateKeyInlineSetting] = seedBase64,
					}));
			}
			catch (InvalidOperationException) { fileThrew = true; }
			Check("a missing key file throws instead of falling through to another source", fileThrew);

			// Environment variable source.
			Environment.SetEnvironmentVariable(VersionManifestSigner.PrivateKeyEnvironmentVariable, seedBase64);
			try
			{
				var fromEnv = new VersionManifestSigner(new TestEnvironment("Production"), EmptyConfig());
				Check("environment variable key loads", fromEnv.IsConfigured && fromEnv.PublicKeyBase64 == Convert.ToBase64String(pub));
				fromEnv.Dispose();
			}
			finally
			{
				Environment.SetEnvironmentVariable(VersionManifestSigner.PrivateKeyEnvironmentVariable, null);
			}

			// A malformed key is a hard error rather than a silent downgrade to unsigned.
			bool badThrew = false;
			try
			{
				_ = new VersionManifestSigner(new TestEnvironment("Development"),
					Config(new Dictionary<string, string> { [VersionManifestSigner.PrivateKeyInlineSetting] = "@@@ not a key @@@" }));
			}
			catch (InvalidOperationException) { badThrew = true; }
			Check("a malformed key throws rather than downgrading to unsigned", badThrew);

			// Dispose must zero the seed.
			byte[] copy = (byte[])seed.Clone();
			var disposable = new VersionManifestSigner(new TestEnvironment("Development"),
				Config(new Dictionary<string, string> { [VersionManifestSigner.PrivateKeyInlineSetting] = Convert.ToBase64String(copy) }));
			disposable.Dispose();
			Check("Dispose leaves the signer unconfigured", !disposable.IsConfigured);

			inline.Dispose();
			dev.Dispose();
		}

		/// <summary>
		/// Guards the fix at the centre of this work: the canonical message must NOT contain the
		/// signature.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Both this verifier and the <c>ApiPinUpdateSidecar</c> it was extracted from used to
		/// return <c>stripped + signatureBase64</c> from <c>BuildCanonicalSignedMessage</c>, i.e.
		/// a message containing the signature being verified. Producing one requires solving
		/// <c>sig = Sign(sk, stripped || base64(sig))</c>.
		/// </para>
		/// <para>
		/// It cannot be asserted directly that no such signature exists — that is the point — so
		/// this proves it two ways that a future re-introduction of the append would fail. First,
		/// a correctly-signed document does not verify against the appended form, so restoring
		/// the append would reject every genuine manifest. Second, the obvious way somebody might
		/// try to satisfy it — iterate <c>sig = Sign(canonical || b64(sig))</c> and hope it
		/// settles — is shown not to converge, because Ed25519 derives R from H(prefix || M) and
		/// re-randomises the whole signature on any change to M.
		/// </para>
		/// </remarks>
		private static void SelfReferentialRegression()
		{
			var (seed, pub) = NewKey();
			string body = PatchAvailableBody();
			string canonical = "{" + body + ", " + ManifestSigning.BlankSignatureField + "}";
			string signature = Convert.ToBase64String(ManifestSigning.SignMessage(seed, canonical));

			Check("a correctly-signed message verifies over the canonical form",
				ManifestSigning.VerifyMessage(pub, canonical, signature));
			Check("the same signature does NOT verify over canonical+signature (the old form)",
				!ManifestSigning.VerifyMessage(pub, canonical + signature, signature));
			Check("the live client verifier no longer appends the signature",
				Ed25519ManifestVerifier.BuildCanonicalSignedMessage(
					"{" + body + ", \"signature\": \"" + signature + "\"}", signature) == canonical);

			// Fixed-point search: 500 iterations, and record whether any of them closes the loop.
			int converged = 0;
			string current = signature;
			var seen = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < 500; i++)
			{
				string next = Convert.ToBase64String(ManifestSigning.SignMessage(seed, canonical + current));
				if (string.Equals(next, current, StringComparison.Ordinal))
				{
					converged++;
					break;
				}
				if (!seen.Add(next))
				{
					// A cycle is not a fixed point either, but note it if it ever happens.
					break;
				}
				current = next;
			}
			Check($"500-iteration fixed-point search finds none (hits: {converged})", converged == 0);
		}

		private static IConfiguration EmptyConfig() => Config(new Dictionary<string, string>());

		private static IConfiguration Config(Dictionary<string, string> values) =>
			new ConfigurationBuilder().AddInMemoryCollection(values).Build();

		/// <summary>Minimal <see cref="IHostEnvironment"/> so the policy can be exercised for real.</summary>
		private sealed class TestEnvironment : IHostEnvironment
		{
			public TestEnvironment(string environmentName) => EnvironmentName = environmentName;
			public string EnvironmentName { get; set; }
			public string ApplicationName { get; set; } = "ManifestSigning.Tests";
			public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
			public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
				= new Microsoft.Extensions.FileProviders.NullFileProvider();
		}
	}
}
