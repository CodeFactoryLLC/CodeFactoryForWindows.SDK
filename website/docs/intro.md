---
id: intro
title: Introduction
sidebar_label: Introduction
sidebar_position: 1
slug: /intro
description: Overview of the CodeFactory for Windows SDK and how this documentation is organized.
---

# CodeFactory for Windows SDK

The **CodeFactory for Windows SDK** is the library set for authoring **CodeFactory
automation** — code-generation and refactoring commands that run *inside Visual
Studio for Windows*. You write a .NET Framework 4.8 *command library*, build it
into a `.cfa` automation package, and the CodeFactory runtime surfaces your
commands on Solution Explorer and IDE menus.

## How this site is organized

| Section | What you'll find |
|---|---|
| **[Getting Started](/docs/getting-started/installation)** | Install the SDK and scaffold your first command library. |
| **[Guides](/docs/guides/authentication)** | Task-focused walkthroughs of core concepts. |
| **[API Reference](/docs/api)** | The generated C# reference for every public type. |

:::tip Version alignment
The SDK version and the **CodeFactory runtime installed in Visual Studio** must
match (runtime ≥ SDK). After upgrading the SDK you must **recompile** your
automation projects. See [Authentication & Validation](/docs/guides/authentication)
for how the version handshake works.
:::

## The packages

| Package | Target | Role |
|---|---|---|
| `CodeFactory` | .NET Standard 2.0 | Core contracts shared across IDE targets. |
| `CodeFactory.WinVs` | .NET Standard 2.0 | The primary automation API surface. |
| `CodeFactory.WinVs.Wpf` | .NET Framework 4.8 | WPF UI controls for command dialogs. |
| `CodeFactory.WinVs.SDK` | (meta) | Aggregates all of the above plus the packager. |

Ready to build something? Head to **[Installation](/docs/getting-started/installation)**.
