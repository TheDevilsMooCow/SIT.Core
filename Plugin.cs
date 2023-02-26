﻿using Aki.Custom.Patches;
using BepInEx;
using Comfort.Common;
using EFT;
using SIT.Core.AkiSupport.Airdrops;
using SIT.Core.AkiSupport.Custom;
using SIT.Core.AkiSupport.Singleplayer;
using SIT.Core.AkiSupport.SITFixes;
using SIT.Core.Coop;
using SIT.Core.Menus;
using SIT.Core.Misc;
using SIT.Core.SP.ScavMode;
using SIT.Tarkov.Core;
using SIT.Tarkov.Core.Menus;
using SIT.Tarkov.Core.PlayerPatches;
using SIT.Tarkov.Core.PlayerPatches.Health;
using SIT.Tarkov.Core.SP;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace SIT.Core
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    //[BepInDependency()] // Should probably be dependant on Aki right?
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;

        private void Awake()
        {
            EnableCorePatches();
            EnableSPPatches();
            EnableCoopPatches();

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            SceneManager.sceneLoaded += SceneManager_sceneLoaded;
            Instance = this;
        }

        private void EnableCorePatches()
        {
            var enabled = Config.Bind<bool>("SIT Core Patches", "Enable", true);
            if (!enabled.Value) // if it is disabled. stop all SIT Core Patches.
            {
                Logger.LogInfo("SIT Core Patches has been disabled! Ignoring Patches.");
                return;
            }

            new ConsistencySinglePatch().Enable();
            new ConsistencyMultiPatch().Enable();
            new BattlEyePatch().Enable();
            new SslCertificatePatch().Enable();
            new UnityWebRequestPatch().Enable();
            new TransportPrefixPatch().Enable();
            new WebSocketPatch().Enable();
        }

        private void EnableSPPatches()
        {
            var enabled = Config.Bind<bool>("SIT SP Patches", "Enable", true);
            if (!enabled.Value) // if it is disabled. stop all SIT SP Patches.
            {
                Logger.LogInfo("SIT SP Patches has been disabled! Ignoring Patches.");
                return;
            }

            //// --------- PMC Dogtags -------------------
            new UpdateDogtagPatch().Enable();

            //// --------- On Dead -----------------------
            new OnDeadPatch(Config).Enable();

            //// --------- Player Init & Health -------------------
            new PlayerInitPatch().Enable();
            new ChangeHealthPatch().Enable();
            new ChangeHydrationPatch().Enable();
            new ChangeEnergyPatch().Enable();

            //// --------- SCAV MODE ---------------------
            new DisableScavModePatch().Enable();

            //// --------- Airdrop -----------------------
            new AirdropPatch().Enable();

            //// --------- Screens ----------------
            new TarkovApplicationInternalStartGamePatch().Enable();
            new OfflineRaidMenuPatch().Enable();
            new AutoSetOfflineMatch2().Enable();
            new InsuranceScreenPatch().Enable();
            new VersionLabelPatch().Enable();

            //// --------- Progression -----------------------
            new OfflineSaveProfile().Enable();
            new ExperienceGainFix().Enable();

            //// --------------------------------------
            // Bots
            EnableSPPatches_Bots();

            new QTEPatch().Enable();
            new TinnitusFixPatch().Enable();

            try
            {
                BundleManager.GetBundles();
                //new EasyAssetsPatch().Enable();
                //new EasyBundlePatch().Enable();
            }
            catch (Exception ex)
            {
                Logger.LogError("// --- ERROR -----------------------------------------------");
                Logger.LogError("Bundle System Failed!!");
                Logger.LogError(ex.ToString());
                Logger.LogError("// --- ERROR -----------------------------------------------");

            }

        }

        private static void EnableSPPatches_Bots()
        {
            new BotDifficultyPatch().Enable();
            new GetNewBotTemplatesPatch().Enable();
            new BotSettingsRepoClassIsFollowerFixPatch().Enable();
            new BotEnemyTargetPatch().Enable();
            new BotSelfEnemyPatch().Enable();
            new AkiSupport.Singleplayer.RemoveUsedBotProfilePatch().Enable();
        }

        private void EnableCoopPatches()
        {
            Logger.LogInfo("Enabling Coop Patches");
            CoopPatches.Run(Config);
        }

        //private void SceneManager_sceneUnloaded(Scene arg0)
        //{

        //}

        public static GameWorld gameWorld { get; private set; }


        private void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            GetPoolManager();
            GetBackendConfigurationInstance();

            gameWorld = Singleton<GameWorld>.Instance;

            //EnableCoopPatches();

        }

        private void GetBackendConfigurationInstance()
        {
            if (
                PatchConstants.BackendStaticConfigurationType != null &&
                PatchConstants.BackendStaticConfigurationConfigInstance == null)
            {
                PatchConstants.BackendStaticConfigurationConfigInstance = PatchConstants.GetPropertyFromType(PatchConstants.BackendStaticConfigurationType, "Config").GetValue(null);
                //Logger.LogInfo($"BackendStaticConfigurationConfigInstance Type:{ PatchConstants.BackendStaticConfigurationConfigInstance.GetType().Name }");
            }

            if (PatchConstants.BackendStaticConfigurationConfigInstance != null
                && PatchConstants.CharacterControllerSettings.CharacterControllerInstance == null
                )
            {
                PatchConstants.CharacterControllerSettings.CharacterControllerInstance
                    = PatchConstants.GetFieldOrPropertyFromInstance<object>(PatchConstants.BackendStaticConfigurationConfigInstance, "CharacterController", false);
                Logger.LogInfo($"PatchConstants.CharacterControllerInstance Type:{PatchConstants.CharacterControllerSettings.CharacterControllerInstance.GetType().Name}");
            }

            if (PatchConstants.CharacterControllerSettings.CharacterControllerInstance != null
                && PatchConstants.CharacterControllerSettings.ClientPlayerMode == null
                )
            {
                PatchConstants.CharacterControllerSettings.ClientPlayerMode
                    = PatchConstants.GetFieldOrPropertyFromInstance<CharacterControllerSpawner.Mode>(PatchConstants.CharacterControllerSettings.CharacterControllerInstance, "ClientPlayerMode", false);

                PatchConstants.CharacterControllerSettings.ObservedPlayerMode
                    = PatchConstants.GetFieldOrPropertyFromInstance<CharacterControllerSpawner.Mode>(PatchConstants.CharacterControllerSettings.CharacterControllerInstance, "ObservedPlayerMode", false);

                PatchConstants.CharacterControllerSettings.BotPlayerMode
                    = PatchConstants.GetFieldOrPropertyFromInstance<CharacterControllerSpawner.Mode>(PatchConstants.CharacterControllerSettings.CharacterControllerInstance, "BotPlayerMode", false);
            }

        }

        private void GetPoolManager()
        {
            if (PatchConstants.PoolManagerType == null)
            {
                PatchConstants.PoolManagerType = PatchConstants.EftTypes.Single(x => PatchConstants.GetAllMethodsForType(x).Any(x => x.Name == "LoadBundlesAndCreatePools"));
                Type generic = typeof(Singleton<>);
                Type[] typeArgs = { PatchConstants.PoolManagerType };
                ConstructedBundleAndPoolManagerSingletonType = generic.MakeGenericType(typeArgs);
            }
        }

        private Type ConstructedBundleAndPoolManagerSingletonType { get; set; }
        public static object BundleAndPoolManager { get; set; }

        public static Type poolsCategoryType { get; set; }
        public static Type assemblyTypeType { get; set; }

        public static MethodInfo LoadBundlesAndCreatePoolsMethod { get; set; }

        public static Task LoadBundlesAndCreatePools(ResourceKey[] resources)
        {
            try
            {
                if (BundleAndPoolManager == null)
                {
                    PatchConstants.Logger.LogInfo("LoadBundlesAndCreatePools: BundleAndPoolManager is missing");
                    return null;
                }

                var raidE = Enum.Parse(poolsCategoryType, "Raid");
                //PatchConstants.Logger.LogInfo("LoadBundlesAndCreatePools: raidE is " + raidE.ToString());

                var localE = Enum.Parse(assemblyTypeType, "Local");
                //PatchConstants.Logger.LogInfo("LoadBundlesAndCreatePools: localE is " + localE.ToString());

                var GenProp = PatchConstants.GetPropertyFromType(PatchConstants.JobPriorityType, "General").GetValue(null, null);
                //PatchConstants.Logger.LogInfo("LoadBundlesAndCreatePools: GenProp is " + GenProp.ToString());


                return PatchConstants.InvokeAsyncStaticByReflection(
                    LoadBundlesAndCreatePoolsMethod,
                    BundleAndPoolManager
                    , raidE
                    , localE
                    , resources
                    , GenProp
                    , (object o) => { PatchConstants.Logger.LogInfo("LoadBundlesAndCreatePools: Progressing!"); }
                    , default(CancellationToken)
                    );
            }
            catch (Exception ex)
            {
                PatchConstants.Logger.LogInfo("LoadBundlesAndCreatePools -- ERROR ->>>");
                PatchConstants.Logger.LogInfo(ex.ToString());
            }
            return null;
        }

    }
}
