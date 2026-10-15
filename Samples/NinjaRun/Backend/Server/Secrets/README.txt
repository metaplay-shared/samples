We recommend storing important secrets in Kubernetes Secrets instead of in
the code repository. Storing secrets in the code repository is not recommended,
because there they are visible to anyone who is able to read the repository
(whether intended or leaked). Also, simply deleting a file that has been in
the repository is generally not enough, because it remains in the repository's
history; if leaked, a secret should be rotated by revoking the old one and
issuing a new one.

Despite being named Secrets, this directory isn't treated any more
securely than any of the other directories, such as for example ../Config. In
particular, it's in the repository like anything else, and it gets baked in the
server image the same way as ../Config. This directory should only be used for
local development convenience and only for low-value secrets that would not
cause damage if leaked.

In a cloud environment, the game backend is able to read its secrets from
Kubernetes Secrets, which is a more secure place to store important secrets.

You can use the following syntax in the Options.*.yaml files (in ../Config)
to tell the server to fetch a server from Kubernetes Secrets instead
of the local filesystem:
  kube-secret://<secret-name>#<key>
For example:
  PushNotification:
    FirebaseCredentialsPath: "kube-secret://firebase#credentials"

Especially production-environment secrets should never be stored in the
repository. You should make sure that production and development environments
do not use the same secrets.

For more information about secret management, check out
https://docs.metaplay.io/cloud-deployments/how-to-guides/kubernetes-secrets.html.