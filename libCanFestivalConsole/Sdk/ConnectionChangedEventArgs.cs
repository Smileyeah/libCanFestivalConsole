using System;

namespace libCanFestivalConsole.Sdk
{
    public class ConnectionChangedEventArgs : EventArgs
    {
        public bool Connected
        {
            get;
            set;
        }

        public ConnectionChangedEventArgs(bool connected)
        {
            Connected = connected;
        }
    }
}
