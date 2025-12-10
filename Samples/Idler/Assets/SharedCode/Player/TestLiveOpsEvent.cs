// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Model;

namespace Game.Logic
{
    [LiveOpsEvent(1, "Test LiveOps Event")]
    public class IdlerTestLiveOpsEvent : LiveOpsEventContent
    {
        [MetaMember(3)] public string Group;
        [MetaMember(1)] public string TestString;
        [MetaMember(2)] public int TestInt;

        public override bool ShouldWarnAboutOverlapWith(LiveOpsEventContent otherContent)
        {
            return Group != null
                && otherContent is IdlerTestLiveOpsEvent otherIdlerEvent
                && otherIdlerEvent.Group != null
                && Group == otherIdlerEvent.Group;
        }
    }

    [MetaSerializableDerived(1)]
    public class IdlerTestLiveOpsEventModel : PlayerLiveOpsEventModel<IdlerTestLiveOpsEvent>
    {
        [MetaMember(1)] public int TestStateInt;

        // Example of using Initialize() which sets initial state based on base member Content
        protected override void Initialize()
        {
            TestStateInt = Content.TestInt;
        }
    }

    [LiveOpsEvent(2, "Another LiveOps Event")]
    public class IdlerAnotherTestLiveOpsEvent : LiveOpsEventContent
    {
        [MetaMember(1)] public string TestString;
        [MetaMember(2)] public int TestInt;

        // Example of using custom CreateModel() which ends up using constructor PlayerLiveOpsEventModel(PlayerLiveOpsEventInfo info)
        public override PlayerLiveOpsEventModel CreateModel(PlayerLiveOpsEventInfo info)
        {
            return new IdlerAnotherTestLiveOpsEventModel(info);
        }
    }

    [MetaSerializableDerived(2)]
    public class IdlerAnotherTestLiveOpsEventModel : PlayerLiveOpsEventModel
    {
        [MetaMember(1)] public int TestStateInt;

        IdlerAnotherTestLiveOpsEventModel() { }
        public IdlerAnotherTestLiveOpsEventModel(PlayerLiveOpsEventInfo info)
            : base(info)
        {
            TestStateInt = ((IdlerAnotherTestLiveOpsEvent)info.Content).TestInt;
        }
    }
}
