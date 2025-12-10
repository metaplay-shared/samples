// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EventListItemScript : MonoBehaviour
{
    public EventKey EventKey;

    public TextMeshProUGUI TitleText;
    public TextMeshProUGUI DescriptionText;
    public TextMeshProUGUI PhaseText;
    public TextMeshProUGUI NextPhaseText;
    public Image PhaseImage;
    public Button ClaimButton;
    public Color activeColor;
    public Color inactiveColor;
    public Color endingColor;
    public Color errorColor;

    public delegate void OnClickClaimHandler(EventKey eventKey);
    public event OnClickClaimHandler OnClickClaimEvent;

    void Start()
    {
    }

    void Update()
    {
    }

    public void OnClickClaim()
    {
        OnClickClaimEvent?.Invoke(EventKey);
    }
}

public struct EventKey : IEquatable<EventKey>
{
    public MetaActivableKey? Activable;
    public MetaGuid? LiveOpsEvent;

    public static EventKey ForActivable(MetaActivableKey activable)
        => new EventKey { Activable = activable };

    public static EventKey ForLiveOpsEvent(MetaGuid liveOpsEvent)
        => new EventKey { LiveOpsEvent = liveOpsEvent };

    public override bool Equals(object obj)
    {
        return obj is EventKey key && Equals(key);
    }

    public bool Equals(EventKey other)
    {
        return EqualityComparer<MetaActivableKey?>.Default.Equals(Activable, other.Activable) &&
               EqualityComparer<MetaGuid?>.Default.Equals(LiveOpsEvent, other.LiveOpsEvent);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Activable, LiveOpsEvent);
    }

    public static bool operator ==(EventKey left, EventKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EventKey left, EventKey right)
    {
        return !(left == right);
    }
}
