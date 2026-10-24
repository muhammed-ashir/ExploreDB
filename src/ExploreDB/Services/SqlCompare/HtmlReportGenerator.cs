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
      --bg: #0f172a;
      --surface: #1e293b;
      --surface-hover: #334155;
      --border: #334155;
      --text: #f8fafc;
      --text-muted: #94a3b8;
      --accent: #38bdf8;
      --added: #22c55e;
      --added-bg: rgba(34, 197, 94, 0.15);
      --deleted: #ef4444;
      --deleted-bg: rgba(239, 68, 68, 0.15);
      --modified: #f59e0b;
      --modified-bg: rgba(245, 158, 11, 0.15);
      --identical: #64748b;
      --code-bg: #0b1120;
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
      padding: 12px 24px;
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
      padding: 6px 12px;
      border-radius: 8px;
      font-size: 15px;
      letter-spacing: 0.5px;
      text-decoration: none;
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }
    .endpoint-badge {
      font-size: 13px;
      color: var(--text-muted);
      background: rgba(255,255,255,0.05);
      padding: 4px 10px;
      border-radius: 6px;
      border: 1px solid var(--border);
    }
    .stats-bar {
      display: flex;
      gap: 16px;
      background: var(--surface);
      padding: 10px 24px;
      border-bottom: 1px solid var(--border);
      align-items: center;
      flex-shrink: 0;
      font-size: 13px;
    }
    .stat-pill {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 10px;
      border-radius: 6px;
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
      padding: 6px 12px;
      border-radius: 6px;
      font-size: 13px;
      width: 240px;
      outline: none;
    }
    .search-box:focus { border-color: var(--accent); }

    .main-layout {
      display: flex;
      flex: 1;
      height: calc(100vh - 120px);
      overflow: hidden;
    }
    .sidebar {
      width: 380px;
      min-width: 260px;
      border-right: 1px solid var(--border);
      overflow-y: auto;
      background: var(--surface);
    }
    .object-item {
      padding: 10px 16px;
      border-bottom: 1px solid rgba(255,255,255,0.04);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: space-between;
      transition: background 0.1s;
    }
    .object-item:hover { background: var(--surface-hover); }
    .object-item.selected {
      background: rgba(56, 189, 248, 0.12);
      border-left: 3px solid var(--accent);
    }
    .object-info { display: flex; flex-direction: column; gap: 2px; overflow: hidden; }
    .object-name { font-weight: 500; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .object-category { font-size: 11px; color: var(--text-muted); }
    .status-tag {
      font-size: 11px;
      font-weight: 700;
      padding: 2px 8px;
      border-radius: 4px;
      text-transform: uppercase;
      flex-shrink: 0;
    }
    .tag-Added { background: var(--added-bg); color: var(--added); }
    .tag-Deleted { background: var(--deleted-bg); color: var(--deleted); }
    .tag-Modified { background: var(--modified-bg); color: var(--modified); }
    .tag-Identical { background: rgba(100, 116, 139, 0.2); color: var(--identical); }

    .diff-container {
      flex: 1;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      background: var(--code-bg);
    }
    .diff-header {
      display: flex;
      background: var(--surface);
      border-bottom: 1px solid var(--border);
      font-weight: 600;
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }
    .diff-pane-title {
      flex: 1;
      padding: 10px 16px;
      border-right: 1px solid var(--border);
      display: flex;
      justify-content: space-between;
      color: var(--text-muted);
    }
    .diff-body {
      flex: 1;
      display: flex;
      overflow: auto;
      font-family: "Cascadia Code", "Fira Code", Consolas, Menlo, monospace;
      font-size: 12px;
      line-height: 1.6;
    }
    .diff-pane {
      flex: 1;
      overflow-x: auto;
      white-space: pre;
      border-right: 1px solid var(--border);
    }
    .diff-line {
      display: flex;
      min-width: fit-content;
      padding: 0 8px;
    }
    .line-num {
      width: 44px;
      text-align: right;
      padding-right: 12px;
      color: #475569;
      user-select: none;
      flex-shrink: 0;
    }
    .line-text { flex: 1; }
    .line-add { background: var(--added-bg); color: #86efac; }
    .line-del { background: var(--deleted-bg); color: #fca5a5; }
    .line-empty { background: rgba(255,255,255,0.02); }

    .kw { color: #f472b6; font-weight: 600; }
    .typ { color: #38bdf8; }
    .str { color: #a7f3d0; }
    .com { color: #64748b; font-style: italic; }

    .empty-state {
      display: flex;
      align-items: center;
      justify-content: center;
      height: 100%;
      color: var(--text-muted);
      font-size: 14px;
    }
  </style>
</head>
<body>

  <header>
    <div class="header-title">
      <a href="https://apps.microsoft.com/store/detail/9NF6M6NT09BM?cid=DevShareMCLPCS" target="_blank" rel="noopener noreferrer" class="logo">ExploreDB</a>
      <span style="font-size: 14px; font-weight: 600;">SQL Compare Report</span>
      <div class="endpoint-badge" id="sourceDesc">Source: ...</div>
      <span style="color: var(--text-muted);">➔</span>
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
    <input type="text" id="searchInput" class="search-box" placeholder="Filter objects (e.g. Orders)..." oninput="applyFilters()">
  </div>

  <div class="main-layout">
    <div class="sidebar" id="objectList"></div>
    <div class="diff-container">
      <div class="diff-header">
        <div class="diff-pane-title"><span>Source Definition</span><span id="selectedObjSource"></span></div>
        <div class="diff-pane-title"><span>Target Definition</span><span id="selectedObjTarget"></span></div>
      </div>
      <div class="diff-body" id="diffBody">
        <div class="empty-state">Select an object from the list to view side-by-side DDL differences</div>
      </div>
    </div>
  </div>

  <script>
    const reportData = {{jsonModel}};

    let currentFilter = 'All';
    let selectedId = null;

    document.getElementById('sourceDesc').textContent = 'Source: ' + reportData.source;
    document.getElementById('targetDesc').textContent = 'Target: ' + reportData.target;
    document.getElementById('reportDate').textContent = 'Generated: ' + new Date(reportData.summary.comparisonTimestamp).toLocaleString();

    document.getElementById('cntAll').textContent = reportData.differences.length;
    document.getElementById('cntAdded').textContent = reportData.summary.addedCount;
    document.getElementById('cntDeleted').textContent = reportData.summary.deletedCount;
    document.getElementById('cntModified').textContent = reportData.summary.modifiedCount;
    document.getElementById('cntIdentical').textContent = reportData.summary.identicalCount;

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
      document.querySelectorAll('.object-item').forEach(el => el.classList.remove('selected'));
      const found = Array.from(document.querySelectorAll('.object-item')).find(el => el.innerHTML.includes(obj.name));
      if (found) found.classList.add('selected');

      document.getElementById('selectedObjSource').textContent = obj.name;
      document.getElementById('selectedObjTarget').textContent = obj.name;

      renderDiff(obj.sourceScript || '', obj.targetScript || '');
    }

    function highlightSql(text) {
      if (!text) return '';
      const escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
      return escaped
        .replace(/\b(CREATE|ALTER|DROP|TABLE|VIEW|PROCEDURE|FUNCTION|TRIGGER|INDEX|CONSTRAINT|PRIMARY|KEY|FOREIGN|REFERENCES|SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|ON|AND|OR|NOT|NULL|DEFAULT|AS|GO|BEGIN|END|IF|ELSE|RETURNS|SET)\b/gi, '<span class="kw">$1</span>')
        .replace(/\b(INT|BIGINT|SMALLINT|TINYINT|VARCHAR|NVARCHAR|CHAR|NCHAR|DATETIME|DATETIME2|DATE|TIME|DECIMAL|NUMERIC|FLOAT|BIT|UNIQUEIDENTIFIER|VARBINARY|TEXT)\b/gi, '<span class="typ">$1</span>')
        .replace(/(--.*$)/gm, '<span class="com">$1</span>');
    }

    function renderDiff(sourceText, targetText) {
      const srcLines = sourceText ? sourceText.split(/\r?\n/) : [];
      const tgtLines = targetText ? targetText.split(/\r?\n/) : [];

      const maxLines = Math.max(srcLines.length, tgtLines.length);
      const diffBody = document.getElementById('diffBody');

      let leftHtml = '';
      let rightHtml = '';

      for (let i = 0; i < maxLines; i++) {
        const s = srcLines[i];
        const t = tgtLines[i];

        const lineNum = i + 1;

        if (s !== undefined && t !== undefined) {
          if (s === t) {
            leftHtml += `<div class="diff-line"><span class="line-num">${lineNum}</span><span class="line-text">${highlightSql(s)}</span></div>`;
            rightHtml += `<div class="diff-line"><span class="line-num">${lineNum}</span><span class="line-text">${highlightSql(t)}</span></div>`;
          } else {
            leftHtml += `<div class="diff-line line-del"><span class="line-num">${lineNum}</span><span class="line-text">${highlightSql(s)}</span></div>`;
            rightHtml += `<div class="diff-line line-add"><span class="line-num">${lineNum}</span><span class="line-text">${highlightSql(t)}</span></div>`;
          }
        } else if (s !== undefined) {
          leftHtml += `<div class="diff-line line-add"><span class="line-num">${lineNum}</span><span class="line-text">${highlightSql(s)}</span></div>`;
          rightHtml += `<div class="diff-line line-empty"><span class="line-num">&nbsp;</span><span class="line-text">&nbsp;</span></div>`;
        } else if (t !== undefined) {
          leftHtml += `<div class="diff-line line-empty"><span class="line-num">&nbsp;</span><span class="line-text">&nbsp;</span></div>`;
          rightHtml += `<div class="diff-line line-del"><span class="line-num">${lineNum}</span><span class="line-text">${highlightSql(t)}</span></div>`;
        }
      }

      diffBody.innerHTML = `
        <div class="diff-pane" id="paneLeft">${leftHtml}</div>
        <div class="diff-pane" id="paneRight">${rightHtml}</div>
      `;

      // Synchronize horizontal/vertical scrolling
      const pLeft = document.getElementById('paneLeft');
      const pRight = document.getElementById('paneRight');
      let syncLock = false;
      pLeft.onscroll = () => {
        if (!syncLock) {
          syncLock = true;
          pRight.scrollTop = pLeft.scrollTop;
          pRight.scrollLeft = pLeft.scrollLeft;
          syncLock = false;
        }
      };
      pRight.onscroll = () => {
        if (!syncLock) {
          syncLock = true;
          pLeft.scrollTop = pRight.scrollTop;
          pLeft.scrollLeft = pRight.scrollLeft;
          syncLock = false;
        }
      };
    }

    // Initialize
    applyFilters();
  </script>
</body>
</html>
""";
    }
}
