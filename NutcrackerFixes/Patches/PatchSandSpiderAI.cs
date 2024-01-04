using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;

using HarmonyLib;
using UnityEngine;
using Unity.Netcode;

namespace NutcrackerFixes.Patches
{
    [HarmonyPatch(typeof(SandSpiderAI))]
    internal class PatchSandSpiderAI
    {
        static readonly MethodInfo m_NetworkBehavior_get_IsOwner = typeof(NetworkBehaviour).GetMethod("get_IsOwner");
        static readonly MethodInfo m_SandSpiderAI_TriggerChaseWithPlayer = typeof(SandSpiderAI).GetMethod(nameof(SandSpiderAI.TriggerChaseWithPlayer));

        static IEnumerable<CodeInstruction> DoHitEnemyTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var instructionsList = instructions.ToList();

            var chasePlayer = instructionsList.FindIndexOfSequence(new Predicate<CodeInstruction>[]
            {
                insn => insn.IsLdarg(0),
                insn => insn.Calls(m_NetworkBehavior_get_IsOwner),
                insn => insn.Branches(out _),

                insn => insn.IsLdarg(0),
                insn => insn.IsLdarg(),
                insn => insn.Calls(m_SandSpiderAI_TriggerChaseWithPlayer),
            });
            var skipTriggerChaseLabel = (Label)instructionsList[chasePlayer.Start + 2].operand;
            instructionsList.InsertRange(chasePlayer.Start + 3, new CodeInstruction[]
            {
                new CodeInstruction(instructionsList[chasePlayer.End - 2]),
                new CodeInstruction(OpCodes.Brfalse_S, skipTriggerChaseLabel),
            });

            return instructionsList;
        }

        [HarmonyTranspiler]
        [HarmonyPatch("HitEnemy")]
        static IEnumerable<CodeInstruction> HitEnemyTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            foreach (var instruction in DoHitEnemyTranspiler(instructions, generator))
            {
                if (Plugin.DEBUG_TRANSPILERS)
                    Plugin.Instance.Logger.LogInfo(instruction.ToString());
                yield return instruction;
            }
        }
    }
}
