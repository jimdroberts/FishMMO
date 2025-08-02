using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace FishMMO.Shared
{
	/// <summary>
	/// Static class providing cryptographic helper methods for RSA key export/import, key generation, and AES encryption/decryption.
	/// </summary>
	public static class CryptoHelper
	{
		/// <summary>
		/// Exports the public key from an RSA instance as a structured byte array (modulus + exponent).
		/// </summary>
		/// <param name="rsa">RSA instance to export the public key from.</param>
		/// <returns>Byte array containing the modulus and exponent of the public key.</returns>
		public static byte[] ExportPublicKey(RSA rsa)
		{
			RSAParameters rsaParameters = rsa.ExportParameters(false); // false to get the public key only
			byte[] modulus = rsaParameters.Modulus;
			byte[] exponent = rsaParameters.Exponent;

			// Ensure that modulus and exponent are padded to correct lengths if necessary
			int modulusLength = (rsa.KeySize + 7) / 8; // Convert bits to bytes
			int exponentLength = exponent.Length;

			// Create a structured byte array for the public key
			byte[] publicKeyBytes = new byte[modulusLength + exponentLength];

			// Copy modulus and exponent into the structured byte array
			Buffer.BlockCopy(modulus, 0, publicKeyBytes, 0, modulus.Length);
			Buffer.BlockCopy(exponent, 0, publicKeyBytes, modulus.Length, exponent.Length);

			return publicKeyBytes;
		}

		/// <summary>
		/// Imports a public key into an RSA instance from a structured byte array (modulus + exponent).
		/// </summary>
		/// <param name="rsa">RSA instance to import the public key into.</param>
		/// <param name="publicKeyBytes">Byte array containing the modulus and exponent.</param>
		public static void ImportPublicKey(RSA rsa, byte[] publicKeyBytes)
		{
			int modulusLength = (rsa.KeySize + 7) / 8; // Calculate modulus length in bytes

			// Split the publicKeyBytes into modulus and exponent
			byte[] modulus = new byte[modulusLength];
			byte[] exponent = new byte[publicKeyBytes.Length - modulusLength];

			Buffer.BlockCopy(publicKeyBytes, 0, modulus, 0, modulusLength);
			Buffer.BlockCopy(publicKeyBytes, modulusLength, exponent, 0, exponent.Length);

			// Create RSAParameters from modulus and exponent
			RSAParameters rsaParameters = new RSAParameters
			{
				Modulus = modulus,
				Exponent = exponent
			};

			// Import RSAParameters into RSA instance
			rsa.ImportParameters(rsaParameters);
		}

		/// <summary>
		/// Generates a cryptographically secure random key of the specified length in bytes.
		/// </summary>
		/// <param name="length">Length of the key in bytes.</param>
		/// <returns>Randomly generated key as a byte array.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte[] GenerateKey(int length)
		{
			using (var rng = new RNGCryptoServiceProvider())
			{
				byte[] key = new byte[length];
				rng.GetBytes(key);
				return key;
			}
		}

		/// <summary>
		/// Encrypts input data using AES symmetric encryption with the provided key and IV.
		/// </summary>
		/// <param name="symmetricKey">AES symmetric key.</param>
		/// <param name="iv">Initialization vector for AES.</param>
		/// <param name="input">Input data to encrypt.</param>
		/// <returns>Encrypted data as a byte array.</returns>
		public static byte[] EncryptAES(byte[] symmetricKey, byte[] iv, byte[] input)
		{
			using (Aes aes = Aes.Create())
			{
				aes.Key = symmetricKey;
				aes.IV = iv;
				using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
				using (var ms = new System.IO.MemoryStream())
				{
					using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
					{
						cs.Write(input, 0, input.Length);
					}
					return ms.ToArray();
				}
			}
		}

		/// <summary>
		/// Decrypts input data using AES symmetric decryption with the provided key and IV.
		/// </summary>
		/// <param name="symmetricKey">AES symmetric key.</param>
		/// <param name="iv">Initialization vector for AES.</param>
		/// <param name="input">Input data to decrypt.</param>
		/// <returns>Decrypted data as a byte array.</returns>
		public static byte[] DecryptAES(byte[] symmetricKey, byte[] iv, byte[] input)
		{
			using (Aes aes = Aes.Create())
			{
				aes.Key = symmetricKey;
				aes.IV = iv;
				using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
				using (var ms = new System.IO.MemoryStream(input))
				{
					using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
					using (var resultStream = new System.IO.MemoryStream())
					{
						cs.CopyTo(resultStream);
						return resultStream.ToArray();
					}
				}
			}
		}
	}
}
