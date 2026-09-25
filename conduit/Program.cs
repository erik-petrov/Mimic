using System;

namespace Conduit
{
    class Program
    {
        public static string APP_NAME = "Mimic Conduit";
        public static string VERSION = "2.2.0";

        // The Rift server to register with. Defaults to the shared one; to use your own, write its
        // address (like https://mimic.example.com) to %APPDATA%\Mimic\server and restart Conduit.
        public static string HUB = Persistence.GetServerAddress();
        public static string HUB_WS = (HUB.StartsWith("https://") ? "wss://" + HUB.Substring("https://".Length) : "ws://" + HUB.Substring("http://".Length)) + "/conduit";

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
