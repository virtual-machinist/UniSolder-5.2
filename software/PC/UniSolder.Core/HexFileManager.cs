using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace UniSolder.Core
{
    public class HexFileManager
    {
        private const uint BOOT_SECTOR_BEGIN = 0x9fc00000;
        private const uint APPLICATION_START = 0x9d000000;
        private const byte DATA_RECORD = 0;
        private const byte END_OF_FILE_RECORD = 1;
        private const byte EXT_SEG_ADRS_RECORD = 2;
        private const byte EXT_LIN_ADRS_RECORD = 4;

        public class HexDataRecord
        {
            public uint Address { get; set; }
            public uint RecDataLen { get; set; }
            public byte[] Data { get; set; } = new byte[1024];
            public byte CheckSum { get; set; }
        }

        public List<HexDataRecord> Records { get; } = new List<HexDataRecord>();

        public uint PaToVfa(uint x)
        {
            return x - APPLICATION_START;
        }

        public uint PaToKva0(uint x)
        {
            return x | 0x80000000;
        }

        public bool LoadHexFile(string filepath)
        {
            if (!File.Exists(filepath))
            {
                return false;
            }

            using var reader = new StreamReader(filepath);
            return LoadHex(reader);
        }

        public bool LoadHex(TextReader reader)
        {
            Records.Clear();
            uint cExtSegAddress = 0;
            uint cExtLinAddress = 0;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var s = line.Trim();
                if (s.Length < 11 || s[0] != ':')
                {
                    continue;
                }

                uint ccs = 0;
                int byteCount = ((s.Length - 1) / 2) - 1;
                for (int i = 0; i < byteCount; i++)
                {
                    if (!uint.TryParse(s.Substring(1 + (i * 2), 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint b))
                    {
                        return false;
                    }
                    ccs += b;
                    ccs &= 0xff;
                }
                ccs = (0x100 - ccs) & 0xff;

                if (!uint.TryParse(s.Substring(s.Length - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint expectedCcs) || ccs != expectedCcs)
                {
                    return false;
                }

                if (!byte.TryParse(s.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte rectype))
                {
                    return false;
                }

                switch (rectype)
                {
                    case DATA_RECORD:
                        var previous = Records.Count > 0 ? Records.Last() : null;
                        uint addressOffset = uint.Parse(s.Substring(3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        uint recDataLen = uint.Parse(s.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                        var current = new HexDataRecord
                        {
                            Address = addressOffset + cExtSegAddress + cExtLinAddress,
                            RecDataLen = recDataLen
                        };

                        for (int i = 0; i < current.RecDataLen; i++)
                        {
                            current.Data[i] = byte.Parse(s.Substring(9 + (i * 2), 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        }

                        if (previous != null && current.Address == (previous.Address + previous.RecDataLen))
                        {
                            while (current.RecDataLen > 0)
                            {
                                if (previous.RecDataLen < 48)
                                {
                                    previous.RecDataLen++;
                                    previous.Data[previous.RecDataLen - 1] = current.Data[0];
                                    for (int i = 1; i < current.RecDataLen; i++)
                                    {
                                        current.Data[i - 1] = current.Data[i];
                                    }
                                    current.Address++;
                                    current.RecDataLen--;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }

                        if (current.RecDataLen > 0)
                        {
                            Records.Add(current);
                        }
                        break;

                    case EXT_SEG_ADRS_RECORD:
                        cExtSegAddress = uint.Parse(s.Substring(9, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture) << 8;
                        cExtLinAddress = 0;
                        break;

                    case EXT_LIN_ADRS_RECORD:
                        cExtLinAddress = uint.Parse(s.Substring(9, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture) << 16;
                        cExtSegAddress = 0;
                        break;

                    case END_OF_FILE_RECORD:
                        break;

                    default:
                        cExtLinAddress = 0;
                        cExtSegAddress = 0;
                        break;
                }
            }

            return true;
        }
    }
}
