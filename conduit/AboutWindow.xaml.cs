using QRCoder;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Conduit
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public AboutWindow()
        {
            InitializeComponent();
            Logo.Source = Imaging.CreateBitmapSourceFromHIcon(Properties.Resources.mimic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            StartOnStartupCheckbox.IsChecked = Persistence.LaunchesAtStartup();

            AboutTitle.Content = "Mimic Conduit v" + Program.VERSION;

            ServerBox.Text = Program.HUB == Persistence.DEFAULT_SERVER ? "" : Program.HUB;
            ShowServer(null);

            if (Persistence.GetHubCode() != null)
            {
                RenderCode();
            }
            Persistence.OnHubCodeChanged += RenderCode;
        }

        /**
         * Shows which server Conduit uses, with an optional extra line.
         */
        private void ShowServer(string extra)
        {
            var text = Program.HUB == Persistence.DEFAULT_SERVER
                ? "Using the shared server (" + Persistence.DEFAULT_SERVER + "). To use your own, enter the address you open Mimic at on your phone."
                : "Using your server " + Program.HUB + ".";
            ServerStatus.Text = extra == null ? text : text + " " + extra;
        }

        /**
         * Switches to the server in the box, after checking that a Mimic server answers there.
         */
        private async void UseServer(object sender, RoutedEventArgs e)
        {
            var address = ServerBox.Text.Trim().TrimEnd('/');
            if (address == "")
            {
                UseSharedServer(sender, e);
                return;
            }

            if (!address.StartsWith("http://") && !address.StartsWith("https://")) address = "https://" + address;
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host))
            {
                ServerStatus.Text = "\"" + ServerBox.Text + "\" isn't a web address. Enter one like https://mimic.example.com.";
                return;
            }
            if (address == Program.HUB)
            {
                ShowServer(null);
                return;
            }

            UseServerButton.IsEnabled = false;
            SharedServerButton.IsEnabled = false;
            ServerStatus.Text = "Checking " + address + "...";
            var problem = await CheckServer(address);
            UseServerButton.IsEnabled = true;
            SharedServerButton.IsEnabled = true;

            if (problem != null)
            {
                ServerStatus.Text = problem + " Conduit keeps using " + Program.HUB + ".";
                return;
            }

            SwitchTo(address);
        }

        private void UseSharedServer(object sender, RoutedEventArgs e)
        {
            ServerBox.Text = "";
            if (Program.HUB == Persistence.DEFAULT_SERVER)
            {
                ShowServer(null);
                return;
            }

            SwitchTo(Persistence.DEFAULT_SERVER);
        }

        private void SwitchTo(string address)
        {
            ((App) Application.Current).ChangeServer(address);
            ServerBox.Text = Program.HUB == Persistence.DEFAULT_SERVER ? "" : Program.HUB;
            ShowServer("Your code changes with the server: start League to get the new one, then enter it on your phone.");
        }

        /**
         * Asks the server's token check about a made-up token. Rift answers "false"; anything else
         * means it isn't a Mimic server. Returns what went wrong, or null.
         */
        private static async Task<string> CheckServer(string address)
        {
            try
            {
                var response = await httpClient.GetAsync(address + "/check?token=mimic");
                var body = (await response.Content.ReadAsStringAsync()).Trim();
                if (body == "false") return null;
                return "No Mimic server answered at " + address + " (it answered with status " + (int) response.StatusCode + ").";
            }
            catch (Exception ex)
            {
                var reason = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return "Could not reach " + address + ": " + reason;
            }
        }

        /**
         * Renders the current hub code, or the text for when there is none.
         */
        private void RenderCode()
        {
            Dispatcher.Invoke(() =>
            {
                AppAddress.Text = Program.HUB == Persistence.DEFAULT_SERVER ? "https://app.mimic.lol" : Program.HUB;

                // No code yet, like right after switching servers.
                if (Persistence.GetHubCode() == null)
                {
                    ConnectionQR.Visibility = Visibility.Hidden;
                    CodeLabel.Visibility = Visibility.Hidden;
                    ConnectionSteps.Visibility = Visibility.Hidden;
                    NoCodeText.Visibility = Visibility.Visible;
                    return;
                }

                QRCodeGenerator qrGenerator = new QRCodeGenerator();
                // With your own server, the web app is hosted at the same address as Rift.
                var url = Program.HUB == Persistence.DEFAULT_SERVER
                    ? "https://remote.mimic.lol/" + Persistence.GetHubCode()
                    : Program.HUB + "/?code=" + Persistence.GetHubCode();
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                XamlQRCode qrCode = new XamlQRCode(qrCodeData);

                ConnectionQR.Source = qrCode.GetGraphic(20);
                ConnectionQR.Visibility = Visibility.Visible;

                CodeLabel.Content = Persistence.FormatCode(Persistence.GetHubCode());
                CodeLabel.Visibility = Visibility.Visible;

                ConnectionSteps.Visibility = Visibility.Visible;
                NoCodeText.Visibility = Visibility.Hidden;
            });
        }

        /**
         * Opens the project github link in the default browser.
         */
        public void OpenGithub(object sender, EventArgs args)
        {
            Process.Start("https://github.com/molenzwiebel/mimic");
        }

        /**
         * Opens the project discord in the default browser.
         */
        public void OpenDiscord(object sender, EventArgs args)
        {
            Process.Start("https://discord.gg/bfxdsRC");
        }

        /**
         * (Attempts to) uninstall sentinel.
         */
        public void Uninstall(object sender, EventArgs args)
        {
            MessageBoxResult result = MessageBox.Show("Are you sure you want to uninstall Mimic Conduit? All files will be deleted.", "Mimic Conduit", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.No) return;

            // Step 1: Delete AppData.
            try { Directory.Delete(Persistence.DATA_DIRECTORY, true); } catch { /* ignored */ }

            // Step 2: Unlink launch-on-start if enabled.
            if (Persistence.LaunchesAtStartup()) Persistence.ToggleLaunchAtStartup();

            // Step 3: Delete Executable.
            Process.Start(new ProcessStartInfo
            {
                Arguments = "/C choice /C Y /N /D Y /T 3 & Del " + System.Reflection.Assembly.GetExecutingAssembly().Location,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                FileName = "cmd.exe"
            });

            // Step 4: Stop Program.
            Application.Current.Shutdown();
        }

        /**
         * Invoked when window closes, unregisters from persistence listeners.
         */
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Persistence.OnHubCodeChanged -= RenderCode;
        }

        private void HandleStartupChange(object sender, EventArgs e)
        {
            Persistence.ToggleLaunchAtStartup();
        }
    }
}
