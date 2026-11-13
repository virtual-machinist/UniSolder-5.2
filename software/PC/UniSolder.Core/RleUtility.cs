using System;
using System.Collections.Generic;
using System.IO;

namespace UniSolder.Core
{
    /// <summary>
    /// Run-Length Encoding (RLE) utility for UniSolder data streams.
    /// Values must be in range 0..127. Byte values >= 128 represent run length counts (length = value - 128).
    /// </summary>
    public static class RleUtility
    {
        public static byte[] Encode(byte[] input)
        {
            if (input == null || input.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var output = new List<byte>(input.Length);
            byte cCount = 1;
            int length = input.Length;

            for (int i = 0; i < length; i++)
            {
                if (input[i] > 127)
                {
                    throw new ArgumentException($"Byte value at index {i} is {input[i]}, which exceeds maximum allowed value 127.");
                }

                if (i < length - 1 && cCount < 127 && input[i] == input[i + 1])
                {
                    cCount++;
                }
                else
                {
                    if (cCount > 1)
                    {
                        output.Add((byte)(cCount + 128));
                    }
                    output.Add(input[i]);
                    cCount = 1;
                }
            }

            return output.ToArray();
        }

        public static byte[] Decode(byte[] compressed)
        {
            if (compressed == null || compressed.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var output = new List<byte>(compressed.Length * 2);
            int cCount = 1;

            for (int i = 0; i < compressed.Length; i++)
            {
                byte b = compressed[i];
                if (b >= 128)
                {
                    cCount = b - 128;
                }
                else
                {
                    while (cCount > 0)
                    {
                        output.Add(b);
                        cCount--;
                    }
                    cCount = 1;
                }
            }

            return output.ToArray();
        }

        public static byte[] CompressFile(string filepath)
        {
            byte[] fileBytes = File.ReadAllBytes(filepath);
            return Encode(fileBytes);
        }
    }
}
