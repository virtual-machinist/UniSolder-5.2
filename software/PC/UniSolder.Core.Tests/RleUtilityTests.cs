using System;
using UniSolder.Core;
using Xunit;

namespace UniSolder.Core.Tests
{
    public class RleUtilityTests
    {
        [Fact]
        public void Encode_EmptyOrNull_ReturnsEmpty()
        {
            Assert.Empty(RleUtility.Encode(Array.Empty<byte>()));
            Assert.Empty(RleUtility.Decode(Array.Empty<byte>()));
        }

        [Fact]
        public void Encode_SingleByte_ReturnsSameByte()
        {
            byte[] input = { 42 };
            byte[] encoded = RleUtility.Encode(input);

            Assert.Equal(new byte[] { 42 }, encoded);

            byte[] decoded = RleUtility.Decode(encoded);
            Assert.Equal(input, decoded);
        }

        [Fact]
        public void Encode_RepeatedBytes_CompressesWithCountHeader()
        {
            // 5 consecutive 10s: count 5 + 128 = 133 (0x85), value 10
            byte[] input = { 10, 10, 10, 10, 10 };
            byte[] encoded = RleUtility.Encode(input);

            Assert.Equal(new byte[] { 133, 10 }, encoded);

            byte[] decoded = RleUtility.Decode(encoded);
            Assert.Equal(input, decoded);
        }

        [Fact]
        public void Encode_MixedBytes_EncodesAndDecodesCorrectly()
        {
            byte[] input = { 1, 2, 2, 2, 3, 4, 4, 5 };
            byte[] encoded = RleUtility.Encode(input);

            // 1, (3+128=131), 2, 3, (2+128=130), 4, 5
            Assert.Equal(new byte[] { 1, 131, 2, 3, 130, 4, 5 }, encoded);

            byte[] decoded = RleUtility.Decode(encoded);
            Assert.Equal(input, decoded);
        }

        [Fact]
        public void Encode_ValuesAbove127_ThrowsArgumentException()
        {
            byte[] input = { 1, 2, 128 };
            Assert.Throws<ArgumentException>(() => RleUtility.Encode(input));
        }

        [Fact]
        public void RoundTrip_LongRun_PreservesData()
        {
            byte[] input = new byte[300];
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = (byte)(i % 100);
            }

            byte[] encoded = RleUtility.Encode(input);
            byte[] decoded = RleUtility.Decode(encoded);

            Assert.Equal(input, decoded);
        }
    }
}
