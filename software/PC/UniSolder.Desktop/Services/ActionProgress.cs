using System;

namespace UniSolder.Desktop.Services
{
    public class ActionProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public ActionProgress(Action<T> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(T value) => _handler(value);
    }
}
