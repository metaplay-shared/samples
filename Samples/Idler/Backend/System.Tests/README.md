
# Game-Specific System Tests

These tests allow testing the game's server and the LiveOps Dashboard together.

The tests are written using [Playwright.NET](https://playwright.dev/dotnet/).

## Prerequisites

Install Playwright & CLI on your machine:

```bash
dotnet tool install --global Microsoft.Playwright.CLI
playwright install
```

## Local Setup

When running the tests, you need to have your game server and the LiveOps dashboard running. You can start the prerequisite services with:

```bash
<Project>/Backend/Dashboard$ pnpm dev
<Project>/Backend/Server$ dotnet run
```

After that, you can run the test suite with

```bash
<Project>/Backend/System.Tests$ dotnet test
```

## Running the Tests

Run all the tests:

```bash
<Project>/Backend/System.Tests$ dotnet test
```

Running a single test (matches anything with 'TestName' in the class or method name):

```bash
<Project>/Backend/System.Tests$ dotnet test --filter TestName
```

To run in headed mode, i.e., with a visible browser, set the environment variable `HEADED=1`:

```bash
# On Windows command prompt:
<Project>/Backend/System.Tests$ set HEADED=1
<Project>/Backend/System.Tests$ dotnet test

# On Linux/Mac:
<Project>/Backend/System.Tests$ HEADED=1 dotnet test
```

To get video outputs for the tests, set the environment variable `CAPTURE_VIDEO=1` when running:

```bash
# On Windows command prompt:
<Project>/Backend/System.Tests$ set CAPTURE_VIDEO=1
<Project>/Backend/System.Tests$ dotnet test

# On Linux/Mac:
<Project>/Backend/System.Tests$ CAPTURE_VIDEO=1 dotnet test
```

You can also run the tests using the 'Test Explorer' in Visual Studio. Other IDEs should have similar integrated runners.

See https://playwright.dev/dotnet/docs/running-tests for more run options.
