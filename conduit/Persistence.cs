using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace Conduit
{
    /**
     * Class responsible for handling filesystem persistence. In particular, this stores our JWT and keypairs.
     */
    class Persistence
    {
        public static string DATA_DIRECTORY = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mimic");
        public static event Action OnHubCodeChanged;

        private static readonly string HUB_TOKEN_PATH = Path.Combine(DATA_DIRECTORY, "token");
        private static readonly string KEYPAIR_PATH = Path.Combine(DATA_DIRECTORY, "keys");
        private static readonly string DEVICES_PATH = Path.Combine(DATA_DIRECTORY, "devices");
        private static readonly string SERVER_PATH = Path.Combine(DATA_DIRECTORY, "server");
        private static readonly string AUTOPICK_PATH = Path.Combine(DATA_DIRECTORY, "autopick.json");

        public const string DEFAULT_SERVER = "https://rift.mimic.lol";
        private static readonly RegistryKey BOOT_KEY = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

        // Where Task Manager's Startup tab keeps apps that were switched off there.
        private const string STARTUP_APPROVED_PATH = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";

        // Passed to Conduit when Windows starts it, so it knows it wasn't started by hand.
        public const string AUTOSTART_ARGUMENT = "--autostart";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFile(string path);

        static Persistence()
        {
            // Create directory if needed.
            if (!Directory.Exists(DATA_DIRECTORY)) Directory.CreateDirectory(DATA_DIRECTORY);
        }

        /**
         * Returns the address of the Rift server to use, without a trailing slash. This is the
         * contents of the "server" file in the data directory if it holds an http(s) address,
         * or the shared server otherwise.
         */
        public static string GetServerAddress()
        {
            try
            {
                if (File.Exists(SERVER_PATH))
                {
                    var address = File.ReadAllText(SERVER_PATH).Trim().TrimEnd('/');
                    if (address.StartsWith("https://") || address.StartsWith("http://")) return address;
                }
            }
            catch
            {
                // Fall back to the shared server if the file can't be read.
            }

            return DEFAULT_SERVER;
        }

        /**
         * Stores the address of the Rift server to use. Null or empty goes back to the shared server.
         */
        public static void SetServerAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address) || address.Trim().TrimEnd('/') == DEFAULT_SERVER)
            {
                if (File.Exists(SERVER_PATH)) File.Delete(SERVER_PATH);
                return;
            }

            File.WriteAllText(SERVER_PATH, address.Trim().TrimEnd('/'));
        }

        /**
         * Returns the stored autopick setup (JSON), or null if there is none.
         */
        public static string GetAutopickSetup()
        {
            try
            {
                return File.Exists(AUTOPICK_PATH) ? File.ReadAllText(AUTOPICK_PATH) : null;
            }
            catch
            {
                return null;
            }
        }

        /**
         * Stores the autopick setup (JSON).
         */
        public static void SetAutopickSetup(string setup)
        {
            File.WriteAllText(AUTOPICK_PATH, setup);
        }

        /**
         * Returns the stored hub token for this computer, or null if none are found.
         */
        public static string GetHubToken()
        {
            try
            {
                if (!File.Exists(HUB_TOKEN_PATH)) return null;
                return File.ReadAllText(HUB_TOKEN_PATH);
            }
            catch
            {
                // If we have an error, just ignore it and return null.
                return null;
            }
        }

        /**
         * Returns the code that needs to be entered on the mobile interface to connect to this
         * phone. Returns null if no token is received yet.
         */
        public static string GetHubCode()
        {
            var token = GetHubToken();
            if (token == null) return null;

            var base64Json = token.Split('.')[1];

            // We need to pad to the nearest multiple of 4 here since jwts are stored without padding =s.
            var jsonContents = Encoding.UTF8.GetString(Convert.FromBase64String(base64Json.PadRight(4 * ((base64Json.Length + 3) / 4), '=')));
            return SimpleJson.DeserializeObject<dynamic>(jsonContents)["code"];
        }

        /**
         * Returns the code split in two halves, so it's easier to read and type. Phones
         * accept it with or without the space.
         */
        public static string FormatCode(string code)
        {
            if (code == null || code.Length != 10) return code;
            return code.Substring(0, 5) + " " + code.Substring(5);
        }

        /**
         * Writes the specified new hub JWT to storage.
         */
        public static void SetHubToken(string token)
        {
            File.WriteAllText(HUB_TOKEN_PATH, token);

            // Invoke listeners
            OnHubCodeChanged?.Invoke();
        }

        /**
         * Forgets the hub token, so that Conduit registers again. Used when switching servers,
         * since a token (and its code) only works on the server that gave it out.
         */
        public static void ClearHubToken()
        {
            try
            {
                if (File.Exists(HUB_TOKEN_PATH)) File.Delete(HUB_TOKEN_PATH);
            }
            catch
            {
                // Registering again replaces it anyway.
            }

            OnHubCodeChanged?.Invoke();
        }

        /**
         * Checks if the specified device UUID has been seen and approved before.
         */
        public static bool IsDeviceApproved(string deviceUUID)
        {
            try
            {
                if (!File.Exists(DEVICES_PATH)) return false;

                var contents = File.ReadAllLines(DEVICES_PATH);
                return contents.Any(x => x == deviceUUID);
            }
            catch
            {
                // Ignore errors.
                return false;
            }
        }

        /**
         * Adds the specified device UUID to the list of approved devices. Note: this
         * does not check if the device was previously approved, calling this with an
         * approved device will lead to duplicate entries.
         */
        public static void ApproveDevice(string deviceUUID)
        {
            try
            {
                // Simply append the UUID to the list of approved devices. This will
                // create the file if it did not yet exist.
                File.AppendAllText(DEVICES_PATH, deviceUUID + "\n");
            }
            catch
            {
                // Ignore errors.
            }
        }

        /**
         * The command Windows runs at startup. The path is quoted, because Windows can't start an
         * unquoted path with spaces in it (like C:\Users\First Last\Downloads\Conduit.exe).
         */
        public static string StartupCommand()
        {
            return "\"" + Assembly.GetExecutingAssembly().Location + "\" " + AUTOSTART_ARGUMENT;
        }

        /**
         * Checks if conduit is configured to launch at startup, and not switched off in Task Manager.
         */
        public static bool LaunchesAtStartup()
        {
            var exists = BOOT_KEY.GetValue(Program.APP_NAME) != null;

            // Update the command, in case Conduit moved or an older version wrote it unquoted.
            if (exists && (string) BOOT_KEY.GetValue(Program.APP_NAME) != StartupCommand())
            {
                BOOT_KEY.SetValue(Program.APP_NAME, StartupCommand());
            }

            return exists && !IsDisabledInTaskManager();
        }

        /**
         * Toggles whether or not mimic should launch at startup.
         */
        public static void ToggleLaunchAtStartup()
        {
            if (LaunchesAtStartup())
            {
                BOOT_KEY.DeleteValue(Program.APP_NAME, false);
                return;
            }

            BOOT_KEY.SetValue(Program.APP_NAME, StartupCommand());

            // Switching it on here also undoes switching it off in Task Manager.
            try
            {
                using (var approved = Registry.CurrentUser.OpenSubKey(STARTUP_APPROVED_PATH, true))
                {
                    approved?.DeleteValue(Program.APP_NAME, false);
                }
            }
            catch
            {
                // Nothing to undo.
            }

            // Windows marks downloaded files, and can ask "Do you want to run this file?" before
            // starting a marked file at startup. Remove the mark, like Properties > Unblock does.
            DeleteFile(Assembly.GetExecutingAssembly().Location + ":Zone.Identifier");
        }

        /**
         * Whether Conduit was switched off in Task Manager's Startup tab. Windows keeps that as a
         * binary value whose first byte is odd when the app is switched off.
         */
        private static bool IsDisabledInTaskManager()
        {
            try
            {
                using (var approved = Registry.CurrentUser.OpenSubKey(STARTUP_APPROVED_PATH, false))
                {
                    var value = approved?.GetValue(Program.APP_NAME) as byte[];
                    return value != null && value.Length > 0 && (value[0] & 1) == 1;
                }
            }
            catch
            {
                return false;
            }
        }

        /**
         * Either loads the stored keys into a new RSACryptoServiceProvider, or generates
         * new keys and stores them.
         */
        public static RSACryptoServiceProvider GetRSAProvider()
        {
            try
            {
                // The stored file doesn't exist, generate a new one.
                if (!File.Exists(KEYPAIR_PATH)) return GenerateAndStoreKeys();

                // Else, import from XML.
                var reader = new StringReader(File.ReadAllText(KEYPAIR_PATH));
                var deserializer = new XmlSerializer(typeof(RSAParameters));
                var rsaParams = (RSAParameters) deserializer.Deserialize(reader);

                var provider = new RSACryptoServiceProvider();
                provider.ImportParameters(rsaParams);

                return provider;
            }
            catch
            {
                // Something bad happened. Regen our keys.
                return GenerateAndStoreKeys();
            }
        }

        /**
         * Utility helper to generate and store a new set of 2048bit RSA keys.
         */
        private static RSACryptoServiceProvider GenerateAndStoreKeys()
        {
            var provider = new RSACryptoServiceProvider(2048);

            // Write the constants as XML to file.
            var writer = new StringWriter();
            var serializer = new XmlSerializer(typeof(RSAParameters));
            serializer.Serialize(writer, provider.ExportParameters(true));
            File.WriteAllText(KEYPAIR_PATH, writer.ToString());

            return provider;
        }
    }
}
