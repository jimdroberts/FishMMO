using System;

namespace FishMMO.Database.Data
{
    /// <summary>
    /// Data transfer object for a connection token HMAC key.
    /// Represents a single active key used for signing/verifying stateless
    /// connection tokens between the IpFetchServer (signing) and game servers (verification).
    /// </summary>
    public struct ConnectionTokenKeyData
    {
        /// <summary>
        /// Primary key of the record.
        /// </summary>
        public readonly long ID;

        /// <summary>
        /// Logical key identifier (e.g., region code like "us-east", "eu-west").
        /// Embedded in the connection token payload so the verifying server
        /// can select the correct HMAC key.
        /// </summary>
        public readonly string KeyId;

        /// <summary>
        /// HMAC-SHA256 key material.
        /// CAUTION: <see cref="byte[]"/> is a reference type; the array is not copied on construction.
        /// Callers must not mutate the array contents after passing it to this struct.
        /// </summary>
        public readonly byte[] HmacKey;

        /// <summary>
        /// Whether this key is currently active and should be used for verification.
        /// Inactive keys are kept during rotation overlap windows.
        /// </summary>
        public readonly bool IsActive;

        /// <summary>
        /// UTC timestamp when this key was first persisted.
        /// </summary>
        public readonly DateTime TimeCreated;

        public ConnectionTokenKeyData(long id, string keyId, byte[] hmacKey, bool isActive, DateTime timeCreated)
        {
            ID = id;
            KeyId = keyId;
            HmacKey = hmacKey;
            IsActive = isActive;
            TimeCreated = timeCreated;
        }
    }
}
