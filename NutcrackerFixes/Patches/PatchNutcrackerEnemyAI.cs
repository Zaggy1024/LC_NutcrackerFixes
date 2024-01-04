using System.Collections.Generic;
using System.Reflection.Emit;

using HarmonyLib;

namespace NutcrackerFixes.Patches
{
    [HarmonyPatch(typeof(NutcrackerEnemyAI))]
    internal class PatchNutcrackerEnemyAI
    {
        [HarmonyTranspiler]
        [HarmonyPatch("TurnTorsoToTargetDegrees")]
        static IEnumerable<CodeInstruction> TurnTorsoToTargetDegreesTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                // Remove debug logs that spam constantly from the AI Update() function.
                if (instruction.Calls(Common.m_Debug_Log))
                    yield return new CodeInstruction(OpCodes.Pop);
                else
                    yield return instruction;
            }
        }
    }
}
