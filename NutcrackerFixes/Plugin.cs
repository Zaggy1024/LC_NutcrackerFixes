using BepInEx;
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

        public const bool DEBUG_TRANSPILERS = false;

        private readonly Harmony harmony = new Harmony(MOD_UNIQUE_NAME);

        public static Plugin Instance { get; private set; }
        public new ManualLogSource Logger => base.Logger;

        void Awake()
        {
            Instance = this;

            harmony.PatchAll(typeof(PatchNutcrackerEnemyAI));
            harmony.PatchAll(typeof(PatchShotgunItem));
            harmony.PatchAll(typeof(PatchBlobAI));
            harmony.PatchAll(typeof(PatchSandSpiderAI));
        }
    }
}
