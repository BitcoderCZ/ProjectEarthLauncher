using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ProjectEarthLauncher
{
    internal class Context
    {
        public Installer.Config Config;

        public IPEndPoint Ip { get; private set; }
        public string InstallDir { get; private set; }
        public string ApiDir { get; private set; }
        public string CloudburstDir { get; private set; }
        public string TileServerDir { get; private set; }
        /// <summary>
        /// 0 - ask
        /// <br>1 - overwrite, don't ask</br>
        /// <br>2 - ignore, don't ask</br>
        /// </summary>
        private int fileExistsAction;

        public Context(Installer.Config _config, string _installDir, IPEndPoint _ip)
        {
            Config = _config;

            InstallDir = _installDir;
            ApiDir = Path.Combine(InstallDir, "Api");
            CloudburstDir = Path.Combine(InstallDir, "Cloudburst");
            TileServerDir = Path.Combine(InstallDir, "TileServer");

            Ip = _ip;
        }

        public int Api_IsReleaseNoUI()
        {
            if (File.Exists(Path.Combine(ApiDir, "ProjectEarthServerAPI.exe")))
                return 1;
            else if (File.Exists(Path.Combine(ApiDir, "ProjectEarthServerAPI", "ProjectEarthServerAPI.csproj")))
                return 2;
            else
                return 0;
        }
        public bool Api_IsRelease()
        {
            if (File.Exists(Path.Combine(ApiDir, "ProjectEarthServerAPI.exe")))
            {
                Logger.Debug("Detected that api is release");
                return true;
            }
            else if (File.Exists(Path.Combine(ApiDir, "ProjectEarthServerAPI", "ProjectEarthServerAPI.csproj")))
            {
                Logger.Debug("Detected that api is source code");
                return false;
            }
            else
                return Input.YN("Couldn't automatically detect if Api is release or source code, please specify it manually (release - Y, source - N)", false);
        }

        public void DownloadFile(RemoteFileInfo info, string path, string displayName)
        {
            download:
            if (checkFileAction(path))
            {
                Utils.DownloadFile(info.Url, path, displayName);
                if (info.ExpectedMinSize > -1)
                {
                    FileInfo fInfo = new FileInfo(path);
                    if (!fInfo.Exists || fInfo.Length < info.ExpectedMinSize)
                    {
                        Logger.Info($"Failed to download {displayName}, only {(fInfo.Exists ? fInfo.Length : 0)}B were downloaded, minimum file size: {info.ExpectedMinSize}B", true);
                        if (Input.YN("Do you want to attempt to download the file again?", true))
                        {
                            fInfo.Delete();
                            goto download;
                        } else
                        {
                            fInfo.Delete();
                            throw new Exception($"Failed to download \"{path}\"");
                        }
                    }
                }
            }
        }

        public void DownloadFile(string url, string path, string displayName)
        {
            if (checkFileAction(path))
                Utils.DownloadFile(url, path, displayName);
        }

        public void ExtractZip(string path)
        {
            Utils.ExtractZip(path, checkFileAction, true);
        }

        private bool checkFileAction(string filePath)
        {
            if (File.Exists(filePath))
            {
                bool asked = false;
                if (fileExistsAction == 0) {
                    fileExistsAction = Input.Enum($"File \"{filePath}\" already exist, do you want to overwrite it?", "Yes", "No", "Yes, don't ask me again for other files", "No, don't ask me again for other files") + 1;
                    asked = true;
                }

                if (fileExistsAction == 1 || fileExistsAction == 3)
                    File.Delete(filePath);
                else if (fileExistsAction == 2 || fileExistsAction == 4)
                {
                    Logger.Debug($"File \"{filePath}\" already exists, skipped");
                    if (asked)
                        fileExistsAction = Math.Max(fileExistsAction - 2, 0);
                    return false;
                }

                if (asked)
                    fileExistsAction = Math.Max(fileExistsAction - 2, 0);
            }

            return true;
        }

        public void WriteAllText(string path, string contents)
        {
            if (checkFileAction(path))
                File.WriteAllText(path, contents);
        }

        public string Api_BuildPath()
        {
            string path = Path.Combine(ApiDir, "ProjectEarthServerAPI", "bin");

            if (Directory.Exists(Path.Combine(path, "Debug")))
                path = Path.Combine(path, "Debug");
            else if (Directory.Exists(Path.Combine(path, "Release")))
                path = Path.Combine(path, "Release");
            else
                throw new Exception($"No Debug or Release folder in \"{path}\"");

            List<string> dirs = Directory.GetDirectories(path).ToList();
            for (int i = 0; i < dirs.Count; i++)
                if (!Path.GetFileName(dirs[i]).StartsWith("net"))
                {
                    dirs.RemoveAt(i);
                    i--;
                }

            if (dirs.Count == 0)
                throw new Exception("No net directories");

            dirs.Sort();

            return dirs.Last(); // return the one with highest .net version
        }
    }
}
