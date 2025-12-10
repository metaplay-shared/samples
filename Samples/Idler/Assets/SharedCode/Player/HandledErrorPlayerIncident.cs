// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Debugging;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Example of a custom Player Incident type.
    /// </summary>
    [MetaSerializableDerived(101)]
    public class HandledErrorPlayerIncident : PlayerIncidentReport
    {
        [MetaMember(1)] public string ErrorType;
        [MetaMember(2)] public string ErrorMessage;
        [MetaMember(3)] public string StackTrace;
        [MetaMember(4)] public string DebugInfo;

        public HandledErrorPlayerIncident() { }

        public HandledErrorPlayerIncident(
            SharedIncidentInfo info,
            string errorType,
            string errorMessage,
            string stackTrace,
            string debugInfo)
            : base(info)
        {
            ErrorType = errorType;
            ErrorMessage = errorMessage;
            StackTrace = stackTrace;
            DebugInfo = debugInfo;
        }

        public override string Type => nameof(HandledErrorPlayerIncident);
        public override string SubType => ErrorType;
        public override string GetReason()
        {
            return ErrorMessage;
        }

        [MetaSerializableDerived(101)]
        public class ExtraDashboardInfo : IncidentDashboardInfo.ExtraIncidentDashboardInfo
        {
            public string DebugInfo;
        }

        public override IncidentDashboardInfo GetIncidentDashboardInfo()
        {
            IncidentDashboardInfo dashboardInfo = IncidentDashboardInfo.PreFillDashboardInfo(this);
            dashboardInfo.ErrorInfo = new IncidentDashboardInfo.ErrorDashboardInfo()
            {
                ErrorType = ErrorType,
                ErrorMessage = ErrorMessage,
                StackTrace = StackTrace,
            };
            dashboardInfo.ExtraInfo = new ExtraDashboardInfo()
            {
                DebugInfo = DebugInfo,
            };
            return dashboardInfo;
        }
    }
}
