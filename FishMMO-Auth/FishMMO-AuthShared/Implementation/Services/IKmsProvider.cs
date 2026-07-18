using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Abstraction over a Key Management Service. Allows operators to plug in AWS KMS, Vault,
	/// PKCS#11/HSM, or other backends in place of the local-derive default.
	/// </summary>
	/// <remarks>
	/// The TOTP master KEK is currently derived from the LoginServer's HMAC
	/// signing key via HMAC-SHA256 with a domain separator. That binds the KEK lifetime to the
	/// signing-key lifetime and exposes it in process memory for the lifetime of the server.
	/// An external KMS unwrap call should only return the cleartext KEK for the duration of an
	/// envelope-decrypt operation; integrating one requires this interface.
	/// </remarks>
	public interface IKmsProvider
	{
		/// <summary>
		/// Derives or unwraps a 32-byte symmetric key for the given <paramref name="context"/>.
		/// Callers should zero the returned buffer with <see cref="CryptographicOperations.ZeroMemory"/>
		/// as soon as the operation completes.
		/// </summary>
		/// <param name="context">Stable application-defined key identifier (e.g. "totp-master-key-v1").</param>
		/// <returns>Cleartext 32-byte key material.</returns>
		byte[] DeriveKey(string context);
	}

	/// <summary>
	/// Local fallback <see cref="IKmsProvider"/> implementation that derives keys via HMAC-SHA256
	/// from a long-lived root key. This is the legacy behaviour and is exposed here for parity;
	/// production deployments should swap in a hardware-backed implementation.
	/// </summary>
	public sealed class LocalDeriveKmsProvider : IKmsProvider, IDisposable
	{
		/// <summary>
		/// The root key material used for HMAC-SHA256 key derivation.
		/// Null once disposed; all access must be under <see cref="gate"/>.
		/// </summary>
		private byte[]? rootKey;

		/// <summary>
		/// Synchronization gate protecting <see cref="rootKey"/> during derivation and disposal.
		/// </summary>
		private readonly object gate = new object();

		public LocalDeriveKmsProvider(byte[] rootKey)
		{
			if (rootKey == null || rootKey.Length < 32)
				throw new ArgumentException("Root key must be at least 32 bytes.", nameof(rootKey));

			this.rootKey = new byte[rootKey.Length];
			Buffer.BlockCopy(rootKey, 0, this.rootKey, 0, rootKey.Length);
		}

		/// <inheritdoc/>
		public byte[] DeriveKey(string context)
		{
			if (string.IsNullOrEmpty(context))
				throw new ArgumentException("Context must be non-empty.", nameof(context));

			lock (gate)
			{
				if (rootKey == null)
					throw new ObjectDisposedException(nameof(LocalDeriveKmsProvider));

				using var kdf = new HMACSHA256(rootKey);
				return kdf.ComputeHash(Encoding.UTF8.GetBytes(context));
			}
		}

		public void Dispose()
		{
			lock (gate)
			{
				if (rootKey != null)
				{
					CryptographicOperations.ZeroMemory(rootKey);
					rootKey = null;
				}
			}
		}
	}

	/// <summary>
	/// OS-level hardening hooks invoked early during server startup to reduce the risk that
	/// sensitive in-memory key material (TOTP KEK, signing keys, SRP verifier) leaks via
	/// process core dumps or ptrace from unprivileged peers.
	/// </summary>
	/// <remarks>
	/// On Linux, calling <c>prctl(PR_SET_DUMPABLE, 0)</c> disables core
	/// dumps and prevents non-root processes from attaching <c>ptrace</c> regardless of the
	/// global <c>ptrace_scope</c> setting. This call is a no-op on non-Linux platforms.
	/// </remarks>
	public static class ProcessHardening
	{
		// prctl(2): PR_SET_DUMPABLE = 4
		private const int PrSetDumpable = 4;

		[DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
		private static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

		/// <summary>
		/// Attempts to disable core dumps and unprivileged ptrace on Linux. Returns <c>true</c>
		/// if the syscall succeeded; returns <c>false</c> on non-Linux platforms or if the
		/// syscall is unavailable. Failure is non-fatal — callers should log and continue.
		/// </summary>
		public static bool TryDisableCoreDumpAndPtrace(out string status)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				status = "Not Linux; PR_SET_DUMPABLE is a no-op.";
				return false;
			}

			try
			{
				int rc = prctl(PrSetDumpable, 0, 0, 0, 0);
				if (rc == 0)
				{
					status = "PR_SET_DUMPABLE=0 applied.";
					return true;
				}

				int err = Marshal.GetLastWin32Error();
				status = $"prctl(PR_SET_DUMPABLE,0) failed with errno {err}.";
				return false;
			}
			catch (DllNotFoundException)
			{
				status = "libc not available; cannot disable dumps.";
				return false;
			}
			catch (EntryPointNotFoundException)
			{
				status = "prctl entry point missing; cannot disable dumps.";
				return false;
			}
		}
	}
}
