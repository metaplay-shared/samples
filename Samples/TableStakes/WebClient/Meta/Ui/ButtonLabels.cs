using Game.Logic;

namespace WebClient.Meta.Ui;

/// <summary>
/// Button labels for the meta shell. A label is a verb of one or two words. Amounts, costs and conditions are
/// shown next to the button, not in the label, so the label fits on a narrow phone screen (docs/meta-shell.md).
/// <para>
/// The strings are in sentence case. The CSS for <see cref="MetaButtonVariant.Hero"/>,
/// <see cref="MetaButtonVariant.Primary"/>, <see cref="MetaButtonVariant.Claim"/>,
/// <see cref="MetaButtonVariant.Live"/> and <see cref="MetaButtonVariant.Danger"/> displays them in capitals.
/// </para>
/// </summary>
public static class ButtonLabels
{
    public const string Claim    = "Claim";
    public const string Play     = "Play";
    public const string Spin     = "Spin";
    public const string Join     = "Join";
    public const string Buy      = "Buy";
    public const string Equip    = "Equip";
    public const string View     = "View";
    public const string Continue = "Continue";
    public const string Browse   = "Browse";
    public const string Earn     = "Earn";
    public const string Save     = "Save";
    public const string Cancel   = "Cancel";
    public const string Close    = "Close";

    /// <summary>The retry label.</summary>
    public const string Retry = "Try again";

    /// <summary>
    /// Whether a label follows the label rules: a length limit, at most two words, and no digits. Digits are
    /// excluded because prices and counts belong next to the button, not in its label.
    /// </summary>
    public static bool IsShort(string label) =>
        label.Length <= 12 && !label.Any(char.IsDigit) && label.Split(' ').Length <= 2;

    /// <summary>
    /// The button label for a feature card on the Events hub, based on the feature's current state. It is here
    /// instead of in the markup so that tests can check it.
    /// </summary>
    public static string For(IFeatureView view)
    {
        if (FeatureClaims.HasClaimable(view))
            return view is SpinWheelView ? Spin : Claim;

        return view is FirstWeekView ? Continue : View;
    }
}
