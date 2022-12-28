using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using System.Collections;

namespace MorePlayers {
	[BepInAutoPlugin]
	public partial class MPPlugin : BaseUnityPlugin {
		private readonly Harmony harmony = new Harmony(Id);
		public static MPPlugin Instance;

		private ConfigEntry<int> maxPlayers;

		private void Awake() {
			Instance = this;
            maxPlayers = Config.Bind("General",
                                     "maxPlayers",
                                     8,
                                     "Amount of players allowed to join a world");
			
			harmony.PatchAll();			
			Logger.LogInfo($"Plugin {Id} is loaded!");
		}
		
		public static int getMaxPlayers() {
			return MPPlugin.Instance.maxPlayers.Value;
		}
		
		public static void Log(string msg) {
			MPPlugin.Instance.Logger.LogInfo(msg);
		}
	}
	
	// TODO: Patch NetworkPlayersManager::refreshButtons
	
	[HarmonyPatch(typeof(CustomNetworkManager), "OnEnable")]
	public static class NetworkManagerStartupPatch {
		[HarmonyPostfix]
        static void EnablePostfix(ref CustomNetworkManager __instance) {
			int pre = __instance.maxConnections;
			__instance.maxConnections = MPPlugin.getMaxPlayers();
			MPPlugin.Log($"Setting maxConnections {pre} to {__instance.maxConnections}");
		}
	}
	
	[HarmonyPatch(typeof(CustomNetworkManager), "createLobbyBeforeConnection")]
	public static class NetworkManagerPlayerPatch {
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
			MPPlugin.Log("Patching Max Players");

			var insnList = new List<CodeInstruction>(instructions);
			for (int i = 0; i < insnList.Count; i++) {
				if (insnList[i].opcode == OpCodes.Ldc_I4_4) { // Hardcoded 4 should only appear for target (once)
					insnList.RemoveAt(i);

					insnList.Insert(i++,
						CodeInstruction.LoadField(typeof(CustomNetworkManager), "manage"));
					insnList.Insert(i,
						CodeInstruction.LoadField(typeof(CustomNetworkManager), "maxConnections"));
				}
			}

			return insnList.AsEnumerable();
		}
	}
	
	[HarmonyPatch(typeof(SteamLobby), "OnLobbyEntered")]
	public static class LobbyCountPatch {
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
			MPPlugin.Log("Patching Lobby Count");
			return new CodeMatcher(instructions)
				   .MatchForward(false,
						new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(SteamMatchmaking), "SetLobbyData")),
						new CodeMatch(OpCodes.Pop),
						new CodeMatch(OpCodes.Ret)
					)
					.Advance(2)
					.Insert(
						new CodeInstruction(OpCodes.Ldarg_0),
						CodeInstruction.LoadField(typeof(SteamLobby), "currentLobby"),
						new CodeInstruction(OpCodes.Ldstr, "noOfPlayers"),
						new CodeInstruction(OpCodes.Ldstr, "1"), // to avoid member call
						new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SteamMatchmaking), "SetLobbyData")),
						new CodeInstruction(OpCodes.Pop)
					)
					.InstructionEnumeration();
		}
	}
}