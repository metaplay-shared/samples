namespace WebClient.Meta.Ui;

/// <summary>
/// A button's visual style. Each variant stands for a meaning, not a colour, so actions with the same meaning
/// use the same variant (docs/meta-shell.md).
/// <para>
/// <see cref="Hero"/> is used at most once per screen, for example for Play or the daily claim.
/// <see cref="Primary"/> is for ordinary confirm, buy and save actions, so that <see cref="Hero"/> stays rare.
/// </para>
/// </summary>
public enum MetaButtonVariant
{
    /// <summary>The single most important action on a screen. Primary gold with an added highlight animation.</summary>
    Hero,

    /// <summary>Flat gold, for the main action in a dialog or tile.</summary>
    Primary,

    /// <summary>Teal, for something to collect right now.</summary>
    Claim,

    /// <summary>Plum, for a time-limited activity that is running.</summary>
    Live,

    /// <summary>Neutral, for actions that need no other variant.</summary>
    Secondary,

    /// <summary>Borderless and low-emphasis, for a cancel or close action next to a more prominent one.</summary>
    Ghost,

    /// <summary>Rose, for a destructive action, so it is not mistaken for a claim.</summary>
    Danger,
}

/// <summary>
/// A button's size. Every size keeps the minimum touch target height.
/// </summary>
public enum MetaButtonSize
{
    /// <summary>For a button on a list row: smaller padding and text, same minimum height.</summary>
    Sm,

    /// <summary>The standard size.</summary>
    Md,

    /// <summary>For <see cref="MetaButtonVariant.Hero"/> and full-width buttons.</summary>
    Lg,
}

/// <summary>
/// An interaction state that the component gallery forces on a button, so that hover, press and focus styles can
/// be compared side by side and captured in screenshots. The app itself never sets one.
/// </summary>
public enum MetaButtonForceState
{
    /// <summary>No forced state. The button reacts to the pointer and keyboard normally.</summary>
    None,

    /// <summary>Always shown with the hover style.</summary>
    Hover,

    /// <summary>Always shown with the pressed style.</summary>
    Active,

    /// <summary>Always shown with the focus style.</summary>
    Focus,
}

/// <summary>
/// Builds the CSS class string for button-shaped components. The button and the status component share the same
/// styles, so both get their classes here instead of writing <c>m-btn</c> classes by hand.
/// </summary>
public static class MetaButtonClasses
{
    /// <summary>
    /// The class string: the <c>m-btn</c> base, then the variant, size and modifier classes. An icon-only button is
    /// square, so <paramref name="block"/> is ignored when <paramref name="iconOnly"/> is true.
    /// </summary>
    public static string For(
        MetaButtonVariant variant,
        MetaButtonSize size,
        bool block,
        bool iconOnly,
        MetaButtonForceState forceState = MetaButtonForceState.None)
    {
        System.Text.StringBuilder classes = new System.Text.StringBuilder("m-btn");

        switch (variant)
        {
            case MetaButtonVariant.Hero:
                classes.Append(" m-btn--primary m-btn--key");
                break;
            case MetaButtonVariant.Primary:
                classes.Append(" m-btn--primary");
                break;
            case MetaButtonVariant.Claim:
                classes.Append(" m-btn--claim");
                break;
            case MetaButtonVariant.Live:
                classes.Append(" m-btn--live");
                break;
            case MetaButtonVariant.Secondary:
                break;
            case MetaButtonVariant.Ghost:
                classes.Append(" m-btn--quiet");
                break;
            case MetaButtonVariant.Danger:
                classes.Append(" m-btn--danger");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), (int)variant, "Unknown button variant.");
        }

        if (size == MetaButtonSize.Sm)
            classes.Append(" m-btn--sm");
        else if (size == MetaButtonSize.Lg)
            classes.Append(" m-btn--lg");

        if (block && !iconOnly)
            classes.Append(" m-btn--block");

        if (iconOnly)
            classes.Append(" m-btn--icon");

        if (forceState == MetaButtonForceState.Hover)
            classes.Append(" m-btn--force-hover");
        else if (forceState == MetaButtonForceState.Active)
            classes.Append(" m-btn--force-active");
        else if (forceState == MetaButtonForceState.Focus)
            classes.Append(" m-btn--force-focus");

        return classes.ToString();
    }
}
