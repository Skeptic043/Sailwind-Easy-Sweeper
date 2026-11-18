using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EasySweeper
{
    [BepInPlugin(Guid, PluginName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.skeptic043.sailwind.easysweeper";
        public const string PluginName = "Easy Sweeper";
        public const string Version = "0.1.1";

        private const string Section = "Sweeping";

        private static ManualLogSource log;
        private static ConfigEntry<int> strokeLength;
        private static ConfigEntry<float> sweepSpeed;
        private static AccessTools.FieldRef<ShipItemBroom, Cleaner> cleanerRef;
        private static float strokeStart = float.NegativeInfinity;
        private static bool swingPatched;

        private void Awake()
        {
            log = Logger;
            strokeLength = Config.Bind(Section, "StrokeLength", 75, new ConfigDescription(
                "How far each held stroke goes before the broom turns back, as a percent of a full stroke. "
                + "Lower values give shorter, quicker strokes.",
                new AcceptableValueRange<int>(25, 95)));
            sweepSpeed = Config.Bind(Section, "SweepSpeed", 1f, new ConfigDescription(
                "How fast the broom swings, for both held and clicked strokes. 1 is the normal game speed, "
                + "2 is twice as fast and 3 is three times as fast.",
                new AcceptableValueRange<float>(1f, 3f)));

            var harmony = new Harmony(Guid);
            try
            {
                // GoPointer calls OnAltHeld() on the held item every frame while Use is held.
                // ShipItemBroom doesn't override it, so the postfix goes on the base method.
                MethodInfo altHeld = AccessTools.Method(typeof(GoPointerButton), nameof(GoPointerButton.OnAltHeld), Type.EmptyTypes)
                    ?? throw new MissingMethodException("GoPointerButton.OnAltHeld() was not found.");
                RequireField(typeof(Cleaner), nameof(Cleaner.activated));
                RequireField(typeof(ShipItem), nameof(ShipItem.sold));
                cleanerRef = AccessTools.FieldRefAccess<ShipItemBroom, Cleaner>("cleaner");

                harmony.Patch(altHeld, postfix: new HarmonyMethod(typeof(Plugin), nameof(Postfix)));
            }
            catch (Exception e)
            {
                Logger.LogError("Easy Sweeper could not patch the game and is inactive: " + e.Message);
            }

            try
            {
                MethodInfo cleanerLateUpdate = AccessTools.Method(typeof(Cleaner), "LateUpdate", Type.EmptyTypes)
                    ?? throw new MissingMethodException("Cleaner.LateUpdate() was not found.");
                harmony.Patch(cleanerLateUpdate, transpiler: new HarmonyMethod(typeof(Plugin), nameof(Transpiler)));
            }
            catch (Exception e)
            {
                Logger.LogWarning("SweepSpeed is unavailable and the broom swings at normal speed: " + e.Message);
            }
        }

        private static void RequireField(Type type, string name)
        {
            if (AccessTools.Field(type, name)?.FieldType != typeof(bool))
                throw new MissingFieldException(type.Name, name);
        }

        private static void Postfix(GoPointerButton __instance)
        {
            if (!(__instance is ShipItemBroom broom) || !broom.sold)
                return;

            // Setting activated is exactly what a native click does; the game does the sweeping.
            Cleaner cleaner = cleanerRef(broom);
            float seconds = StrokeTimer.StrokeSeconds(strokeLength.Value, SwingSpeed);
            if (StrokeTimer.ShouldRequest(cleaner.activated, Time.time, seconds, ref strokeStart))
                cleaner.activated = true;
        }

        // The held stroke timing follows the swing only when the swing was actually sped up.
        private static float SwingSpeed => swingPatched ? sweepSpeed.Value : 1f;

        private static float SwingRate() => StrokeTimer.NativeRate * sweepSpeed.Value;

        /// <summary>
        /// The broom's swing eases at Time.deltaTime * 5, once for its rotation and once for its position.
        /// Both 5s become SwingRate(). Anything else leaves the method unchanged.
        /// </summary>
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            MethodInfo deltaTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
            List<int> rates = Enumerable.Range(1, code.Count - 1)
                .Where(i => code[i - 1].Calls(deltaTime) && code[i].opcode == OpCodes.Ldc_R4
                    && code[i].operand is float f && f == StrokeTimer.NativeRate)
                .ToList();

            if (rates.Count != 2)
            {
                log.LogWarning($"SweepSpeed is unavailable: expected 2 broom swing rates in Cleaner.LateUpdate, found {rates.Count}.");
                return code;
            }

            MethodInfo swingRate = AccessTools.Method(typeof(Plugin), nameof(SwingRate));
            foreach (int i in rates)
            {
                code[i].opcode = OpCodes.Call;
                code[i].operand = swingRate;
            }
            swingPatched = true;
            return code;
        }
    }
}
