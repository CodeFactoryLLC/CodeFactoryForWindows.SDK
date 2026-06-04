// @ts-check
// CodeFactory for Windows SDK — Docusaurus site configuration.
// Docs: https://docusaurus.io/docs/api/docusaurus-config

const { themes: prismThemes } = require('prism-react-renderer');

const organizationName = 'CodeFactoryLLC';
const projectName = 'CodeFactoryForWindows.SDK';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'CodeFactory for Windows SDK',
  tagline: 'Author, build, and deploy CodeFactory automation for Visual Studio.',
  favicon: 'img/favicon.svg',

  // Production URL for GitHub Pages (project site).
  url: `https://${organizationName.toLowerCase()}.github.io`,
  baseUrl: `/${projectName}/`,

  organizationName, // GitHub org/user.
  projectName, // GitHub repo.
  trailingSlash: false,
  deploymentBranch: 'gh-pages',

  // Fail loudly on broken links so generated API links are validated at build time.
  // Strict link checking. The generated C# API cross-references are normalized by
  // docfx/postprocess-api.mjs (appends .md so Docusaurus resolves them via the
  // doc graph), so the build stays clean — any new broken link fails the build.
  onBrokenLinks: 'throw',
  onBrokenAnchors: 'warn',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  // Mermaid diagram support (enabled in markdown + theme below).
  markdown: {
    mermaid: true,
    // Parse by extension: .mdx → MDX (JSX), .md → CommonMark. The generated C#
    // API reference is .md and contains raw angle brackets (e.g. List<T>) that
    // would break the MDX/JSX parser; CommonMark passes them through safely.
    format: 'detect',
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },

  themes: [
    '@docusaurus/theme-mermaid',
    // Renders ```lang reference fenced blocks that pull source straight from GitHub.
    'docusaurus-theme-github-codeblock',
    // Offline / local full-text search (no external service required).
    [
      require.resolve('@easyops-cn/docusaurus-search-local'),
      /** @type {import('@easyops-cn/docusaurus-search-local').PluginOptions} */
      ({
        hashed: true,
        indexDocs: true,
        indexBlog: false,
        docsRouteBasePath: ['/docs'],
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
      }),
    ],
  ],

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          sidebarPath: require.resolve('./sidebars.js'),
          routeBasePath: 'docs',
          editUrl: `https://github.com/${organizationName}/${projectName}/tree/main/website/`,
          // The API reference is generated; don't surface "edit this page" there.
          editCurrentVersion: false,
        },
        blog: false,
        theme: {
          customCss: require.resolve('./src/css/custom.css'),
        },
        sitemap: {
          changefreq: 'weekly',
          priority: 0.5,
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      image: 'img/logo.svg',

      colorMode: {
        defaultMode: 'dark',
        disableSwitch: false,
        respectPrefersColorScheme: true,
      },

      // Configuration consumed by docusaurus-theme-github-codeblock.
      codeblock: {
        showGithubLink: true,
        githubLinkLabel: 'View on GitHub',
        showRunmeLink: false,
      },

      navbar: {
        title: 'CodeFactory for Windows',
        logo: {
          alt: 'CodeFactory Logo',
          src: 'img/logo.svg',
          srcDark: 'img/logo-dark.svg',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'docsSidebar',
            position: 'left',
            label: 'Documentation',
          },
          {
            type: 'docSidebar',
            sidebarId: 'apiSidebar',
            position: 'left',
            label: 'API Reference',
          },
          {
            href: 'https://www.nuget.org/packages/CodeFactory.WinVs',
            label: 'NuGet',
            position: 'right',
          },
          {
            href: `https://github.com/${organizationName}/${projectName}`,
            label: 'GitHub',
            position: 'right',
          },
        ],
      },

      footer: {
        style: 'dark',
        links: [
          {
            title: 'Docs',
            items: [
              { label: 'Getting Started', to: '/docs/getting-started/installation' },
              { label: 'Guides', to: '/docs/guides/authentication' },
              { label: 'API Reference', to: '/docs/api' },
            ],
          },
          {
            title: 'Packages',
            items: [
              { label: 'CodeFactory.WinVs.SDK', href: 'https://www.nuget.org/packages/CodeFactory.WinVs.SDK' },
              { label: 'CodeFactory.WinVs', href: 'https://www.nuget.org/packages/CodeFactory.WinVs' },
              { label: 'CodeFactory', href: 'https://www.nuget.org/packages/CodeFactory' },
            ],
          },
          {
            title: 'More',
            items: [
              { label: 'GitHub', href: `https://github.com/${organizationName}/${projectName}` },
              {
                label: 'Visual Studio Marketplace',
                href: 'https://marketplace.visualstudio.com/items?itemName=CodeFactoryLLC.CodeFactoryForWindows',
              },
            ],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} CodeFactory, LLC. Built with Docusaurus.`,
      },

      prism: {
        theme: prismThemes.github,
        darkTheme: prismThemes.vsDark,
        // Languages Prism does not bundle by default but that appear in the docs.
        additionalLanguages: [
          'csharp',
          'powershell',
          'bash',
          'json',
          'yaml',
          'xml-doc',
          'diff',
        ],
        defaultLanguage: 'csharp',
        magicComments: [
          {
            className: 'theme-code-block-highlighted-line',
            line: 'highlight-next-line',
            block: { start: 'highlight-start', end: 'highlight-end' },
          },
        ],
      },

      docs: {
        sidebar: {
          hideable: true,
          autoCollapseCategories: true,
        },
      },

      tableOfContents: {
        minHeadingLevel: 2,
        maxHeadingLevel: 4,
      },
    }),
};

module.exports = config;
