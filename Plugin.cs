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
using TMPro;

namespace MorePlayers {
	[BepInAutoPlugin]
	public partial class MPPlugin : BaseUnityPlugin {
		private readonly Harmony harmony = new Harmony(Id);
		public static MPPlugin Instance;

		public static GameObject maxPlayerPrefab = null;
		public static TextMeshProUGUI numTMP = null;
		
		private ConfigEntry<int> maxPlayers;
		private ConfigEntry<int> sleepPercentage;

		private void Awake() {
			Instance = this;
            maxPlayers = Config.Bind("General",
                                     "maxPlayers",
                                     8,
                                     "Amount of players allowed to join a world");
			
            sleepPercentage = Config.Bind("General",
                                     "sleepPercentage",
                                     70,
                                     "Required percentage of players to sleep for next day");

			harmony.PatchAll();			
			Logger.LogInfo($"Plugin {Id} is loaded!");
		}

		private void Start() {
			// Select UI	
			GameObject optionWindow = OptionsMenu.options.optionWindow;
			GameObject xspd = optionWindow.transform.Find("Content/OptionMask/ButtonParent/Camera Controls/XSpeed").gameObject;
			
			maxPlayerPrefab = UnityEngine.Object.Instantiate<GameObject>(xspd);
			maxPlayerPrefab.name = "Max Players (Prefab)";
			
			TextMeshProUGUI title = maxPlayerPrefab.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
			title.text = "Max Players";

			DontDestroyOnLoad(maxPlayerPrefab);
		}

		public static void attachSelect() {
			if (maxPlayerPrefab != null && numTMP == null) {
				GameObject mapCanv = GameObject.Find("MapCanvas");
				GameObject saveSlot = mapCanv.transform.Find("MenuScreen/Multiplayer/Contents/Host Game /SaveSlot").gameObject;
				
				GameObject maxPlayerSelect = UnityEngine.Object.Instantiate<GameObject>(maxPlayerPrefab);
				maxPlayerSelect.name = "Max Players";
				maxPlayerSelect.transform.SetParent(saveSlot.transform);
				maxPlayerSelect.transform.localPosition = new Vector3(-7.5f, -137.5f, 0);
				maxPlayerSelect.transform.localScale = new Vector3(.8f, .8f, .8f);
				
				numTMP = maxPlayerSelect.transform.Find("VolumeSlider/Background/Sound Effect").gameObject.GetComponent<TextMeshProUGUI>();
				numTMP.text = CustomNetworkManager.manage.maxConnections.ToString();
				
				InvButton upBtn = maxPlayerSelect.transform.Find("VolumeSlider/UpButton").gameObject.GetComponent<InvButton>();
				InvButton downBtn = maxPlayerSelect.transform.Find("VolumeSlider/DownButton").gameObject.GetComponent<InvButton>();

				upBtn.onButtonPress.AddListener(() => changePlayers(1));
				downBtn.onButtonPress.AddListener(() => changePlayers(-1));
				// TODO: add fade
			}
		}

		public static int clampPlayers(int nv) {
			if (nv > 32) nv = 32;
			else if (nv < 2) nv = 2;
			
			return nv;
		}

		public static void changePlayers(int diff) {
			int nv = clampPlayers(getMaxPlayers() + diff);

			Instance.maxPlayers.Value = nv;
			CustomNetworkManager.manage.maxConnections = nv;
			if (numTMP != null)
				numTMP.text = nv.ToString();
		}
		
		public static int getMaxPlayers() {
			return Instance.maxPlayers.Value;
		}
		
		public static void Log(System.Object msg) {
			Instance.Logger.LogInfo(msg);
		}
		
		public static int getMinSleepAmount(int forPlayers) {
			int perc = Mathf.Clamp(Instance.sleepPercentage.Value, 1, 100);
			return (int)(forPlayers * perc / 100f);
		}
		
		public static int getMinSleepAmount() {
			return getMinSleepAmount(NetworkNavMesh.nav.getPlayerCount());
		}
	}
	
	
	// TODO: Patch NetworkPlayersManager::refreshButtons
	
	[HarmonyPatch(typeof(MultiplayerLoadWindow), "OnEnable")]
	public static class MPLoadPatch {
		[HarmonyPostfix]
        static void EnablePostfix(ref MultiplayerLoadWindow __instance) {
			MPPlugin.attachSelect();
		}
	}
	
	[HarmonyPatch(typeof(CustomNetworkManager), "OnEnable")]
	public static class NetworkManagerStartupPatch {
		[HarmonyPostfix]
        static void EnablePostfix(ref CustomNetworkManager __instance) {
			int pre = __instance.maxConnections;
			__instance.maxConnections = MPPlugin.clampPlayers(MPPlugin.getMaxPlayers());
			MPPlugin.Log($"Setting maxConnections {pre} to {__instance.maxConnections}");
		}
	}
	/*
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
	*/
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
						CodeInstruction.Call(typeof(SteamMatchmaking), "SetLobbyData"),
						new CodeInstruction(OpCodes.Pop)
					)
					.InstructionEnumeration();
		}
	}
	
	[HarmonyPatch(typeof(NetworkNavMesh), "checkSleepingList")]
	public static class NetworkNavMeshSleepingPatch {
		[HarmonyPrefix]
        static bool CheckSleepingListPostfix(ref NetworkNavMesh __instance) {
			int playerCount = __instance.getPlayerCount();
			if (playerCount != 0 && 
				__instance.sleepingChars.Count >= MPPlugin.getMinSleepAmount(playerCount) && 
				!RealWorldTimeLight.time.underGround && !RealWorldTimeLight.time.offIsland && 
				!TownManager.manage.checkIfInMovingBuildingForSleep()) 
			{
				WorldManager.Instance.nextDay();
				__instance.sleepingChars.Clear();
			}
			return false; // Don't exec original
		}
	}
}