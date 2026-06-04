---
id: installation
title: Installation & Your First Command
sidebar_label: Installation
sidebar_position: 1
description: Install the CodeFactory for Windows SDK and scaffold a working automation command.
---

# Installation & Your First Command

This page is the canonical "getting started" page — use it as the template for
the structure (front matter → prerequisites → numbered steps → verification → next
steps) that the rest of the documentation should follow.

## Prerequisites

Before you begin, make sure you have:

- **Visual Studio 2022 or 2026** (Windows).
- The **[CodeFactory for Windows](https://marketplace.visualstudio.com/items?itemName=CodeFactoryLLC.CodeFactoryForWindows)**
  extension installed (this is the *runtime* that loads your automation).
- The **.NET Framework 4.8** developer pack (the command library target).

:::info Runtime ≥ SDK
The CodeFactory runtime installed in Visual Studio must be **at the same version
or higher** than the SDK you compile against. This release targets runtime
`2.26151.0.1` or higher.
:::

## 1. Create a command library project

Use the **CodeFactory for Windows — Command Library** project template. It
produces a .NET Framework 4.8 class library wired up for packaging.

## 2. Add the SDK package

Install the aggregate SDK package, which pulls in `CodeFactory`,
`CodeFactory.WinVs`, and `CodeFactory.WinVs.Wpf` together:

```powershell
dotnet add package CodeFactory.WinVs.SDK
```

Or reference it directly in your `.csproj`:

```xml
<PackageReference Include="CodeFactory.WinVs.SDK" Version="2.26151.0.1-PreRelease" />
```

## 3. Author a command

Solution Explorer commands inherit from a `*CommandBase` class. The example
below adds a command to the right-click menu of any C# document and reports the
file name back to the developer.

```csharp title="HelloCommand.cs"
using System.Threading.Tasks;
using CodeFactory.WinVs.Commands;
using CodeFactory.WinVs.Commands.SolutionExplorer;
using CodeFactory.WinVs.Models.ProjectSystem;

public class HelloCommand : CSharpSourceCommandBase
{
    private static readonly string Title = "Say Hello";

    public HelloCommand(ILogger logger, IVsActions vsActions)
        : base(logger, vsActions, Title, "Greets the selected C# file.")
    {
    }

    // Decide whether the command should appear for the clicked file.
    public override async Task<bool> EnableCommandAsync(VsCSharpSource result) =>
        result is not null;

    // Run when the developer clicks the command.
    public override async Task ExecuteCommandAsync(VsCSharpSource result)
    {
        await _visualStudioActions.UserInterfaceActions
            .ShowInformationAsync(Title, $"Hello from {result.SourceCode.SourceDocument}!");
    }
}
```

:::note Convention
Every code sample should specify the language (` ```csharp `) so Prism applies
C# highlighting, and a `title=` so readers know which file it belongs in.
:::

### Referencing live source

To embed code straight from the repository (kept in sync automatically), use a
`reference` fenced block — the GitHub code-block theme renders it with a "View on
GitHub" link:

```csharp reference
https://github.com/CodeFactoryLLC/CodeFactoryForWindows.SDK/blob/main/src/CodeFactoryForWindows/CodeFactory.WinVs/Commands/SolutionExplorer/CSharpSourceCommandBase.cs#L1-L40
```

## 4. Build & package

Build the project. The SDK's MSBuild targets automatically invoke the
**CodeFactory Packager**, producing a `.cfa` file in your output directory.

```powershell
dotnet build -c Release
```

## Verify it worked

1. Load the `.cfa` into Visual Studio via the CodeFactory automation
   configuration.
2. Right-click a C# file in Solution Explorer.
3. Confirm **Say Hello** appears and shows the dialog when clicked.

## Next steps

- Learn how the runtime validates your library in
  **[Authentication & Validation](/docs/guides/authentication)**.
- Make your commands resilient with
  **[Error Handling](/docs/guides/error-handling)**.
- Browse the **[API Reference](/docs/api)** for every available type.
