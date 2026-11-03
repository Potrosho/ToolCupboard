using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// Loaded by Rust from the HarmonyMods folder. ToolCupboard talks to it over RCON with the toolcupboard.* commands.
    /// </summary>
    public class ToolCupboardMod : IHarmonyModHooks
    {
        // ToolCupboard compares this with the copy it ships, and replaces an older mod
        public const string Version = "8";

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            ModCommands.Register();
            Hooks.Apply();
            Events.Add("loaded", new { version = Version });
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            Hooks.Remove();
            ModCommands.Unregister();
        }
    }

    /// <summary>
    /// Things that happened in game, kept until ToolCupboard collects them with toolcupboard.events.
    /// </summary>
    public static class Events
    {
        const int MAX_QUEUED = 5000;
        static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();

        public static void Add(string type, object data)
        {
            _queue.Enqueue(JsonConvert.SerializeObject(new { type, time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), data }));

            // If ToolCupboard is away for a long time, the oldest events go first
            while (_queue.Count > MAX_QUEUED && _queue.TryDequeue(out _)) { }
        }

        // A JSON array of everything queued, emptying the queue
        public static string Take()
        {
            var taken = new List<string>();
            while (taken.Count < MAX_QUEUED && _queue.TryDequeue(out string item)) { taken.Add(item); }
            return "[" + string.Join(",", taken) + "]";
        }
    }

    public static class ModCommands
    {
        const string PARENT = "toolcupboard";
        static readonly List<ConsoleSystem.Command> _added = new List<ConsoleSystem.Command>();

        public static void Register()
        {
            Add("version", "The ToolCupboard mod version", arg => arg.ReplyWith(JsonConvert.SerializeObject(new { version = ToolCupboardMod.Version })));
            Add("events", "Returns and clears the queued ToolCupboard events", arg => arg.ReplyWith(Events.Take()));
            Add("context", "Where two players are relative to each other: toolcupboard.context <steamid> <steamid>", arg => arg.ReplyWith(Context(arg.GetString(0), arg.GetString(1))));
            Add("rendermap", "Saves the map as a JPG for the ToolCupboard live map", arg => arg.ReplyWith(MapCommands.Render()));
            Add("mapinfo", "The world size, seed and monuments", arg => arg.ReplyWith(MapCommands.Info()));
            Add("positions", "Where every player and sleeper is", arg => arg.ReplyWith(MapCommands.Positions()));
            Add("teleport", "toolcupboard.teleport <steamid> <x> <z> | <steamid> <steamid>", arg => arg.ReplyWith(MapCommands.Teleport(arg.GetString(0), arg.GetString(1), arg.GetString(2))));
            Add("roster", "Who is queued or joining, plus staff, bans and mutes", arg => arg.ReplyWith(Roster.Get()));
            Add("teams", "Lists every team", arg => arg.ReplyWith(TeamsAndClans.Teams()));
            Add("team", "toolcupboard.team promote|kick|disband <teamId> [steamid]", arg => arg.ReplyWith(TeamsAndClans.Team(arg.GetString(0), arg.GetString(1), arg.GetString(2))));
            Add("clans", "Lists every clan", arg => arg.ReplyWith(TeamsAndClans.Clans()));
            Add("clan", "toolcupboard.clan kick|role|uninvite|disband <clanId> [steamid] [roleId]", arg => arg.ReplyWith(TeamsAndClans.Clan(arg.GetString(0), arg.GetString(1), arg.GetString(2), arg.GetString(3))));
        }

        // For a player report: how far apart the two players are and whether they share a team
        static string Context(string first, string second)
        {
            BasePlayer a = ulong.TryParse(first, out ulong idA) ? BasePlayer.FindAwakeOrSleepingByID(idA) : null;
            BasePlayer b = ulong.TryParse(second, out ulong idB) ? BasePlayer.FindAwakeOrSleepingByID(idB) : null;
            if (a == null || b == null) { return JsonConvert.SerializeObject(new { found = false }); }

            return JsonConvert.SerializeObject(new
            {
                found = true,
                distance = Mathf.Round(Vector3.Distance(a.transform.position, b.transform.position)),
                sameTeam = a.currentTeam != 0 && a.currentTeam == b.currentTeam,
                targetOnline = b.IsConnected
            });
        }

        static void Add(string name, string description, Action<ConsoleSystem.Arg> call)
        {
            var command = new ConsoleSystem.Command
            {
                Name = name,
                Parent = PARENT,
                FullName = $"{PARENT}.{name}",
                ServerAdmin = true,
                Variable = false,
                Description = description,
                Call = call
            };

            ConsoleSystem.Index.Server.Dict[command.FullName] = command;
            _added.Add(command);
        }

        public static void Unregister()
        {
            foreach (ConsoleSystem.Command command in _added) { ConsoleSystem.Index.Server.Dict.Remove(command.FullName); }
            _added.Clear();
        }

        // Rust rebuilds its command index while starting, which can drop commands added before that
        public static void KeepAfterInitialize()
        {
            Unregister();
            Register();
        }
    }
}
