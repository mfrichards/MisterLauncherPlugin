using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace MisterLauncherPlugin
{
    public class AppConfig
    {
        public MisterConfig Mister { get; set; } = new MisterConfig();
        public CommandConfig? PostLaunchCommand { get; set; }
        public string[] Consoles { get; set; } = [];
        public string[] Computers { get; set; } = [];
    }

    public class MisterConfig
    {
        public string ApiUrl { get; set; } = "http://mister:8182";
        public int ApiTimeoutMs { get; set; } = 2000;
        public bool TriggerAutosave { get; set; } = true;
        public int AutosaveTimeMs { get; set; } = 3000;
        public string ArcadePath { get; set; } = "/media/fat";
    }

    public class CommandConfig
    {
        public string FileName { get; set; } = "cmd.exe";
        public string Arguments { get; set; } = "";
        public int DelayMs { get; set; } = 500;
    }

    [SupportedOSPlatform("windows")]
    internal class MisterLauncherPlugin : IGameMultiMenuItemPlugin
    {
        static readonly HttpClient httpClient;
        static readonly AppConfig appConfig;
        static readonly Dictionary<string, string> platformMap;
        static readonly HashSet<string> computers;

        static MisterLauncherPlugin()
        {
            string jsonConfig = File.ReadAllText(@"Plugins\MisterLauncher.settings.json");
            var config = JsonSerializer.Deserialize<AppConfig>(jsonConfig);
            appConfig = config == null ? new AppConfig() : config;

            platformMap = new Dictionary<string, string>();
            computers = new HashSet<string>();
            foreach (var platform in appConfig.Consoles)
            {
                AddPlatform(platform, false);
            }
            foreach (var platform in appConfig.Computers)
            {
                AddPlatform(platform, true);
            }

            httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(appConfig.Mister.ApiUrl);
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.Timeout = TimeSpan.FromMilliseconds(appConfig.Mister.ApiTimeoutMs);
        }

        static void AddPlatform(string platform, bool isComputer = false)
        {
            var names = platform.Split(":");
            var misterName = (names.Length > 1) ? names[1].Trim() : platform.Trim();
            platformMap[names[0].Trim().ToLower()] = misterName;
            if (isComputer) computers.Add(misterName);
        }

        IEnumerable<IGameMenuItem> IGameMultiMenuItemPlugin.GetMenuItems(params IGame[] selectedGames)
        {
            // Only supported for single game selection.
            if (selectedGames == null || selectedGames.Length != 1)
            {
                return null;
            }

            var selectedGame = selectedGames[0];
            List<IGameMenuItem> items = new List<IGameMenuItem>();

            if (selectedGame.Platform.ToLower().Equals("arcade"))
            {
                var setname = Path.GetFileNameWithoutExtension(selectedGame.ApplicationPath);
                var misterGames = FindArcadeGames(setname);
                foreach (var game in misterGames)
                {
                    items.Add(new MisterEmulatorMenuItem(appConfig, httpClient, game));
                }
            }
            else
            {
                var platform = platformMap.GetValueOrDefault(selectedGame.Platform.ToLower());
                if (platform != null)
                {
                    var filename = Path.GetFileNameWithoutExtension(selectedGame.ApplicationPath);
                    var misterGames = FindConsoleGames(platform, filename);
                    foreach (var game in misterGames)
                    {
                        items.Add(new MisterEmulatorMenuItem(appConfig, httpClient, game));
                    }
                }
            }
            
            return items;
        }

        List<MisterGame> FindArcadeGames(string setname)
        {
            List<MisterGame> games = new List<MisterGame>();
            using (var con = new SqliteConnection(@"Data Source=Metadata\arcade.db"))
            {

                con.Open();
                var cmd = con.CreateCommand();
                MisterGame? defaultGame = null;

                cmd.CommandText = "SELECT setname,name,version,description,path,is_default from games " +
                                  "WHERE setname = $set";
                cmd.Parameters.AddWithValue("$set", setname);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        defaultGame = new MisterGame(reader);
                        if (defaultGame.Path != null)
                        {
                            defaultGame.LaunchboxDefault = true;
                            games.Add(defaultGame);
                        }
                    }
                }

                if (defaultGame != null && defaultGame.Name.Length > 0)
                {
                    cmd.CommandText = "SELECT setname,name,version,description,path,is_default from games " +
                                      "WHERE name = $name AND path IS NOT NULL";
                    cmd.Parameters.AddWithValue("$name", defaultGame.Name);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.GetString(0).Equals(setname))
                            {
                                games.Add(new MisterGame(reader));
                            }
                        }
                    }
                }
            }
            games.Sort();
            return games;
        }

        private IEnumerable<MisterGame> FindConsoleGames(string platform, string filename)
        {
            List<MisterGame> games = new List<MisterGame>();
            var (gameTitle, _) = splitName(filename);

            try
            {
                // Search for game.
                var json = JsonSerializer.Serialize(new { query = gameTitle, system = platform });
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/games/search")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                var response = httpClient.Send(request);
                response.EnsureSuccessStatusCode();
                var searchResult = JsonSerializer.Deserialize<MisterSearchResponse>(response.Content.ReadAsStream());
                if (searchResult != null && searchResult.data.Length > 0)
                {
                    foreach (var item in searchResult.data) {
                        if (item.name != null) {
                            var (title, version) = splitName(item.name);
                            if (title.Equals(gameTitle, StringComparison.OrdinalIgnoreCase))
                            {
                                var ext = Path.GetExtension(item.path) ?? "";
                                if (computers.Contains(platform) && ext.Length > 1)
                                {
                                    version = $"{version} [{ext.Substring(1)}]".Trim();
                                }
                                var misterGame = new MisterGame(title, version, item);
                                if (misterGame.Description.Equals(filename, StringComparison.OrdinalIgnoreCase))
                                {
                                    misterGame.LaunchboxDefault = true;
                                }
                                games.Add(misterGame);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error searching MiSTer games: " + filename);
                Console.WriteLine(ex.ToString());
            }
            games.Sort();
            return games;
        }

        private (string, string) splitName(string name)
        {
            int index1 = name.IndexOf("(");
            int index2 = name.IndexOf("[");
            int index = index1 >= 0 && (index2 < 0 || index1 < index2) ? index1 : index2;
            var title = index > 0 ? name.Substring(0, index).Trim() : name;
            var version = index > 0 ? name.Substring(index).Trim() : "";
            return (title, version);
        }
    }


    public class MisterSearchResponse
    {
        public int? total {  get; set; }
        public int? pageSize { get; set; }
        public int? page { get; set; }
        public MisterGameInfo[] data { get; set; } = [];

    }

    public class MisterGameInfo
    {
        public MisterSystem? system { get; set; }
        public string? name { get; set; }
        public string? path { get; set; }

    }

    public class MisterSystem
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? category { get; set; }
    }

    public class MisterGame : IComparable<MisterGame>
    {
        public MisterGame(SqliteDataReader reader)
        {
            Setname = reader.GetString(0);
            Name = reader.GetString(1);
            Version = reader.GetString(2);
            Description = reader.GetString(3);
            Path = reader.IsDBNull(4) ? null : reader.GetString(4);
            IsDefault = (reader.GetInt16(5) == 1);
            LaunchboxDefault = false;
        }

        public MisterGame(string title, string version, MisterGameInfo item)
        {
            Setname = item.system?.id ?? "";
            Description = item.name ?? "";
            Name = title;
            Version = version;
            Path = item.path;
            IsDefault = false;
            LaunchboxDefault = false;
        }

        public string Setname { get; }
        public string Name { get; }
        public string Version { get; }
        public string Description { get; }
        public string? Path { get; }
        public bool IsDefault { get; }
        public bool LaunchboxDefault { get; set; }

        public int CompareTo(MisterGame? other)
        {
            if (other == null) return 1;
            if (LaunchboxDefault) return -1;
            if (other.LaunchboxDefault) return 1;
            if (IsDefault) return -1;
            if (other.IsDefault) return 1;
            return Version.CompareTo(other.Version);
        }
    }

    [SupportedOSPlatform("windows")]
    public class MisterEmulatorMenuItem : IGameMenuItem
    {
        private static readonly Image? misterIcon;

        private MisterGame game;
        private HttpClient httpClient;
        private AppConfig appConfig;

        static MisterEmulatorMenuItem()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("MisterLauncherPlugin.logo.png"))
                {
                    if (stream != null)
                    {
                        misterIcon = new Bitmap(stream);
                        return;
                    }
                }
            }
            catch { }
            misterIcon = null;
        }

        public MisterEmulatorMenuItem(AppConfig appConfig, HttpClient httpClient, MisterGame game)
        {
            this.game = game;
            this.httpClient = httpClient;
            this.appConfig = appConfig;
        }

        string IGameMenuItem.Caption
        {
            get
            {
                return (game.LaunchboxDefault ? "*Play " : (game.IsDefault ? "+Play " : "Play ")) +
                       (game.Version.Length > 0 ? game.Version : game.Description) +
                       " Version on MiSTer...";
            }
        }

        IEnumerable<IGameMenuItem> IGameMenuItem.Children
        {
            get { return null; }
        }

        bool IGameMenuItem.Enabled
        {
            get { return true; }
        }

        Image IGameMenuItem.Icon
        {
            get { return misterIcon; }
        }

        private String formatPath(string path)
        {
            if (path.StartsWith("_Arcade"))
            {
                var basePath = appConfig.Mister.ArcadePath.Trim();
                return basePath.EndsWith("/") ? $"{basePath}{path}" : $"{basePath}/{path}";
            }
            return path;
        }

        async public void OnSelect(params IGame[] games)
        {
            if (game.Path != null && game.Path.Length > 0)
            {
                try
                {
                    // Launch the OSD to trigger autosave, if necessary.
                    if (appConfig.Mister.TriggerAutosave)
                    {
                        var osdResponse = await httpClient.PostAsync("/api/controls/keyboard/osd", null);
                        osdResponse.EnsureSuccessStatusCode();
                        if (appConfig.Mister.AutosaveTimeMs > 0)
                        {
                            await Task.Delay(appConfig.Mister.AutosaveTimeMs);
                        }
                    }

                    // Launch the game.
                    var response = await httpClient.PostAsJsonAsync("/api/games/launch", new { path = formatPath(game.Path) });
                    response.EnsureSuccessStatusCode();

                    // Run the post launch command, if necessary.
                    if (appConfig.PostLaunchCommand != null)
                    {
                        await Task.Delay(appConfig.PostLaunchCommand.DelayMs);
                        Process process = new Process();
                        process.StartInfo = new ProcessStartInfo
                        {
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            FileName = appConfig.PostLaunchCommand.FileName,
                            Arguments = appConfig.PostLaunchCommand.Arguments
                        };
                        process.Start();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error launching MiSTer game: " + game.Path);
                    Console.WriteLine("Error launching MiSTer game: " + game.Path);
                    Console.WriteLine(ex.ToString());
                }
            }
            else
            {
                MessageBox.Show("Game " + game.Description + " not found on MiSTer!");
            }
        }
    }
}
