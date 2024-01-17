using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

using NutcrackerFixes.Patches;

namespace NutcrackerFixes
{
    [BepInPlugin(MOD_UNIQUE_NAME, MOD_NAME, MOD_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private const string MOD_NAME = "NutcrackerFixes";
        private const string MOD_UNIQUE_NAME = "Zaggy1024." + MOD_NAME;
        private const string MOD_VERSION = "1.0.1";

        private readonly Harmony harmony = new Harmony(MOD_UNIQUE_NAME);

        public static Plugin Instance { get; private set; }
        public new ManualLogSource Logger => base.Logger;

        public static ConfigEntry<bool> Shotgun_SynchronizeShellsAndSafety;

        void Awake()
        {
            Instance = this;

            Shotgun_SynchronizeShellsAndSafety = Config.Bind("Shotgun", "SynchronizeShellsAndSafety", true,
                "Synchronize the shotgun shell count and safety setting upon picking up the shotgun. " +
                "This feature must be installed and enabled for everyone, or the safety will not function as expected. " +
                "Fixes an issue where too many items on the ship would cause the number of loaded shells as well as the safety " +
                "to be desynced between the server and clients.");

            harmony.PatchAll(typeof(PatchNutcrackerEnemyAI));
            harmony.PatchAll(typeof(PatchShotgunItem));
            harmony.PatchAll(typeof(PatchBlobAI));
            harmony.PatchAll(typeof(PatchSandSpiderAI));
        }
    }
}
