// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Server.Database;
using Metaplay.Server.League;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Metaplay.Core.Schedule;
using static System.FormattableString;
using System.Runtime.Serialization;
using Metaplay.Cloud.Utility;

namespace Game.Server.SecretExample
{
    /// <summary>
    /// This class shows an example of how to use Kubernetes-based secrets in your game.
    /// This is also used by the Metaplay's platform tests to validate that secrets work.
    /// </summary>
    [RuntimeOptions("SecretExample", isStatic: true, "Runtime options for configuring example secrets.")]
    public class SecretExampleOptions : RuntimeOptionsBase
    {
        [MetaDescription("Enables resolving of the example secrets.")]
        public bool     Enabled                 { get; private set; } = false;
        [MetaDescription("The special URL of the Kubernetes secret.")]
        public string   KubernetesSecretPath    { get; private set; } = null;

        [IgnoreDataMember, Sensitive]
        public string ResolvedKubernetesSecret { get; private set; }

        public override async Task OnLoadedAsync(RuntimeOptionsRegistry registry)
        {
            if (Enabled)
            {
                // KubernetesSecretPath must be specified.
                if (string.IsNullOrEmpty(KubernetesSecretPath))
                    throw new InvalidOperationException("KubernetesSecretPath must be non-empty");

                // Make sure we actually have a Kubernetes secret.
                if (!KubernetesSecretPath.StartsWith("kube-secret://"))
                    throw new InvalidOperationException("Kubernetes secret path must start with 'kube-secret://'");

                // Resolve the secret value.
                ResolvedKubernetesSecret = await SecretUtil.ResolveSecretAsync(Log, KubernetesSecretPath, defaultToFile: false);

                // Check that the payload is what we expect it to be.
                if (ResolvedKubernetesSecret != "expected-secret-payload")
                    throw new InvalidOperationException("Invalid payload in resolved Kubernetes secret");
            }
        }
    }
}

