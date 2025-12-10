// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections;
using System.Collections.Generic;
using Game.Logic;
using Metaplay.Unity;

public class CustomIncidentReporter
{
    public static void ReportHandledErrorPlayerIncident(Exception ex, string debugInfo)
    {
        MetaplaySDK.IncidentTracker.AddIncidentAndAnalyticsEvent(
            new HandledErrorPlayerIncident(
                MetaplaySDK.IncidentTracker.GetSharedIncidentInfo(),
                ex.GetType().ToString(),
                ex.Message,
                ex.StackTrace,
                debugInfo
                ));

        // // Alternative way to report incidents if you want to include a network report
        // MetaplaySDK.IncidentTracker.AddIncidentWithNetworkReportAndAnalyticsEvent(
        //     new HandledErrorPlayerIncident(
        //         MetaplaySDK.IncidentTracker.GetSharedIncidentInfo(),
        //         ex.GetType().ToString(),
        //         ex.Message,
        //         ex.StackTrace
        //         ));
    }
}