using System;

namespace SSComm
{
    public interface IUniComm : IDisposable
    {
        bool Connected { get; }
        int BytesSent();
        int BytesReceived();
        void Init();
        bool Connect();
        void Disconnect();
        byte ReadByte();
        int Read(ref byte[] DBuffer, int DOffset, int MaxBytes);
        void WriteByte(ref byte wb, bool DoWake = true);
        bool Write(ref byte[] DBuffer, int DOffset, int NumBytes);
        int RXDataCount { get; }
        int TXFreeSpace { get; }
        event EventHandler? DataReceived;
    }
}

namespace UniSolder.Core
{
    // Alias / interface in UniSolder.Core namespace
    public interface IUniComm : SSComm.IUniComm
    {
    }
}
