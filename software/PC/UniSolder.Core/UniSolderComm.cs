using System;
using System.Diagnostics;
using SSComm;

namespace UniSolder.Core
{
    public class UniSolderComm
    {
        #region "Enums"
        public enum Commands : byte
        {
            DEV_QUERY = 0x02,
            DEV_GET_INFO = 0x60,
            DEV_GET_OPERATING_MODE = 0x61,
            DEV_RESET = 0x62,

            APP_GET_INFO = 0x80,
            APP_JUMP_TO_BOOTLOADER = 0x81,
            APP_RESTART = 0x82,

            APP_SET_PID = 3,
            APP_GET_PID = 4,

            BL_GET_INFO = 0xE0,
            BL_ERASE_FLASH = 0xE1,
            BL_PROGRAM_FLASH = 0xE2,
            BL_READ_CRC = 0xE3,
            BL_JUMP_TO_APP = 0xE4,
            BL_PROGRAM_COMPLETE = 0xE5
        }
        #endregion

        #region "Public subclasses"
        public class T_CommandFeedback
        {
            public bool IsEmpty { get; set; }
            public byte Command { get; set; }
            public byte Status { get; set; }
            public byte[] Data { get; set; } = new byte[1024];
            public uint DataLength { get; set; }

            public void Clear()
            {
                IsEmpty = true;
                Command = 0;
                Status = 0;
                DataLength = 0;
            }

            public bool ReadFromBuffer(ref byte[] bb, uint offset, uint dLen)
            {
                Command = bb[offset];
                DataLength = dLen - 1;
                for (uint i = 0; i <= dLen - 2; i++)
                {
                    Data[i] = bb[offset + 1 + i];
                }
                IsEmpty = false;
                return true;
            }

            public T_CommandFeedback()
            {
                Clear();
            }
        }

        public class PID
        {
            public ushort Gain { get; set; }
            public ushort Offset { get; set; }
            public ushort KP { get; set; }
            public ushort KI { get; set; }
            public byte DGain { get; set; }
            public byte OVSGain { get; set; }
        }

        public class LiveDataReceivedEventData : EventArgs
        {
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public TelemetryData? Telemetry => Data.Length > 0 ? TelemetryData.Parse(Data) : null;
        }
        #endregion

        private IUniComm? _transport;
        public IUniComm? Transport
        {
            get => _transport;
            set
            {
                if (_transport != null && _transport != value)
                {
                    _transport.DataReceived -= Transport_DataReceived;
                }
                _transport = value;
                if (_transport != null)
                {
                    _transport.DataReceived += Transport_DataReceived;
                }
            }
        }

        public T_CommandFeedback CommandFeedBack { get; } = new T_CommandFeedback();

        public event EventHandler<LiveDataReceivedEventData>? LiveDataReceived;
        public event EventHandler? InstrumentChange;

        private readonly Stopwatch _toTimer = new Stopwatch();

        public void Init()
        {
            CommandFeedBack.Clear();
        }

        public int DevQuery()
        {
            byte[] bb = { (byte)Commands.DEV_QUERY };
            return SendBINCommand(bb, 0, 1, bb, 1000);
        }

        public int DevGetInfo(ref uint dDevID, ref byte dVerMaj, ref byte dVerMin)
        {
            byte[] bb = {
                (byte)Commands.DEV_GET_INFO,
                0,
                0,
                0,
                0,
                0,
                0
            };
            var result = SendBINCommand(bb, 0, 1, bb, 1000);
            if (result == 0)
            {
                dDevID = bb[3];
                dDevID <<= 8;
                dDevID += bb[2];
                dDevID <<= 8;
                dDevID += bb[1];
                dDevID <<= 8;
                dDevID += bb[0];
                dVerMin = bb[4];
                dVerMaj = bb[5];
                Debug.WriteLine($"Device info: ID={dDevID:X8} Version: {dVerMaj}.{dVerMin}");
            }
            return result;
        }

        public int DevGetOpMode(ref byte dOpM)
        {
            byte[] bb = { (byte)Commands.DEV_GET_OPERATING_MODE };
            var result = SendBINCommand(bb, 0, 1, bb, 1000);
            if (result == 0)
            {
                dOpM = bb[0];
                Debug.WriteLine($"Device operating mode: {bb[0]}");
            }
            return result;
        }

        public void DevReset()
        {
            byte[] bb = { (byte)Commands.DEV_RESET };
            SendBINCommand(bb, 0, 1, null, 0);
            Debug.WriteLine("Device reset.");
        }

        public int AppGetInfo(ref byte aVerMin, ref byte aVerMaj)
        {
            byte[] bb = {
                (byte)Commands.APP_GET_INFO,
                0
            };
            var result = SendBINCommand(bb, 0, 1, bb, 1000);
            if (result == 0)
            {
                aVerMin = bb[0];
                aVerMaj = bb[1];
                Debug.WriteLine($"Device application info: Version: {aVerMaj}.{aVerMin}");
            }
            return result;
        }

        public void AppJumpToBootloader()
        {
            byte[] bb = { (byte)Commands.APP_JUMP_TO_BOOTLOADER };
            SendBINCommand(bb, 0, 1, null, 0);
            Debug.WriteLine("Jump to bootloader.");
        }

        public void AppRestart()
        {
            byte[] bb = { (byte)Commands.APP_RESTART };
            SendBINCommand(bb, 0, 1, null, 0);
            Debug.WriteLine("Application restart.");
        }

        public PID AppGetPIDParameters()
        {
            byte[] bb = {
                (byte)Commands.APP_GET_PID,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0
            };
            Debug.WriteLine("Get PID parameters");
            SendBINCommand(bb, 0, 1, bb, 1000);
            return new PID
            {
                Gain = (ushort)((ushort)bb[0] + (ushort)bb[1] * 256),
                Offset = (ushort)((ushort)bb[2] + (ushort)bb[3] * 256),
                KP = (ushort)((ushort)bb[4] + (ushort)bb[5] * 256),
                KI = (ushort)((ushort)bb[6] + (ushort)bb[7] * 256),
                DGain = bb[8],
                OVSGain = bb[10]
            };
        }

        public void AppSetPIDParameters(ref PID pid)
        {
            byte[] bb = {
                (byte)Commands.APP_SET_PID,
                (byte)(pid.Gain & 255),
                (byte)(pid.Gain / 256),
                0,
                0,
                (byte)(pid.KP & 255),
                (byte)(pid.KP / 256),
                (byte)(pid.KI & 255),
                (byte)(pid.KI / 256),
                pid.DGain,
                0,
                pid.OVSGain,
                0
            };
            SendBINCommand(bb, 0, bb.Length, null, 0);
        }

        public int BlGetInfo(ref byte blVerMaj, ref byte blVerMin)
        {
            byte[] bb = {
                (byte)Commands.BL_GET_INFO,
                0
            };
            var result = SendBINCommand(bb, 0, 1, bb, 1000);
            if (result == 0)
            {
                blVerMin = bb[0];
                blVerMaj = bb[1];
                Debug.WriteLine($"Bootloader information: Version={blVerMaj}.{blVerMin}");
            }
            return result;
        }

        public int BlEraseFlash()
        {
            byte[] bb = {
                (byte)Commands.BL_ERASE_FLASH,
                0x34,
                0x12,
                0x21,
                0x43
            };
            Debug.WriteLine("BlEraseFlash");
            return SendBINCommand(bb, 0, 5, null, 6000);
        }

        public int BlProgramFlash(uint pfAddr, ref byte[] byteBuff, int bbOffset, int bbCount)
        {
            byte[] bb = new byte[65];
            if (bbCount > 0)
            {
                bb[0] = (byte)Commands.BL_PROGRAM_FLASH;
                bb[1] = 0x34;
                bb[2] = 0x12;
                bb[3] = 0x21;
                bb[4] = 0x43;
                bb[5] = (byte)(pfAddr & 0xffL);
                bb[6] = (byte)((pfAddr >> 8) & 0xffL);
                bb[7] = (byte)((pfAddr >> 16) & 0xffL);
                bb[8] = (byte)((pfAddr >> 24) & 0xffL);
                bb[9] = (byte)(bbCount & 0xffL);
                bb[10] = (byte)((bbCount >> 8) & 0xffL);
                bb[11] = (byte)((bbCount >> 16) & 0xffL);
                bb[12] = (byte)((bbCount >> 24) & 0xffL);
                for (int i = 0; i <= bbCount - 1; i++)
                {
                    bb[13 + i] = byteBuff[bbOffset + i];
                }
                return SendBINCommand(bb, 0, 13 + bbCount, null, 1000);
            }
            return 0;
        }

        public int BlProgramComplete()
        {
            byte[] bb = {
                (byte)Commands.BL_PROGRAM_COMPLETE,
                0x34,
                0x12,
                0x21,
                0x43
            };
            Debug.WriteLine("BlProgramComplete");
            return SendBINCommand(bb, 0, 5, null, 1000);
        }

        public int BlReadCRC(uint pfAddr, uint pfCount, ref ushort pfCRC)
        {
            if (pfCount > 0)
            {
                byte[] bb = {
                    (byte)Commands.BL_READ_CRC,
                    (byte)(pfAddr & 0xffL),
                    (byte)((pfAddr >> 8) & 0xffL),
                    (byte)((pfAddr >> 16) & 0xffL),
                    (byte)((pfAddr >> 24) & 0xffL),
                    (byte)(pfCount & 0xffL),
                    (byte)((pfCount >> 8) & 0xffL),
                    (byte)((pfCount >> 16) & 0xffL),
                    (byte)((pfCount >> 24) & 0xffL)
                };
                var result = SendBINCommand(bb, 0, 9, bb, 5000);
                if (result == 0)
                {
                    pfCRC = bb[1];
                    pfCRC <<= 8;
                    pfCRC += bb[0];
                }
                return result;
            }
            return 0;
        }

        public void BlJumpToApplication()
        {
            byte[] bb = { (byte)Commands.BL_JUMP_TO_APP };
            SendBINCommand(bb, 0, 1, null, 0);
        }

        public int SendBINCommand(byte[] outBuffer, int outOffset, int outCount, byte[]? respBuffer = null, int timeOut = 5000)
        {
            if (_transport == null)
            {
                return -1;
            }

            int rv = 0;
            byte[] ob = new byte[64];
            if (outCount > 0)
            {
                lock (_toTimer)
                {
                    Array.Copy(outBuffer, outOffset, ob, 0, outCount);
                    if (timeOut > 0)
                    {
                        CommandFeedBack.IsEmpty = true;
                        rv = -1;
                        _toTimer.Restart();
                    }

                    _transport.Write(ref ob, 0, 64);

                    // wait for feedback
                    if (timeOut > 0)
                    {
                        while (_toTimer.ElapsedMilliseconds < timeOut)
                        {
                            if (!CommandFeedBack.IsEmpty)
                            {
                                if (outBuffer[outOffset] == CommandFeedBack.Command)
                                {
                                    rv = CommandFeedBack.Status;
                                    if (respBuffer != null)
                                    {
                                        Array.Copy(CommandFeedBack.Data, respBuffer, Math.Min(respBuffer.Length, (int)CommandFeedBack.DataLength));
                                    }
                                    break;
                                }
                                else
                                {
                                    CommandFeedBack.IsEmpty = true;
                                }
                            }
                        }
                        _toTimer.Stop();
                    }
                }
            }
            return rv;
        }

        public void ProcessReceivedBytes(byte[] rxb)
        {
            ProcessRXMessage(ref rxb);
        }

        private void Transport_DataReceived(object? sender, EventArgs e)
        {
            if (_transport == null) return;

            byte[] rxb = new byte[65];
            while (_transport.RXDataCount >= 64)
            {
                _transport.Read(ref rxb, 0, 64);
                ProcessRXMessage(ref rxb);
            }
        }

        private bool ProcessRXMessage(ref byte[] rxb)
        {
            switch (rxb[0])
            {
                case 1:
                    InstrumentChange?.Invoke(this, EventArgs.Empty);
                    break;
                case 3:
                    LiveDataReceived?.Invoke(this, new LiveDataReceivedEventData { Data = rxb });
                    break;
                default:
                    CommandFeedBack.ReadFromBuffer(ref rxb, 0, 64);
                    break;
            }
            return true;
        }

        public ushort CalculateCRC(byte[] data, int boff, int blen)
        {
            ushort[] crcTable = {
                0x0,
                0x1021,
                0x2042,
                0x3063,
                0x4084,
                0x50a5,
                0x60c6,
                0x70e7,
                0x8108,
                0x9129,
                0xa14a,
                0xb16b,
                0xc18c,
                0xd1ad,
                0xe1ce,
                0xf1ef
            };
            ushort crc = 0;

            while (blen > 0)
            {
                uint i = (uint)((crc >> 12) ^ (data[boff] >> 4));
                crc = (ushort)(crcTable[i & 15] ^ (crc << 4));
                i = (uint)((crc >> 12) ^ (uint)data[boff]);
                crc = (ushort)(crcTable[i & 15] ^ (crc << 4));
                boff++;
                blen--;
            }
            return crc;
        }
    }
}
