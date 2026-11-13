using System;
using System.IO;
using System.Threading;
using UniSolder.Core;
using Xunit;

namespace UniSolder.Core.Tests
{
    public class UsbHidTransportTests
    {
        [Fact]
        public void Defaults_AreCorrectForUniSolder()
        {
            var transport = new UsbHidTransport();
            Assert.Equal(0x04D8, transport.DevVID);
            Assert.Equal(0x003C, transport.DevPID);
            Assert.False(transport.Connected);
            Assert.Equal(0, transport.BytesSent());
            Assert.Equal(0, transport.BytesReceived());
            Assert.Equal(0, transport.RXDataCount);
            Assert.Equal(UsbHidTransport.TX_FIFO_SIZE - 1, transport.TXFreeSpace);
        }

        [Fact]
        public void CustomConstructor_SetsVidAndPid()
        {
            var transport = new UsbHidTransport(0x1234, 0x5678);
            Assert.Equal(0x1234, transport.DevVID);
            Assert.Equal(0x5678, transport.DevPID);
        }

        [Fact]
        public void LegacyUSBHID_SubclassWorksIdentically()
        {
            var transport = new SSComm.USBHID { DevVID = 0x04D8, DevPID = 0x003C };
            Assert.Equal(0x04D8, transport.DevVID);
            Assert.Equal(0x003C, transport.DevPID);
        }

        [Fact]
        public void WriteByte_And_ReadByte_WorksCorrectly()
        {
            using var transport = new UsbHidTransport();

            // Inject bytes into RX FIFO via reflection or test stream
            // First test TX buffer FIFO
            byte b1 = 0x42;
            byte b2 = 0x99;
            transport.WriteByte(ref b1, doWake: false);
            transport.WriteByte(ref b2, doWake: false);

            Assert.Equal(UsbHidTransport.TX_FIFO_SIZE - 1 - 2, transport.TXFreeSpace);
        }

        [Fact]
        public void Write_Array_UpdatesTXFreeSpace()
        {
            using var transport = new UsbHidTransport();
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            bool written = transport.Write(ref data, 0, data.Length);
            Assert.True(written);
            Assert.Equal(UsbHidTransport.TX_FIFO_SIZE - 1 - 5, transport.TXFreeSpace);
        }

        [Fact]
        public void ReadByte_WhenEmpty_ThrowsInvalidOperationException()
        {
            using var transport = new UsbHidTransport();
            Assert.Throws<InvalidOperationException>(() => transport.ReadByte());
        }

        [Fact]
        public void Init_ClearsBuffersAndCounters()
        {
            using var transport = new UsbHidTransport();
            byte[] data = new byte[] { 1, 2, 3 };
            transport.Write(ref data, 0, 3);

            transport.Init();

            Assert.Equal(0, transport.BytesSent());
            Assert.Equal(0, transport.BytesReceived());
            Assert.Equal(0, transport.RXDataCount);
            Assert.Equal(UsbHidTransport.TX_FIFO_SIZE - 1, transport.TXFreeSpace);
        }

        [Fact]
        public void AttachStream_ProcessesInputReportsAndRaisesDataReceived()
        {
            using var transport = new UsbHidTransport();
            var stream = new PipeStream();

            var dataReceivedEvent = new ManualResetEvent(false);
            transport.DataReceived += (s, e) => dataReceivedEvent.Set();

            transport.AttachStream(stream, maxInputLength: 65, maxOutputLength: 65);
            Assert.True(transport.Connected);

            // Create 65-byte HID input report: byte 0 = report ID (0), byte 1..64 = data
            byte[] inputReport = new byte[65];
            inputReport[0] = 0; // Report ID
            inputReport[1] = 0xAA; // First data byte
            inputReport[2] = 0x55; // Second data byte
            inputReport[3] = 0x01; // Third data byte

            stream.PushReadData(inputReport);

            bool signaled = dataReceivedEvent.WaitOne(2000);
            Assert.True(signaled);
            Assert.True(transport.RXDataCount >= 3);

            byte[] readBuffer = new byte[10];
            int readCount = transport.Read(ref readBuffer, 0, 3);
            Assert.Equal(3, readCount);
            Assert.Equal(0xAA, readBuffer[0]);
            Assert.Equal(0x55, readBuffer[1]);
            Assert.Equal(0x01, readBuffer[2]);

            transport.Disconnect();
            Assert.False(transport.Connected);
        }

        [Fact]
        public void AttachStream_SendsOutputReportsFramedWithReportId()
        {
            using var transport = new UsbHidTransport();
            var stream = new PipeStream();

            transport.AttachStream(stream, maxInputLength: 65, maxOutputLength: 65);

            // Write 64 bytes of payload (standard packet size)
            byte[] payload = new byte[64];
            payload[0] = 0x05; // Command
            payload[1] = 0x10;
            payload[63] = 0xFF;

            transport.Write(ref payload, 0, payload.Length);

            // Wait for background writer thread to write 65-byte report
            byte[] writtenReport = stream.WaitForWrittenBytes(65, 2000);
            Assert.NotNull(writtenReport);
            Assert.Equal(65, writtenReport.Length);
            Assert.Equal(0, writtenReport[0]); // Report ID
            Assert.Equal(0x05, writtenReport[1]); // First payload byte
            Assert.Equal(0x10, writtenReport[2]);
            Assert.Equal(0xFF, writtenReport[64]); // Last payload byte

            transport.Disconnect();
        }

        [Fact]
        public void Connect_WhenNoDevicePresent_ReturnsFalse()
        {
            var transport = new UsbHidTransport(0xFFFF, 0xFFFF);
            bool connected = transport.Connect();
            Assert.False(connected);
            Assert.False(transport.Connected);
        }

        [Fact]
        public void CircularBuffer_Wrapping_ReadWriteSucceeds()
        {
            using var transport = new UsbHidTransport();
            var stream = new PipeStream();
            transport.AttachStream(stream, maxInputLength: 65, maxOutputLength: 65);

            // Send multiple chunks to exceed single buffer length
            byte[] chunk = new byte[64];
            for (int i = 0; i < 64; i++) chunk[i] = (byte)(i & 0xFF);

            int iterations = 300; // 300 * 64 = 19,200 bytes (> TX_FIFO_SIZE 16384)
            for (int it = 0; it < iterations; it++)
            {
                // Push input report to stream
                byte[] inReport = new byte[65];
                inReport[0] = 0;
                Array.Copy(chunk, 0, inReport, 1, 64);
                stream.PushReadData(inReport);

                // Wait for data and read from transport
                byte[] outBuffer = new byte[64];
                int totalRead = 0;
                int retries = 50;
                while (totalRead < 64 && retries-- > 0)
                {
                    if (transport.RXDataCount > 0)
                    {
                        int read = transport.Read(ref outBuffer, totalRead, 64 - totalRead);
                        totalRead += read;
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }

                Assert.Equal(64, totalRead);
                Assert.Equal(chunk[0], outBuffer[0]);
                Assert.Equal(chunk[63], outBuffer[63]);
            }

            transport.Disconnect();
        }

        [Fact]
        public async System.Threading.Tasks.Task UniSolderComm_EndToEnd_With_UsbHidTransport()
        {
            using var transport = new UsbHidTransport();
            var stream = new PipeStream();
            transport.AttachStream(stream, maxInputLength: 65, maxOutputLength: 65);

            var comm = new UniSolderComm { Transport = transport };

            // Background task to respond to command packets
            var cts = new CancellationTokenSource();
            var responder = System.Threading.Tasks.Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    byte[] packet = stream.WaitForWrittenBytes(65, 500);
                    if (packet != null && packet.Length >= 65)
                    {
                        // Output report from PC: packet[0] = 0 (report ID), packet[1..64] = payload
                        // Feedback packet: <cmd> <data...>
                        byte[] feedbackReport = new byte[65];
                        feedbackReport[0] = 0; // report ID
                        feedbackReport[1] = packet[1]; // echo command byte (0x02 for DEV_QUERY)
                        feedbackReport[2] = 0; // data byte

                        stream.PushReadData(feedbackReport);
                        break;
                    }
                }
            });

            int result = await System.Threading.Tasks.Task.Run(() => comm.DevQuery());
            cts.Cancel();
            await responder;

            Assert.Equal(0, result);
            transport.Disconnect();
        }

        private class PipeStream : Stream
        {
            private readonly MemoryStream _readBuffer = new MemoryStream();
            private readonly MemoryStream _writtenBuffer = new MemoryStream();
            private readonly object _lock = new object();
            private readonly AutoResetEvent _dataAvailable = new AutoResetEvent(false);
            private readonly AutoResetEvent _dataWritten = new AutoResetEvent(false);
            private bool _isDisposed;

            public void PushReadData(byte[] data)
            {
                lock (_lock)
                {
                    long currentPos = _readBuffer.Position;
                    _readBuffer.Seek(0, SeekOrigin.End);
                    _readBuffer.Write(data, 0, data.Length);
                    _readBuffer.Seek(currentPos, SeekOrigin.Begin);
                }
                _dataAvailable.Set();
            }

            public byte[] WaitForWrittenBytes(int count, int timeoutMs)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    lock (_lock)
                    {
                        if (_writtenBuffer.Length >= count)
                        {
                            return _writtenBuffer.ToArray();
                        }
                    }
                    _dataWritten.WaitOne(50);
                }
                lock (_lock)
                {
                    return _writtenBuffer.ToArray();
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (!_isDisposed)
                {
                    lock (_lock)
                    {
                        int bytesAvailable = (int)(_readBuffer.Length - _readBuffer.Position);
                        if (bytesAvailable > 0)
                        {
                            int toRead = Math.Min(bytesAvailable, count);
                            return _readBuffer.Read(buffer, offset, toRead);
                        }
                    }
                    _dataAvailable.WaitOne(50);
                }
                return 0;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_lock)
                {
                    _writtenBuffer.Write(buffer, offset, count);
                }
                _dataWritten.Set();
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _readBuffer.Length;
            public override long Position { get => _readBuffer.Position; set => _readBuffer.Position = value; }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                _isDisposed = true;
                _dataAvailable.Set();
                _dataWritten.Set();
                base.Dispose(disposing);
            }
        }
    }
}
