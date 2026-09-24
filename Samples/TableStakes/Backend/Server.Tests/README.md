# Server.Tests

NUnit tests for decisions that use server-only types, and for checks that need the server assembly loaded. They
cover the player actor's in-flight request tracking and throttles, seat setup, the tournament season schedule,
the weekly event seed package, the shipped timing defaults, analytics formatting and the game config build. Logic
that needs no server type is tested in `Backend/SharedCode.Tests` instead.

```bash
dotnet test Backend/Server.Tests/Server.Tests.csproj
```

The tests need no running server and no database. See [`docs/testing.md`](../../docs/testing.md#server-unit-tests)
for what belongs in this layer and the other test layers.
