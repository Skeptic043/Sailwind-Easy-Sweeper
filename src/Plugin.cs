using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace EasySweeper
{
    [BepInPlugin(Guid, PluginName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.skeptic043.sailwind.easysweeper";
        public const string PluginName = "Easy Sweeper";
        public const string Version = "0.1.0";

        private static AccessTools.FieldRef<ShipItemBroom, Cleaner> cleanerRef;
        private static float nextStroke;

        private void Awake()
        {
            try
            {
                // GoPointer calls OnAltHeld() on the held item every frame while Use is held.
                // ShipItemBroom doesn't override it, so the postfix goes on the base method.
                MethodInfo altHeld = AccessTools.Method(typeof(GoPointerButton), nameof(GoPointerButton.OnAltHeld), Type.EmptyTypes)
                    ?? throw new MissingMethodException("GoPointerButton.OnAltHeld() was not found.");
                RequireField(typeof(Cleaner), nameof(Cleaner.activated));
                RequireField(typeof(ShipItem), nameof(ShipItem.sold));
                cleanerRef = AccessTools.FieldRefAccess<ShipItemBroom, Cleaner>("cleaner");

                new Harmony(Guid).Patch(altHeld, postfix: new HarmonyMethod(typeof(Plugin), nameof(Postfix)));
            }
            catch (Exception e)
            {
                Logger.LogError("Easy Sweeper could not patch the game and is inactive: " + e.Message);
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
            if (StrokeTimer.ShouldRequest(cleaner.activated, Time.time, ref nextStroke))
                cleaner.activated = true;
        }
    }
}
