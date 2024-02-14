using System.Globalization;
using SystemPlus.Extensions;
using SystemPlus.UI;
using SystemPlus.Vectors;

namespace ProjectEarthLauncher
{
    /*
     * TODO:
     * 
     */
    internal static class Program
    {
        public const string Version = "2.0.2";

#if WINDOWS
        [STAThread]
#endif
        static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");

            while (true)
            {
                prepScreen();
                Menu menu = new Menu(GeneralExtensions.ToBIG("ProjectEarth Launcher 2.0"), new string[] { "Start", "Install", "About", "Exit" });
                int selected = menu.Show(Vector2Int.Up);

                prepScreen();
                switch (selected)
                {
                    case 0:
                        Console.Clear();
                        Launcher.Launch();
                        break;
                    case 1:
                        prepScreen();
                        MenuSettings settings = new MenuSettings("Select which parts to install", new[] {
                            new MSIBool("Api", true) { dispType = 3 },
                            new MSIBool("Cloudburst", true) { dispType = 3 },
                            new MSIBool("TileServer", false) { dispType = 3 },
                        });
                        if (settings.Show(Vector2Int.Up * 2) == MenuSettings.STATUS.OK)
                        {
                            Console.Clear();
                            Installer.Install((bool)settings.lastValues[0].GetValue(), (bool)settings.lastValues[1].GetValue(), (bool)settings.lastValues[2].GetValue());
                        }
                        break;
                    case 2:
                        Console.Clear();
                        Console.WriteLine($"ProjectEarth Launcher, Version: {Version}");
                        Console.WriteLine("Contacts:");
                        Console.WriteLine(" -Discord: bitcodercz");
                        Console.WriteLine("Made possible by:");
                        Console.WriteLine(" -Project Earth Team - https://github.com/Project-Earth-Team");
                        Console.WriteLine(" -Jackcaver (Api, ApiData) - https://github.com/jackcaver");
                        Logger.PAKC();
                        break;
                    default:
                        return;
                }
            }
        }

        static void prepScreen()
        {
            Console.Clear();
            Console.Write("(Up, Down, Enter)");
        }
    }
}
