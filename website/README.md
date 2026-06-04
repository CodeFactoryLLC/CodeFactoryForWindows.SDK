# CodeFactory for Windows SDK — Documentation Site

The documentation site is built with [Docusaurus](https://docusaurus.io/). The C#
**API Reference** is generated from the SDK's XML doc comments via **DocFX** →
**DocFxMarkdownGen**, then rendered by Docusaurus alongside the hand-written
guides.

## Layout

```
website/
  docusaurus.config.js   # Site config: dark mode, C# Prism, Mermaid, local search, GitHub codeblocks
  sidebars.js            # docsSidebar (curated) + apiSidebar (auto-generated from docs/api)
  src/css/custom.css     # GitHub-dark palette
  docs/
    intro.md
    getting-started/     # Hand-written guidance
    guides/
    api/                 # GENERATED — written by DocFX + DocFxMarkdownGen (git-ignored)
docfx/
  docfx.json             # DocFX metadata config (reads the 3 built library DLLs)
  filterConfig.yml       # Strips internal namespaces + obsolete members
  config.yaml            # dfmg (DocFxMarkdownGen): docfx/api (YAML) -> website/docs/api (Markdown)
  postprocess-api.mjs    # Normalizes links (.md), links BCL types to MS Learn, drops empty entries
.github/workflows/deploy.yml  # dotnet build -> docfx metadata -> dfmg -> postprocess -> npm build -> deploy
```

> **Windows required for generation.** One library targets .NET Framework 4.8 +
> WPF, and DocFX reads the compiled assemblies, so the API-generation steps must
> run on Windows (the CI build job uses `windows-latest`).

## First-time setup

Generate and commit the lockfile so CI's `npm ci` works:

```powershell
cd website
npm install   # creates package-lock.json — commit it
```

## Local development

```powershell
# 1. Build the libraries so DocFX can read the DLLs (from the repo root)
dotnet build src/CodeFactoryForWindows/CodeFactory/CodeFactory.csproj -c Release
dotnet build src/CodeFactoryForWindows/CodeFactory.WinVs/CodeFactory.WinVs.csproj -c Release
dotnet build src/CodeFactoryForWindows/CodeFactory.WinVs.Wpf/CodeFactory.WinVs.Wpf.csproj -c Release

# 2. Install the tools. docfxmarkdowngen installs the `dfmg` command.
#    --ignore-failed-sources skips any private/auth'd NuGet feed in your config.
dotnet tool install -g docfx
dotnet tool install -g docfxmarkdowngen --add-source https://api.nuget.org/v3/index.json --ignore-failed-sources

# 3. Generate the API Markdown into website/docs/api
cd docfx
docfx metadata docfx.json    # YAML -> docfx/api
dfmg                         # reads ./config.yaml; YAML -> website/docs/api
cd ..
node docfx/postprocess-api.mjs  # normalize cross-reference links (.md) + drop empty entries

# 4. Run the site
cd website
npm start
```

`npm start` serves the guides immediately; the API Reference pages appear once
steps 1–3 have populated `docs/api`. CI runs the same sequence (plus a small
`sed` that retitles the generated index to "API Reference").

> **Why the post-process?** DocFxMarkdownGen emits inline cross-reference links
> without a `.md` extension, which Docusaurus resolves with trailing-slash URL
> math — dropping the `api` segment on namespace summary pages. Appending `.md`
> makes Docusaurus resolve them through the doc graph to the correct permalinks.

## Deployment

Pushing to `main` (changes under `src/`, `website/`, or `docfx/`) triggers
`.github/workflows/deploy.yml`, which builds everything and publishes to GitHub
Pages. Enable **Settings → Pages → Source: GitHub Actions** in the repository
once.
