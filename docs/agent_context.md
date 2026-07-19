# AI Agent Context Document

> **Note to Future AI Agents:** Read this document first to gain full context of the ExploreDB project, its architecture, and its history. This acts as the long-term memory for the project.

## 1. Project Overview
**Name**: ExploreDB (Formerly *DbExplore*)
**Purpose**: A powerful, modern database exploration and querying tool that connects directly to SQL Server databases. It provides an intuitive, glassmorphic UI to navigate, analyze, and query data without requiring deep SQL knowledge.

## 2. Technology Stack
- **Framework**: .NET 8.0 MAUI Blazor Hybrid
- **Language**: C#, HTML, CSS, JavaScript
- **Database Access**: `Microsoft.Data.SqlClient` (SQL Server)
- **UI/UX**: Custom CSS with a dark-themed, glassmorphic design philosophy. (Focus on modern aesthetics, smooth transitions, and dynamic components).
- **Packaging**: Windows MSIX Package (`win-x64`)

## 3. Core Features & Business Logic
- **Pathfinder (Auto-Join Engine)**: The flagship feature. Users select columns from multiple tables, and the app automatically calculates the shortest JOIN path between them. It intelligently chooses between `LEFT JOIN` and `INNER JOIN` based on foreign key nullability constraints and directionality.
- **Query Runner**: Allows execution of arbitrary, custom SQL queries with a dynamic results grid that handles unknown column structures on the fly.
- **Schema Explorer**: Discovers and visualizes database tables, views, stored procedures, column types, row counts, and complex parent-child foreign key relationships.

## 4. Deployment & Release Architecture
The deployment pipeline is highly customized to bypass Visual Studio's heavy publishing UI and enable silent, web-based background updates for end users.

- **Build Script**: `build_installer.bat` automates the `dotnet publish` process for MSIX and uses `signtool.exe` to digitally sign the package.
- **Security**: The MSIX is signed using a custom self-signed certificate (`cert\ExploreDB.pfx`). Because it is self-signed, end users must run `trust_certificate.bat` one time to inject it into their Trusted Root Certification Authorities store.
- **Auto-Updates**: The app utilizes Windows **AppInstaller**. The `ExploreDB.appinstaller` XML file configures the app to check for updates on launch.
- **Hosting Strategy**: 
  - We use a dual-hosting setup to keep the main repository clean.
  - A secondary public repository (`ExploreDB-Releases`) is used.
  - The tiny `ExploreDB.appinstaller` file is hosted on **GitHub Pages** (providing a permanent URL for Windows to check).
  - The massive 18 MB `ExploreDB.msix` binaries are uploaded to **GitHub Releases**. The `.appinstaller` file redirects to these binaries.

## 5. Important Project History & Quirks
- **Project Rename**: The project was originally named `DbExplore` but was globally renamed to `ExploreDB`. If you ever see legacy references to `DbExplore` in old system paths or user context, map it to `ExploreDB`.
- **Identity Strictness**: Windows App Installer is extremely strict about the package `Identity`. The `Name` in `Package.appxmanifest` (`ExploreDB.App`) MUST exactly match the `Name` in `ExploreDB.appinstaller`.
- **Custom Tools Priority**: When working in this repository, always prioritize specialized tools (like `grep_search` or `multi_replace_file_content`) over generic shell commands to maintain code integrity.
