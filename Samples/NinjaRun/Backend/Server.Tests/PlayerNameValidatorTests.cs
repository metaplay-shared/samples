using Game.Logic;
using Game.Server.Player;
using Metaplay.Core;
using Metaplay.Core.Player;
using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>The name rule the server holds the SDK's own rename routes to.</summary>
    [TestFixture]
    public class PlayerNameValidatorTests
    {
        [Test]
        public void TheSdkRenameRoutesApplyTheSameNameRule()
        {
            PlayerRequirementsValidator validator = IntegrationRegistry.Get<PlayerRequirementsValidator>();

            Assert.That(validator, Is.InstanceOf<PlayerNameValidator>());
            Assert.That(validator.ValidatePlayerName("K"), Is.True, "one character, which the SDK's default would refuse");
            Assert.That(validator.ValidatePlayerName("🦊 Kaito 🇫🇮"), Is.True);
            Assert.That(validator.ValidatePlayerName(TimeTrialSkills.MaskedNinjaName), Is.False, "the house name marks a player as having none");
            Assert.That(validator.ValidatePlayerName("Kai<b>"), Is.False);
            Assert.That(validator.ValidatePlayerName("ABCDEFGHIJKLMNOPQRS"), Is.False, "nineteen characters");
        }
    }
}
