# JCMU GitHub Release Creator 🚀

A lightweight, zero-friction Windows Context Menu extension that compiles your C# application into a standalone executable and publishes it directly to GitHub Releases.

Built on the **Jinn Context Menu Utility (JCMU)** framework.

## ✨ Why this exists

Creating a GitHub release for a simple CLI tool or desktop app usually involves a tedious dance: updating the `.csproj` version, running `dotnet publish`, zipping the output, opening the browser, drafting a release, uploading the binary, and generating release notes.

This addon reduces that entire process to a single **Right-Click**.

## 🧠 What it actually does

When you right-click your project folder and select **Create GitHub Release...**, this tool:
1. **Reads your `.csproj`** to instantly figure out your current version and Target Framework.
2. **Validates `<PublishSingleFile>true</PublishSingleFile>`** so you never accidentally upload a broken executable that is missing its dependency DLLs.
3. **Checks Git Integrity** to ensure you are on `main`/`master` and your working tree is clean (unless you explicitly override it).
4. **Compiles** a Release build on the fly.
5. **Uploads** the standalone `.exe` to GitHub, prepending a `v` to your version (e.g., `v1.2.0`) and auto-generating the release notes using the GitHub CLI.

## ⚡ The Pro-Tip: Triggering CI/CD

While this tool is incredibly simple (it just builds and uploads a single `.exe`), **creating a GitHub Release is a powerful webhook event**. 

By publishing your executable via this JCMU addon, you can effortlessly trigger complex downstream **GitHub Actions**. Just add this to any workflow file in your repo:

```yaml
on:
  release:
    types: [published]
```

Now, your simple right-click just built your `.exe` *and* kicked off your Docker builds, NuGet pushes, or deployment scripts in the cloud!

## 📦 Prerequisites

1. [Jinn Context Menu Utility (JCMU)](https://github.com/JinnDev/JCMU) installed on your machine.
2. The [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
3. The [GitHub CLI (`gh`)](https://cli.github.com/) installed and authenticated (`gh auth login`).

## 🛠️ Usage

1. Open **Windows Explorer**.
2. Right-click on any `.NET 8` executable project directory.
3. Select **JCMU Tools -> Create GitHub Release...**
4. Confirm the version number in the console window.
5. Done! The URL to your new release will be printed in the console.