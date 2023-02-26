﻿using Comfort.Common;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIT.Coop.Core.Player;
using SIT.Tarkov.Core;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SIT.Core.Coop
{
    public class CoopGameComponent : MonoBehaviour
    {
        #region Fields/Properties        
        public WorldInteractiveObject[] ListOfInteractiveObjects { get; set; }
        private Request RequestingObj { get; set; }
        public string ServerId { get; set; } = null;
        public ConcurrentDictionary<string, LocalPlayer> Players { get; private set; } = new();
        public ConcurrentQueue<Dictionary<string, object>> QueuedPackets { get; } = new();
        public static List<string> PeopleParityRequestedAccounts = new();
        BepInEx.Logging.ManualLogSource Logger;
        public static Vector3? ClientSpawnLocation;
        private long ReadFromServerLastActionsLastTime = -1;
        #endregion

        #region Public Voids
        public static CoopGameComponent GetCoopGameComponent()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
                return null;

            var coopGC = gameWorld.GetComponent<CoopGameComponent>();
            return coopGC;
        }
        public static string GetServerId()
        {
            var coopGC = GetCoopGameComponent();
            if (coopGC == null)
                return null;

            return coopGC.ServerId;
        }
        #endregion

        #region Unity Component Methods

        void Awake()
        {

            // ----------------------------------------------------
            // Create a BepInEx Logger for CoopGameComponent
            Logger = BepInEx.Logging.Logger.CreateLogSource("CoopGameComponent");

        }

        void Start()
        {
            Logger.LogInfo("CoopGameComponent:Start");

            // ----------------------------------------------------
            // Always clear "Players" when creating a new CoopGameComponent
            Players = new ConcurrentDictionary<string, LocalPlayer>();


            // ----------------------------------------------------
            // Consume Data Received from ServerCommunication class
            //ServerCommunication.OnDataReceived += ServerCommunication_OnDataReceived;
            //ServerCommunication.OnDataStringReceived += ServerCommunication_OnDataStringReceived;
            //ServerCommunication.OnDataArrayReceived += ServerCommunication_OnDataArrayReceived;
            // ----------------------------------------------------

            StartCoroutine(ReadFromServerLastActions());
            StartCoroutine(ReadFromServerLastMoves());
            StartCoroutine(ReadFromServerCharacters());

            //StartCoroutine(RunQueuedActions());
            //StartCoroutine(UpdateClientSpawnPlayers());

            ListOfInteractiveObjects = FindObjectsOfType<WorldInteractiveObject>();
            PatchConstants.Logger.LogInfo($"Found {ListOfInteractiveObjects.Length} interactive objects");

            //StartCoroutine(DiscoverMissingPlayers());
        }

        private IEnumerator ReadFromServerCharacters()
        {
            var waitEndOfFrame = new WaitForEndOfFrame();

            if (GetServerId() == null)
                yield return waitEndOfFrame;

            var waitSeconds = new WaitForSeconds(3f);

            Dictionary<string, object> d = new Dictionary<string, object>();
            d.Add("serverId", GetServerId());
            var jsonDataServerId = JsonConvert.SerializeObject(d);
            while (true)
            {
                yield return waitSeconds;

                if (Players == null)
                    continue;


                if (RequestingObj == null)
                    RequestingObj = new Request();

                var actionsToValuesJson = RequestingObj.PostJson("/coop/server/read/players", jsonDataServerId);
                if (actionsToValuesJson == null)
                    continue;

                //Logger.LogInfo("CoopGameComponent.ReadFromServerCharacters:");
                //Logger.LogInfo(actionsToValuesJson);
                try
                {
                    Dictionary<string, object>[] actionsToValues = JsonConvert.DeserializeObject<Dictionary<string, object>[]>(actionsToValuesJson);
                    if (actionsToValues == null)
                        continue;

                    var dictionaries = actionsToValues
                         .Where(x => x != null)
                         .Select(x => x);
                    if (dictionaries == null)
                        continue;

                    foreach (var dictionary in dictionaries)
                    {
                        if (dictionary != null && dictionary.Count > 0)
                        {
                            if (dictionary.ContainsKey("accountId"))
                            {
                                QueuedPackets.Enqueue(dictionary);
                            }
                        }
                    }
                }
                finally
                {

                }

                yield return waitEndOfFrame;

            }
        }


        
        /// <summary>
        /// Gets the Last Actions Dictionary from the Server. This should not be used for things like Moves. Just other stuff.
        /// </summary>
        /// <returns></returns>
        private IEnumerator ReadFromServerLastActions()
        {
            Stopwatch swDebugPerformance = new Stopwatch();
            Stopwatch swRequests = new Stopwatch();

            var waitEndOfFrame = new WaitForEndOfFrame();

            if (GetServerId() == null)
                yield return waitEndOfFrame;

            var waitSeconds = new WaitForSeconds(0.5f);

            var jsonDataServerId = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                { "serverId", GetServerId() },
                { "t", ReadFromServerLastActionsLastTime }
            });
            while (true)
            {
                yield return waitSeconds;

                swDebugPerformance.Reset();
                swDebugPerformance.Start();
                if (Players == null)
                {
                    PatchConstants.Logger.LogInfo("CoopGameComponent:No Players Found! Nothing to process!");
                    yield return waitSeconds;
                    continue;
                }

                if (RequestingObj == null)
                    RequestingObj = new Request();

                try
                {
                    //Task.Run(async() =>
                    {
                        swRequests.Reset();
                        swRequests.Start();
                        var actionsToValuesJson = RequestingObj.PostJsonAsync("/coop/server/read/lastActions", jsonDataServerId).Result;
                        ReadFromServerLastActionsParseData(actionsToValuesJson);
                    }
                    //);
                }
                finally
                {

                }

                swRequests.Stop();
                //Logger.LogInfo($"CoopGameComponent.ReadFromServerLastActions took {swRequests.ElapsedMilliseconds}ms");
                //yield return waitSeconds;
                yield return waitEndOfFrame;

            }
        }

        public void ReadFromServerLastActionsParseData(string actionsToValuesJson)
        {
            if (actionsToValuesJson == null)
            {
                PatchConstants.Logger.LogInfo("CoopGameComponent:No Data Returned from Last Actions!");
                return;
            }

            //Logger.LogInfo($"CoopGameComponent:ReadFromServerLastActions:{actionsToValuesJson}");
            Dictionary<string, JObject> actionsToValues = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(actionsToValuesJson);
            if (actionsToValues == null)
            {
                return;

            }

            var packets = actionsToValues.Values
                 .Where(x => x != null)
                 .Where(x => x.Count > 0)
                 .Select(x => x.ToObject<Dictionary<string, object>>());
            //.Where(x => x.ContainsKey("m") && x["m"].ToString() != "Move")
            //.Where(x => x.ContainsKey("accountId"));
            if (packets == null)
            {
                //PatchConstants.Logger.LogInfo("CoopGameComponent:No Data Returned from Last Actions!");
                return;

            }

            if (!packets.Any())
            {
                //PatchConstants.Logger.LogInfo("CoopGameComponent:No Data Returned from Last Actions!");
                return;

            }

            //// go through all items apart from "Move"
            foreach (var packet in packets.Where(x => x.ContainsKey("m") && x["m"].ToString() != "Move"))
            {
                if (packet != null && packet.Count > 0)
                {
                    var accountId = packet["accountId"].ToString();
                    //if (dictionary.ContainsKey("accountId"))
                    {
                        if (!Players.ContainsKey(accountId))
                        {
                            PatchConstants.Logger.LogInfo($"CoopGameComponent:Players does not contain {accountId}. Searching. This is SLOW. FIXME! Don't do this!");
                            foreach (var p in FindObjectsOfType<LocalPlayer>())
                            {
                                if (!Players.ContainsKey(p.Profile.AccountId))
                                {
                                    Players.TryAdd(p.Profile.AccountId, p);
                                    var nPRC = p.GetOrAddComponent<PlayerReplicatedComponent>();
                                    nPRC.player = p;
                                }
                            }
                            continue;
                        }

                        if (!Players[packet["accountId"].ToString()].TryGetComponent<PlayerReplicatedComponent>(out var prc))
                        {
                            PatchConstants.Logger.LogInfo($"CoopGameComponent:{accountId} does not have a PlayerReplicatedComponent");
                            continue;
                        }

                        if (prc == null)
                            continue;

                        //if (prc.QueuedPackets == null)
                        //    continue;

                        //prc.QueuedPackets.Enqueue(dictionary);

                        prc.HandlePacket(packet);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the Last Moves Dictionary from the Server. This should be the last move action each account id (character) made
        /// </summary>
        /// <returns></returns>
        private IEnumerator ReadFromServerLastMoves()
        {
            var waitEndOfFrame = new WaitForEndOfFrame();
            var waitSeconds = new WaitForSeconds(1f);

            if (GetServerId() == null)
                yield return waitSeconds;

            var jsonDataServerId = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                { "serverId", GetServerId() }
            });
            while (true)
            {
                yield return waitSeconds;

                if (Players == null)
                    continue;

                if (RequestingObj == null)
                    RequestingObj = new Request();

                var actionsToValuesJson = RequestingObj.PostJsonAsync("/coop/server/read/lastMoves", jsonDataServerId).Result;
                ReadFromServerLastMoves_ParseData(actionsToValuesJson);

                yield return waitEndOfFrame;

            }
        }

        public void ReadFromServerLastMoves_ParseData(string actionsToValuesJson)
        {
            if (actionsToValuesJson == null)
                return;

            try
            {
                //Logger.LogInfo($"CoopGameComponent:ReadFromServerLastMoves:{actionsToValuesJson}");
                Dictionary<string, JObject> actionsToValues = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(actionsToValuesJson);
                if (actionsToValues == null)
                    return;

                var dictionaries = actionsToValues.Values
                    .Where(x => x != null)
                    .Where(x => x.Count > 0)
                    .Select(x => x.ToObject<Dictionary<string, object>>());
                if (dictionaries == null)
                    return;

                foreach (var dictionary in dictionaries)
                {
                    if (dictionary != null && dictionary.Count > 0)
                    {
                        if (dictionary.ContainsKey("accountId"))
                        {
                            if (Players == null)
                                continue;

                            if (Players.ContainsKey(dictionary["accountId"].ToString()))
                            {
                                if (!Players[dictionary["accountId"].ToString()].TryGetComponent<PlayerReplicatedComponent>(out var prc))
                                    continue;

                                if (prc == null)
                                    continue;

                                if (prc.QueuedPackets == null)
                                    continue;

                                if (prc.QueuedPackets.Any(x => x["m"].ToString() == "Move"))
                                    continue;

                                prc.QueuedPackets.Enqueue(dictionary);
                            }
                        }
                    }
                }
            }
            finally
            {

            }
        }
        #endregion

        private void ServerCommunication_OnDataReceived(byte[] buffer)
        {
            if (buffer.Length == 0)
                return;

            try
            {
                //string @string = streamReader.ReadToEnd();
                string @string = Encoding.UTF8.GetString(buffer);

                if (@string.Length == 4)
                {
                    return;
                }
                else
                {
                    //Task.Run(() =>
                    {
                        if (@string.Length == 0)
                            return;

                        //Logger.LogInfo($"CoopGameComponent:OnDataReceived:{buffer.Length}");
                        //Logger.LogInfo($"CoopGameComponent:OnDataReceived:{@string}");

                        if (@string[0] == '{' && @string[@string.Length - 1] == '}')
                        {
                            //var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(@string)
                            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(@string);

                            if (dictionary != null && dictionary.Count > 0)
                            {
                                if (dictionary.ContainsKey("SERVER"))
                                {
                                    //Logger.LogInfo($"LocalGameStartingPatch:OnDataReceived:SERVER:{buffer.Length}");
                                    QueuedPackets.Enqueue(dictionary);
                                }
                                else if (dictionary.ContainsKey("m"))
                                {
                                    if (dictionary["m"].ToString() == "HostDied")
                                    {
                                        Logger.LogInfo("Host Died");
                                        //if (MatchmakerAcceptPatches.IsClient)
                                        //    LocalGameEndingPatch.EndSession(LocalGamePatches.LocalGameInstance, LocalGamePatches.MyPlayerProfile.Id, ExitStatus.Survived, "", 0);
                                    }

                                    if (dictionary.ContainsKey("accountId"))
                                    {
                                        //Logger.LogInfo(dictionary["accountId"]);

                                        if (Players.ContainsKey(dictionary["accountId"].ToString()))
                                        {
                                            var prc = Players[dictionary["accountId"].ToString()].GetComponent<PlayerReplicatedComponent>();
                                            if (prc == null)
                                                return;




                                            prc.QueuedPackets.Enqueue(dictionary);
                                        }
                                    }
                                }
                                else
                                {
                                    //Logger.LogInfo($"ServerCommunication_OnDataReceived:Unhandled:{@string}");
                                }
                            }
                        }
                        else if (@string[0] == '[' && @string[@string.Length - 1] == ']')
                        {
                            foreach (var item in JsonConvert.DeserializeObject<object[]>(@string).Select(x => JsonConvert.SerializeObject(x)))
                                ServerCommunication_OnDataReceived(Encoding.UTF8.GetBytes(item));
                        }
                        else
                        {
                            Logger.LogInfo($"ServerCommunication_OnDataReceived:Unhandled:{@string}");
                        }
                    }
                    //);
                }
            }
            catch (Exception)
            {
                return;
            }
        }

        // no longer used
        IEnumerator RunQueuedActions()
        {
            while (true)
            {
                yield return new WaitForSeconds(1);

                if (QueuedPackets.Any())
                {
                    if (QueuedPackets.TryDequeue(out var queuedPacket))
                    {
                        if (queuedPacket != null)
                        {
                            if (queuedPacket.ContainsKey("m"))
                            {
                                var method = queuedPacket["m"];
                                //PatchConstants.Logger.LogInfo("CoopGameComponent.RunQueuedActions:method:" + method);
                                switch (method)
                                {
                                    case "PlayerSpawn":
                                        string accountId = queuedPacket["accountId"].ToString();
                                        if (Players != null && !Players.ContainsKey(accountId))
                                        {

                                            Vector3 newPosition = Players.First().Value.Position;
                                            if (queuedPacket.ContainsKey("sPx")
                                                && queuedPacket.ContainsKey("sPy")
                                                && queuedPacket.ContainsKey("sPz"))
                                            {
                                                string npxString = queuedPacket["sPx"].ToString();
                                                newPosition.x = float.Parse(npxString);
                                                string npyString = queuedPacket["sPy"].ToString();
                                                newPosition.y = float.Parse(npyString);
                                                string npzString = queuedPacket["sPz"].ToString();
                                                newPosition.z = float.Parse(npzString) + 0.5f;

                                                //QuickLog("New Position found for Spawning Player");
                                            }
                                            //DataReceivedClient_PlayerBotSpawn(queuedPacket, accountId, queuedPacket["profileId"].ToString(), newPosition, false);
                                        }
                                        else
                                        {
                                            Logger.LogDebug($"Ignoring call to Spawn player {accountId}. The player already exists in the game.");
                                        }
                                        break;
                                    case "HostDied":
                                        {
                                            Logger.LogInfo("Host Died");
                                            //if (MatchmakerAcceptPatches.IsClient)
                                            //    LocalGameEndingPatch.EndSession(LocalGamePatches.LocalGameInstance, LocalGamePatches.MyPlayerProfile.Id, ExitStatus.Survived, "", 0);
                                        }
                                        break;
                                }
                            }
                        }
                    }
                }
            }
        }

    }


}
