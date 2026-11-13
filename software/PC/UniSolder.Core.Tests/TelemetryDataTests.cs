using System;
using UniSolder.Core;
using Xunit;

namespace UniSolder.Core.Tests
{
    public class TelemetryDataTests
    {
        [Fact]
        public void Parse_ValidDataPacket_ExtractsAllFieldsAccurately()
        {
            byte[] packet = new byte[64];
            packet[0] = 3; // live data packet id
            packet[3] = 12; // CTTemp = 12 * 2 = 24.0
            packet[4] = 50; // CTemp low: 50 * 0.5 = 25
            packet[5] = 2;  // CTemp high: 2 * 128 = 256 -> 281.0
            packet[6] = 20; // ADCTemp low: 20 * 0.5 = 10
            packet[7] = 2;  // ADCTemp high: 2 * 128 = 256 -> 266.0
            packet[8] = 40; // TAvgF low: 40 * 0.5 = 20
            packet[9] = 2;  // TAvgF high: 2 * 128 = 256 -> 276.0
            packet[10] = 15; // CHRes low: 15
            packet[11] = 0;  // CHRes high: 0 -> 15.0
            packet[12] = 30; // TAvgP low: 30 * 0.5 = 15
            packet[13] = 2;  // TAvgP high: 2 * 128 = 256 -> 271.0
            packet[14] = 1;  // PowerOn = true
            packet[31] = 1;  // DestinationReached: 1 * 100 = 100.0
            packet[32] = 64; // Duty low: 64
            packet[33] = 1;  // Duty high: 256 -> 320 / 128 = 2.5

            var telemetry = TelemetryData.Parse(packet);

            Assert.Equal(24.0f, telemetry.ColdJunctionTemp);
            Assert.Equal(281.0f, telemetry.TipTemp);
            Assert.Equal(266.0f, telemetry.AdcTemp);
            Assert.Equal(276.0f, telemetry.TargetAvgFiltered);
            Assert.Equal(15.0f, telemetry.HeaterResistance);
            Assert.Equal(271.0f, telemetry.TargetAvgPower);
            Assert.True(telemetry.IsPowerOn);
            Assert.Equal(100.0f, telemetry.DestinationReached);
            Assert.Equal(2.5f, telemetry.Duty);
        }

        [Fact]
        public void Parse_InvalidPacketType_ReturnsDefaults()
        {
            byte[] packet = new byte[64];
            packet[0] = 1; // not type 3

            var telemetry = TelemetryData.Parse(packet);

            Assert.Equal(0, telemetry.TipTemp);
            Assert.False(telemetry.IsPowerOn);
        }
    }
}
