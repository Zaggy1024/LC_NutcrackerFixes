using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace NutcrackerFixes.Patches
{
    [HarmonyPatch(typeof(BlobAI))]
    internal class PatchBlobAI
    {
        static readonly MethodInfo m_Component_get_transform = AccessTools.Method(typeof(Component), "get_transform", new Type[0]);
        static readonly MethodInfo m_Transform_get_position = AccessTools.Method(typeof(Transform), "get_position", new Type[0]);
        static readonly MethodInfo m_Transform_set_position = AccessTools.Method(typeof(Transform), "set_position", new Type[] { typeof(Vector3) });

        static readonly FieldInfo f_PlayerControllerB_gameplayCamera = typeof(PlayerControllerB).GetField(nameof(PlayerControllerB.gameplayCamera));

        [HarmonyTranspiler]
        [HarmonyPatch("HitEnemy")]
        static IEnumerable<CodeInstruction> DoHitEnemyTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            // Looking for the following line:
            // movableAudioSource.transform.position = playerWhoHit.gameplayCamera.transform.position + playerWhoHit.gameplayCamera.transform.forward * 1.5f;

            // We will transform it into the following instead:
            // movableAudioSource.transform.position = playerWhoHit == null ? transform.position : playerWhoHit.gameplayCamera.transform.position + playerWhoHit.gameplayCamera.transform.forward * 1.5f;

            // Otherwise, Nutcrackers that shoot blobs will cause an exception to be thrown in the rpc handler for the shotgun,
            // which in turn causes a massive lag spike.

            var instructionsList = instructions.ToList();

            // Find where the PlayerControllerB parameter is dereferenced.
            //   ldarg.2
            //   ldfld UnityEngine.Camera GameNetcodeStuff.PlayerControllerB::gameplayCamera
            var getPlayerCamera = instructionsList.FindIndexOfSequence(new Predicate<CodeInstruction>[]
            {
                insn => insn.IsLdarg(2),
                insn => insn.LoadsField(f_PlayerControllerB_gameplayCamera),
            });
            var playerParameterInstruction = instructionsList[getPlayerCamera.Start];

            var loadPlayerPositionLabel = generator.DefineLabel();
            instructionsList[getPlayerCamera.Start].labels.Add(loadPlayerPositionLabel);

            // Find the set_position() call call to jump to when we have a Vector3 on the stack.
            var setPositionLabel = generator.DefineLabel();
            var setPosition = instructionsList.FindIndex(getPlayerCamera.End, insn => insn.Calls(m_Transform_set_position));
            instructionsList[setPosition].labels.Add(setPositionLabel);

            // If the parameter is null, get the position of the blob instead.
            instructionsList.InsertRange(getPlayerCamera.Start, new CodeInstruction[]
            {
                new CodeInstruction(playerParameterInstruction.opcode, playerParameterInstruction.operand),
                new CodeInstruction(OpCodes.Brtrue_S, loadPlayerPositionLabel),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, m_Component_get_transform),
                new CodeInstruction(OpCodes.Call, m_Transform_get_position),
                new CodeInstruction(OpCodes.Br, setPositionLabel),
            });

            return instructionsList;
        }
    }
}
