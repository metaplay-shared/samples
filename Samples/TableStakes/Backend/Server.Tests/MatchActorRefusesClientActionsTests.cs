using Game.Server.Match;
using NUnit.Framework;
using System.Reflection;

namespace Game.Server.Tests
{
    /// <summary>
    /// Checks that <see cref="MatchActor"/> refuses client-originating actions. The SDK's validation hook allows
    /// them by default, and calling the override requires a live actor, so the tests check the constant it
    /// returns and that the override is declared.
    /// </summary>
    [TestFixture]
    public class MatchActorRefusesClientActionsTests
    {
        [Test]
        public void TheActorRefusesClientOriginatingActions()
        {
            Assert.That(MatchActor.AllowsClientOriginatingActions, Is.False, "the match timeline is server-only; a client may not put anything on it");
        }

        [Test]
        public void TheValidationHookIsOverridden()
        {
            MethodInfo hook = typeof(MatchActor).GetMethod("ValidateClientOriginatingAction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            Assert.That(hook, Is.Not.Null, "MatchActor no longer overrides ValidateClientOriginatingAction; the SDK's default allows");
            Assert.That(hook.ReturnType, Is.EqualTo(typeof(bool)));
        }
    }
}
