using System;
using System.Runtime.InteropServices.JavaScript;

namespace WebClient.Integration;

/// <summary>
/// Whether the browser reports the <c>prefers-reduced-motion</c> setting, for C# code that times animations.
/// CSS handles the setting for its own animations, and callers read it here for waits timed in C#, such as
/// ending a wheel spin. <c>wallet-burst.js</c> sets the <c>tsReducedMotion</c> global. If the global is missing or
/// cannot be read, <see cref="ReducedMotion"/> returns false and callers use their full durations.
/// </summary>
public static class MotionPreference
{
    public static bool ReducedMotion
    {
        get
        {
            try
            {
                return JSHost.GlobalThis.GetPropertyAsBoolean("tsReducedMotion");
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
