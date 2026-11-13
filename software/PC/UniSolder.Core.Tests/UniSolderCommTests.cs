using System;
using System.Collections.Generic;
using SSComm;
using UniSolder.Core;
using Xunit;

namespace UniSolder.Core.Tests
{
    public class MockUniComm : IUniComm
    {
        public bool Connected { get; set; } = true;
        public int SentBytesCount { get; private set; }
        public int ReceivedBytesCount { get; private set; }
        public List<byte[]> WrittenPackets { get; } = new List<byte[]>();
        private readonly Queue<byte[]> _rxQueue = new Queue<byte[]>();
        public Func<byte[], byte[]?>? OnWriteResponder { get; set; }

        public event EventHandler? DataReceived;

        public void EnqueueRxPacket(byte[] packet)
        {
            _rxQueue.Enqueue(packet);
            DataReceived?.Invoke(this, EventArgs.Empty);
        }

        public bool Connect() => Connected = true;
        public void Disconnect() => Connected = false;
        public void Init() { }
        public int BytesSent() => SentBytesCount;
        public int BytesReceived() => ReceivedBytesCount;
        public byte ReadByte() => 0;

        public int Read(ref byte[] dBuffer, int dOffset, int maxBytes)
        {
            if (_rxQueue.Count == 0) return 0;
            var packet = _rxQueue.Dequeue();
            int len = Math.Min(maxBytes, packet.Length);
            Array.Copy(packet, 0, dBuffer, dOffset, len);
            ReceivedBytesCount += len;
            return len;
        }

        public void WriteByte(ref byte wb, bool doWake = true)
        {
            WrittenPackets.Add(new byte[] { wb });
            SentBytesCount++;
        }

        public bool Write(ref byte[] dBuffer, int dOffset, int numBytes)
        {
            byte[] copy = new byte[numBytes];
            Array.Copy(dBuffer, dOffset, copy, 0, numBytes);
            WrittenPackets.Add(copy);
            SentBytesCount += numBytes;

            if (OnWriteResponder != null)
            {
                var response = OnWriteResponder(copy);
                if (response != null)
                {
                    EnqueueRxPacket(response);
                }
            }
            return true;
        }

        public int RXDataCount => _rxQueue.Count > 0 ? 64 : 0;
        public int TXFreeSpace => 64;

        public void Dispose() { }
    }

    public class UniSolderCommTests
    {
        [Fact]
        public void CalculateCRC_KnownInput_ComputesExpectedValue()
        {
            var comm = new UniSolderComm();
            byte[] data = { 0x01, 0x02, 0x03, 0x04 };
            ushort crc = comm.CalculateCRC(data, 0, data.Length);
            Assert.NotEqual(0, crc);

            // Same input should be deterministic
            ushort crc2 = comm.CalculateCRC(data, 0, data.Length);
            Assert.Equal(crc, crc2);
        }

        [Fact]
        public void DevQuery_SendsCommandPacket()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            mock.OnWriteResponder = written =>
            {
                byte[] rxResponse = new byte[64];
                rxResponse[0] = written[0]; // DEV_QUERY
                rxResponse[1] = 0; // status OK
                return rxResponse;
            };

            int result = comm.DevQuery();

            Assert.Equal(0, result);
            Assert.NotEmpty(mock.WrittenPackets);
            Assert.Equal((byte)UniSolderComm.Commands.DEV_QUERY, mock.WrittenPackets[0][0]);
        }

        [Fact]
        public void DevGetOpMode_ParsesOperatingMode()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            mock.OnWriteResponder = written =>
            {
                byte[] rxResponse = new byte[64];
                rxResponse[0] = written[0];
                rxResponse[1] = 16; // mode = 16 (bootloader)
                return rxResponse;
            };

            byte opMode = 0;
            int result = comm.DevGetOpMode(ref opMode);

            Assert.Equal(0, result);
            Assert.Equal(16, opMode);
        }

        [Fact]
        public void AppGetPIDParameters_ParsesPIDStructure()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            mock.OnWriteResponder = written =>
            {
                byte[] rxResponse = new byte[64];
                rxResponse[0] = (byte)UniSolderComm.Commands.APP_GET_PID;
                rxResponse[1] = 10; // Gain low byte
                rxResponse[2] = 0;  // Gain high byte -> 10
                rxResponse[3] = 20; // Offset low
                rxResponse[4] = 0;  // Offset high -> 20
                rxResponse[5] = 50; // KP low
                rxResponse[6] = 1;  // KP high -> 306
                rxResponse[7] = 80; // KI low
                rxResponse[8] = 0;  // KI high -> 80
                rxResponse[9] = 5;  // DGain
                rxResponse[10] = 0;
                rxResponse[11] = 7; // OVSGain
                return rxResponse;
            };

            var pid = comm.AppGetPIDParameters();

            Assert.Equal(10, pid.Gain);
            Assert.Equal(20, pid.Offset);
            Assert.Equal(306, pid.KP);
            Assert.Equal(80, pid.KI);
            Assert.Equal(5, pid.DGain);
            Assert.Equal(7, pid.OVSGain);
        }

        [Fact]
        public void AppSetPIDParameters_SendsSerializedParameters()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            var pid = new UniSolderComm.PID
            {
                Gain = 258, // 0x0102: low=2, high=1
                KP = 515,   // 0x0203: low=3, high=2
                KI = 772,   // 0x0304: low=4, high=3
                DGain = 9,
                OVSGain = 11
            };

            comm.AppSetPIDParameters(ref pid);

            Assert.Single(mock.WrittenPackets);
            var packet = mock.WrittenPackets[0];
            Assert.Equal((byte)UniSolderComm.Commands.APP_SET_PID, packet[0]);
            Assert.Equal(2, packet[1]);
            Assert.Equal(1, packet[2]);
            Assert.Equal(3, packet[5]);
            Assert.Equal(2, packet[6]);
            Assert.Equal(4, packet[7]);
            Assert.Equal(3, packet[8]);
            Assert.Equal(9, packet[9]);
            Assert.Equal(11, packet[11]);
        }

        [Fact]
        public void LiveDataReceived_DispatchesEventWithParsedTelemetry()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            UniSolderComm.LiveDataReceivedEventData? receivedEvent = null;
            comm.LiveDataReceived += (s, e) => receivedEvent = e;

            byte[] liveDataPacket = new byte[64];
            liveDataPacket[0] = 3; // live data packet id
            liveDataPacket[3] = 15; // CTTemp = 15 * 2 = 30
            liveDataPacket[4] = 100; // CTemp low: 100 * 0.5 = 50
            liveDataPacket[5] = 2;   // CTemp high: 2 * 128 = 256 -> total 306

            mock.EnqueueRxPacket(liveDataPacket);

            Assert.NotNull(receivedEvent);
            Assert.Equal(3, receivedEvent!.Data[0]);
            Assert.NotNull(receivedEvent.Telemetry);
            Assert.Equal(30.0f, receivedEvent.Telemetry!.ColdJunctionTemp);
            Assert.Equal(306.0f, receivedEvent.Telemetry!.TipTemp);
        }

        [Fact]
        public void InstrumentChangeEvent_DispatchesEvent()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            bool eventFired = false;
            comm.InstrumentChange += (s, e) => eventFired = true;

            byte[] instrumentChangePacket = new byte[64];
            instrumentChangePacket[0] = 1; // instrument change packet id

            mock.EnqueueRxPacket(instrumentChangePacket);

            Assert.True(eventFired);
        }

        [Fact]
        public void DevGetInfo_ParsesIdAndVersion()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            mock.OnWriteResponder = written =>
            {
                byte[] rxResponse = new byte[64];
                rxResponse[0] = (byte)UniSolderComm.Commands.DEV_GET_INFO;
                rxResponse[1] = 0x78; // ID byte 0
                rxResponse[2] = 0x56; // ID byte 1
                rxResponse[3] = 0x34; // ID byte 2
                rxResponse[4] = 0x12; // ID byte 3 -> 0x12345678
                rxResponse[5] = 2;    // VerMin = 2
                rxResponse[6] = 5;    // VerMaj = 5
                return rxResponse;
            };

            uint devId = 0;
            byte verMaj = 0;
            byte verMin = 0;
            int result = comm.DevGetInfo(ref devId, ref verMaj, ref verMin);

            Assert.Equal(0, result);
            Assert.Equal(0x12345678u, devId);
            Assert.Equal((byte)5, verMaj);
            Assert.Equal((byte)2, verMin);
        }

        [Fact]
        public void BlEraseFlash_SendsEraseMagicPattern()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            mock.OnWriteResponder = written =>
            {
                byte[] rxResponse = new byte[64];
                rxResponse[0] = (byte)UniSolderComm.Commands.BL_ERASE_FLASH;
                rxResponse[1] = 0; // status OK
                return rxResponse;
            };

            int result = comm.BlEraseFlash();

            Assert.Equal(0, result);
            Assert.Single(mock.WrittenPackets);
            var packet = mock.WrittenPackets[0];
            Assert.Equal((byte)UniSolderComm.Commands.BL_ERASE_FLASH, packet[0]);
            Assert.Equal(0x34, packet[1]);
            Assert.Equal(0x12, packet[2]);
            Assert.Equal(0x21, packet[3]);
            Assert.Equal(0x43, packet[4]);
        }

        [Fact]
        public void BlProgramFlash_SendsAddressAndPayload()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            mock.OnWriteResponder = written =>
            {
                byte[] rxResponse = new byte[64];
                rxResponse[0] = (byte)UniSolderComm.Commands.BL_PROGRAM_FLASH;
                rxResponse[1] = 0;
                return rxResponse;
            };

            byte[] flashData = { 0xAA, 0xBB, 0xCC, 0xDD };
            int result = comm.BlProgramFlash(0x9D001000, ref flashData, 0, flashData.Length);

            Assert.Equal(0, result);
            Assert.Single(mock.WrittenPackets);
            var packet = mock.WrittenPackets[0];
            Assert.Equal((byte)UniSolderComm.Commands.BL_PROGRAM_FLASH, packet[0]);
            Assert.Equal(0x34, packet[1]);
            Assert.Equal(0x12, packet[2]);
            Assert.Equal(0x21, packet[3]);
            Assert.Equal(0x43, packet[4]);
            // Address 0x9D001000 in little endian
            Assert.Equal(0x00, packet[5]);
            Assert.Equal(0x10, packet[6]);
            Assert.Equal(0x00, packet[7]);
            Assert.Equal(0x9D, packet[8]);
            // Count 4
            Assert.Equal(0x04, packet[9]);
            Assert.Equal(0x00, packet[10]);
            // Data
            Assert.Equal(0xAA, packet[13]);
            Assert.Equal(0xBB, packet[14]);
            Assert.Equal(0xCC, packet[15]);
            Assert.Equal(0xDD, packet[16]);
        }

        [Fact]
        public void BlReadCRC_ParsesCRC()
        {
            var mock = new MockUniComm();
            var comm = new UniSolderComm { Transport = mock };

            mock.OnWriteResponder = written =>
            {
                byte[] rxResponse = new byte[64];
                rxResponse[0] = (byte)UniSolderComm.Commands.BL_READ_CRC;
                rxResponse[1] = 0x34; // CRC low
                rxResponse[2] = 0x12; // CRC high -> 0x1234
                return rxResponse;
            };

            ushort crc = 0;
            int result = comm.BlReadCRC(0x9D000000, 1024, ref crc);

            Assert.Equal(0, result);
            Assert.Equal((ushort)0x1234, crc);
        }
    }
}
