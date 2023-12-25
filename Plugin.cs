﻿using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LC_API.ClientAPI;
using LC_API.Comp;
using LC_API.GameInterfaceAPI.Events;
using LC_API.ManualPatches;
using LC_API.ServerAPI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace LC_API
{
    // .____    _________           _____  __________ .___  
    // |    |   \_   ___ \         /  _  \ \______   \|   | 
    // |    |   /    \  \/        /  /_\  \ |     ___/|   | 
    // |    |___\     \____      /    |    \|    |    |   | 
    // |_______ \\______  /______\____|__  /|____|    |___| 
    //         \/       \//_____/        \/                 
    /// <summary>
    /// The Lethal Company modding API plugin!
    /// </summary>
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public sealed class Plugin : BaseUnityPlugin
    {
        /// <summary>
        /// Runs after the LC API plugin's "Awake" method is finished.
        /// </summary>
        public static bool Initialized { get; private set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        internal static ManualLogSource Log;

        private ConfigEntry<bool> configOverrideModServer;
        private ConfigEntry<bool> configLegacyAssetLoading;
        private ConfigEntry<bool> configDisableBundleLoader;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        private void Awake()
        {
            configOverrideModServer = Config.Bind("General", "Force modded server browser", false, "Should the API force you into the modded server browser?");
            configLegacyAssetLoading = Config.Bind("General", "Legacy asset bundle loading", false, "Should the BundleLoader use legacy asset loading? Turning this on may help with loading assets from older plugins.");
            configDisableBundleLoader = Config.Bind("General", "Disable BundleLoader", false, "Should the BundleLoader be turned off? Enable this if you are having problems with mods that load assets using a different method from LC_API's BundleLoader.");
            CommandHandler.commandPrefix = Config.Bind("General", "Prefix", "/", "Command prefix");

            Log = Logger;
            // Plugin startup logic
            Logger.LogWarning("\n.____    _________           _____  __________ .___  \r\n|    |   \\_   ___ \\         /  _  \\ \\______   \\|   | \r\n|    |   /    \\  \\/        /  /_\\  \\ |     ___/|   | \r\n|    |___\\     \\____      /    |    \\|    |    |   | \r\n|_______ \\\\______  /______\\____|__  /|____|    |___| \r\n        \\/       \\//_____/        \\/                 \r\n                                                     ");
            Logger.LogInfo($"LC_API Starting up..");
            if (configOverrideModServer.Value)
            {
                ModdedServer.SetServerModdedOnly();
            }

            Harmony harmony = new Harmony("ModAPI");
            MethodInfo originalLobbyCreated = AccessTools.Method(typeof(GameNetworkManager), "SteamMatchmaking_OnLobbyCreated");
            MethodInfo originalLobbyJoinable = AccessTools.Method(typeof(GameNetworkManager), "LobbyDataIsJoinable");

            MethodInfo patchLobbyCreate = AccessTools.Method(typeof(ServerPatch), nameof(ServerPatch.OnLobbyCreate));

            MethodInfo originalMenuAwake = AccessTools.Method(typeof(MenuManager), "Awake");

            MethodInfo patchCacheMenuMgr = AccessTools.Method(typeof(ServerPatch), nameof(ServerPatch.CacheMenuManager));

            MethodInfo originalAddChatMsg = AccessTools.Method(typeof(HUDManager), "AddChatMessage");

            //MethodInfo patchChatInterpreter = AccessTools.Method(typeof(ServerPatch), nameof(ServerPatch.ChatInterpreter));

            MethodInfo originalSubmitChat = AccessTools.Method(typeof(HUDManager), "SubmitChat_performed");

            MethodInfo patchSubmitChat = AccessTools.Method(typeof(CommandHandler.SubmitChatPatch), nameof(CommandHandler.SubmitChatPatch.Transpiler));

            MethodInfo originalGameManagerAwake = AccessTools.Method(typeof(GameNetworkManager), nameof(GameNetworkManager.Awake));

            MethodInfo patchGameManagerAwake = AccessTools.Method(typeof(ServerPatch), nameof(ServerPatch.GameNetworkManagerAwake));

            MethodInfo originalStartClient = AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.StartClient));
            MethodInfo originalStartHost = AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.StartHost));

            MethodInfo registerPatch = AccessTools.Method(typeof(RegisterPatch), nameof(RegisterPatch.Postfix));

            harmony.Patch(originalMenuAwake, new HarmonyMethod(patchCacheMenuMgr));
            //harmony.Patch(originalAddChatMsg, new HarmonyMethod(patchChatInterpreter));
            harmony.Patch(originalLobbyCreated, new HarmonyMethod(patchLobbyCreate));
            harmony.Patch(originalSubmitChat, null, null, new HarmonyMethod(patchSubmitChat));
            harmony.Patch(originalGameManagerAwake, new HarmonyMethod(patchGameManagerAwake));

            harmony.Patch(originalStartClient, null, new HarmonyMethod(registerPatch));
            harmony.Patch(originalStartHost, null, new HarmonyMethod(registerPatch));

            //Networking.GetString += CheatDatabase.CDNetGetString;
            //Networking.GetListString += Networking.LCAPI_NET_SYNCVAR_SET;

            Networking.SetupNetworking();
            Events.Patch(harmony);

            Networking.RegisterNetworkMessages += () =>
            {
                Log.LogInfo("EVENT SENT!");

                Networking.RegisterMessage("LC_API_TEST", (Networking.NetworkMessage<TestClass> message) =>
                {
                    Log.LogInfo("RECEIVED MESSAGE");
                    Log.LogInfo(message.Message.value);
                    Log.LogInfo(message.Message.test);
                    
                    foreach(Vector3 vector3 in message.Message.vector3S)
                    {
                        Log.LogInfo(vector3.ToString());
                    }

                    Log.LogInfo("-------------");
                });

                NetworkManager.Singleton.StartCoroutine(TestSendMessage());
            };
        }

        private IEnumerator TestSendMessage()
        {
            Log.LogInfo("Testing");
            yield return new WaitForSeconds(10f);
            Log.LogInfo("Broadcasting");

            Networking.Broadcast("LC_API_TEST", new TestClass()
            {
                value = 69,
                test = "nice",
                vector3S = new List<Networking.Vector3S>()
                {
                    new Vector3(42, 42, 42),
                    new Vector3(3.14f, 3.14f, 3.14f)
                }
            });
        }

        [System.Serializable]
        internal class TestClass
        {
            public int value;
            public string test;
            public List<Networking.Vector3S> vector3S;
        }

        internal void Start()
        {
            Initialize();
        }

        internal void OnDestroy()
        {
            Initialize();
        }

        internal void Initialize()
        {
            if (!Initialized)
            {
                Initialized = true;
                if (!configDisableBundleLoader.Value)
                {
                    BundleAPI.BundleLoader.Load(configLegacyAssetLoading.Value);
                }
                GameObject gameObject = new GameObject("API");
                DontDestroyOnLoad(gameObject);
                gameObject.AddComponent<LC_APIManager>();
                Logger.LogInfo($"LC_API Started!");
                CheatDatabase.RunLocalCheatDetector();
            }
        }

        internal static void PatchMethodManual(MethodInfo method, MethodInfo patch, Harmony harmony)
        {
            harmony.Patch(method, new HarmonyMethod(patch));
        }
    }
}