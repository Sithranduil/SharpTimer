using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace SharpTimer
{
    public class MapInfo
    {
        public string MapStartC1 { get; set; }
        public string MapStartC2 { get; set; }
        public string MapEndC1 { get; set; }
        public string MapEndC2 { get; set; }
        public string RespawnPos { get; set; }
    }

    public class PlayerTimerInfo
    {
        public bool IsTimerRunning { get; set; }
        public int TimerTicks { get; set; }
    }

    public class PlayerRecord
    {
        public string PlayerName { get; set; }
        public int TimerTicks { get; set; }
    }

    public partial class SharpTimer : BasePlugin
    {
        private Dictionary<int, PlayerTimerInfo> playerTimers = new Dictionary<int, PlayerTimerInfo>();
        private List<CCSPlayerController> connectedPlayers = new List<CCSPlayerController>();

        public override string ModuleName => "SharpTimer";
        public override string ModuleVersion => "0.0.1";
        public override string ModuleAuthor => "DEAFPS https://github.com/DEAFPS/";
        public override string ModuleDescription => "A simple CSS Timer Plugin";

        public string msgPrefix = $" {ChatColors.Green} [SharpTimer] {ChatColors.White}";
        public Vector currentMapStartC1 = new Vector(0, 0, 0);
        public Vector currentMapStartC2 = new Vector(0, 0, 0);
        public Vector currentMapEndC1 = new Vector(0, 0, 0);
        public Vector currentMapEndC2 = new Vector(0, 0, 0);
        public Vector currentRespawnPos = new Vector(0, 0, 0);

        public bool configLoaded = false;

        public override void Load(bool hotReload)
        {
            RegisterEventHandler<EventPlayerConnectFull>((@event, info) =>
            {
                var player = @event.Userid;

                if (player.IsBot || !player.IsValid)
                {
                    return HookResult.Continue;
                }
                else
                {
                    connectedPlayers.Add(player);
                    playerTimers[player.UserId ?? 0] = new PlayerTimerInfo();
                    Server.PrintToChatAll($"{msgPrefix}Player {ChatColors.Red}{player.PlayerName} {ChatColors.White}connected!");
                    player.PrintToChat($"{msgPrefix}Welcome {ChatColors.Red}{player.PlayerName} {ChatColors.White}to the server!");

                    return HookResult.Continue;
                }
            });

            RegisterEventHandler<EventRoundStart>((@event, info) =>
            {
                LoadConfig();
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
            {
                var player = @event.Userid;

                if (player.IsBot || !player.IsValid)
                {
                    return HookResult.Continue;
                }
                else
                {
                    connectedPlayers.Remove(player);
                    playerTimers.Remove(player.UserId ?? 0);
                    Server.PrintToChatAll($"{msgPrefix}Player {ChatColors.Red}{player.PlayerName} {ChatColors.White}disconnected!");

                    return HookResult.Continue;
                }
            });

            RegisterEventHandler<EventPlayerChat>((@event, info) =>
            {
                var playerID = @event.Userid;
                var msg = @event.Text.Trim().ToLower();

                var player = connectedPlayers.FirstOrDefault(p => p.UserId == playerID);

                if (player == null)
                {
                    return HookResult.Continue;
                }

                if (msg.StartsWith("!"))
                {
                    var parts = msg.Split(' ');
                    var command = parts[0];
                    var arguments = parts.Skip(1).ToArray();

                    switch (command)
                    {
                        case "!top":
                            PrintTopRecords(player);
                            break;
                        case "!r":
                            RespawnPlayer(player);
                            break;
                        default:
                            break;
                    }
                    return HookResult.Continue;
                }
                return HookResult.Continue;
            });

            RegisterListener<Listeners.OnTick>(() =>
            {
                foreach (var player in connectedPlayers)
                {
                    if (player.IsValid && !player.IsBot && player.PawnIsAlive)
                    {
                        var buttons = player.Buttons;
                        string playerVel = Math.Round(player.PlayerPawn.Value.AbsVelocity.Length2D()).ToString().PadLeft(3, '0');
                        string playerTime = FormatTime(playerTimers[player.UserId ?? 0].TimerTicks);

                        player.PrintToCenterHtml(
                            $"<font color='white'>{GetPlayerPlacement(player)}</font> <font class='fontSize-l' color='green'>{playerTime}</font><br>" +
                            $"<font color=\"white\">Speed:</font> <font color='orange'>{playerVel}</font><br>" +
                            $"{((buttons & PlayerButtons.Forward) != 0 ? "W" : "_")} " +
                            $"{((buttons & PlayerButtons.Moveleft) != 0 ? "A" : "_")} " +
                            $"{((buttons & PlayerButtons.Back) != 0 ? "S" : "_")} " +
                            $"{((buttons & PlayerButtons.Moveright) != 0 ? "D" : "_")} " +
                            $"{((buttons & PlayerButtons.Jump) != 0 ? "J" : "_")} " +
                            $"{((buttons & PlayerButtons.Duck) != 0 ? "C" : "_")}</font>");

                        if (playerTimers[player.UserId ?? 0].IsTimerRunning)
                        {
                            playerTimers[player.UserId ?? 0].TimerTicks++;
                        }

                        CheckPlayerActions(player);
                    }
                }
            });

            Console.WriteLine("[SharpTimer] Plugin Loaded");
        }

        private string FormatTime(int ticks)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(ticks / 64.0);
            int centiseconds = (int)((ticks % 64) * (100.0 / 64.0));

            return $"{timeSpan.Minutes:D1}:{timeSpan.Seconds:D2}.{centiseconds:D2}";
        }

        private void CheckPlayerActions(CCSPlayerController? player)
        {
            if (player == null) return;

            Vector playerPos = player.Pawn.Value.CBodyComponent!.SceneNode.AbsOrigin;

            if (IsVectorInsideBox(playerPos, currentMapStartC1, currentMapStartC2))
            {
                OnTimerStart(player);
            }

            if (IsVectorInsideBox(playerPos, currentMapEndC1, currentMapEndC2))
            {
                OnTimerStop(player);
            }
        }

        static bool IsVectorInsideBox(Vector playerVector, Vector corner1, Vector corner2, float height = 50)
        {
            float minX = Math.Min(corner1.X, corner2.X);
            float minY = Math.Min(corner1.Y, corner2.Y);
            float minZ = Math.Min(corner1.Z, corner2.Z);

            float maxX = Math.Max(corner1.X, corner2.X);
            float maxY = Math.Max(corner1.Y, corner2.Y);
            float maxZ = Math.Max(corner1.Z, corner1.Z);

            return playerVector.X >= minX && playerVector.X <= maxX &&
                   playerVector.Y >= minY && playerVector.Y <= maxY &&
                   playerVector.Z >= minZ && playerVector.Z <= maxZ + height;
        }

        public void OnTimerStart(CCSPlayerController? player)
        {
            if (player == null) return;

            playerTimers[player.UserId ?? 0].IsTimerRunning = true;
            playerTimers[player.UserId ?? 0].TimerTicks = 0;
        }

        public void SavePlayerTime(CCSPlayerController? player)
        {
            if (player == null) return;

            string currentMapName = Server.MapName;
            string steamId = player.SteamID.ToString();
            string playerName = player.PlayerName;

            string recordsFileName = "SharpTimer/player_records.json";
            string recordsPath = Path.Join(Server.GameDirectory + "/csgo/cfg", recordsFileName);

            Dictionary<string, Dictionary<string, PlayerRecord>> records;
            if (File.Exists(recordsPath))
            {
                string json = File.ReadAllText(recordsPath);
                records = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, PlayerRecord>>>(json) ?? new Dictionary<string, Dictionary<string, PlayerRecord>>();
            }
            else
            {
                records = new Dictionary<string, Dictionary<string, PlayerRecord>>();
            }

            if (!records.ContainsKey(currentMapName))
            {
                records[currentMapName] = new Dictionary<string, PlayerRecord>();
            }

            if (!records[currentMapName].ContainsKey(steamId) || records[currentMapName][steamId].TimerTicks > playerTimers[player.UserId ?? 0].TimerTicks)
            {
                records[currentMapName][steamId] = new PlayerRecord
                {
                    PlayerName = playerName,
                    TimerTicks = playerTimers[player.UserId ?? 0].TimerTicks
                };

                string updatedJson = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(recordsPath, updatedJson);
            }
        }

        public void OnTimerStop(CCSPlayerController? player)
        {
            if (player == null || playerTimers[player.UserId ?? 0].IsTimerRunning == false) return;

            SavePlayerTime(player);

            Server.PrintToChatAll(msgPrefix + $"{ChatColors.Red}{player.PlayerName} just finished the map in: {ChatColors.Green}[{FormatTime(playerTimers[player.UserId ?? 0].TimerTicks)}]!");
            playerTimers[player.UserId ?? 0].IsTimerRunning = false;
        }

        public Dictionary<string, int> GetSortedRecords()
        {
            string currentMapName = Server.MapName;

            string recordsFileName = "SharpTimer/player_records.json";
            string recordsPath = Path.Join(Server.GameDirectory + "/csgo/cfg", recordsFileName);

            Dictionary<string, Dictionary<string, PlayerRecord>> records;
            if (File.Exists(recordsPath))
            {
                string json = File.ReadAllText(recordsPath);
                records = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, PlayerRecord>>>(json) ?? new Dictionary<string, Dictionary<string, PlayerRecord>>();
            }
            else
            {
                records = new Dictionary<string, Dictionary<string, PlayerRecord>>();
            }

            if (records.ContainsKey(currentMapName))
            {
                var sortedRecords = records[currentMapName]
                    .OrderBy(record => record.Value.TimerTicks)
                    .ToDictionary(record => record.Key, record => record.Value.TimerTicks);

                return sortedRecords;
            }

            return new Dictionary<string, int>();
        }

        public string GetPlayerPlacement(CCSPlayerController? player)
        {
            if (player == null || !playerTimers.ContainsKey(player.UserId ?? 0) || !playerTimers[player.UserId ?? 0].IsTimerRunning) return "";

            Dictionary<string, int> sortedRecords = GetSortedRecords();
            int currentPlayerTime = playerTimers[player.UserId ?? 0].TimerTicks;

            int placement = 1;

            foreach (var record in sortedRecords)
            {
                if (currentPlayerTime > record.Value)
                {
                    placement++;
                }
                else
                {
                    break;
                }
            }

            return "#" + placement;
        }

        private string GetPlayerNameFromSavedSteamID(string steamId)
        {
            string currentMapName = Server.MapName;

            string recordsFileName = "SharpTimer/player_records.json";
            string recordsPath = Path.Join(Server.GameDirectory + "/csgo/cfg", recordsFileName);

            Dictionary<string, Dictionary<string, PlayerRecord>> records;
            if (File.Exists(recordsPath))
            {
                string json = File.ReadAllText(recordsPath);
                records = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, PlayerRecord>>>(json) ?? new Dictionary<string, Dictionary<string, PlayerRecord>>();

                if (records.ContainsKey(currentMapName) && records[currentMapName].ContainsKey(steamId))
                {
                    return records[currentMapName][steamId].PlayerName;
                }
            }

            return "Unknown"; // Return a default name if the player name is not found
        }

        private Vector ParseVector(string vectorString)
        {
            var values = vectorString.Split(' ');
            if (values.Length == 3 &&
                float.TryParse(values[0], out float x) &&
                float.TryParse(values[1], out float y) &&
                float.TryParse(values[2], out float z))
            {
                return new Vector(x, y, z);
            }

            return new Vector(0, 0, 0);
        }

        public void PrintTopRecords(CCSPlayerController? player)
        {
            if (player == null) return;

            string currentMapName = Server.MapName;

            Dictionary<string, int> sortedRecords = GetSortedRecords();

            if (sortedRecords.Count == 0)
            {
                player.PrintToChat(msgPrefix +$" No records available for {currentMapName}.");
                return;
            }

            player.PrintToChat(msgPrefix + $" Top 10 Records for {currentMapName}:");
            int rank = 1;

            foreach (var record in sortedRecords.Take(10))
            {
                string playerName = GetPlayerNameFromSavedSteamID(record.Key); // Get the player name using SteamID
                player.PrintToChat(msgPrefix + $" #{rank}: {ChatColors.Green}{playerName} {ChatColors.White}- {ChatColors.Green}{FormatTime(record.Value)}");
                rank++;
            }
        }

        public void RespawnPlayer(CCSPlayerController? player)
        {
            if (player == null) return;
            player.PlayerPawn.Value.Teleport(currentRespawnPos, new QAngle(0, 0, 0), new Vector(0, 0, 0));
        }

        private void LoadConfig()
        {
            string currentMapName = Server.MapName;

            string mapdataFileName = "SharpTimer/mapdata.json";
            string mapdataPath = Path.Join(Server.GameDirectory + "/csgo/cfg", mapdataFileName);

            if (File.Exists(mapdataPath))
            {
                string json = File.ReadAllText(mapdataPath);
                var mapData = JsonSerializer.Deserialize<Dictionary<string, MapInfo>>(json);

                if (mapData == null) return;

                if (mapData.TryGetValue(currentMapName, out var mapInfo))
                {
                    currentMapStartC1 = ParseVector(mapInfo.MapStartC1);
                    currentMapStartC2 = ParseVector(mapInfo.MapStartC2);
                    currentMapEndC1 = ParseVector(mapInfo.MapEndC1);
                    currentMapEndC2 = ParseVector(mapInfo.MapEndC2);
                    currentRespawnPos = ParseVector(mapInfo.RespawnPos);
                }
            }
        }
    }
}