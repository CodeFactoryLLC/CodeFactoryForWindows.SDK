---
id: error-handling
title: Error Handling
sidebar_label: Error Handling
sidebar_position: 2
description: Patterns for handling failures in CodeFactory automation commands.
---

# Error Handling

Automation runs against a live solution, so failures are inevitable — a file is
locked, a model can't be loaded, the developer cancels a dialog. This guide
covers the exception types the SDK raises and the recommended handling pattern.
Use it as the template for **reference + recommended-pattern** style pages.

## The exception hierarchy

The SDK raises a small, intentional set of exceptions, all rooted in
`CodeFactoryException`. Catching the base type lets you handle any SDK-originated
failure in one place.

| Exception | Raised when |
|---|---|
| `CodeFactoryException` | Base type for all SDK errors. |
| `ActionException` | A Visual Studio action (read/write/navigation) failed. |
| `ModelException` | A code or project-system model could not be used. |
| `ModelLoadException` | A model failed to load from source. |
| `DocumentException` | A source document operation failed. |
| `UnsupportedSdkLibraryException` | The library is outside the runtime's SDK window. |

## Recommended pattern

Wrap the body of `ExecuteCommandAsync`, surface a friendly message to the
developer, and **log the detail** for diagnostics. Never let an exception escape
the command — an unhandled exception aborts the whole automation run.

```csharp title="Resilient command execution"
public override async Task ExecuteCommandAsync(VsCSharpSource result)
{
    try
    {
        await GenerateAsync(result);
    }
    catch (ModelLoadException ex)
    {
        // Expected, recoverable: tell the user what to fix.
        _logger.Warning(ex, "Model could not be loaded for {Source}.", result.Name);
        await _visualStudioActions.UserInterfaceActions.ShowErrorAsync(
            "Generation skipped",
            "The selected file could not be parsed. Resolve build errors and retry.");
    }
    catch (CodeFactoryException ex)
    {
        // Any other SDK failure: log and report generically.
        _logger.Error(ex, "Automation failed for {Source}.", result.Name);
        await _visualStudioActions.UserInterfaceActions.ShowErrorAsync(
            "Automation error",
            "Something went wrong. See the CodeFactory log for details.");
    }
}
```

:::danger Don't swallow silently
Catching an exception **without logging it** makes failed automation impossible
to diagnose. Always log with the original exception as the first argument so the
stack trace is preserved.
:::

## Guarding inputs

Most runtime failures are avoidable with up-front checks. Prefer `EnableCommandAsync`
to keep a command hidden when it can't run, and guard arguments inside the body:

```csharp title="Fail fast on bad input"
public override async Task<bool> EnableCommandAsync(VsCSharpSource result)
{
    // Only show the command when there is a class to operate on.
    return result?.SourceCode?.Classes.Any() ?? false;
}
```

:::note Convention
Pair every "what can fail" table with a single **recommended pattern** code block,
then list the smaller guard clauses. Readers copy the pattern; the table explains
the cases.
:::

## Related

- [Authentication & Validation](/docs/guides/authentication)
- [Installation & Your First Command](/docs/getting-started/installation)
- [API Reference](/docs/api)
