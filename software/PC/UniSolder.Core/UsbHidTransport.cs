using System;
using System.IO;
using System.Linq;
using System.Threading;
using HidSharp;
using SSComm;

namespace UniSolder.Core
{
    /// <summary>
    /// Cross-platform USB HID transport implementation using HidSharp.
    /// Compatible with Windows, Linux (/dev/hidraw), and macOS.
    /// </summary>
    public class UsbHidTransport : IUniComm
    {
        public const int RX_FIFO_SIZE = 32768;
        public const int TX_FIFO_SIZE = 16384;
        public const int DEFAULT_VID = 0x04D8; // Microchip Technology Inc.
        public const int DEFAULT_PID = 0x003C; // UniSolder 5.2

        public int DevVID { get; set; } = DEFAULT_VID;
        public int DevPID { get; set; } = DEFAULT_PID;
        public int DevRevision { get; set; } = int.MaxValue;
        public string DevSerialNumber { get; set; } = string.Empty;
        public Guid DevGUID { get; set; } = Guid.Empty;
        public string? DevicePath { get; private set; }

        public bool Connected { get; private set; }

        public event EventHandler? DataReceived;

        private int _bytesSent;
        private int _bytesReceived;

        private readonly byte[] _rxFifo = new byte[RX_FIFO_SIZE];
        private int _rxReadPos;
        private int _rxWritePos;
        private readonly object _rxLock = new object();

        private readonly byte[] _txFifo = new byte[TX_FIFO_SIZE];
        private int _txReadPos;
        private int _txWritePos;
        private readonly object _txLock = new object();

        private readonly AutoResetEvent _txSignal = new AutoResetEvent(false);
        private Thread? _readerThread;
        private Thread? _writerThread;
        private volatile bool _stopRequested;

        private HidDevice? _hidDevice;
        private Stream? _hidStream;
        private int _maxInputReportLength = 65;
        private int _maxOutputReportLength = 65;

        public UsbHidTransport()
        {
        }

        public UsbHidTransport(int vid, int pid)
        {
            DevVID = vid;
            DevPID = pid;
        }

        public int BytesSent() => _bytesSent;
        public int BytesReceived() => _bytesReceived;

        public void Init()
        {
            Disconnect();
            _bytesSent = 0;
            _bytesReceived = 0;
            lock (_rxLock)
            {
                _rxReadPos = 0;
                _rxWritePos = 0;
            }
            lock (_txLock)
            {
                _txReadPos = 0;
                _txWritePos = 0;
            }
        }

        public int RXDataCount
        {
            get
            {
                lock (_rxLock)
                {
                    int count = _rxWritePos - _rxReadPos;
                    if (count < 0) count += RX_FIFO_SIZE;
                    return count;
                }
            }
        }

        public int TXFreeSpace
        {
            get
            {
                lock (_txLock)
                {
                    int used = _txWritePos - _txReadPos;
                    if (used < 0) used += TX_FIFO_SIZE;
                    return TX_FIFO_SIZE - 1 - used;
                }
            }
        }

        public byte ReadByte()
        {
            lock (_rxLock)
            {
                if (_rxWritePos == _rxReadPos)
                {
                    throw new InvalidOperationException("RX FIFO is empty.");
                }

                byte val = _rxFifo[_rxReadPos];
                _rxReadPos = (_rxReadPos + 1) % RX_FIFO_SIZE;
                return val;
            }
        }

        public int Read(ref byte[] buffer, int offset, int maxBytes)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (maxBytes <= 0) return 0;

            lock (_rxLock)
            {
                int available = _rxWritePos - _rxReadPos;
                if (available < 0) available += RX_FIFO_SIZE;

                int toRead = Math.Min(available, maxBytes);
                for (int i = 0; i < toRead; i++)
                {
                    buffer[offset + i] = _rxFifo[_rxReadPos];
                    _rxReadPos = (_rxReadPos + 1) % RX_FIFO_SIZE;
                }
                return toRead;
            }
        }

        public void WriteByte(ref byte wb, bool doWake = true)
        {
            lock (_txLock)
            {
                int used = _txWritePos - _txReadPos;
                if (used < 0) used += TX_FIFO_SIZE;
                if (used >= TX_FIFO_SIZE - 1)
                {
                    throw new InvalidOperationException("TX FIFO is full.");
                }

                _txFifo[_txWritePos] = wb;
                _txWritePos = (_txWritePos + 1) % TX_FIFO_SIZE;
            }

            if (doWake)
            {
                _txSignal.Set();
            }
        }

        public bool Write(ref byte[] buffer, int offset, int numBytes)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (numBytes <= 0) return true;

            lock (_txLock)
            {
                int used = _txWritePos - _txReadPos;
                if (used < 0) used += TX_FIFO_SIZE;
                int free = TX_FIFO_SIZE - 1 - used;

                if (numBytes > free)
                {
                    throw new InvalidOperationException($"Not enough space in TX buffer. Required: {numBytes}, Available: {free}");
                }

                for (int i = 0; i < numBytes; i++)
                {
                    _txFifo[_txWritePos] = buffer[offset + i];
                    _txWritePos = (_txWritePos + 1) % TX_FIFO_SIZE;
                }
            }

            _txSignal.Set();
            return true;
        }

        public virtual bool Connect()
        {
            if (Connected)
            {
                Disconnect();
            }

            try
            {
                var devices = DeviceList.Local.GetHidDevices(DevVID, DevPID).ToList();
                if (!string.IsNullOrEmpty(DevSerialNumber))
                {
                    devices = devices.Where(d =>
                    {
                        try { return d.GetSerialNumber() == DevSerialNumber; }
                        catch { return false; }
                    }).ToList();
                }

                if (devices.Count == 0)
                {
                    Connected = false;
                    return false;
                }

                _hidDevice = devices.First();
                DevicePath = _hidDevice.DevicePath;

                if (!_hidDevice.TryOpen(out HidStream? stream) || stream == null)
                {
                    Connected = false;
                    return false;
                }

                _hidStream = stream;
                _maxInputReportLength = _hidDevice.GetMaxInputReportLength();
                if (_maxInputReportLength <= 0) _maxInputReportLength = 65;

                _maxOutputReportLength = _hidDevice.GetMaxOutputReportLength();
                if (_maxOutputReportLength <= 0) _maxOutputReportLength = 65;

                _stopRequested = false;
                _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "UsbHidReaderThread" };
                _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "UsbHidWriterThread" };

                _readerThread.Start();
                _writerThread.Start();

                Connected = true;
                return true;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        public void AttachStream(Stream stream, int maxInputLength = 65, int maxOutputLength = 65)
        {
            Disconnect();
            _hidStream = stream ?? throw new ArgumentNullException(nameof(stream));
            _maxInputReportLength = maxInputLength;
            _maxOutputReportLength = maxOutputLength;

            _stopRequested = false;
            _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "UsbHidReaderThread" };
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "UsbHidWriterThread" };

            _readerThread.Start();
            _writerThread.Start();
            Connected = true;
        }

        public virtual void Disconnect()
        {
            _stopRequested = true;
            _txSignal.Set();

            if (_hidStream != null)
            {
                try
                {
                    _hidStream.Dispose();
                }
                catch { }
                _hidStream = null;
            }

            if (_readerThread != null && _readerThread.IsAlive)
            {
                _readerThread.Join(500);
            }
            _readerThread = null;

            if (_writerThread != null && _writerThread.IsAlive)
            {
                _writerThread.Join(500);
            }
            _writerThread = null;

            _hidDevice = null;
            DevicePath = null;
            Connected = false;
        }

        public void Dispose()
        {
            Disconnect();
            _txSignal.Dispose();
        }

        private void ReaderLoop()
        {
            byte[] inBuffer = new byte[_maxInputReportLength];

            while (!_stopRequested)
            {
                var stream = _hidStream;
                if (stream == null) break;

                try
                {
                    int bytesRead = stream.Read(inBuffer, 0, inBuffer.Length);
                    if (bytesRead <= 0)
                    {
                        if (_stopRequested) break;
                        Thread.Sleep(5);
                        continue;
                    }

                    Interlocked.Add(ref _bytesReceived, bytesRead);

                    int dataOffset = (_maxInputReportLength > 64) ? 1 : 0;
                    int dataLen = bytesRead - dataOffset;
                    if (dataLen > 0)
                    {
                        bool overflow = false;
                        lock (_rxLock)
                        {
                            int free = RX_FIFO_SIZE - 1 - (_rxWritePos >= _rxReadPos ? _rxWritePos - _rxReadPos : RX_FIFO_SIZE - (_rxReadPos - _rxWritePos));
                            if (dataLen > free)
                            {
                                overflow = true;
                            }
                            else
                            {
                                for (int i = 0; i < dataLen; i++)
                                {
                                    _rxFifo[_rxWritePos] = inBuffer[dataOffset + i];
                                    _rxWritePos = (_rxWritePos + 1) % RX_FIFO_SIZE;
                                }
                            }
                        }

                        if (!overflow)
                        {
                            DataReceived?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
                catch (IOException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    if (!_stopRequested)
                    {
                        Thread.Sleep(10);
                    }
                }
            }
        }

        private void WriterLoop()
        {
            int payloadLength = (_maxOutputReportLength > 64) ? _maxOutputReportLength - 1 : _maxOutputReportLength;
            byte[] outBuffer = new byte[_maxOutputReportLength];

            while (!_stopRequested)
            {
                var stream = _hidStream;
                if (stream == null) break;

                int available;
                lock (_txLock)
                {
                    available = _txWritePos - _txReadPos;
                    if (available < 0) available += TX_FIFO_SIZE;
                }

                if (available >= payloadLength)
                {
                    Array.Clear(outBuffer, 0, outBuffer.Length);
                    int offset = (_maxOutputReportLength > 64) ? 1 : 0;

                    lock (_txLock)
                    {
                        for (int i = 0; i < payloadLength; i++)
                        {
                            outBuffer[offset + i] = _txFifo[_txReadPos];
                            _txReadPos = (_txReadPos + 1) % TX_FIFO_SIZE;
                        }
                    }

                    try
                    {
                        stream.Write(outBuffer, 0, outBuffer.Length);
                        stream.Flush();
                        Interlocked.Add(ref _bytesSent, payloadLength);
                    }
                    catch
                    {
                        break;
                    }
                }
                else
                {
                    _txSignal.WaitOne(100);
                }
            }
        }
    }
}
