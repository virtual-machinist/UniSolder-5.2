using System;

namespace UniSolder.Core
{
    public class TelemetryData
    {
        public float ColdJunctionTemp { get; set; }
        public float TipTemp { get; set; }
        public float AdcTemp { get; set; }
        public float TargetAvgFiltered { get; set; }
        public float HeaterResistance { get; set; }
        public float TargetAvgPower { get; set; }
        public bool IsPowerOn { get; set; }
        public float[] WsDelta { get; set; } = new float[7];
        public float DestinationReached { get; set; }
        public float Duty { get; set; }
        public byte[] RawData { get; set; } = Array.Empty<byte>();

        public static TelemetryData Parse(byte[] data)
        {
            var telemetry = new TelemetryData { RawData = data };
            if (data == null || data.Length < 34 || data[0] != 3)
            {
                return telemetry;
            }

            telemetry.ColdJunctionTemp = data[3] * 2.0f;
            telemetry.TipTemp = (data[4] * 0.5f) + (data[5] * 128.0f);
            telemetry.AdcTemp = (data[6] * 0.5f) + (data[7] * 128.0f);
            telemetry.TargetAvgFiltered = (data[8] * 0.5f) + (data[9] * 128.0f);
            telemetry.HeaterResistance = (data[10] * 1.0f) + (data[11] * 256.0f);
            telemetry.TargetAvgPower = (data[12] * 0.5f) + (data[13] * 128.0f);
            telemetry.IsPowerOn = data[14] != 0;

            for (int i = 0; i < 7; i++)
            {
                int idx = 15 + (i * 2);
                if (idx + 1 < data.Length)
                {
                    telemetry.WsDelta[i] = ((data[idx] * 1.0f + data[idx + 1] * 256.0f - 2048.0f) / 4.0f) + 200.0f;
                }
            }

            telemetry.DestinationReached = data[31] * 100.0f;
            telemetry.Duty = ((data[32] * 1.0f) + (data[33] * 256.0f)) / 128.0f;

            return telemetry;
        }
    }
}
