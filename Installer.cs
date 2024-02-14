using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProjectEarthLauncher.FileTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace ProjectEarthLauncher
{
    internal static class Installer
    {
        public static void Install(bool api, bool cloudburst, bool tileServer)
        {
            if (CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName != "eng")
            {
                Logger.Warning("Your current ui language isn't english, this might cause problems");
                if (!Input.YN("Do you want to continue anyway?", false))
                    return;
            }

            if (!Utils.TryPickFolder("Select install folder", out string installDir))
            {
                Logger.PAKC("You need to select install folder");
                return;
            }

            try
            {
                string configPath = Path.Combine(Environment.CurrentDirectory, "config.json");

                if (!File.Exists(configPath))
                {
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(Config.Default, Formatting.Indented));
                    Logger.PAKC($"Config file was created: {configPath}");
                }

                Config config;
                try
                {
                    config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                }
                catch
                {
                    Logger.PAKC("Config file is invalid, it will be overwritten");
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(Config.Default, Formatting.Indented));
                    config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                }

                Context context = new Context(config, installDir, api || cloudburst ? Utils.GetLocalIP() : null);

                if (api && !installApi(context)) return;
                if (cloudburst && !installCloudburst(context)) return;
                if (tileServer && !installTileServer(context)) return;
            }
            catch (Exception ex)
            {
                Logger.Info($"There was an exception during installation:");
                Logger.Exception(ex);
                Logger.PAKC();
                return;
            }

            Logger.PAKC("Installed");
        }

        private static bool installApi(Context context)
        {
            Logger.Info("**********Installing Api**********");

            // trying to install release while source is currenty installed, very bad things would probably happen
            // TODO: if this isn't repo, detect if source or release and do this check
            if (context.Config.Api.IsGitRepo && context.Api_IsReleaseNoUI() == 1)
            {
                Logger.PAKC("Api folder will be deleted, because current installation isn't compatible");
                Utils.DeleteDirectory(context.ApiDir);
            }

            if (!Directory.Exists(context.ApiDir) || Input.YN("Api folder already exists, do you want to download it again?", false))
            {
                Directory.CreateDirectory(context.ApiDir);
                // download and extract
                if (context.Config.Api.IsGitRepo)
                {
                    string tempPath = Path.Combine(context.InstallDir, "temp");
                    if (Directory.Exists(tempPath))
                        Utils.DeleteDirectory(tempPath);
                    Directory.CreateDirectory(tempPath);

                    Utils.DownloadGitRepo(context.Config.Api.Url, tempPath, "Api");

                    Utils.MoveFolderContents(tempPath, context.ApiDir);

                    Utils.DeleteDirectory(tempPath);
                }
                else
                {
                    string apiZip = Path.Combine(context.ApiDir, "api.zip");
                    context.DownloadFile(context.Config.Api.Url, apiZip, "Api");

                    context.ExtractZip(apiZip);
                }
            }

            bool isRelease = context.Api_IsRelease();
            string dataPath = isRelease ? Path.Combine(context.ApiDir, "data") : Path.Combine(context.ApiDir, "ProjectEarthServerAPI", "data");

            // config
            string configPath = Path.Combine(dataPath, "config", "apiconfig.json");
            JObject apiConfig = JObject.Parse(File.ReadAllText(configPath));
            apiConfig["baseServerIP"] = $"http://{context.Ip}";
            apiConfig["useBaseServerIP"] = false;
            apiConfig["multiplayerAuthKeys"] = JObject.FromObject(new Dictionary<string, string>()
            {
                { context.Ip.Address.ToString(), "/g1xCS33QYGC+F2s016WXaQWT8ICnzJvdqcVltNtWljrkCyjd5Ut4tvy2d/IgNga0uniZxv/t0hELdZmvx+cdA==" }
            });

            File.WriteAllText(configPath, apiConfig.ToString());
            Logger.Debug("Edited apiconfig.json");

            // appsettings
            if (context.Ip.Port != 80)
            {
                if (isRelease)
                    Logger.PAKC("Cannot change port that api is running on (80), because api is release. If you want to change it, api needs to be source code");
                else
                {
                    string appSettingsPath = Path.Combine(context.ApiDir, "ProjectEarthServerAPI", "appsettings.json");
                    JObject appSettings = JObject.Parse(File.ReadAllText(appSettingsPath));
                    appSettings["Kestrel"]["EndPoints"]["Http"]["Url"] = $"http://*:{context.Ip.Port}";
                    File.WriteAllText(appSettingsPath, appSettings.ToString());
                    Logger.Debug("Edited appsettings.json");
                }
            }

            // resource pack
            context.DownloadFile(context.Config.ResourcepackUrl, Path.Combine(dataPath, "resourcepacks", "vanilla.zip"), "Resourcepack");

            // build
            if (!isRelease)
            {
                Logger.PAKC("Make sure you have .net 5 SDK installed");
                Logger.Debug("Building Api...");
                string buildScriptPath = Path.Combine(context.ApiDir, "ProjectEarthServerAPI", "build.bat");
                context.WriteAllText(buildScriptPath, "dotnet build --nologo /clp:ErrorsOnly");

                string commandText = File.ReadAllText(buildScriptPath);
                Logger.Info($"Running: \"{commandText}\"");

                bool gotBuildSucceeded = false;
                Process process = Utils.RunCommand(commandText, Path.Combine(context.ApiDir, "ProjectEarthServerAPI"), true, (string text, bool isError) =>
                {
                    Logger.Debug($"[{(isError ? "err" : "out")}] {text}");
                    if (!isError && text != null && text.Contains("Build succeeded"))
                        gotBuildSucceeded = true;
                });

                // wait max 90s
                if (!process.WaitForExit(90 * 1000))
                    process.Kill(true);

                int exitCode = process.ExitCode;
                process.Dispose();

                if (!gotBuildSucceeded && exitCode != 0)
                {
                    Logger.PAKC($"Failed to build api");
                    return false;
                }
                else
                    Logger.Info("Build Api");

                Logger.Debug("Copying data folder...");
                Utils.CopyDir(dataPath, Path.Combine(context.Api_BuildPath(), "data"));
                Logger.Debug("Copied data folder", true);
            }

            Logger.Info("**********Api installed**********");
            return true;
        }

        private static bool installCloudburst(Context context)
        {
            Logger.Info("**********Installing Cloudburst**********");

            Directory.CreateDirectory(context.CloudburstDir);

            context.DownloadFile(context.Config.CloudburstUrl, Path.Combine(context.CloudburstDir, "cloudburst.jar"), "Cloudburst");

            string runScriptPath = Path.Combine(context.CloudburstDir, "run.bat");
            context.WriteAllText(runScriptPath, "java -jar cloudburst.jar");

            // setup files
            Logger.Debug("Running Cloudburst to generate file structure...");
            string runScript = File.ReadAllText(runScriptPath);
            Logger.Info($"Running: {runScript}");
            Process process = Utils.RunCommand(runScript, context.CloudburstDir, true, (string text, bool isError) =>
            {
                Logger.Debug($"[{(isError ? "err" : "out")}] {text}");
            });

            // select language
            process.StandardInput.WriteLine("en_US");

            // wait max 60s
            if (!process.WaitForExit(60 * 1000))
                process.Kill(true);
            process.Dispose();

            if (!Directory.Exists(Path.Combine(context.CloudburstDir, "plugins")) || !File.Exists(Path.Combine(context.CloudburstDir, "cloudburst.yml")))
            {
                Logger.PAKC("Failed to generate cloudburst files, make sure you have right java version");
                return false;
            }
            Logger.Debug("Generated Cloudburst file structure");

            context.DownloadFile(context.Config.GenoaPluginUrl, Path.Combine(context.CloudburstDir, "plugins", "GenoaPlugin.jar"), "GenoaPlugin");
            context.DownloadFile(context.Config.GenoaAllocatorPluginUrl, Path.Combine(context.CloudburstDir, "plugins", "ZGenoaAllocatorPlugin.jar"), "GenoaAllocatorPlugin");

            Directory.CreateDirectory(Path.Combine(context.CloudburstDir, "plugins", "GenoaAllocatorPlugin"));
            context.WriteAllText(Path.Combine(context.CloudburstDir, "plugins", "GenoaAllocatorPlugin", "key.txt"),
                    "/g1xCS33QYGC+F2s016WXaQWT8ICnzJvdqcVltNtWljrkCyjd5Ut4tvy2d/IgNga0uniZxv/t0hELdZmvx+cdA==");
            Logger.Debug("Created key.txt");
            context.WriteAllText(Path.Combine(context.CloudburstDir, "plugins", "GenoaAllocatorPlugin", "ip.txt"),
                context.Ip.Address.ToString());
            Logger.Debug("Created ip.txt");

            // edit cloudburst.yml
            YamlFile cloudburstYml = new YamlFile(Path.Join(context.CloudburstDir, "cloudburst.yml"));
            cloudburstYml.Obj["settings"]["earth-api"] = $"{context.Ip}/1/api";
            Dictionary<object, object> Settings = cloudburstYml.Obj["worlds"]["world"] as Dictionary<object, object>;
            Settings["generator"] = "genoa:void";
            cloudburstYml.Obj["worlds"]["world"] = Settings;
            Settings = cloudburstYml.Obj["worlds"]["nether"] as Dictionary<object, object>;
            Settings["generator"] = "genoa:void";
            cloudburstYml.Obj["worlds"]["nether"] = Settings;
            cloudburstYml.Save();
            Logger.Debug("Edited Cloudburst.yml");

            // edit server.properties
            LBLFile serverProps = new LBLFile(Path.Combine(context.CloudburstDir, "server.properties"), '=');
            serverProps["server-ip"] = context.Ip.Address.ToString();
            serverProps["spawn-protection"] = "0";
            serverProps["gamemode"] = "1";
            serverProps["allow-nether"] = "true";
            serverProps["xbox-auth"] = "false";
            serverProps.Save();
            Logger.Debug("Edited server.properties");

            Logger.Info("**********Cloudburst installed**********");
            return true;
        }

        private static bool installTileServer(Context context)
        {
            Logger.Info("**********Installing TileServer**********");

#if WINDOWS
            Logger.Info("It's recommended that you install wsl update: https://wslstorestorage.blob.core.windows.net/wslblob/wsl_update_x64.msi");
#endif
            Logger.Info("If you don't have docker installed, you can download it here: https://docs.docker.com/get-docker/");
#if WINDOWS
            Logger.Info("After you install docker go to the config file at C:\\Users\\<username>\\AppData\\Roaming\\Docker\\settings.json, and set \"wslEngineEnabled\": true");
#endif
            Logger.Info("After that, restart your system");
            Logger.PAKC();

            // make sure dir is empty, bc tile server is git repo
            if (Directory.Exists(context.TileServerDir) && (Directory.GetFiles(context.TileServerDir).Length > 0 || Directory.GetDirectories(context.TileServerDir).Length > 0))
            {
                Logger.PAKC($"All files in \"{context.TileServerDir}\" will be deleted");
                Utils.DeleteDirectory(context.TileServerDir);
            }

            Directory.CreateDirectory(context.TileServerDir);

            Utils.DownloadGitRepo(context.Config.TileServerUrl, context.TileServerDir, "TileServer");

            // tile server config
            string tsConfigPath = Path.Combine(context.TileServerDir, "config.json");
            JObject tsConfig = JObject.Parse(File.ReadAllText(tsConfigPath));
            tsConfig["styles"]["mc-earth"]["mc-earth"] = true;
            tsConfig["options"]["domains"] = JArray.FromObject(new string[] { "127.0.0.1" });
            File.WriteAllText(tsConfigPath, tsConfig.ToString());
            Logger.Debug("Edited config.json");

            // get port
            ushort tsPort = 8080;
            if (Input.YN("Do you want to change port, that tileServer will be running on (default is 8080)", false))
            {
            getPort:
                string read = Input.String("Server's port (0 - 65535)"); // ushort.MaxValue
                if (ushort.TryParse(read, out ushort _port))
                    tsPort = _port;
                else
                {
                    Logger.Info("Couldn't parse ip, make sure it's in range");
                    goto getPort;
                }
            }

            // api config
            bool isRelease = context.Api_IsRelease();
            string dataPath = isRelease ? Path.Combine(context.ApiDir, "data") : Path.Combine(context.ApiDir, "ProjectEarthServerAPI", "data");

            editApi(Path.Combine(dataPath, "config", "apiconfig.json"));
            if (!isRelease)
                editApi(Path.Combine(context.Api_BuildPath(), "data", "config", "apiconfig.json"));

            void editApi(string path)
            {
                JObject apiConfig = JObject.Parse(File.ReadAllText(path));
                apiConfig["tileServerUrl"] = $"http://127.0.0.1:{tsPort}";
                File.WriteAllText(path, apiConfig.ToString());
            }
            Logger.Debug("Edited apiConfig(s)");

            context.WriteAllText(Path.Combine(context.TileServerDir, "start.bat"), $"docker run --rm -it -v \"{context.TileServerDir}\\:\"/data -p {tsPort}:{tsPort} maptiler/tileserver-gl");
            Logger.Debug("Created start.bat");

            Logger.Info("**********TileServer installed**********");
            return true;
        }

        public class Config
        {
            public static readonly Config Default = new Config()
            {
                Api = new ApiConfig()
                {
                    IsGitRepo = true,
                    Url = "https://github.com/BitcoderCZ/ProjectEarthApi.git",
                },
                ResourcepackUrl = new RemoteFileInfo()
                {
                    Url = "https://www.dropbox.com/scl/fi/kbyysugyhb94zj9zb6pgc/vanilla.zip?rlkey=xt6c7nhpbzzyw7gua524ssb40&dl=1",
                    ExpectedMinSize = 100000000
                },
                CloudburstUrl = new RemoteFileInfo()
                {
                    Url = "https://ci.rtm516.co.uk/job/ProjectEarth/job/Server/job/earth-inventory/lastSuccessfulBuild/artifact/target/Cloudburst.jar",
                    ExpectedMinSize = 25000000
                },
                GenoaPluginUrl = new RemoteFileInfo()
                {
                    Url = "https://www.googleapis.com/drive/v3/files/1DIX9pT7B460iPd8tWysi4KQCxQqwQNL8?alt=media&key=AIzaSyAA9ERw-9LZVEohRYtCWka_TQc6oXmvcVU&supportsAllDrives=True",
                    ExpectedMinSize = 90000
                },
                GenoaAllocatorPluginUrl = new RemoteFileInfo()
                {
                    Url = "https://www.googleapis.com/drive/v3/files/1m6PrdPTAl6k4k36pq44Lw-U-hDhixPwk?alt=media&key=AIzaSyAA9ERw-9LZVEohRYtCWka_TQc6oXmvcVU&supportsAllDrives=True",
                    ExpectedMinSize = 140000
                },
                TileServerUrl = "https://github.com/SuperMatejCZ/TileServer.git",
            };

            public ApiConfig Api { get; set; }
            public RemoteFileInfo ResourcepackUrl { get; set; }

            public RemoteFileInfo CloudburstUrl { get; set; }
            public RemoteFileInfo GenoaPluginUrl { get; set; }
            public RemoteFileInfo GenoaAllocatorPluginUrl { get; set; }

            public string TileServerUrl { get; set; }

            public class ApiConfig
            {
                public bool IsGitRepo { get; set; }
                public string Url { get; set; }
            }
        }
    }
}
