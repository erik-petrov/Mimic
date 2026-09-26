using System;

namespace Conduit
{
    class Program
    {
        public static string APP_NAME = "Mimic Conduit";
        public static string VERSION = "2.4.0";

        // The Rift server to register with. Defaults to the shared one; Settings can change it, and
        // it is kept in %APPDATA%\Mimic\server.
        public static string HUB = Persistence.GetServerAddress();
        public static string HUB_WS = WebsocketAddress(HUB);

        /**
         * Switches to the specified Rift server. The caller reconnects.
         */
        public static void SetServer(string address)
        {
            HUB = address;
            HUB_WS = WebsocketAddress(address);
        }

        private static string WebsocketAddress(string hub)
        {
            return (hub.StartsWith("https://") ? "wss://" + hub.Substring("https://".Length) : "ws://" + hub.Substring("http://".Length)) + "/conduit";
        }

        private static App _instance;

        [STAThread]
        public static void Main()
        {
            // Start the application.
            _instance = new App();
            _instance.InitializeComponent();
            _instance.Run();
        }
    }
}
