using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Windows.Forms;

namespace VSAutoModLauncher
{
    internal partial class Launcher
    {
        private readonly string name;
        private readonly TextBox consoleTextBox;

        // TODO: move these helpers into another class
        private string HttpGet(string url)
        {
            var httpClient = new HttpClient();
            var task0 = Task.Run(() => httpClient.GetAsync(url));
            task0.Wait();
            var task1 = Task.Run(() => task0.Result.Content.ReadAsStringAsync());
            task1.Wait();
            return task1.Result;
        }
        private void HttpGetSaveFile(string url, string filePath)
        {
            var httpClient = new HttpClient();
            var task0 = Task.Run(() => httpClient.GetAsync(url));
            task0.Wait();
            if (File.Exists(ResolvePath(filePath))) { File.Delete(ResolvePath(filePath)); }
            var fs = new FileStream(ResolvePath(filePath), FileMode.CreateNew);
            var task1 = Task.Run(() => task0.Result.Content.CopyToAsync(fs));
            task1.Wait();
            fs.Dispose();
        }

        public Launcher(string name, TextBox consoleTextBox)
        {
            this.name = name;
            this.consoleTextBox = consoleTextBox;
        }

        private void Log(string msg)
        {
            if (consoleTextBox.Text != "")
            {
                consoleTextBox.AppendText(Environment.NewLine);
            }
            consoleTextBox.AppendText(msg);
        }

        public void DeleteServerDir()
        {
            Directory.Delete(ResolvePath(ServerDir), true);
        }

        public void Launch(string host, string password)
        {
            string[] hostParts = host.Split(':');
            string hostname = hostParts[0];
            int port = hostParts.Length > 1 ? Int32.Parse(hostParts[1]) : 42420;

            consoleTextBox.Text = "";
            try
            {
                InitDataDir(hostname, port, password);
                List<ModIdVersionDownloadUrl> requiredModList = RequestRequiredModList(hostname, port, password);
                DownloadMods(requiredModList);
                CleanupDisusedMods(requiredModList);
                StartGame(hostname, port, password);
            }
            catch (Exception e)
            {
                Log("=== START OF ERROR ===");
                Log(e.ToString());
                Log("=== END OF ERROR ===");
            }
        }
        public string SafeName { get { return Path.GetInvalidFileNameChars().Aggregate(name, (current, c) => current.Replace(c, '-')); } }
        public string ServerDir { get { return $"%APPDATA%/VintagestoryMooltiPack/{SafeName}"; } }
        private void InitDataDir(string hostname, int port, string password)
        {
            Directory.CreateDirectory(ResolvePath($"{ServerDir}/Mods"));

            string multiplayerServerString = $"{name},{hostname}:{port},{password}";


            dynamic clientSettings;
            string serverClientSettingsFilePath = $"{ServerDir}/clientsettings.json";
            if (!File.Exists(ResolvePath(serverClientSettingsFilePath)))
            {
                string defaultClientSettingsFilePath = "%APPDATA%/VintagestoryData/clientsettings.json";
                Log($"Creating dataPath at {ServerDir}...");
                if (!File.Exists(ResolvePath(defaultClientSettingsFilePath)))
                {
                    throw new Exception($"Please run Vintagestory with its standard launcher first, to generate the {defaultClientSettingsFilePath} file");
                }
                File.Copy(ResolvePath(defaultClientSettingsFilePath), ResolvePath(serverClientSettingsFilePath));
                clientSettings = JsonConvert.DeserializeObject(File.ReadAllText(ResolvePath(serverClientSettingsFilePath)));
                clientSettings["stringListSettings"].Replace(new JObject {
                    { "multiplayerservers", new JArray(new[]
                        { multiplayerServerString }
                    ) },
                    { "modPaths", new JArray(new[]
                        { ResolvePath($"{ServerDir}/Mods").Replace('/', '\\') }
                    ) }
                });
                File.WriteAllText(ResolvePath(serverClientSettingsFilePath), JsonConvert.SerializeObject(clientSettings, Formatting.Indented));

                var coreModFileNames = new[] { "VSCreativeMod.dll", "VSEssentials.dll", "VSSurvivalMod.dll" };
                foreach (var coreModFileName in coreModFileNames)
                {
                    File.Copy(ResolvePath($"%APPDATA%/Vintagestory/Mods/{coreModFileName}"), ResolvePath($"{ServerDir}/Mods/{coreModFileName}"));
                }
            }
            else
            {
                Log($"dataPath already exists at {ServerDir}");
                // update server details, if they've changed!
                clientSettings = JsonConvert.DeserializeObject(File.ReadAllText(ResolvePath(serverClientSettingsFilePath)));
                var multiplayerServers = new JArray(clientSettings["stringListSettings"]["multiplayerservers"]);
                multiplayerServers.Where(x => x.Type == JTokenType.String && ((string)x).StartsWith($"{name},")).ToList().ForEach(x => x.Remove());
                multiplayerServers.AddFirst(multiplayerServerString);
                clientSettings["stringListSettings"]["multiplayerservers"].Replace(multiplayerServers);
                File.WriteAllText(ResolvePath(serverClientSettingsFilePath), JsonConvert.SerializeObject(clientSettings));
            }

            cacheData = File.Exists(ResolvePath(CacheDataFilePath)) ? JsonConvert.DeserializeObject<CacheData>(File.ReadAllText(ResolvePath(CacheDataFilePath))) : new CacheData();
        }

        private string CacheDataFilePath { get { return $"{ServerDir}/vsautomod-cache.json"; } }
        private CacheData cacheData;

        private void SaveCacheData()
        {
            File.WriteAllText(ResolvePath(CacheDataFilePath), JsonConvert.SerializeObject(cacheData, Formatting.Indented));
        }

        private List<ModIdVersionDownloadUrl> RequestRequiredModList(string hostname, int port, string password) {
            TcpClient client;
            Log($"Connecting to Vintage Story Server at {hostname}:{port}...");
            try
            {
                client = new TcpClient(hostname, port);
            }
            catch (Exception e)
            {
                throw new Exception($"Could not connect to Vintage Story Server at {hostname}:{port}", e);
            }
            NetworkStream nwStream = client.GetStream();

            Log($"Requesting mod list...");

            byte[] bytesToSend = new byte[] { 0x00, 0x00, 0x00, 0x04, 0xff, 0xff, 0xff, 0xff };
            nwStream.Write(bytesToSend, 0, bytesToSend.Length);

            Log($"Reading response...");

            byte[] bytesToRead = new byte[client.ReceiveBufferSize];
            int bytesRead = nwStream.Read(bytesToRead, 0, client.ReceiveBufferSize);
            client.Close();

            if (bytesRead == 0) { throw new Exception("No response for mod list request (is the server definitely running MooltiPassServerMod?)"); }
            if (bytesRead < 4) { throw new Exception("Mysterious response for mod list request did not contain enough bytes for packet size header"); }

            string responseString = Encoding.ASCII.GetString(bytesToRead, 0, bytesRead);
            responseString = responseString.Substring(4, responseString.Length - 4); // [{"modId":"MoreRoads","version":"1.3.3","downloadUrl":null},...

            List<ModIdVersionDownloadUrl> response;
            try
            {
                response = JsonConvert.DeserializeObject<List<ModIdVersionDownloadUrl>>(responseString);
            }
            catch (Exception e)
            {
                if (responseString.Contains("An action you (or your client) did caused an unhandled exception"))
                {
                    throw new Exception($"Got an error from the server. Is it still loading?\n{responseString}", e);
                }
                throw new Exception($"Bad response for mod list request: did not parse as JSON!\nMaybe you need to update this launcher?\n{responseString}", e);
            }

            return response;
        }
        private void DownloadMods(List<ModIdVersionDownloadUrl> requiredMods)
        {
            foreach (var requiredMod in requiredMods)
            {
                if (requiredMod.modId == "game" || requiredMod.modId == "survival" || requiredMod.modId == "creative") { continue; }
                // do we already have this mod downloaded?
                if (cacheData.modsDownloaded.TryGetValue(requiredMod.modId, out var cachedMod))
                {
                    string existingModFilePath = $"{ServerDir}/Mods/{cachedMod.fileName}";
                    // is it the correct version?
                    if (cachedMod.version == requiredMod.version)
                    {
                        if (File.Exists(ResolvePath(existingModFilePath))) { continue; } // already got this mod! nothing to do
                    }
                    else
                    {
                        // clean up the old version
                        Log($"Removing old version of '{requiredMod.modId}'...");
                        File.Delete(ResolvePath(existingModFilePath));
                        cacheData.modsDownloaded.Remove(requiredMod.modId);
                        SaveCacheData();
                    }
                }
                // figure out where to download this mod from
                var downloadUrl = requiredMod.downloadUrl;
                if (downloadUrl == null)
                {
                    var modDbLookupUrl = $"http://mods.vintagestory.at/api/mod/{requiredMod.modId}";
                    Log($"Finding '{requiredMod.modId}' version '{requiredMod.version}' via {modDbLookupUrl}...");
                    dynamic modDbLookupResponse = JsonConvert.DeserializeObject(HttpGet(modDbLookupUrl));
                    if (modDbLookupResponse["statuscode"] != "200")
                    {
                        throw new Exception($"Could not find mod '{requiredMod.modId}' at {modDbLookupUrl} -- maybe the server should specify a downloadUrl!");
                    }
                    dynamic correctRelease = null;
                    foreach (var release in modDbLookupResponse.mod.releases)
                    {
                        if (release["modversion"] == requiredMod.version) { correctRelease = release; }
                    }
                    if (correctRelease == null)
                    {
                        throw new Exception($"Could not find release for '{requiredMod.modId}' version {requiredMod.version} at {modDbLookupUrl} -- maybe the server should specify a downloadUrl!");
                    }
                    downloadUrl = $"http://mods.vintagestory.at/files/{correctRelease["mainfile"]}";
                }

                var fileName = $"{requiredMod.modId}-v{requiredMod.version}.zip";
                Uri uri = new Uri(downloadUrl);
                if (uri.IsFile)
                {
                    fileName = Path.GetFileName(uri.LocalPath);
                }

                Log($"Downloading '{requiredMod.modId}' version '{requiredMod.version}' from {downloadUrl}...");
                HttpGetSaveFile(downloadUrl, $"{ServerDir}/Mods/{fileName}"); ;

                if (cacheData.modsDownloaded.ContainsKey(requiredMod.modId)) { cacheData.modsDownloaded.Remove(requiredMod.modId); }
                cacheData.modsDownloaded.Add(requiredMod.modId, new ModVersionFileName()
                {
                    fileName = fileName,
                    version = requiredMod.version
                });
                SaveCacheData();
            }
        }
        private void CleanupDisusedMods(List<ModIdVersionDownloadUrl> requiredMods)
        {
            var requiredModIds = new HashSet<string>();
            foreach (var requiredMod in requiredMods)
            {
                requiredModIds.Add(requiredMod.modId);
            }
            foreach (var cachedModId in cacheData.modsDownloaded.Keys)
            {
                var cachedMod = cacheData.modsDownloaded[cachedModId];
                if (!requiredModIds.Contains(cachedModId))
                {
                    Log($"Removing disused mod '{cachedModId}'...");
                    File.Delete(ResolvePath($"{ServerDir}/Mods/{cachedMod.fileName}"));
                    cacheData.modsDownloaded.Remove(cachedModId);
                    SaveCacheData();
                }
            }
        }
        private void StartGame(string hostname, int port, string password)
        {
            Log("Launching Vintagestory.exe...");
            string path = ResolvePath("%APPDATA%/VintageStory/VintageStory.exe");
            string args = $"--dataPath \"{ResolvePath(ServerDir)}\" --c {hostname}:{port}";
            if (password != "")
            {
                args += " --pw \"{password}\"";
            }
            System.Diagnostics.Process.Start(path, args);
        }

        private string ResolvePath(string path)
        {
            return Environment.ExpandEnvironmentVariables(path);
        }
    }
}