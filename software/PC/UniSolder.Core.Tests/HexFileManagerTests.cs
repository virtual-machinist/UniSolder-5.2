using System;
using System.IO;
using System.Linq;
using UniSolder.Core;
using Xunit;

namespace UniSolder.Core.Tests
{
    public class HexFileManagerTests
    {
        [Fact]
        public void LoadHex_ValidSimpleDataRecord_ParsesCorrectly()
        {
            // :0400000001020304F2
            // 04 = len, 0000 = addr, 00 = data rec, 01 02 03 04, checksum = ~(04+00+00+00+01+02+03+04) + 1 = 0x100 - 0x0E = 0xF2
            string hex = ":0400000001020304F2\n:00000001FF\n";
            var manager = new HexFileManager();

            using var reader = new StringReader(hex);
            bool success = manager.LoadHex(reader);

            Assert.True(success);
            Assert.Single(manager.Records);
            Assert.Equal(0u, manager.Records[0].Address);
            Assert.Equal(4u, manager.Records[0].RecDataLen);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, manager.Records[0].Data.Take(4).ToArray());
        }

        [Fact]
        public void LoadHex_ExtendedLinearAddressRecord_CalculatesHighAddress()
        {
            // :020000041D00DD
            // :0400000005060708E2
            // :00000001FF
            string hex = ":020000041D00DD\n:0400000005060708E2\n:00000001FF\n";
            var manager = new HexFileManager();

            using var reader = new StringReader(hex);
            bool success = manager.LoadHex(reader);

            Assert.True(success);
            Assert.Single(manager.Records);
            Assert.Equal(0x1D000000u, manager.Records[0].Address);
            Assert.Equal(4u, manager.Records[0].RecDataLen);
            Assert.Equal(new byte[] { 5, 6, 7, 8 }, manager.Records[0].Data.Take(4).ToArray());
        }

        [Fact]
        public void LoadHex_ConsecutiveRecords_MergesUpTo48Bytes()
        {
            // First record: 16 bytes at 0x0000
            // Second record: 16 bytes at 0x0010
            // Third record: 16 bytes at 0x0020
            // Fourth record: 16 bytes at 0x0030
            // After merging: record 1 has 48 bytes (0x0000-0x002F), record 2 has 16 bytes (0x0030-0x003F)
            string line1 = ":10000000000102030405060708090A0B0C0D0E0F78\n";
            string line2 = ":10001000101112131415161718191A1B1C1D1E1F68\n";
            string line3 = ":10002000202122232425262728292A2B2C2D2E2F58\n";
            string line4 = ":10003000303132333435363738393A3B3C3D3E3F48\n";
            string eof = ":00000001FF\n";

            var manager = new HexFileManager();
            using var reader = new StringReader(line1 + line2 + line3 + line4 + eof);
            bool success = manager.LoadHex(reader);

            Assert.True(success);
            Assert.Equal(2, manager.Records.Count);
            Assert.Equal(48u, manager.Records[0].RecDataLen);
            Assert.Equal(0u, manager.Records[0].Address);
            Assert.Equal(16u, manager.Records[1].RecDataLen);
            Assert.Equal(0x30u, manager.Records[1].Address);
        }

        [Fact]
        public void LoadHex_CorruptChecksum_ReturnsFalse()
        {
            string hex = ":040000000102030400\n:00000001FF\n"; // Bad checksum (00 instead of F2)
            var manager = new HexFileManager();

            using var reader = new StringReader(hex);
            bool success = manager.LoadHex(reader);

            Assert.False(success);
        }

        [Fact]
        public void AddressConversion_CalculatesExpectedValues()
        {
            var manager = new HexFileManager();
            uint vfa = manager.PaToVfa(0x9d001000);
            Assert.Equal(0x1000u, vfa);

            uint kva = manager.PaToKva0(0x1D000000);
            Assert.Equal(0x9D000000u, kva);
        }
    }
}
