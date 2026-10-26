namespace ExploreDB.Services.SqlCompare;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ExploreDB.Services.SqlCompare.Models;

public interface IHtmlReportGenerator
{
    string GenerateReportHtml(SchemaComparisonResultModel model);
    Task ExportToFileAsync(SchemaComparisonResultModel model, string filePath, CancellationToken cancellationToken = default);
}

public class HtmlReportGenerator : IHtmlReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task ExportToFileAsync(SchemaComparisonResultModel model, string filePath, CancellationToken cancellationToken = default)
    {
        var html = GenerateReportHtml(model);
        return File.WriteAllTextAsync(filePath, html, Encoding.UTF8, cancellationToken);
    }

    public string GenerateReportHtml(SchemaComparisonResultModel model)
    {
        var jsonModel = JsonSerializer.Serialize(new
        {
            summary = model.Summary,
            source = model.SourceEndpoint.ToString(),
            target = model.TargetEndpoint.ToString(),
            sourceDisplayName = GetEndpointDisplayName(model.SourceEndpoint),
            targetDisplayName = GetEndpointDisplayName(model.TargetEndpoint),
            sourceServer = model.SourceEndpoint.Server ?? (model.SourceEndpoint.Type == EndpointType.DacpacFile ? "Dacpac Package" : "localhost"),
            targetServer = model.TargetEndpoint.Server ?? (model.TargetEndpoint.Type == EndpointType.DacpacFile ? "Dacpac Package" : "localhost"),
            differences = model.Differences.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                category = d.CategoryDisplayName,
                changeType = d.ChangeType.ToString(),
                sourceScript = d.SourceScript,
                targetScript = d.TargetScript,
                hasDifference = d.HasDifference
            }),
            syncScript = model.SynchronizationScript
        }, JsonOptions);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>ExploreDB Schema Comparison Report</title>
  <style>
    :root {
      --bg: #141619;
      --surface: #1e2126;
      --surface-secondary: #181a1f;
      --surface-hover: #292d34;
      --border: #2d3139;
      --text: #f8fafc;
      --text-muted: #94a3b8;
      --accent: #0d6efd;
      --added: #22c55e;
      --added-bg: rgba(34, 197, 94, 0.15);
      --deleted: #ef4444;
      --deleted-bg: rgba(239, 68, 68, 0.15);
      --modified: #f59e0b;
      --modified-bg: rgba(245, 158, 11, 0.15);
      --identical: #64748b;
      --diff-bg: #1e1e1e;
      --scrollbar-thumb: rgba(255, 255, 255, 0.2);
      --scrollbar-thumb-hover: rgba(255, 255, 255, 0.32);
      --scrollbar-track: rgba(255, 255, 255, 0.03);
      --endpoint-badge-bg: rgba(255, 255, 255, 0.04);
      --endpoint-badge-color: #cbd5e1;
      --link-color: #38bdf8;
      --selected-bg: rgba(13, 110, 253, 0.15);
      --selected-border: #38bdf8;
    }

    [data-theme="light"] {
      --bg: #f8fafc;
      --surface: #ffffff;
      --surface-secondary: #f1f5f9;
      --surface-hover: #e2e8f0;
      --border: #cbd5e1;
      --text: #0f172a;
      --text-muted: #64748b;
      --accent: #0d6efd;
      --added: #16a34a;
      --added-bg: rgba(22, 163, 74, 0.12);
      --deleted: #dc2626;
      --deleted-bg: rgba(220, 38, 38, 0.12);
      --modified: #d97706;
      --modified-bg: rgba(217, 119, 6, 0.12);
      --identical: #64748b;
      --diff-bg: #ffffff;
      --scrollbar-thumb: rgba(0, 0, 0, 0.2);
      --scrollbar-thumb-hover: rgba(0, 0, 0, 0.35);
      --scrollbar-track: rgba(0, 0, 0, 0.03);
      --endpoint-badge-bg: rgba(0, 0, 0, 0.04);
      --endpoint-badge-color: #334155;
      --link-color: #0284c7;
      --selected-bg: #e0f2fe;
      --selected-border: #0284c7;
    }

    * {
      box-sizing: border-box;
      margin: 0;
      padding: 0;
      scrollbar-width: thin;
      scrollbar-color: var(--scrollbar-thumb) transparent;
    }
    *::-webkit-scrollbar {
      width: 8px;
      height: 8px;
    }
    *::-webkit-scrollbar-track {
      background: var(--scrollbar-track);
    }
    *::-webkit-scrollbar-thumb {
      background: var(--scrollbar-thumb);
      border-radius: 4px;
    }
    *::-webkit-scrollbar-thumb:hover {
      background: var(--scrollbar-thumb-hover);
    }
    *::-webkit-scrollbar-corner {
      background: transparent;
    }
    body {
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      background: var(--bg);
      color: var(--text);
      display: flex;
      flex-direction: column;
      height: 100vh;
      overflow: hidden;
    }
    header {
      background: var(--surface);
      border-bottom: 1px solid var(--border);
      padding: 10px 20px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-shrink: 0;
    }
    .header-title {
      display: flex;
      align-items: center;
      gap: 10px;
      font-size: 13.5px;
    }
    .report-title {
      font-weight: 700;
      color: var(--text);
      letter-spacing: 0.3px;
    }
    .header-divider {
      color: #475569;
      font-weight: 300;
    }
    .report-date-text {
      color: var(--text-muted);
      font-size: 12.5px;
    }
    .header-right {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: var(--text-muted);
    }
    .btn-theme-toggle {
      background: transparent;
      border: 1px solid var(--border);
      color: var(--text-muted);
      border-radius: 4px;
      width: 26px;
      height: 26px;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      transition: all 0.15s ease;
      padding: 0;
    }
    .btn-theme-toggle:hover {
      background: var(--surface-hover);
      color: var(--text);
      border-color: #64748b;
    }
    .header-credit-text {
      color: var(--text-muted);
      cursor: default;
    }
    .header-credit-link {
      color: var(--link-color);
      font-weight: 600;
      text-decoration: none;
      display: inline-flex;
      align-items: center;
      gap: 4px;
      cursor: pointer;
    }
    .header-credit-link:hover {
      text-decoration: underline;
      text-underline-offset: 2px;
    }
    .header-credit-link svg {
      opacity: 0.8;
      transition: opacity 0.15s ease;
    }
    .header-credit-link:hover svg {
      opacity: 1;
    }

    /* Filters & Endpoints Summary Row */
    .filters-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 16px;
      padding: 10px 20px;
      background: var(--surface-secondary);
      border-bottom: 1px solid var(--border);
      flex-shrink: 0;
      flex-wrap: wrap;
    }
    .kpi-cards-group {
      display: flex;
      gap: 10px;
      align-items: center;
      flex-wrap: wrap;
    }
    .kpi-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 8px;
      padding: 7px 16px;
      min-width: 125px;
      text-align: center;
      cursor: pointer;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 2px;
      user-select: none;
      transition: all 0.15s ease;
      box-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
    }
    .kpi-card:hover {
      background: var(--surface-hover);
      border-color: rgba(255, 255, 255, 0.2);
    }
    .kpi-label {
      font-size: 10.5px;
      font-weight: 700;
      color: #94a3b8;
      letter-spacing: 0.6px;
      text-transform: uppercase;
    }
    .kpi-value {
      font-size: 22px;
      font-weight: 800;
      line-height: 1.15;
    }
    .card-all .kpi-value { color: #38bdf8; }
    .card-all.active {
      border-color: #0d6efd;
      box-shadow: 0 0 0 1px #0d6efd;
      background: rgba(13, 110, 253, 0.1);
    }
    .card-added .kpi-value { color: #22c55e; }
    .card-added.active {
      border-color: #22c55e;
      box-shadow: 0 0 0 1px #22c55e;
      background: rgba(34, 197, 94, 0.1);
    }
    .card-modified .kpi-value { color: #f59e0b; }
    .card-modified.active {
      border-color: #f59e0b;
      box-shadow: 0 0 0 1px #f59e0b;
      background: rgba(245, 158, 11, 0.1);
    }
    .card-deleted .kpi-value { color: #ef4444; }
    .card-deleted.active {
      border-color: #ef4444;
      box-shadow: 0 0 0 1px #ef4444;
      background: rgba(239, 68, 68, 0.1);
    }
    .card-identical .kpi-value { color: #94a3b8; }
    .card-identical.active {
      border-color: #94a3b8;
      box-shadow: 0 0 0 1px #94a3b8;
      background: rgba(148, 163, 184, 0.1);
    }

    .endpoints-summary {
      display: flex;
      flex-direction: column;
      gap: 5px;
      margin-left: auto;
      flex-shrink: 0;
      justify-content: center;
    }
    .endpoint-badge {
      font-size: 11.5px;
      color: var(--endpoint-badge-color);
      background: var(--endpoint-badge-bg);
      padding: 3px 10px;
      border-radius: 5px;
      border: 1px solid var(--border);
      max-width: 480px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      line-height: 1.4;
      cursor: help;
    }
    .source-prefix {
      color: #38bdf8;
      font-weight: 700;
      margin-right: 3px;
    }
    .target-prefix {
      color: #22c55e;
      font-weight: 700;
      margin-right: 3px;
    }
    [data-theme="light"] .source-prefix {
      color: #0284c7;
    }
    [data-theme="light"] .target-prefix {
      color: #16a34a;
    }

    .main-layout {
      display: flex;
      flex: 1;
      min-height: 0;
      overflow: hidden;
    }
    .sidebar {
      width: 360px;
      min-width: 260px;
      border-right: 1px solid var(--border);
      display: flex;
      flex-direction: column;
      background: var(--surface);
      overflow: hidden;
    }
    .sidebar.collapsed {
      display: none;
    }
    .sidebar-search-row {
      padding: 8px 10px 8px 12px;
      background: var(--surface);
      border-bottom: 1px solid var(--border);
      flex-shrink: 0;
      display: flex;
      align-items: center;
      gap: 6px;
    }
    .search-input-wrap {
      position: relative;
      display: flex;
      align-items: center;
      flex: 1;
      min-width: 0;
    }
    .btn-collapse-sidebar,
    .btn-nav-obj {
      background: transparent;
      border: 1px solid var(--border);
      color: var(--text-muted);
      border-radius: 5px;
      width: 28px;
      height: 28px;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      flex-shrink: 0;
      transition: all 0.15s ease;
      padding: 0;
    }
    .btn-collapse-sidebar:hover,
    .btn-nav-obj:hover:not(:disabled) {
      background: var(--surface-hover);
      color: var(--text);
      border-color: #475569;
    }
    .btn-nav-obj:disabled {
      opacity: 0.35;
      cursor: not-allowed;
    }
    .search-icon {
      position: absolute;
      left: 10px;
      color: var(--text-muted);
      pointer-events: none;
    }
    .sidebar-search-input {
      width: 100%;
      background: var(--bg);
      border: 1px solid var(--border);
      color: var(--text);
      padding: 6px 28px 6px 30px;
      border-radius: 5px;
      font-size: 12px;
      outline: none;
      font-family: inherit;
      transition: border-color 0.15s;
    }
    .sidebar-search-input:focus {
      border-color: var(--accent);
    }
    .clear-search-btn {
      position: absolute;
      right: 6px;
      background: transparent;
      border: none;
      color: var(--text-muted);
      cursor: pointer;
      display: none;
      align-items: center;
      justify-content: center;
      padding: 3px 5px;
      border-radius: 3px;
    }
    .clear-search-btn:hover {
      color: var(--text);
    }
    .sidebar-object-list {
      flex: 1;
      overflow-y: auto;
    }
    .object-item {
      padding: 8px 14px;
      border-bottom: 1px solid rgba(255,255,255,0.04);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: space-between;
      transition: background 0.1s;
    }
    .object-item:hover { background: var(--surface-hover); }
    .object-item.selected {
      background: rgba(13, 110, 253, 0.15);
      border-left: 3px solid var(--accent);
    }
    .object-info { display: flex; flex-direction: column; gap: 2px; overflow: hidden; }
    .object-name { font-weight: 500; font-size: 12.5px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .object-category { font-size: 11px; color: var(--text-muted); }
    .status-tag {
      font-size: 10.5px;
      font-weight: 700;
      padding: 2px 7px;
      border-radius: 4px;
      text-transform: uppercase;
      letter-spacing: 0.4px;
      flex-shrink: 0;
    }
    .tag-Added { background: var(--added-bg); color: var(--added); }
    .tag-Deleted { background: var(--deleted-bg); color: var(--deleted); }
    .tag-Modified { background: var(--modified-bg); color: var(--modified); }
    .tag-Identical { background: rgba(100, 116, 139, 0.2); color: var(--identical); }

    /* Diff Viewer Container - Matches App Design (Image 2) */
    .diff-container {
      flex: 1;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      background: var(--diff-bg);
    }

    /* Row 1: Object Toolbar */
    .diff-toolbar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 7px 16px;
      background: var(--surface);
      border-bottom: 1px solid var(--border);
      flex-shrink: 0;
    }
    .diff-toolbar-left {
      display: flex;
      align-items: center;
      gap: 10px;
      overflow: hidden;
    }
    .diff-object-title {
      font-weight: 700;
      font-size: 13.5px;
      color: var(--text);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .diff-toolbar-actions {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-shrink: 0;
    }
    .btn-tool {
      background: transparent;
      border: 1px solid var(--border);
      color: var(--text-muted);
      font-size: 11.5px;
      padding: 3px 9px;
      border-radius: 4px;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 5px;
      font-family: inherit;
      transition: all 0.15s;
    }
    .btn-tool:hover {
      background: var(--surface-hover);
      color: var(--text);
    }
    .btn-tool.btn-tool-icon {
      padding: 3px 7px;
      min-width: 26px;
      justify-content: center;
    }
    .btn-tool:disabled {
      opacity: 0.35;
      cursor: not-allowed;
      pointer-events: none;
    }

    /* Row 2: 50/50 Split Endpoints Sub-Header (Image 2 exact match) */
    .diff-endpoints-header {
      display: flex;
      background: var(--surface-secondary);
      border-bottom: 1px solid var(--border);
      font-size: 12px;
      flex-shrink: 0;
    }
    .diff-endpoint-col {
      flex: 1;
      padding: 5px 14px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      overflow: hidden;
    }
    .diff-endpoint-col.source-col {
      border-right: 1px solid var(--border);
    }
    .endpoint-info-group {
      display: flex;
      align-items: center;
      gap: 8px;
      overflow: hidden;
    }
    .pill-source {
      background: rgba(13, 110, 253, 0.18);
      color: #38bdf8;
      border: 1px solid rgba(56, 189, 248, 0.35);
      font-size: 10.5px;
      font-weight: 700;
      padding: 2px 7px;
      border-radius: 4px;
      letter-spacing: 0.5px;
      flex-shrink: 0;
    }
    .pill-target {
      background: rgba(34, 197, 94, 0.18);
      color: #22c55e;
      border: 1px solid rgba(34, 197, 94, 0.35);
      font-size: 10.5px;
      font-weight: 700;
      padding: 2px 7px;
      border-radius: 4px;
      letter-spacing: 0.5px;
      flex-shrink: 0;
    }
    [data-theme="light"] .pill-source {
      background: rgba(13, 110, 253, 0.12);
      color: #0284c7;
      border-color: rgba(2, 132, 199, 0.35);
    }
    [data-theme="light"] .pill-target {
      background: rgba(22, 163, 74, 0.12);
      color: #16a34a;
      border-color: rgba(22, 163, 74, 0.35);
    }
    .endpoint-title {
      font-weight: 600;
      color: var(--text);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      font-size: 12.5px;
    }
    .copy-btn {
      background: transparent;
      border: none;
      color: var(--text-muted);
      font-size: 11.5px;
      display: inline-flex;
      align-items: center;
      gap: 4px;
      cursor: pointer;
      padding: 2px 7px;
      border-radius: 4px;
      transition: all 0.15s ease;
      flex-shrink: 0;
      font-family: inherit;
    }
    .copy-btn:hover {
      color: var(--text);
      background: var(--surface-hover);
    }
    .copy-btn.copied {
      color: #22c55e;
    }

    /* Row 3: Monaco & Fallback Diff Editor Wrapper */
    .diff-editor-wrap {
      flex: 1;
      position: relative;
      overflow: hidden;
      background: var(--diff-bg);
    }
    #monacoDiffContainer {
      width: 100%;
      height: 100%;
    }
    /* Match Monaco's internal scrollbars to ExploreDB's muted scrollbar theme */
    .monaco-editor .scrollbar .slider {
      background: var(--scrollbar-thumb) !important;
      border-radius: 4px !important;
    }
    .monaco-editor .scrollbar .slider:hover {
      background: var(--scrollbar-thumb-hover) !important;
    }
    .monaco-editor .scrollbar.vertical,
    .monaco-editor .scrollbar.horizontal {
      background: var(--scrollbar-track) !important;
    }
    .monaco-diff-editor .overview-ruler,
    .monaco-editor .decorationsOverviewRuler {
      display: none !important;
      width: 0 !important;
    }
    .diff-loading {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      background: var(--diff-bg);
      color: var(--text-muted);
      font-size: 13px;
      gap: 12px;
      z-index: 5;
    }
    .spinner {
      width: 24px;
      height: 24px;
      border: 2px solid rgba(255, 255, 255, 0.1);
      border-top-color: #38bdf8;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* Fallback Static Diff Styles (Used if Monaco CDN is unavailable) */
    #diffFallback {
      width: 100%;
      height: 100%;
      display: flex;
      overflow: hidden;
      background: var(--diff-bg);
    }
    .fallback-pane {
      flex: 1;
      overflow: auto;
      white-space: pre;
      font-family: "Cascadia Code", Consolas, Menlo, monospace;
      font-size: 12px;
      line-height: 1.6;
      background: var(--diff-bg);
    }
    .fallback-line {
      display: flex;
      min-width: fit-content;
      height: 21px;
      padding: 0 4px;
      box-sizing: border-box;
    }
    .fallback-num {
      width: 44px;
      text-align: right;
      padding-right: 12px;
      color: #6e7681;
      user-select: none;
      flex-shrink: 0;
      font-size: 11.5px;
    }
    .fallback-text { flex: 1; overflow: visible; }
    .fallback-add { background: rgba(34, 197, 94, 0.2); color: #86efac; }
    .fallback-del { background: rgba(239, 68, 68, 0.2); color: #fca5a5; }
    .fallback-gap {
      background: repeating-linear-gradient(-45deg, rgba(255, 255, 255, 0.03), rgba(255, 255, 255, 0.03) 6px, transparent 6px, transparent 12px);
    }

    /* SQL Syntax Colors for Fallback Engine (VS Code Dark) */
    .kw { color: #569cd6; font-weight: 600; }
    .typ { color: #4ec9b0; }
    .str { color: #ce9178; }
    .com { color: #6a9955; font-style: italic; }

    /* SQL Syntax Colors for Fallback Engine (VS Code Light mode) */
    [data-theme="light"] .kw { color: #0000ff; }
    [data-theme="light"] .typ { color: #267f99; }
    [data-theme="light"] .str { color: #a31515; }
    [data-theme="light"] .com { color: #008000; }
    [data-theme="light"] .fallback-num { color: #94a3b8; }
    [data-theme="light"] .fallback-line { color: #0f172a; }
    [data-theme="light"] .fallback-add { background: rgba(34, 197, 94, 0.18); color: #15803d; }
    [data-theme="light"] .fallback-del { background: rgba(239, 68, 68, 0.18); color: #b91c1c; }
    [data-theme="light"] .fallback-gap {
      background: repeating-linear-gradient(-45deg, rgba(0, 0, 0, 0.04), rgba(0, 0, 0, 0.04) 6px, transparent 6px, transparent 12px);
    }
  </style>
</head>
<body>

  <header>
    <div class="header-title">
      <span class="report-title">SQL Compare Report</span>
      <span class="header-divider">|</span>
      <span class="report-date-text">Generated On: <span id="reportDate">...</span></span>
    </div>
    <div class="header-right">
      <button type="button" class="btn-theme-toggle" id="btnThemeToggle" onclick="toggleTheme()" title="Switch to Light mode">
        <svg id="themeIconSun" width="13" height="13" viewBox="0 0 16 16" fill="currentColor">
          <path d="M8 11a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm0 1a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM8 0a.5.5 0 0 1 .5.5v2a.5.5 0 0 1-1 0v-2A.5.5 0 0 1 8 0zm0 13a.5.5 0 0 1 .5.5v2a.5.5 0 0 1-1 0v-2A.5.5 0 0 1 8 13zm8-5a.5.5 0 0 1-.5.5h-2a.5.5 0 0 1 0-1h2a.5.5 0 0 1 .5.5zM3 8a.5.5 0 0 1-.5.5h-2a.5.5 0 0 1 0-1h2A.5.5 0 0 1 3 8zm10.657-5.657a.5.5 0 0 1 0 .707l-1.414 1.415a.5.5 0 1 1-.707-.708l1.414-1.414a.5.5 0 0 1 .707 0zm-9.193 9.193a.5.5 0 0 1 0 .707L3.05 13.657a.5.5 0 0 1-.707-.707l1.414-1.414a.5.5 0 0 1 .707 0zm9.193 2.121a.5.5 0 0 1-.707 0l-1.414-1.414a.5.5 0 0 1 .707-.707l1.414 1.414a.5.5 0 0 1 0 .707zM4.464 4.465a.5.5 0 0 1-.707 0L2.343 3.05a.5.5 0 1 1 .707-.707l1.414 1.414a.5.5 0 0 1 0 .708z"/>
        </svg>
        <svg id="themeIconMoon" width="13" height="13" viewBox="0 0 16 16" fill="currentColor" style="display: none;">
          <path d="M6 .278a.768.768 0 0 1 .08.858 7.208 7.208 0 0 0-.878 3.46c0 4.021 3.278 7.277 7.318 7.277.527 0 1.04-.055 1.533-.16a.787.787 0 0 1 .81.316.733.733 0 0 1-.031.893A8.349 8.349 0 0 1 8.344 16C3.734 16 0 12.286 0 7.71 0 4.266 2.114 1.312 5.124.06A.752.752 0 0 1 6 .278zM4.858 1.311A7.269 7.269 0 0 0 1.02 7.71c0 4.02 3.279 7.276 7.319 7.276a7.316 7.316 0 0 0 5.205-2.162c-.337.042-.68.063-1.029.063-4.61 0-8.343-3.714-8.343-8.29 0-1.167.242-2.278.686-3.286z"/>
        </svg>
      </button>
      <span class="header-divider" style="margin: 0 4px;">|</span>
      <span class="header-credit-text">Generated by</span>
      <a href="https://apps.microsoft.com/store/detail/9NF6M6NT09BM?cid=DevShareMCLPCS" target="_blank" rel="noopener noreferrer" class="header-credit-link" title="Open ExploreDB on Microsoft Store">
        <span>ExploreDB</span>
        <svg width="11" height="11" viewBox="0 0 16 16" fill="currentColor">
          <path fill-rule="evenodd" d="M8.636 3.5a.5.5 0 0 0-.5-.5H1.5A1.5 1.5 0 0 0 0 4.5v10A1.5 1.5 0 0 0 1.5 16h10a1.5 1.5 0 0 0 1.5-1.5V7.864a.5.5 0 0 0-1 0V14.5a.5.5 0 0 1-.5.5h-10a.5.5 0 0 1-.5-.5v-10a.5.5 0 0 1 .5-.5h6.636a.5.5 0 0 0 .5-.5z"/>
          <path fill-rule="evenodd" d="M16 .5a.5.5 0 0 0-.5-.5h-5a.5.5 0 0 0 0 1h3.793L6.146 9.146a.5.5 0 1 0 .708.708L15 1.707V5.5a.5.5 0 0 0 1 0v-5z"/>
        </svg>
      </a>
    </div>
  </header>

  <div class="filters-row">
    <div class="kpi-cards-group">
      <div class="kpi-card card-all active" onclick="setFilter('All')">
        <span class="kpi-label">ALL DIFFERENCES</span>
        <span class="kpi-value" id="cntAll">0</span>
      </div>
      <div class="kpi-card card-added" onclick="setFilter('Added')">
        <span class="kpi-label">ONLY IN SOURCE</span>
        <span class="kpi-value" id="cntAdded">0</span>
      </div>
      <div class="kpi-card card-modified" onclick="setFilter('Modified')">
        <span class="kpi-label">DIFFERENT</span>
        <span class="kpi-value" id="cntModified">0</span>
      </div>
      <div class="kpi-card card-deleted" onclick="setFilter('Deleted')">
        <span class="kpi-label">ONLY IN TARGET</span>
        <span class="kpi-value" id="cntDeleted">0</span>
      </div>
      <div class="kpi-card card-identical" id="cardIdentical" style="display: none;" onclick="setFilter('Identical')">
        <span class="kpi-label">IDENTICAL</span>
        <span class="kpi-value" id="cntIdentical">0</span>
      </div>
    </div>

    <div class="endpoints-summary">
      <div class="endpoint-badge" id="sourceBadge">
        <span class="source-prefix">Source:</span><span id="sourceDesc">...</span>
      </div>
      <div class="endpoint-badge" id="targetBadge">
        <span class="target-prefix">Target:</span><span id="targetDesc">...</span>
      </div>
    </div>
  </div>

  <div class="main-layout">
    <div class="sidebar">
      <div class="sidebar-search-row">
        <div class="search-input-wrap">
          <svg width="13" height="13" viewBox="0 0 16 16" fill="currentColor" class="search-icon">
            <path d="M11.742 10.344a6.5 6.5 0 1 0-1.397 1.398h-.001c.03.04.062.078.098.115l3.85 3.85a1 1 0 0 0 1.415-1.414l-3.85-3.85a1.007 1.007 0 0 0-.115-.1zM12 6.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z"/>
          </svg>
          <input type="text" id="searchInput" class="sidebar-search-input" placeholder="Filter objects..." oninput="applyFilters()">
          <button type="button" id="clearSearchBtn" class="clear-search-btn" onclick="clearSearch()" title="Clear filter">
            <svg width="11" height="11" viewBox="0 0 16 16" fill="currentColor">
              <path d="M2.146 2.854a.5.5 0 1 1 .708-.708L8 7.293l5.146-5.147a.5.5 0 0 1 .708.708L8.707 8l5.147 5.146a.5.5 0 0 1-.708.708L8 8.707l-5.146 5.147a.5.5 0 0 1-.708-.708L7.293 8 2.146 2.854Z"/>
            </svg>
          </button>
        </div>
        <button type="button" class="btn-nav-obj" id="btnPrevObject" onclick="navigateObject(-1)" title="Previous difference in object list">
          <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
            <path fill-rule="evenodd" d="M8 15a.5.5 0 0 0 .5-.5V2.707l3.146 3.147a.5.5 0 0 0 .708-.708l-4-4a.5.5 0 0 0-.708 0l-4 4a.5.5 0 1 0 .708.708L7.5 2.707V14.5a.5.5 0 0 0 .5.5z"/>
          </svg>
        </button>
        <button type="button" class="btn-nav-obj" id="btnNextObject" onclick="navigateObject(1)" title="Next difference in object list">
          <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
            <path fill-rule="evenodd" d="M8 1a.5.5 0 0 1 .5.5v11.793l3.146-3.147a.5.5 0 0 1 .708.708l-4 4a.5.5 0 0 1-.708 0l-4-4a.5.5 0 0 1 .708-.708L7.5 13.293V1.5A.5.5 0 0 1 8 1z"/>
          </svg>
        </button>
        <button type="button" class="btn-collapse-sidebar" onclick="toggleSidebar()" title="Collapse sidebar">
          <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
            <path fill-rule="evenodd" d="M11.354 1.646a.5.5 0 0 1 0 .708L5.707 8l5.647 5.646a.5.5 0 0 1-.708.708l-6-6a.5.5 0 0 1 0-.708l6-6a.5.5 0 0 1 .708 0z"/>
          </svg>
        </button>
      </div>
      <div class="sidebar-object-list" id="objectList"></div>
    </div>

    <div class="diff-container">
      <!-- Toolbar with Object Title, Badge & View Mode Toggle -->
      <div class="diff-toolbar">
        <div class="diff-toolbar-left">
          <button type="button" class="btn-tool" id="btnExpandSidebar" onclick="toggleSidebar()" title="Expand objects sidebar" style="display: none;">
            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
              <path fill-rule="evenodd" d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
            </svg>
            <span>Objects</span>
          </button>
          <span class="diff-object-title" id="selectedObjTitle">Select an object</span>
          <span class="status-tag" id="selectedObjBadge" style="display: none;"></span>
        </div>
        <div class="diff-toolbar-actions">
          <button type="button" class="btn-tool btn-tool-icon" id="btnPrevDiff" style="display: none;" onclick="navigateDiffBlock('prev')" title="Previous difference block">
            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
              <path fill-rule="evenodd" d="M8 15a.5.5 0 0 0 .5-.5V2.707l3.146 3.147a.5.5 0 0 0 .708-.708l-4-4a.5.5 0 0 0-.708 0l-4 4a.5.5 0 1 0 .708.708L7.5 2.707V14.5a.5.5 0 0 0 .5.5z"/>
            </svg>
          </button>
          <button type="button" class="btn-tool btn-tool-icon" id="btnNextDiff" style="display: none;" onclick="navigateDiffBlock('next')" title="Next difference block">
            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
              <path fill-rule="evenodd" d="M8 1a.5.5 0 0 1 .5.5v11.793l3.146-3.147a.5.5 0 0 1 .708.708l-4 4a.5.5 0 0 1-.708 0l-4-4a.5.5 0 0 1 .708-.708L7.5 13.293V1.5A.5.5 0 0 1 8 1z"/>
            </svg>
          </button>
          <button type="button" class="btn-tool" id="btnToggleSideBySide" style="display: none;" onclick="toggleSideBySide()" title="Toggle Side-by-Side / Inline Diff">
            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
              <path d="M0 3a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V3zm8.5 11V2H14a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H8.5zM7.5 2H2a1 1 0 0 0-1 1v10a1 1 0 0 1 1 1h5.5V2z"/>
            </svg>
            <span id="sideBySideText">Side-by-Side</span>
          </button>
        </div>
      </div>

      <!-- 50/50 Split Endpoints Sub-Header (Image 2 exact match) -->
      <div class="diff-endpoints-header">
        <div class="diff-endpoint-col source-col">
          <div class="endpoint-info-group">
            <span class="pill-source">SOURCE</span>
            <span class="endpoint-title" id="sourceDisplayName">Source</span>
          </div>
          <button type="button" class="copy-btn" id="copySourceBtn" onclick="copySource()" title="Copy Source DDL">
            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
              <path d="M4 1.5H3a2 2 0 0 0-2 2V14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V3.5a2 2 0 0 0-2-2h-1v1h1a1 1 0 0 1 1 1V14a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V3.5a1 1 0 0 1 1-1h1v-1z"/>
              <path d="M9.5 1a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5h-3a.5.5 0 0 1-.5-.5v-1a.5.5 0 0 1 .5-.5h3zm-3-1A1.5 1.5 0 0 0 5 1.5v1A1.5 1.5 0 0 0 6.5 4h3A1.5 1.5 0 0 0 11 2.5v-1A1.5 1.5 0 0 0 9.5 0h-3z"/>
            </svg>
            <span id="copySourceText">Copy</span>
          </button>
        </div>
        <div class="diff-endpoint-col">
          <div class="endpoint-info-group">
            <span class="pill-target">TARGET</span>
            <span class="endpoint-title" id="targetDisplayName">Target</span>
          </div>
          <button type="button" class="copy-btn" id="copyTargetBtn" onclick="copyTarget()" title="Copy Target DDL">
            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
              <path d="M4 1.5H3a2 2 0 0 0-2 2V14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V3.5a2 2 0 0 0-2-2h-1v1h1a1 1 0 0 1 1 1V14a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V3.5a1 1 0 0 1 1-1h1v-1z"/>
              <path d="M9.5 1a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5h-3a.5.5 0 0 1-.5-.5v-1a.5.5 0 0 1 .5-.5h3zm-3-1A1.5 1.5 0 0 0 5 1.5v1A1.5 1.5 0 0 0 6.5 4h3A1.5 1.5 0 0 0 11 2.5v-1A1.5 1.5 0 0 0 9.5 0h-3z"/>
            </svg>
            <span id="copyTargetText">Copy</span>
          </button>
        </div>
      </div>

      <!-- Monaco & Fallback Diff Container -->
      <div class="diff-editor-wrap">
        <div id="monacoDiffContainer"></div>
        <div id="monacoLoader" class="diff-loading">
          <div class="spinner"></div>
          <span>Loading Diff Viewer...</span>
        </div>
        <div id="diffFallback" style="display: none;"></div>
      </div>
    </div>
  </div>

  <script>
    const reportData = {{jsonModel}};

    let currentFilter = 'All';
    let selectedId = null;
    let currentSelectedObj = null;

    let monacoDiffEditor = null;
    let isSideBySide = true;
    let monacoReady = false;
    let fallbackActivated = false;

    let currentTheme = 'dark';
    try {
      const saved = localStorage.getItem('exploredb_report_theme');
      if (saved === 'light' || saved === 'dark') {
        currentTheme = saved;
      }
    } catch (e) {}

    function applyTheme(theme) {
      currentTheme = theme;
      document.documentElement.setAttribute('data-theme', theme);

      const sun = document.getElementById('themeIconSun');
      const moon = document.getElementById('themeIconMoon');
      const toggleBtn = document.getElementById('btnThemeToggle');

      if (theme === 'light') {
        if (sun) sun.style.display = 'none';
        if (moon) moon.style.display = 'inline';
        if (toggleBtn) toggleBtn.title = 'Switch to Dark mode';
      } else {
        if (sun) sun.style.display = 'inline';
        if (moon) moon.style.display = 'none';
        if (toggleBtn) toggleBtn.title = 'Switch to Light mode';
      }

      if (typeof monacoReady !== 'undefined' && monacoReady && typeof monaco !== 'undefined' && monaco.editor) {
        monaco.editor.setTheme(theme === 'light' ? 'vs' : 'vs-dark');
      }

      try {
        localStorage.setItem('exploredb_report_theme', theme);
      } catch (e) {}
    }

    function toggleTheme() {
      applyTheme(currentTheme === 'light' ? 'dark' : 'light');
    }

    applyTheme(currentTheme);

    const srcName = reportData.sourceDisplayName || 'Source';
    const tgtName = reportData.targetDisplayName || 'Target';
    const srcServer = reportData.sourceServer || 'localhost';
    const tgtServer = reportData.targetServer || 'localhost';

    const srcTooltip = (reportData.sourceServer && reportData.sourceServer.includes('Dacpac'))
      ? `Dacpac: ${srcName}\nType: Dacpac Package`
      : `Database: ${srcName}\nServer: ${srcServer}`;
    const tgtTooltip = (reportData.targetServer && reportData.targetServer.includes('Dacpac'))
      ? `Dacpac: ${tgtName}\nType: Dacpac Package`
      : `Database: ${tgtName}\nServer: ${tgtServer}`;

    const srcBadge = document.getElementById('sourceBadge');
    const tgtBadge = document.getElementById('targetBadge');
    if (srcBadge) srcBadge.title = srcTooltip;
    if (tgtBadge) tgtBadge.title = tgtTooltip;

    const srcDesc = document.getElementById('sourceDesc');
    const tgtDesc = document.getElementById('targetDesc');
    if (srcDesc) srcDesc.textContent = srcName;
    if (tgtDesc) tgtDesc.textContent = tgtName;

    const sDn = document.getElementById('sourceDisplayName');
    const tDn = document.getElementById('targetDisplayName');
    if (sDn) { sDn.textContent = srcName; sDn.title = srcTooltip; }
    if (tDn) { tDn.textContent = tgtName; tDn.title = tgtTooltip; }
    document.getElementById('reportDate').textContent = new Date(reportData.summary.comparisonTimestamp).toLocaleString();

    const totalDiffs = (reportData.summary && reportData.summary.totalDifferences !== undefined)
      ? reportData.summary.totalDifferences
      : (reportData.summary.addedCount + reportData.summary.deletedCount + reportData.summary.modifiedCount);

    document.getElementById('cntAll').textContent = totalDiffs;
    document.getElementById('cntAdded').textContent = reportData.summary.addedCount;
    document.getElementById('cntDeleted').textContent = reportData.summary.deletedCount;
    document.getElementById('cntModified').textContent = reportData.summary.modifiedCount;
    document.getElementById('cntIdentical').textContent = reportData.summary.identicalCount;

    if (reportData.summary && reportData.summary.identicalCount > 0) {
      const idCard = document.getElementById('cardIdentical');
      if (idCard) idCard.style.display = 'flex';
    }

    // Multi-CDN Candidate list with automatic fallback
    const cdnCandidates = [
      {
        name: 'cdnjs',
        loader: 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.45.0/min/vs/loader.min.js',
        basePath: 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.45.0/min/vs'
      },
      {
        name: 'jsdelivr',
        loader: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs/loader.js',
        basePath: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs'
      }
    ];

    let currentCdnIndex = 0;
    let watchdogTimer = null;

    function tryLoadMonaco() {
      if (currentCdnIndex >= cdnCandidates.length) {
        activateOfflineEngine('All CDNs unreachable');
        return;
      }

      const cdn = cdnCandidates[currentCdnIndex];
      const script = document.createElement('script');
      script.src = cdn.loader;
      script.async = true;

      script.onload = () => {
        if (typeof require !== 'undefined') {
          try {
            require.config({ paths: { 'vs': cdn.basePath } });
            require(['vs/editor/editor.main'], function () {
              if (fallbackActivated) return;
              clearTimeout(watchdogTimer);
              monacoReady = true;

              const loaderEl = document.getElementById('monacoLoader');
              if (loaderEl) loaderEl.style.display = 'none';

              const btnToggle = document.getElementById('btnToggleSideBySide');
              if (btnToggle) btnToggle.style.display = 'inline-flex';

              const btnPrev = document.getElementById('btnPrevDiff');
              const btnNext = document.getElementById('btnNextDiff');
              if (btnPrev) btnPrev.style.display = 'inline-flex';
              if (btnNext) btnNext.style.display = 'inline-flex';

              const container = document.getElementById('monacoDiffContainer');
              monacoDiffEditor = monaco.editor.createDiffEditor(container, {
                theme: currentTheme === 'light' ? 'vs' : 'vs-dark',
                readOnly: true,
                renderSideBySide: isSideBySide,
                automaticLayout: true,
                scrollBeyondLastLine: false,
                fontSize: 12.5,
                lineNumbers: 'on',
                renderIndicators: true,
                originalEditable: false,
                domReadOnly: true,
                folding: true,
                minimap: { enabled: false },
                renderOverviewRuler: false,
                overviewRulerLanes: 0,
                overviewRulerBorder: false,
                scrollbar: {
                  verticalScrollbarSize: 8,
                  horizontalScrollbarSize: 8,
                  verticalSliderSize: 8,
                  horizontalSliderSize: 8,
                  vertical: 'auto',
                  horizontal: 'auto',
                  useShadows: false
                }
              });

              if (currentSelectedObj) {
                updateMonacoDiff(currentSelectedObj);
              }
            }, function (err) {
              console.warn(`Monaco failed to load from ${cdn.name}, trying next candidate...`, err);
              currentCdnIndex++;
              tryLoadMonaco();
            });
          } catch (e) {
            currentCdnIndex++;
            tryLoadMonaco();
          }
        } else {
          currentCdnIndex++;
          tryLoadMonaco();
        }
      };

      script.onerror = () => {
        console.warn(`CDN script error for ${cdn.name}, trying backup CDN...`);
        currentCdnIndex++;
        tryLoadMonaco();
      };

      document.head.appendChild(script);
    }

    // 2.5-second watchdog timer: if network stalls or drops packets without explicit error
    watchdogTimer = setTimeout(() => {
      if (!monacoReady) {
        console.warn('Monaco initialization timed out (2.5s). Automatically switching to offline engine.');
        activateOfflineEngine('Timeout');
      }
    }, 2500);

    // Start attempting Monaco CDN load
    tryLoadMonaco();

    function activateOfflineEngine(reason) {
      if (fallbackActivated) return;
      fallbackActivated = true;
      clearTimeout(watchdogTimer);

      const loaderEl = document.getElementById('monacoLoader');
      if (loaderEl) loaderEl.style.display = 'none';

      const btnToggle = document.getElementById('btnToggleSideBySide');
      if (btnToggle) btnToggle.style.display = 'none';

      const btnPrev = document.getElementById('btnPrevDiff');
      const btnNext = document.getElementById('btnNextDiff');
      if (btnPrev) btnPrev.style.display = 'none';
      if (btnNext) btnNext.style.display = 'none';

      const monacoContainer = document.getElementById('monacoDiffContainer');
      if (monacoContainer) monacoContainer.style.display = 'none';

      const fallback = document.getElementById('diffFallback');
      if (fallback) fallback.style.display = 'flex';

      if (currentSelectedObj) {
        renderFallbackDiff(currentSelectedObj.sourceScript || '', currentSelectedObj.targetScript || '');
      }
    }

    function toggleSideBySide() {
      isSideBySide = !isSideBySide;
      document.getElementById('sideBySideText').textContent = isSideBySide ? 'Side-by-Side' : 'Inline';
      if (monacoReady && monacoDiffEditor) {
        monacoDiffEditor.updateOptions({ renderSideBySide: isSideBySide });
      }
    }

    let isSidebarCollapsed = false;

    function toggleSidebar() {
      isSidebarCollapsed = !isSidebarCollapsed;
      const sidebar = document.querySelector('.sidebar');
      const expandBtn = document.getElementById('btnExpandSidebar');
      if (sidebar) {
        if (isSidebarCollapsed) {
          sidebar.classList.add('collapsed');
        } else {
          sidebar.classList.remove('collapsed');
        }
      }
      if (expandBtn) {
        expandBtn.style.display = isSidebarCollapsed ? 'inline-flex' : 'none';
      }
      setTimeout(() => {
        if (monacoDiffEditor) {
          monacoDiffEditor.layout();
        }
      }, 50);
    }

    function navigateDiffBlock(direction) {
      if (!monacoReady || !monacoDiffEditor) return;

      const changes = monacoDiffEditor.getLineChanges();
      if (!changes || changes.length === 0) return;

      const origEditor = monacoDiffEditor.getOriginalEditor();
      const modEditor = monacoDiffEditor.getModifiedEditor();
      const isOrigFocused = origEditor && typeof origEditor.hasTextFocus === 'function' && origEditor.hasTextFocus();
      const activeEditor = isOrigFocused ? origEditor : modEditor;

      const currentPos = (activeEditor && typeof activeEditor.getPosition === 'function' && activeEditor.getPosition()) || { lineNumber: 1, column: 1 };
      const currentLine = currentPos.lineNumber;

      function getChangeRange(c) {
        if (isOrigFocused) {
          const start = c.originalStartLineNumber > 0 ? c.originalStartLineNumber : (c.originalEndLineNumber > 0 ? c.originalEndLineNumber : 1);
          const end = c.originalEndLineNumber > 0 ? c.originalEndLineNumber : start;
          return { start, end };
        } else {
          const start = c.modifiedStartLineNumber > 0 ? c.modifiedStartLineNumber : (c.modifiedEndLineNumber > 0 ? c.modifiedEndLineNumber : 1);
          const end = c.modifiedEndLineNumber > 0 ? c.modifiedEndLineNumber : start;
          return { start, end };
        }
      }

      let currentDiffIdx = -1;
      for (let i = 0; i < changes.length; i++) {
        const range = getChangeRange(changes[i]);
        if (currentLine >= range.start && currentLine <= range.end) {
          currentDiffIdx = i;
          break;
        }
      }

      let targetChange = null;
      if (direction === 'next') {
        if (currentDiffIdx !== -1) {
          targetChange = changes[(currentDiffIdx + 1) % changes.length];
        } else {
          for (let i = 0; i < changes.length; i++) {
            const range = getChangeRange(changes[i]);
            if (range.start > currentLine) {
              targetChange = changes[i];
              break;
            }
          }
          if (!targetChange) targetChange = changes[0];
        }
      } else {
        if (currentDiffIdx !== -1) {
          targetChange = changes[(currentDiffIdx - 1 + changes.length) % changes.length];
        } else {
          for (let i = changes.length - 1; i >= 0; i--) {
            const range = getChangeRange(changes[i]);
            if (range.end < currentLine) {
              targetChange = changes[i];
              break;
            }
          }
          if (!targetChange) targetChange = changes[changes.length - 1];
        }
      }

      if (targetChange) {
        const targetEditor = targetChange.modifiedStartLineNumber > 0 ? modEditor : origEditor;
        const targetLine = targetChange.modifiedStartLineNumber > 0 ? targetChange.modifiedStartLineNumber : targetChange.originalStartLineNumber;
        if (targetEditor) {
          const line = targetLine > 0 ? targetLine : 1;
          targetEditor.revealLineInCenter(line);
          targetEditor.setPosition({ lineNumber: line, column: 1 });
          targetEditor.focus();
        }
      }
    }

    function setFilter(type) {
      currentFilter = type;
      document.querySelectorAll('.kpi-card').forEach(el => el.classList.remove('active'));
      const activeCard = document.querySelector('.card-' + type.toLowerCase());
      if (activeCard) activeCard.classList.add('active');
      applyFilters();
    }

    function clearSearch() {
      const input = document.getElementById('searchInput');
      if (input) {
        input.value = '';
        input.focus();
        document.getElementById('clearSearchBtn').style.display = 'none';
        applyFilters();
      }
    }

    function getFilteredDifferences() {
      const qInput = document.getElementById('searchInput');
      const q = (qInput ? qInput.value : '').toLowerCase().trim();
      return reportData.differences.filter(d => {
        let matchesFilter = true;
        if (currentFilter === 'All') {
          matchesFilter = (d.hasDifference !== false);
        } else {
          matchesFilter = (d.changeType === currentFilter);
        }
        const matchesQuery = !q || d.name.toLowerCase().includes(q) || d.category.toLowerCase().includes(q);
        return matchesFilter && matchesQuery;
      });
    }

    function updateObjectNavButtons(filteredList) {
      const list = filteredList || getFilteredDifferences();
      const btnPrev = document.getElementById('btnPrevObject');
      const btnNext = document.getElementById('btnNextObject');
      if (!list || list.length <= 1) {
        if (btnPrev) btnPrev.disabled = true;
        if (btnNext) btnNext.disabled = true;
        return;
      }

      const currentIndex = list.findIndex(d => d.id === selectedId);
      if (btnPrev) btnPrev.disabled = (currentIndex <= 0);
      if (btnNext) btnNext.disabled = (currentIndex === -1 || currentIndex >= list.length - 1);
    }

    function navigateObject(delta) {
      const list = getFilteredDifferences();
      if (!list || list.length === 0) return;

      let currentIndex = list.findIndex(d => d.id === selectedId);
      if (currentIndex === -1) {
        currentIndex = delta > 0 ? -1 : list.length;
      }

      let targetIndex = currentIndex + delta;
      if (targetIndex < 0) {
        targetIndex = 0;
      } else if (targetIndex >= list.length) {
        targetIndex = list.length - 1;
      }

      if (targetIndex === currentIndex) return;

      const targetObj = list[targetIndex];
      if (targetObj) {
        selectObject(targetObj);
        const itemEl = document.querySelector(`.object-item[data-id="${targetObj.id}"]`);
        if (itemEl) {
          itemEl.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }
      }
    }

    function applyFilters() {
      const qInput = document.getElementById('searchInput');
      const q = (qInput ? qInput.value : '').toLowerCase().trim();
      const clearBtn = document.getElementById('clearSearchBtn');
      if (clearBtn) clearBtn.style.display = q ? 'flex' : 'none';

      const listEl = document.getElementById('objectList');
      listEl.innerHTML = '';

      const filtered = getFilteredDifferences();

      filtered.forEach(d => {
        const item = document.createElement('div');
        item.className = 'object-item' + (d.id === selectedId ? ' selected' : '');
        item.setAttribute('data-id', d.id);
        item.onclick = () => selectObject(d);
        item.innerHTML = `
          <div class="object-info">
            <span class="object-name" title="${d.name}">${d.name}</span>
            <span class="object-category">${d.category}</span>
          </div>
          <span class="status-tag tag-${d.changeType}">${d.changeType}</span>
        `;
        listEl.appendChild(item);
      });

      updateObjectNavButtons(filtered);

      if (filtered.length > 0) {
        if (!selectedId || !filtered.some(d => d.id === selectedId)) {
          selectObject(filtered[0]);
        }
      }
    }

    function selectObject(obj) {
      selectedId = obj.id;
      currentSelectedObj = obj;

      document.querySelectorAll('.object-item').forEach(el => el.classList.remove('selected'));
      const found = document.querySelector(`.object-item[data-id="${obj.id}"]`);
      if (found) found.classList.add('selected');
      updateObjectNavButtons();

      document.getElementById('selectedObjTitle').textContent = obj.name;
      const badge = document.getElementById('selectedObjBadge');
      badge.style.display = 'inline-block';
      badge.textContent = obj.changeType;
      badge.className = 'status-tag tag-' + obj.changeType;

      const hasDiff = obj && obj.changeType !== 'Identical' && obj.hasDifference !== false;
      const btnPrev = document.getElementById('btnPrevDiff');
      const btnNext = document.getElementById('btnNextDiff');
      if (btnPrev) {
        btnPrev.disabled = !hasDiff;
        if (!monacoReady) btnPrev.style.display = 'none';
      }
      if (btnNext) {
        btnNext.disabled = !hasDiff;
        if (!monacoReady) btnNext.style.display = 'none';
      }

      if (monacoReady && monacoDiffEditor) {
        updateMonacoDiff(obj);
      } else if (fallbackActivated) {
        renderFallbackDiff(obj.sourceScript || '', obj.targetScript || '');
      }
    }

    function updateMonacoDiff(obj) {
      const srcText = obj.sourceScript || '';
      const tgtText = obj.targetScript || '';

      const originalModel = monaco.editor.createModel(srcText, 'sql');
      const modifiedModel = monaco.editor.createModel(tgtText, 'sql');

      const oldModel = monacoDiffEditor.getModel();
      monacoDiffEditor.setModel({
        original: originalModel,
        modified: modifiedModel
      });

      if (oldModel) {
        if (oldModel.original) oldModel.original.dispose();
        if (oldModel.modified) oldModel.modified.dispose();
      }
    }

    function copySource() {
      if (!currentSelectedObj || !currentSelectedObj.sourceScript) return;
      navigator.clipboard.writeText(currentSelectedObj.sourceScript).then(() => {
        const btn = document.getElementById('copySourceBtn');
        const textSpan = document.getElementById('copySourceText');
        textSpan.textContent = 'Copied';
        btn.classList.add('copied');
        setTimeout(() => {
          textSpan.textContent = 'Copy';
          btn.classList.remove('copied');
        }, 1800);
      });
    }

    function copyTarget() {
      if (!currentSelectedObj || !currentSelectedObj.targetScript) return;
      navigator.clipboard.writeText(currentSelectedObj.targetScript).then(() => {
        const btn = document.getElementById('copyTargetBtn');
        const textSpan = document.getElementById('copyTargetText');
        textSpan.textContent = 'Copied';
        btn.classList.add('copied');
        setTimeout(() => {
          textSpan.textContent = 'Copy';
          btn.classList.remove('copied');
        }, 1800);
      });
    }

    // Inlined Myers / LCS Line Alignment Diff Engine (Matches Image 2 exactly)
    function computeLineDiff(srcLines, tgtLines) {
      const n = srcLines.length;
      const m = tgtLines.length;

      const map = new Map();
      let id = 1;
      const srcH = srcLines.map(line => {
        let h = map.get(line);
        if (!h) { h = id++; map.set(line, h); }
        return h;
      });
      const tgtH = tgtLines.map(line => {
        let h = map.get(line);
        if (!h) { h = id++; map.set(line, h); }
        return h;
      });

      if (n * m > 4000000) {
        // Safe fast-path for massive scripts
        const max = Math.max(n, m);
        const aligned = [];
        for (let i = 0; i < max; i++) {
          const s = srcLines[i];
          const t = tgtLines[i];
          if (s !== undefined && t !== undefined) {
            aligned.push(s === t ? { type: 'equal', srcLine: s, srcNum: i + 1, tgtLine: t, tgtNum: i + 1 } : { type: 'diff', srcLine: s, srcNum: i + 1, tgtLine: t, tgtNum: i + 1 });
          } else if (s !== undefined) {
            aligned.push({ type: 'del', srcLine: s, srcNum: i + 1, tgtLine: null, tgtNum: null });
          } else {
            aligned.push({ type: 'add', srcLine: null, srcNum: null, tgtLine: t, tgtNum: i + 1 });
          }
        }
        return aligned;
      }

      const dp = Array.from({ length: n + 1 }, () => new Uint16Array(m + 1));
      for (let i = 0; i < n; i++) {
        for (let j = 0; j < m; j++) {
          if (srcH[i] === tgtH[j]) {
            dp[i + 1][j + 1] = dp[i][j] + 1;
          } else {
            dp[i + 1][j + 1] = Math.max(dp[i + 1][j], dp[i][j + 1]);
          }
        }
      }

      const aligned = [];
      let i = n, j = m;
      while (i > 0 || j > 0) {
        if (i > 0 && j > 0 && srcH[i - 1] === tgtH[j - 1]) {
          aligned.push({ type: 'equal', srcLine: srcLines[i - 1], srcNum: i, tgtLine: tgtLines[j - 1], tgtNum: j });
          i--; j--;
        } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
          aligned.push({ type: 'add', srcLine: null, srcNum: null, tgtLine: tgtLines[j - 1], tgtNum: j });
          j--;
        } else {
          aligned.push({ type: 'del', srcLine: srcLines[i - 1], srcNum: i, tgtLine: null, tgtNum: null });
          i--;
        }
      }
      return aligned.reverse();
    }

    function renderFallbackDiff(sourceText, targetText) {
      const srcLines = sourceText ? sourceText.split(/\r?\n/) : [];
      const tgtLines = targetText ? targetText.split(/\r?\n/) : [];
      const aligned = computeLineDiff(srcLines, tgtLines);
      const fallback = document.getElementById('diffFallback');
      if (!fallback) return;

      if (!isSideBySide) {
        // Inline Unified View
        let inlineHtml = '';
        aligned.forEach(item => {
          if (item.type === 'equal') {
            inlineHtml += `<div class="fallback-line"><span class="fallback-num">${item.tgtNum}</span><span class="fallback-text">${highlightSql(item.tgtLine)}</span></div>`;
          } else if (item.type === 'del' || (item.type === 'diff' && item.srcLine !== null)) {
            inlineHtml += `<div class="fallback-line fallback-del"><span class="fallback-num">${item.srcNum}-</span><span class="fallback-text">${highlightSql(item.srcLine)}</span></div>`;
          } else if (item.type === 'add' || (item.type === 'diff' && item.tgtLine !== null)) {
            inlineHtml += `<div class="fallback-line fallback-add"><span class="fallback-num">${item.tgtNum}+</span><span class="fallback-text">${highlightSql(item.tgtLine)}</span></div>`;
          }
        });
        fallback.innerHTML = `<div class="fallback-pane" style="width: 100%;">${inlineHtml}</div>`;
        return;
      }

      // Side-by-Side View with Diagonal Stripe Gap Fill (Image 2 exact replica)
      let leftHtml = '';
      let rightHtml = '';

      aligned.forEach(item => {
        if (item.type === 'equal') {
          leftHtml += `<div class="fallback-line"><span class="fallback-num">${item.srcNum}</span><span class="fallback-text">${highlightSql(item.srcLine)}</span></div>`;
          rightHtml += `<div class="fallback-line"><span class="fallback-num">${item.tgtNum}</span><span class="fallback-text">${highlightSql(item.tgtLine)}</span></div>`;
        } else if (item.type === 'del') {
          leftHtml += `<div class="fallback-line fallback-del"><span class="fallback-num">${item.srcNum}-</span><span class="fallback-text">${highlightSql(item.srcLine)}</span></div>`;
          rightHtml += `<div class="fallback-line fallback-gap"><span class="fallback-num">&nbsp;</span><span class="fallback-text">&nbsp;</span></div>`;
        } else if (item.type === 'add') {
          leftHtml += `<div class="fallback-line fallback-gap"><span class="fallback-num">&nbsp;</span><span class="fallback-text">&nbsp;</span></div>`;
          rightHtml += `<div class="fallback-line fallback-add"><span class="fallback-num">${item.tgtNum}+</span><span class="fallback-text">${highlightSql(item.tgtLine)}</span></div>`;
        } else if (item.type === 'diff') {
          leftHtml += `<div class="fallback-line fallback-del"><span class="fallback-num">${item.srcNum}-</span><span class="fallback-text">${highlightSql(item.srcLine)}</span></div>`;
          rightHtml += `<div class="fallback-line fallback-add"><span class="fallback-num">${item.tgtNum}+</span><span class="fallback-text">${highlightSql(item.tgtLine)}</span></div>`;
        }
      });

      fallback.innerHTML = `
        <div class="fallback-pane" id="paneLeft" style="border-right: 1px solid var(--border);">${leftHtml}</div>
        <div class="fallback-pane" id="paneRight">${rightHtml}</div>
      `;

      // Synchronize scrolling between left and right panes
      const pLeft = document.getElementById('paneLeft');
      const pRight = document.getElementById('paneRight');
      let sync = false;
      if (pLeft && pRight) {
        pLeft.onscroll = () => { if (!sync) { sync = true; pRight.scrollTop = pLeft.scrollTop; pRight.scrollLeft = pLeft.scrollLeft; sync = false; } };
        pRight.onscroll = () => { if (!sync) { sync = true; pLeft.scrollTop = pRight.scrollTop; pLeft.scrollLeft = pRight.scrollLeft; sync = false; } };
      }
    }

    function highlightSql(text) {
      if (!text) return '';
      const escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
      return escaped
        .replace(/\b(CREATE|ALTER|DROP|TABLE|VIEW|PROCEDURE|FUNCTION|TRIGGER|INDEX|CONSTRAINT|PRIMARY|KEY|FOREIGN|REFERENCES|SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|ON|AND|OR|NOT|NULL|DEFAULT|AS|GO|BEGIN|END|IF|ELSE|RETURNS|SET)\b/gi, '<span class="kw">$1</span>')
        .replace(/\b(INT|BIGINT|SMALLINT|TINYINT|VARCHAR|NVARCHAR|CHAR|NCHAR|DATETIME|DATETIME2|DATE|TIME|DECIMAL|NUMERIC|FLOAT|BIT|UNIQUEIDENTIFIER|VARBINARY|TEXT)\b/gi, '<span class="typ">$1</span>')
        .replace(/(--.*$)/gm, '<span class="com">$1</span>');
    }

    // Initial Filter
    applyFilters();
  </script>
</body>
</html>
""";
    }

    private static string GetEndpointDisplayName(DatabaseEndpoint endpoint)
    {
        if (endpoint == null) return "Unknown";
        if (endpoint.Type == EndpointType.DacpacFile)
        {
            return !string.IsNullOrWhiteSpace(endpoint.DacpacPath)
                ? Path.GetFileName(endpoint.DacpacPath)
                : "Dacpac Package";
        }

        return !string.IsNullOrWhiteSpace(endpoint.Database)
            ? endpoint.Database
            : (endpoint.Server ?? "Live Database");
    }
}
