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
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
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
    .header-title { display: flex; align-items: center; gap: 12px; }
    .logo {
      background: linear-gradient(135deg, #0ea5e9, #6366f1);
      color: white;
      font-weight: 800;
      padding: 5px 11px;
      border-radius: 6px;
      font-size: 14px;
      letter-spacing: 0.5px;
      text-decoration: none;
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }
    .endpoint-badge {
      font-size: 12px;
      color: var(--text-muted);
      background: rgba(255,255,255,0.04);
      padding: 3px 8px;
      border-radius: 5px;
      border: 1px solid var(--border);
      max-width: 260px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .stats-bar {
      display: flex;
      gap: 12px;
      background: var(--surface-secondary);
      padding: 8px 20px;
      border-bottom: 1px solid var(--border);
      align-items: center;
      flex-shrink: 0;
      font-size: 12px;
      flex-wrap: wrap;
    }
    .stat-pill {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 10px;
      border-radius: 5px;
      font-weight: 600;
      cursor: pointer;
      user-select: none;
      transition: all 0.15s;
    }
    .stat-pill.active { outline: 2px solid var(--accent); }
    .pill-all { background: var(--surface-hover); color: var(--text); }
    .pill-added { background: var(--added-bg); color: var(--added); }
    .pill-deleted { background: var(--deleted-bg); color: var(--deleted); }
    .pill-modified { background: var(--modified-bg); color: var(--modified); }
    .pill-identical { background: rgba(100, 116, 139, 0.2); color: var(--identical); }

    .search-box {
      margin-left: auto;
      background: var(--bg);
      border: 1px solid var(--border);
      color: var(--text);
      padding: 5px 12px;
      border-radius: 5px;
      font-size: 12px;
      width: 220px;
      outline: none;
    }
    .search-box:focus { border-color: var(--accent); }

    .main-layout {
      display: flex;
      flex: 1;
      height: calc(100vh - 98px);
      overflow: hidden;
    }
    .sidebar {
      width: 360px;
      min-width: 260px;
      border-right: 1px solid var(--border);
      overflow-y: auto;
      background: var(--surface);
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
      background: #1e1e1e;
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
      color: #f8fafc;
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
    .engine-badge {
      font-size: 10.5px;
      padding: 2px 7px;
      border-radius: 4px;
      font-weight: 600;
      letter-spacing: 0.3px;
      user-select: none;
    }
    .engine-monaco {
      background: rgba(13, 110, 253, 0.15);
      color: #38bdf8;
      border: 1px solid rgba(56, 189, 248, 0.3);
    }
    .engine-offline {
      background: rgba(245, 158, 11, 0.15);
      color: #f59e0b;
      border: 1px solid rgba(245, 158, 11, 0.3);
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
    .endpoint-title {
      font-weight: 600;
      color: #f8fafc;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      font-size: 12.5px;
    }
    .copy-btn {
      background: transparent;
      border: none;
      color: #94a3b8;
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
      color: #f8fafc;
      background: rgba(255, 255, 255, 0.08);
    }
    .copy-btn.copied {
      color: #22c55e;
    }

    /* Row 3: Monaco & Fallback Diff Editor Wrapper */
    .diff-editor-wrap {
      flex: 1;
      position: relative;
      overflow: hidden;
      background: #1e1e1e;
    }
    #monacoDiffContainer {
      width: 100%;
      height: 100%;
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
      background: #1e1e1e;
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
      background: #1e1e1e;
    }
    .fallback-pane {
      flex: 1;
      overflow: auto;
      white-space: pre;
      font-family: "Cascadia Code", Consolas, Menlo, monospace;
      font-size: 12px;
      line-height: 1.6;
      background: #1e1e1e;
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
  </style>
</head>
<body>

  <header>
    <div class="header-title">
      <a href="https://apps.microsoft.com/store/detail/9NF6M6NT09BM?cid=DevShareMCLPCS" target="_blank" rel="noopener noreferrer" class="logo">ExploreDB</a>
      <span style="font-size: 13.5px; font-weight: 600;">SQL Compare Report</span>
      <div class="endpoint-badge" id="sourceDesc">Source: ...</div>
      <span style="color: var(--text-muted); font-size: 12px;">➔</span>
      <div class="endpoint-badge" id="targetDesc">Target: ...</div>
    </div>
    <div style="font-size: 12px; color: var(--text-muted);" id="reportDate">Generated: ...</div>
  </header>

  <div class="stats-bar">
    <div class="stat-pill pill-all active" onclick="setFilter('All')">All: <span id="cntAll">0</span></div>
    <div class="stat-pill pill-added" onclick="setFilter('Added')">+ Added: <span id="cntAdded">0</span></div>
    <div class="stat-pill pill-deleted" onclick="setFilter('Deleted')">- Deleted: <span id="cntDeleted">0</span></div>
    <div class="stat-pill pill-modified" onclick="setFilter('Modified')">~ Modified: <span id="cntModified">0</span></div>
    <div class="stat-pill pill-identical" onclick="setFilter('Identical')">= Identical: <span id="cntIdentical">0</span></div>
    <input type="text" id="searchInput" class="search-box" placeholder="Filter objects..." oninput="applyFilters()">
  </div>

  <div class="main-layout">
    <div class="sidebar" id="objectList"></div>

    <div class="diff-container">
      <!-- Toolbar with Object Title, Badge & View Mode Toggle -->
      <div class="diff-toolbar">
        <div class="diff-toolbar-left">
          <span class="diff-object-title" id="selectedObjTitle">Select an object</span>
          <span class="status-tag" id="selectedObjBadge" style="display: none;"></span>
        </div>
        <div class="diff-toolbar-actions">
          <span class="engine-badge engine-monaco" id="engineBadge">Loading Engine...</span>
          <button type="button" class="btn-tool" id="btnToggleSideBySide" onclick="toggleSideBySide()" title="Toggle Side-by-Side / Inline Diff">
            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
              <path d="M0 3a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V3zm8.5 11V2H14a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H8.5zM7.5 2H2a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h5.5V2z"/>
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
          <span>Connecting to Monaco Diff Engine...</span>
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

    document.getElementById('sourceDesc').textContent = 'Source: ' + reportData.source;
    document.getElementById('targetDesc').textContent = 'Target: ' + reportData.target;
    document.getElementById('sourceDisplayName').textContent = reportData.sourceDisplayName || 'Source';
    document.getElementById('targetDisplayName').textContent = reportData.targetDisplayName || 'Target';
    document.getElementById('reportDate').textContent = 'Generated: ' + new Date(reportData.summary.comparisonTimestamp).toLocaleString();

    document.getElementById('cntAll').textContent = reportData.differences.length;
    document.getElementById('cntAdded').textContent = reportData.summary.addedCount;
    document.getElementById('cntDeleted').textContent = reportData.summary.deletedCount;
    document.getElementById('cntModified').textContent = reportData.summary.modifiedCount;
    document.getElementById('cntIdentical').textContent = reportData.summary.identicalCount;

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

              const badge = document.getElementById('engineBadge');
              if (badge) {
                badge.textContent = 'Monaco Engine';
                badge.className = 'engine-badge engine-monaco';
                badge.title = 'Powered by Monaco Diff Engine (' + cdn.name + ')';
              }

              const container = document.getElementById('monacoDiffContainer');
              monacoDiffEditor = monaco.editor.createDiffEditor(container, {
                theme: 'vs-dark',
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
                scrollbar: {
                  verticalScrollbarSize: 10,
                  horizontalScrollbarSize: 10
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

      const monacoContainer = document.getElementById('monacoDiffContainer');
      if (monacoContainer) monacoContainer.style.display = 'none';

      const fallback = document.getElementById('diffFallback');
      if (fallback) fallback.style.display = 'flex';

      const badge = document.getElementById('engineBadge');
      if (badge) {
        badge.textContent = 'Offline Engine';
        badge.className = 'engine-badge engine-offline';
        badge.title = 'Offline mode: Monaco CDN unreachable or timed out (' + reason + ')';
      }

      if (currentSelectedObj) {
        renderFallbackDiff(currentSelectedObj.sourceScript || '', currentSelectedObj.targetScript || '');
      }
    }

    function toggleSideBySide() {
      isSideBySide = !isSideBySide;
      document.getElementById('sideBySideText').textContent = isSideBySide ? 'Side-by-Side' : 'Inline';
      if (monacoReady && monacoDiffEditor) {
        monacoDiffEditor.updateOptions({ renderSideBySide: isSideBySide });
      } else if (fallbackActivated && currentSelectedObj) {
        renderFallbackDiff(currentSelectedObj.sourceScript || '', currentSelectedObj.targetScript || '');
      }
    }

    function setFilter(type) {
      currentFilter = type;
      document.querySelectorAll('.stat-pill').forEach(el => el.classList.remove('active'));
      const activePill = document.querySelector('.pill-' + type.toLowerCase());
      if (activePill) activePill.classList.add('active');
      applyFilters();
    }

    function applyFilters() {
      const q = document.getElementById('searchInput').value.toLowerCase();
      const listEl = document.getElementById('objectList');
      listEl.innerHTML = '';

      const filtered = reportData.differences.filter(d => {
        const matchesFilter = (currentFilter === 'All') || (d.changeType === currentFilter);
        const matchesQuery = !q || d.name.toLowerCase().includes(q) || d.category.toLowerCase().includes(q);
        return matchesFilter && matchesQuery;
      });

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

      if (filtered.length > 0 && !selectedId) {
        selectObject(filtered[0]);
      }
    }

    function selectObject(obj) {
      selectedId = obj.id;
      currentSelectedObj = obj;

      document.querySelectorAll('.object-item').forEach(el => el.classList.remove('selected'));
      const found = document.querySelector(`.object-item[data-id="${obj.id}"]`);
      if (found) found.classList.add('selected');

      document.getElementById('selectedObjTitle').textContent = obj.name;
      const badge = document.getElementById('selectedObjBadge');
      badge.style.display = 'inline-block';
      badge.textContent = obj.changeType;
      badge.className = 'status-tag tag-' + obj.changeType;

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
