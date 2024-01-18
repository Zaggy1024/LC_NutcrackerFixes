using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace NutcrackerFixes.Patches
{
    [HarmonyPatch(typeof(ShotgunItem))]
    internal class PatchShotgunItem
    {
        static readonly MethodInfo m_PatchShotgunItem_SetSafetyServerRpc = typeof(PatchShotgunItem).GetMethod(nameof(SetSafetyServerRpc), new Type[] { typeof(ShotgunItem), typeof(bool) });

        static readonly FieldInfo f_ShotgunItem_enemyColliders = AccessTools.Field(typeof(ShotgunItem), "enemyColliders");
        static readonly FieldInfo f_ShotgunItem_safetyOn = typeof(ShotgunItem).GetField(nameof(ShotgunItem.safetyOn));
        static readonly MethodInfo m_ShotgunItem_SetSafetyControlTip = AccessTools.Method(typeof(ShotgunItem), "SetSafetyControlTip", new Type[0]);

        static readonly MethodInfo m_Physics_SphereCastNonAlloc = AccessTools.Method(typeof(Physics), "SphereCastNonAlloc", new Type[] { typeof(Ray), typeof(float), typeof(RaycastHit[]), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });

        static readonly ConstructorInfo m_HashSet_EnemyAI_Constructor = typeof(HashSet<EnemyAI>).GetConstructor(new Type[0]);
        static readonly MethodInfo m_HashSet_EnemyAI_Contains = typeof(HashSet<EnemyAI>).GetMethod(nameof(HashSet<EnemyAI>.Contains), new Type[] { typeof(EnemyAI) });
        static readonly MethodInfo m_HashSet_EnemyAI_Add = typeof(HashSet<EnemyAI>).GetMethod(nameof(HashSet<EnemyAI>.Add), new Type[] { typeof(EnemyAI) });

        static readonly MethodInfo m_Component_GetComponent_EnemyAICollisionDetect = Common.GetGenericMethod(typeof(Component), "GetComponent", new Type[0], new Type[] { typeof(EnemyAICollisionDetect) });
        static readonly MethodInfo m_Component_TryGetComponent_IHittable = Common.GetGenericMethod(typeof(Component), "TryGetComponent", new Type[] { typeof(IHittable).MakeByRefType() }, new Type[] { typeof(IHittable) });

        static readonly MethodInfo m_UnityEngine_Object_op_Implicit = typeof(UnityEngine.Object).GetMethod("op_Implicit", new Type[] { typeof(UnityEngine.Object) });

        static readonly FieldInfo f_EnemyAICollisionDetect_onlyCollideWhenGrounded = typeof(EnemyAICollisionDetect).GetField(nameof(EnemyAICollisionDetect.onlyCollideWhenGrounded));
        static readonly FieldInfo f_EnemyAICollisionDetect_mainScript = typeof(EnemyAICollisionDetect).GetField(nameof(EnemyAICollisionDetect.mainScript));

        static readonly MethodInfo m_RaycastHit_get_transform = AccessTools.Method(typeof(RaycastHit), "get_transform", new Type[0]);

        [HarmonyTranspiler]
        [HarmonyPatch("ShootGun")]
        static IEnumerable<CodeInstruction> ShootGunTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var instructionsAsList = instructions.ToList();

            // Looking for the following:
            //   newarr UnityEngine.RaycastHit
            // This array is stored to the ShotgunItem::enemyColliders field, but we want this array to be larger, so we
            // will catch the instantiation and enlarge it. This will prevent the huge number of colliders that the blobs
            // use from causing non-registering shots.
            var newArrIndex = instructionsAsList.FindIndex(insn => insn.opcode == OpCodes.Newarr && insn.operand as Type == typeof(RaycastHit));
            instructionsAsList[newArrIndex - 1].operand = 50;

            var sphereCast = instructionsAsList.FindIndexOfSequence(new Predicate<CodeInstruction>[]
            {
                insn => insn.Calls(m_Physics_SphereCastNonAlloc),
                insn => insn.IsStloc(),
            });

            // Create a set to contain the EnemyAI instances that have been hit by this shot to avoid hitting them
            // multiple times.
            //   var hitAISet = new HashSet<EnemyAI>();
            // This prevents shooting blobs from triggering a massive number of sound effects and causing a significant
            // stutter.
            LocalBuilder hitAISetVar = generator.DeclareLocal(typeof(HashSet<EnemyAI>));
            var constructHashSetInstructions = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Newobj, m_HashSet_EnemyAI_Constructor),
                new CodeInstruction(OpCodes.Stloc, hitAISetVar),
            };
            instructionsAsList.InsertRange(sphereCast.End, constructHashSetInstructions);
            sphereCast.End += constructHashSetInstructions.Length;

            // Find the start of the loop over the hit enemies. The expected sequence to initialize the
            // index variable is:
            //   ldc.i4.0
            //   stloc.s i(System.Int32)
            //   br Label19
            var loopStart = instructionsAsList.FindIndexOfSequence(sphereCast.End, new Predicate<CodeInstruction>[]
            {
                insn => insn.LoadsConstant(0),
                insn => insn.IsStloc(),
                insn => insn.Branches(out _),
            });
            var hitEnemyIndexVar = (LocalBuilder)instructionsAsList[loopStart.Start + 1].operand;

            var continueLabel = generator.DefineLabel();
            LocalBuilder mainScriptVar;

            {
                // Find the enemy AI's main script in the loop. This should be the field load in the line:
                //   if (!enemyColliders[i].transform.GetComponent<EnemyAICollisionDetect>())
                //     break;
                // The CIL is:
                //   ldarg.0
                //   ldfld valuetype [UnityEngine.PhysicsModule]UnityEngine.RaycastHit[] ShotgunItem::enemyColliders
                //   ldloc.s 13
                //   ldelema [UnityEngine.PhysicsModule]UnityEngine.RaycastHit
                //   call instance class [UnityEngine.CoreModule]UnityEngine.Transform [UnityEngine.PhysicsModule]UnityEngine.RaycastHit::get_transform()
                //   callvirt EnemyAICollisionDetect UnityEngine.Component::GetComponent()
                //   call bool UnityEngine.Object::op_Implicit(UnityEngine.Object)
                //   brtrue.s aiCollisionExists
                //   ret
                var getAICollisionSequence = new Predicate<CodeInstruction>[]
                {
                    insn => insn.IsLdarg(0),
                    insn => insn.LoadsField(f_ShotgunItem_enemyColliders),
                    insn => insn.IsLdloc(hitEnemyIndexVar),
                    insn => insn.opcode == OpCodes.Ldelema,
                    insn => insn.Calls(m_RaycastHit_get_transform),
                    insn => insn.Calls(m_Component_GetComponent_EnemyAICollisionDetect),
                };
                var aiCollisionCheck = instructionsAsList.FindIndexOfSequence(loopStart.End, getAICollisionSequence.Concat(new Predicate<CodeInstruction>[]
                {
                    insn => insn.Calls(m_UnityEngine_Object_op_Implicit),
                    insn => insn.Branches(out _),
                    insn => insn.opcode == OpCodes.Ret,
                }));
                instructionsAsList.RemoveRange(aiCollisionCheck.Start, aiCollisionCheck.Size);
                aiCollisionCheck.End -= aiCollisionCheck.Size;

                // Add a check to skip the collision if it will not pass the hit through to its EnemyAI.
                // Otherwise, the duplicate hit check will discount hits that did not actually cause any damage.
                // Crawlers have the onlyCollideWhenGrounded field set to true, and so they do not take damage
                // without this, unless we deal damage to all colliders regardless of whether they have already
                // been hit.
                var enemyAICollisionVar = generator.DeclareLocal(typeof(EnemyAICollisionDetect));
                var checkIfHitWillOccurInstructions = new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, f_ShotgunItem_enemyColliders),
                    new CodeInstruction(OpCodes.Ldloc, hitEnemyIndexVar),
                    new CodeInstruction(OpCodes.Ldelema, typeof(RaycastHit)),
                    new CodeInstruction(OpCodes.Call, m_RaycastHit_get_transform),
                    new CodeInstruction(OpCodes.Call, m_Component_GetComponent_EnemyAICollisionDetect),
                    new CodeInstruction(OpCodes.Stloc, enemyAICollisionVar),

                    new CodeInstruction(OpCodes.Ldloc, enemyAICollisionVar),
                    new CodeInstruction(OpCodes.Call, m_UnityEngine_Object_op_Implicit),
                    new CodeInstruction(OpCodes.Brfalse_S, continueLabel),

                    new CodeInstruction(OpCodes.Ldloc, enemyAICollisionVar),
                    new CodeInstruction(OpCodes.Ldfld, f_EnemyAICollisionDetect_onlyCollideWhenGrounded),
                    new CodeInstruction(OpCodes.Brtrue_S, continueLabel),
                };
                instructionsAsList.InsertRange(aiCollisionCheck.Start, checkIfHitWillOccurInstructions);

                // This will also match a later portion where the hit AI's main script will be stored to
                // a variable. We need to grab this for the duplicate hit detection. That looks like:
                //   ldfld class EnemyAI EnemyAICollisionDetect::mainScript
                //   stloc.s mainScript
                var mainScriptSequence = instructionsAsList.FindIndexOfSequence(aiCollisionCheck.Start + checkIfHitWillOccurInstructions.Length, getAICollisionSequence.Concat(new Predicate<CodeInstruction>[]
                {
                    insn => insn.LoadsField(f_EnemyAICollisionDetect_mainScript),
                    insn => insn.IsStloc(),
                }));
                mainScriptVar = (LocalBuilder)instructionsAsList[mainScriptSequence.End - 1].operand;

                instructionsAsList.RemoveRange(mainScriptSequence.Start, mainScriptSequence.Size);
                instructionsAsList.InsertRange(mainScriptSequence.Start, new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Ldloc, enemyAICollisionVar),
                    new CodeInstruction(OpCodes.Ldfld, f_EnemyAICollisionDetect_mainScript),
                    new CodeInstruction(OpCodes.Stloc, mainScriptVar),
                });
            }

            {
                // Find the following line:
                //   if (enemyColliders[i].transform.TryGetComponent<IHittable>(out component))
                // IL representation:
                //   ldarg.0
                //   ldfld UnityEngine.RaycastHit[] ShotgunItem::enemyColliders
                //   ldloc.s 13
                //   ldelema UnityEngine.RaycastHit
                //   call UnityEngine.Transform UnityEngine.RaycastHit::get_transform()
                //   ldloca.s 10
                //   callvirt bool UnityEngine.Component::TryGetComponent<class IHittable>(!!0&)
                //   brfalse.s noHit
                var ifHittable = instructionsAsList.FindIndexOfSequence(loopStart.End, new Predicate<CodeInstruction>[]
                {
                    insn => insn.IsLdarg(0),
                    insn => insn.LoadsField(f_ShotgunItem_enemyColliders),
                    insn => insn.IsLdloc(hitEnemyIndexVar),
                    insn => insn.opcode == OpCodes.Ldelema,
                    insn => insn.Calls(m_RaycastHit_get_transform),
                    insn => insn.opcode == OpCodes.Ldloca_S,
                    insn => insn.Calls(m_Component_TryGetComponent_IHittable),
                    insn => insn.Branches(out _),
                });

                // Use the mainScript variable containing the AI component that is damaged by the hit
                // to check whether we are hitting an enemy twice. This will prevent us from doing extra
                // work against blobs.
                //   if (hitAISet.Contains(mainScript))
                //     continue;
                //   hitAISet.Add(mainScript);
                instructionsAsList.InsertRange(ifHittable.End, new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Ldloc_S, hitAISetVar),
                    new CodeInstruction(OpCodes.Ldloc_S, mainScriptVar),
                    new CodeInstruction(OpCodes.Call, m_HashSet_EnemyAI_Contains),
                    new CodeInstruction(OpCodes.Brtrue_S, continueLabel),

                    new CodeInstruction(OpCodes.Ldloc_S, hitAISetVar),
                    new CodeInstruction(OpCodes.Ldloc_S, mainScriptVar),
                    new CodeInstruction(OpCodes.Call, m_HashSet_EnemyAI_Add),
                    new CodeInstruction(OpCodes.Pop),
                });
            }

            // Find the end of the loop to add our new continue label.
            // It will look like:
            //   ldloc.s i(System.Int32)[Label25, Label31]
            //   ldc.i4.1
            //   add
            var loopEnd = instructionsAsList.FindIndexOfSequence(loopStart.End, new Predicate<CodeInstruction>[] {
                insn => insn.IsLdloc(),
                insn => insn.LoadsConstant(1),
                insn => insn.opcode == OpCodes.Add,
            });
            instructionsAsList[loopEnd.Start].labels.Add(continueLabel);

            // Look for loop breaks which will be implemented as:
            //   ret
            // to replace them with:
            //   br continueLabel
            // Where continueLabel refers to the label that continues at the `i++` of the for loop.
            // This will prevent the shotgun from skipping the rest of the hit enemies due to a failed
            // hit early in the loop.
            var instructionIndex = loopStart.End;
            while (true)
            {
                instructionIndex = instructionsAsList.FindIndex(instructionIndex, loopEnd.Start - instructionIndex, insn => insn.opcode == OpCodes.Ret);
                if (instructionIndex < 0)
                    break;
                instructionsAsList[instructionIndex] = new CodeInstruction(OpCodes.Br_S, continueLabel);
            }

            // Find and turn logging in the hit registration loop into no-ops.
            instructionIndex = loopStart.End;
            while (true)
            {
                instructionIndex = instructionsAsList.FindIndex(instructionIndex, loopEnd.Start - instructionIndex, insn => insn.Calls(Common.m_Debug_Log));
                if (instructionIndex < 0)
                    break;
                instructionsAsList[instructionIndex] = new CodeInstruction(OpCodes.Pop);
            }

            return instructionsAsList;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ShotgunItem.EquipItem))]
        static void EquipItemPostfix(ShotgunItem __instance)
        {
            if (!Plugin.Shotgun_SynchronizeShellsAndSafety.Value)
                return;
            SynchronizeServerRpc(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ShotgunItem.LoadItemSaveData))]
        static void LoadItemSaveDataPostfix(ShotgunItem __instance)
        {
            if (!Plugin.Shotgun_SynchronizeShellsAndSafety.Value)
                return;
            SetSafetyServerRpc(__instance, __instance.safetyOn);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(nameof(GrabbableObject.ItemInteractLeftRight))]
        static IEnumerable<CodeInstruction> ItemInteractLeftRightTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var instructionsList = instructions.ToList();

            var checkIsLeftInteract = instructionsList.FindIndexOfSequence(new Predicate<CodeInstruction>[]
            {
                insn => insn.IsLdarg(1),
                insn => insn.opcode == OpCodes.Brtrue || insn.opcode == OpCodes.Brtrue_S,
            });
            var isLeftInteractEndLabel = (Label)instructionsList[checkIsLeftInteract.End - 1].operand;
            var isLeftInteractEnd = instructionsList.FindIndex(insn => insn.labels.Contains(isLeftInteractEndLabel));

            var isOwnerLabel = generator.DefineLabel();
            instructionsList[checkIsLeftInteract.End].labels.Add(isOwnerLabel);
            var skipIfNotOwnerInstructions = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, Reflection.m_NetworkBehaviour_get_IsOwner),
                new CodeInstruction(OpCodes.Brtrue_S, isOwnerLabel),
                new CodeInstruction(OpCodes.Ret),
            };
            instructionsList.InsertRange(checkIsLeftInteract.End, skipIfNotOwnerInstructions);

            var setSafetyOnServerInstructions = new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, f_ShotgunItem_safetyOn),
                new CodeInstruction(OpCodes.Call, m_PatchShotgunItem_SetSafetyServerRpc),
            };
            instructionsList.InsertRange(isLeftInteractEnd - 1 + skipIfNotOwnerInstructions.Length, setSafetyOnServerInstructions);

            return instructionsList;
        }

        const uint SET_SAFETY_SERVER_RPC = 391;
        const uint SET_SAFETY_CLIENT_RPC = 392;

        public static void SetSafetyServerRpc(ShotgunItem shotgun, bool safetyOn)
        {
            if (!shotgun.IsOwner && !shotgun.IsHost && !shotgun.IsServer)
                return;

            var networkManager = shotgun.NetworkManager;
            if (networkManager is null || !networkManager.IsListening)
                return;

            if (shotgun.IsHost || shotgun.IsServer)
            {
                SetSafetyClientRpc(shotgun, safetyOn);
                return;
            }

            if (shotgun.IsClient)
            {
                var rpcParams = default(ServerRpcParams);
                var buffer = Reflection.BeginSendServerRPC(shotgun, SET_SAFETY_SERVER_RPC, rpcParams, RpcDelivery.Reliable);
                buffer.WriteValueSafe(safetyOn);
                Reflection.EndSendServerRPC(shotgun, ref buffer, SET_SAFETY_SERVER_RPC, rpcParams, RpcDelivery.Reliable);
            }
        }

        public static void SetSafetyClientRpc(ShotgunItem shotgun, bool safetyOn)
        {
            if (!shotgun.IsOwner && !shotgun.IsHost && !shotgun.IsServer)
                return;

            var networkManager = shotgun.NetworkManager;
            if (networkManager is null || !networkManager.IsListening)
                return;

            if (shotgun.IsHost || shotgun.IsServer)
            {
                var rpcParams = default(ClientRpcParams);
                var buffer = Reflection.BeginSendClientRPC(shotgun, SET_SAFETY_CLIENT_RPC, rpcParams, RpcDelivery.Reliable);
                buffer.WriteValueSafe(safetyOn);
                Reflection.EndSendClientRPC(shotgun, ref buffer, SET_SAFETY_CLIENT_RPC, rpcParams, RpcDelivery.Reliable);
            }
        }

        static void RPCHandlerSetSafetyServer(NetworkBehaviour target, FastBufferReader reader, __RpcParams parameters)
        {
            var networkManager = target.NetworkManager;
            if (networkManager is null || !networkManager.IsListening)
                return;
            reader.ReadValue(out bool safetyOn);
            SetSafetyClientRpc((ShotgunItem)target, safetyOn);
        }

        static void RPCHandlerSetSafetyClient(NetworkBehaviour target, FastBufferReader reader, __RpcParams parameters)
        {
            var networkManager = target.NetworkManager;
            if (networkManager is null || !networkManager.IsListening)
                return;
            reader.ReadValue(out bool safetyOn);
            SetSafety((ShotgunItem)target, safetyOn);
        }

        static void SetSafety(ShotgunItem shotgun, bool safetyOn)
        {
            shotgun.safetyOn = safetyOn;
            m_ShotgunItem_SetSafetyControlTip.Invoke(shotgun, new object[0]);
        }

        const uint SYNCHRONIZE_CLIENT_RPC = 393;
        const uint SYNCHRONIZE_SERVER_RPC = 394;

        public static void SynchronizeServerRpc(ShotgunItem shotgun)
        {
            if (!shotgun.IsOwner || shotgun.IsHost || shotgun.IsServer)
                return;

            var networkManager = shotgun.NetworkManager;
            if (networkManager is null || !networkManager.IsListening)
                return;

            if (shotgun.IsHost || shotgun.IsServer)
            {
                SynchronizeClientRpc(shotgun);
                return;
            }

            if (shotgun.IsClient)
            {
                var rpcParams = default(ServerRpcParams);
                var buffer = Reflection.BeginSendServerRPC(shotgun, SYNCHRONIZE_SERVER_RPC, rpcParams, RpcDelivery.Reliable);
                Reflection.EndSendServerRPC(shotgun, ref buffer, SYNCHRONIZE_SERVER_RPC, rpcParams, RpcDelivery.Reliable);
            }
        }

        public static void SynchronizeClientRpc(ShotgunItem shotgun)
        {
            if (!shotgun.IsHost && !shotgun.IsServer)
                return;

            var networkManager = shotgun.NetworkManager;
            if (networkManager is null || !networkManager.IsListening)
                return;

            if (shotgun.IsHost || shotgun.IsServer)
            {
                var rpcParams = default(ClientRpcParams);
                var buffer = Reflection.BeginSendClientRPC(shotgun, SYNCHRONIZE_CLIENT_RPC, rpcParams, RpcDelivery.Reliable);
                buffer.WriteValueSafe(shotgun.shellsLoaded);
                buffer.WriteValueSafe(shotgun.safetyOn);
                Reflection.EndSendClientRPC(shotgun, ref buffer, SYNCHRONIZE_CLIENT_RPC, rpcParams, RpcDelivery.Reliable);
            }
        }

        static void RPCHandlerSynchronizeServer(NetworkBehaviour target, FastBufferReader reader, __RpcParams parameters)
        {
            var networkManager = target.NetworkManager;
            if (networkManager is null || !networkManager.IsListening)
                return;
            SynchronizeClientRpc((ShotgunItem)target);
        }

        static void RPCHandlerSynchronizeClient(NetworkBehaviour target, FastBufferReader reader, __RpcParams parameters)
        {
            var networkManager = target.NetworkManager;
            if (networkManager is null || !networkManager.IsListening)
                return;
            reader.ReadValue(out int shellsLoaded);
            reader.ReadValue(out bool safetyOn);
            Synchronize((ShotgunItem)target, shellsLoaded, safetyOn);
        }

        static void Synchronize(ShotgunItem shotgun, int shellsLoaded, bool safetyOn)
        {
            shotgun.shellsLoaded = shellsLoaded;
            shotgun.shotgunShellLeft.enabled = shellsLoaded >= 1;
            shotgun.shotgunShellRight.enabled = shellsLoaded >= 2;
            shotgun.safetyOn = safetyOn;
            m_ShotgunItem_SetSafetyControlTip.Invoke(shotgun, new object[0]);
        }

        [HarmonyPostfix]
        [HarmonyPatch("InitializeRPCS_ShotgunItem")]
        static void InitializeRPCS_ShotgunItemPostfix()
        {
            NetworkManager.__rpc_func_table.Add(SET_SAFETY_SERVER_RPC, RPCHandlerSetSafetyServer);
            NetworkManager.__rpc_func_table.Add(SET_SAFETY_CLIENT_RPC, RPCHandlerSetSafetyClient);

            NetworkManager.__rpc_func_table.Add(SYNCHRONIZE_SERVER_RPC, RPCHandlerSynchronizeServer);
            NetworkManager.__rpc_func_table.Add(SYNCHRONIZE_CLIENT_RPC, RPCHandlerSynchronizeClient);
        }
    }
}
