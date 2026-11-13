namespace SSComm
{
    /// <summary>
    /// Legacy SSComm.USBHID compatibility class implemented on top of UniSolder.Core.UsbHidTransport.
    /// </summary>
    public class USBHID : UniSolder.Core.UsbHidTransport
    {
        public USBHID() : base()
        {
        }

        public USBHID(int vid, int pid) : base(vid, pid)
        {
        }
    }
}
