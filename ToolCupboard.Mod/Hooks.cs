using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// Applies each hook on its own, so a Rust update that changes one method only turns off that one feature.
    /// </summary>
    static class Hooks
    {
        const string ID = "com.toolcupboard.mod";
        static readonly Harmony _harmony = new Harmony(ID);

        public static void Apply()
        {
            Hook(typeof(ConsoleSystem.Index), nameof(ConsoleSystem.Index.Initialize), postfix: nameof(ModCommands.KeepAfterInitialize), holder: typeof(ModCommands));
            Hook(typeof(BasePlayer), nameof(BasePlayer.OnDied), prefix: nameof(DeathEvents.OnDied), holder: typeof(DeathEvents));
            Hook(typeof(BasePlayer), nameof(BasePlayer.PlayerInit), postfix: nameof(PlayerEvents.Join), holder: typeof(PlayerEvents));
            Hook(typeof(BasePlayer), nameof(BasePlayer.OnDisconnected), prefix: nameof(PlayerEvents.Leave), holder: typeof(PlayerEvents));
            Hook(typeof(BasePlayer), nameof(BasePlayer.Kick), prefix: nameof(PlayerEvents.Kick), holder: typeof(PlayerEvents));
            Hook(typeof(ServerUsers), nameof(ServerUsers.Set), postfix: nameof(PlayerEvents.Ban), holder: typeof(PlayerEvents));
        }

        public static void Remove() => _harmony.UnpatchAll(ID);

        // With overloads, the one with the most parameters is hooked, so hooks read arguments by position from __args
        static void Hook(Type type, string method, string prefix = null, string postfix = null, Type holder = null)
        {
            try
            {
                MethodInfo target = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == method).OrderByDescending(m => m.GetParameters().Length).FirstOrDefault()
                    ?? throw new MissingMethodException(type.Name, method);
                _harmony.Patch(target,
                    prefix == null ? null : new HarmonyMethod(AccessTools.Method(holder, prefix)),
                    postfix == null ? null : new HarmonyMethod(AccessTools.Method(holder, postfix)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ToolCupboard] Could not hook {type.Name}.{method}, so that feature is off: {e.Message}");
            }
        }
    }
}
