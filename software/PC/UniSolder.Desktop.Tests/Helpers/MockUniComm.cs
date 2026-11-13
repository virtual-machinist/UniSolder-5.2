using System;
using System.Collections.Generic;
using UniSolder.Core;

namespace UniSolder.Desktop.Tests.Helpers
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
}
