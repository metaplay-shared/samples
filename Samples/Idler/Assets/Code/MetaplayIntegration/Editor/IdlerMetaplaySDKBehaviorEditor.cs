// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Game.Logic.League;
using Game.Logic.Matchmaking;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.League;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Logic.TypeCodes;
using Metaplay.Core.Client;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MetaplaySDKBehavior))]
class IdlerMetaplaySDKBehaviorEditor : MetaplaySDKBehaviorEditor
{
    protected override void GuildUIActions(GuildClient guildClient, IGuildModelBase model)
    {
        GuildModel guild = (GuildModel)model;

        if (GUILayout.Button("Poke random member"))
            guildClient.GuildContext.EnqueueAction(new GuildPokeMember(targetPlayerId: RandomPCG.CreateNew().Choice(guild.Members.Keys)));
        if (GUILayout.Button("Sell poke"))
            guildClient.ExecuteGuildTransaction(new GuildSellPokes.Transaction(numPokesAttemptingToSell: 1));
        if (GUILayout.Button("Buy vanity"))
            guildClient.ExecuteGuildTransaction(new GuildBuyVanity.Transaction(numVanityAttemptingToBuy: 1));
        if (GUILayout.Button("Claim vanity reward: "))
            guildClient.ExecuteGuildTransaction(new GuildClaimVanityRankReward.Transaction());
    }

    /// <inheritdoc />
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        DrawLeaguesGUI();
        DrawMatchmakerGUI();
    }

    void DrawMatchmakerGUI()
    {
        if (MetaplaySDK.SessionContext?.PlayerContext?.Journal?.StagedModel == null)
            return;

        MatchmakingClient matchmakingClient = MatchmakingClient.EditorHookCurrent;

        if (matchmakingClient == null)
            return;


        EditorGUILayout.Space(30);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Async Matchmaker", EditorStyles.boldLabel);

        if (GUILayout.Button("Matchmake now!"))
            matchmakingClient.SendMatchmakingRequest();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("Latest response", EditorStyles.boldLabel);

        string  text      = PrettyPrint.Verbose(matchmakingClient.LatestResponse).ToString();
        Vector2 labelSize = GUI.skin.label.CalcSize(new GUIContent(text));
        EditorGUILayout.SelectableLabel(text, GUILayout.ExpandHeight(true), GUILayout.MinHeight(labelSize.y));
    }

    void DrawLeaguesGUI()
    {
        if (MetaplaySDK.SessionContext?.PlayerContext?.Journal?.StagedModel == null)
            return;

        IdlerDivisionClientState idlerDivisionClientState = MetaplayClient.PlayerModel.IdlerDivisionClientState;
        IdlerPvPDivisionClientState pvpDivisionClientState = MetaplayClient.PlayerModel.PvPDivisionClientState;


        IEnumerable<(IDivisionClientState, ClientSlot)> leagueClientStates = new (IDivisionClientState, ClientSlot)[]
        {
            (idlerDivisionClientState, ClientSlotGame.IdlerLeague),
            (pvpDivisionClientState, ClientSlotGame.IdlerPvPLeague),
        };

        if (idlerDivisionClientState != null)
        {
            EditorGUILayout.Space(30);
            EditorGUILayout.LabelField("Idler Leagues", EditorStyles.boldLabel);

            if (idlerDivisionClientState.CurrentDivision.IsValid)
            {
                EditorGUILayout.LabelField($"Current division: {idlerDivisionClientState.CurrentDivisionIndex}");
            }
            else
            {
                EditorGUILayout.LabelField("Not participating.");
                if (MetaplayClient.IdlerLeagueClient.LeagueJoinRequestInProgress)
                {
                    GUI.enabled = false;
                    GUILayout.Button("Join");
                    GUI.enabled = true;
                }
                else if (GUILayout.Button("Join"))
                    MetaplayClient.IdlerLeagueClient.TryJoinLeagues();

                if (!String.IsNullOrEmpty(MetaplayClient.IdlerLeagueClient.LeagueJoinRequestStatus))
                    EditorGUILayout.LabelField(MetaplayClient.IdlerLeagueClient.LeagueJoinRequestStatus,
                        EditorStyles.miniLabel);
            }
        }

        if (pvpDivisionClientState != null)
        {
            EditorGUILayout.Space(30);
            EditorGUILayout.LabelField("PvP Leagues", EditorStyles.boldLabel);
            
            if (pvpDivisionClientState.CurrentDivision.IsValid)
            {
                EditorGUILayout.LabelField($"Current division: {pvpDivisionClientState.CurrentDivisionIndex}");
            }
            else
            {
                EditorGUILayout.LabelField("Not participating.");
            }
        }

        foreach ((IDivisionClientState clientState, ClientSlot slot) in leagueClientStates)
        {
            if (clientState == null)
                continue;

            EditorGUILayout.Space(30);
            
            IDivisionHistoryEntry divisionToClaim =
                clientState.HistoricalDivisions.FirstOrDefault(x => x.Rewards != null && !x.Rewards.IsClaimed);

            if (divisionToClaim != null)
            {
                EditorGUILayout.LabelField($"New league rewards available for {slot}!");

                if (GUILayout.Button("Claim rewards!"))
                    MetaplayClient.PlayerContext.ExecuteAction(
                        new PlayerClaimHistoricalPlayerDivisionRewards(slot, divisionToClaim.DivisionId));
            }
            else
                EditorGUILayout.LabelField($"No league rewards to claim in {slot}.");
        }
    }
}
