using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// Loaded by Rust from the HarmonyMods folder. ToolCupboard talks to it over RCON with the toolcupboard.* commands.
    /// </summary>
    public class ToolCupboardMod : IHarmonyModHooks
    {
        // ToolCupboard compares this with the copy it ships, and replaces an older mod
        public const string Version = "2";

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            ModCommands.Register();
            Events.Add("loaded", new { version = Version });
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
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
        [HarmonyPatch(typeof(ConsoleSystem.Index), nameof(ConsoleSystem.Index.Initialize))]
        static class KeepCommandsAfterInitialize
        {
            static void Postfix()
            {
                Unregister();
                Register();
            }
        }
    }
}
