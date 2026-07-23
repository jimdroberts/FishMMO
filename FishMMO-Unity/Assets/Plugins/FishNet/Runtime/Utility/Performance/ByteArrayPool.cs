using System;
using System.Collections.Generic;

namespace FishNet.Utility.Performance
{
    /// <summary>
    /// Retrieves and stores byte arrays using a pooling system.
    /// Thread-safe: all queue operations are serialized via a lock.
    /// </summary>
    public static class ByteArrayPool
    {
        /// <summary>
        /// Stored byte arrays.
        /// </summary>
        private static Queue<byte[]> _byteArrays = new();

        /// <summary>
        /// Lock object serializing <see cref="Retrieve"/> and <see cref="Store"/> calls.
        /// </summary>
        private static readonly object _lock = new();

        /// <summary>
        /// Returns a byte array which will be of at lesat minimum length. The returned array must manually be stored.
        /// </summary>
        public static byte[] Retrieve(int minimumLength)
        {
            byte[] result = null;

            lock (_lock)
            {
                if (_byteArrays.Count > 0)
                    result = _byteArrays.Dequeue();
            }

            if (result == null)
                result = new byte[minimumLength];
            else if (result.Length < minimumLength)
                Array.Resize(ref result, minimumLength);

            return result;
        }

        /// <summary>
        /// Stores a byte array for re-use.
        /// </summary>
        public static void Store(byte[] buffer)
        {
            lock (_lock)
            {
                /* Holy cow that's a lot of buffered
                 * buffers. This wouldn't happen under normal
                 * circumstances but if the user is stress
                 * testing connections in one executable perhaps. */
                if (_byteArrays.Count > 300)
                    return;
                _byteArrays.Enqueue(buffer);
            }
        }
    }
}
