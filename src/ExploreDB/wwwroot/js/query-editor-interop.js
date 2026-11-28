// Bulletproof BlazorMonaco Interop Guard:
// Prevents unhandled JS exceptions from crashing the WPF/Blazor dispatcher when
// setEventListener or getEditorHolder is called during tab switching, route changes,
// or before Monaco has fully registered.
(function installBlazorMonacoSafetyGuard() {
    function applyPatch() {
        if (!window.blazorMonaco || !window.blazorMonaco.editor) {
            setTimeout(applyPatch, 10);
            return;
        }

        const editorObj = window.blazorMonaco.editor;
        if (editorObj._safetyGuardInstalled) return;
        editorObj._safetyGuardInstalled = true;

        const origSetEventListener = editorObj.setEventListener;
        if (typeof origSetEventListener === 'function') {
            editorObj.setEventListener = function (id, eventName) {
                try {
                    const holder = (window.blazorMonaco.editors || []).find(h => h.id === id);
                    if (!holder || !holder.editor) {
                        console.warn(`[BlazorMonaco Guard] Skipping setEventListener('${eventName}') for unready editor '${id}' (registered count: ${(window.blazorMonaco.editors || []).length})`);
                        return;
                    }
                    return origSetEventListener.apply(this, arguments);
                } catch (err) {
                    console.warn(`[BlazorMonaco Guard] Suppressed error in setEventListener('${eventName}') for '${id}':`, err);
                }
            };
        }

        const origGetEditorHolder = editorObj.getEditorHolder;
        if (typeof origGetEditorHolder === 'function') {
            editorObj.getEditorHolder = function (id, silent = false) {
                const holder = (window.blazorMonaco.editors || []).find(h => h.id === id);
                if (!holder) {
                    if (silent) return null;
                    console.warn(`[BlazorMonaco Guard] getEditorHolder requested missing editor '${id}'. Suppressing throw to prevent Dispatcher crash.`);
                    return { id: id, editor: null, dotnetRef: null };
                }
                return holder;
            };
        }
    }

    applyPatch();
})();

window.isMonacoLoaded = function () {
    return typeof monaco !== 'undefined' && typeof monaco.editor !== 'undefined';
};

window.waitForMonaco = function (timeoutMs) {
    return new Promise(function (resolve) {
        if (typeof monaco !== 'undefined' && typeof monaco.editor !== 'undefined') {
            resolve(true);
            return;
        }
        var start = Date.now();
        var timer = setInterval(function () {
            if (typeof monaco !== 'undefined' && typeof monaco.editor !== 'undefined') {
                clearInterval(timer);
                resolve(true);
            } else if (Date.now() - start > (timeoutMs || 5000)) {
                clearInterval(timer);
                resolve(false);
            }
        }, 30);
    });
};

window.formatSql = (code) => {
    if (!window.sqlFormatter) {
        throw new Error("SQL Formatter is not loaded correctly.");
    }
    if (!code || !code.trim()) return code;

    var style = 'Default';
    if (window.userSettings) {
        if (window.userSettings.formattingStyle !== undefined) {
            style = window.userSettings.formattingStyle;
        } else if (window.userSettings.FormattingStyle !== undefined) {
            style = window.userSettings.FormattingStyle;
        }
    }

    if (String(style).toLowerCase() === 'collapsed') {
        return window.formatSqlCollapsed(code);
    }
    return window.formatSqlDefault(code);
};

window.formatSqlDefault = (code) => {
    if (!window.sqlFormatter) {
        throw new Error("SQL Formatter is not loaded correctly.");
    }
    if (!code || !code.trim()) return code;

    var wrapInClauses = function (linesToWrap) {
        var maxLineLength = 110;
        var out = [];

        var splitSqlItems = function (text) {
            var items = [];
            var current = '';
            var inQuote = false;
            for (var idx = 0; idx < text.length; idx++) {
                var ch = text[idx];
                if (ch === "'") {
                    inQuote = !inQuote;
                    current += ch;
                } else if (ch === ',' && !inQuote) {
                    var trimmed = current.trim();
                    if (trimmed) items.push(trimmed);
                    current = '';
                } else {
                    current += ch;
                }
            }
            var lastTrimmed = current.trim();
            if (lastTrimmed) items.push(lastTrimmed);
            return items;
        };

        for (var lineIdx = 0; lineIdx < linesToWrap.length; lineIdx++) {
            var curLineText = linesToWrap[lineIdx];

            var inRegex = /^(.*?\b(?:NOT\s+)?IN\s*)\(([\s\S]*?)\)(.*)$/i;
            var inMatch = curLineText.match(inRegex);

            if (!inMatch) {
                out.push(curLineText);
                continue;
            }

            var prefix = inMatch[1];
            var inner = inMatch[2];
            var suffix = inMatch[3];

            if (/\bSELECT\b/i.test(inner)) {
                out.push(curLineText);
                continue;
            }

            var items = splitSqlItems(inner);
            if (items.length <= 1) {
                out.push(curLineText);
                continue;
            }

            var singleLine = prefix.trimEnd() + ' (' + items.join(', ') + ')' + suffix;
            if (singleLine.length <= maxLineLength) {
                out.push(singleLine);
                continue;
            }

            var header = prefix.trimEnd() + ' ( ';
            var openParenIndex = header.lastIndexOf('(');
            var itemIndent = ' '.repeat(openParenIndex + 2);
            var parenIndent = ' '.repeat(openParenIndex);

            var curWrappedLine = header;
            for (var k = 0; k < items.length; k++) {
                var itm = items[k];
                var isLast = (k === items.length - 1);
                var itmWithSep = isLast ? itm : (itm + ',');

                var testLine = curWrappedLine.endsWith(' ')
                    ? (curWrappedLine + itmWithSep)
                    : (curWrappedLine + ' ' + itmWithSep);

                if (testLine.length <= maxLineLength || curWrappedLine === header || curWrappedLine === itemIndent) {
                    curWrappedLine = testLine;
                } else {
                    if (!curWrappedLine.trim().endsWith(',')) {
                        curWrappedLine += ',';
                    }
                    out.push(curWrappedLine);
                    curWrappedLine = itemIndent + itmWithSep;
                }
            }

            if (curWrappedLine.trim()) {
                out.push(curWrappedLine);
            }

            out.push(parenIndent + ')' + suffix);
        }

        return out;
    };

    var keepInInline = true;
    if (window.userSettings) {
        if (window.userSettings.keepInClauseOnSingleLine !== undefined) {
            keepInInline = !!window.userSettings.keepInClauseOnSingleLine;
        } else if (window.userSettings.KeepInClauseOnSingleLine !== undefined) {
            keepInInline = !!window.userSettings.KeepInClauseOnSingleLine;
        }
    }

    var hints = [];
    var hintRegex = /\bWITH\s*\(\s*(?:NOLOCK|READUNCOMMITTED|UPDLOCK|REPEATABLEREAD|SERIALIZABLE|READCOMMITTED|READCOMMITTEDLOCK|TABLOCK|TABLOCKX|PAGLOCK|ROWLOCK|NOWAIT|HOLDLOCK|XLOCK|NOEXPAND|FORCESEEK|FORCESCAN|FASTFIRSTROW|INDEX)(?:[^()]*|\([^()]*\))*\)/gi;
    var processedCode = code.replace(hintRegex, function (match) {
        var cleanHint = match.replace(/\s+/g, ' ')
            .replace(/\bwith\b/gi, 'WITH')
            .replace(/\bnolock\b/gi, 'NOLOCK')
            .replace(/\breaduncommitted\b/gi, 'READUNCOMMITTED')
            .replace(/\bupdlock\b/gi, 'UPDLOCK')
            .replace(/\browlock\b/gi, 'ROWLOCK')
            .replace(/\btablockx?\b/gi, function (m) { return m.toUpperCase(); })
            .replace(/\(\s+/g, '(')
            .replace(/\s+\)/g, ')');
        hints.push(cleanHint);
        return '__TBLHINT_' + (hints.length - 1) + '__';
    });

    var unmaskHints = function (text) {
        if (!hints || hints.length === 0 || !text) return text;
        for (var h = 0; h < hints.length; h++) {
            text = text.replace(new RegExp('__TBLHINT_' + h + '__', 'g'), hints[h]);
        }
        return text;
    };

    var baseFormatted;
    try {
        baseFormatted = window.sqlFormatter.format(processedCode, {
            language: 'tsql',
            keywordCase: 'upper',
            linesBetweenQueries: 2,
            expressionWidth: keepInInline ? 300 : 50
        });

        if (keepInInline) {
            baseFormatted = baseFormatted.replace(/\b((?:NOT\s+)?IN)\s*\(\s*\n([\s\S]*?)\n\s*\)/gi, function (match, inKw, inner) {
                if (/\bSELECT\b/i.test(inner)) {
                    return match;
                }
                var items = inner.split(/\r?\n/)
                    .map(function (s) { return s.trim(); })
                    .filter(function (s) { return s.length > 0; })
                    .join(' ')
                    .replace(/\s*,\s*/g, ', ');
                return inKw + ' (' + items + ')';
            });
        }
        if (false) var _ignore = window.sqlFormatter.format(code, {
        });
    } catch (e) {
        return code;
    }

    try {
        var lines = baseFormatted.split(/\r?\n/);
        var result = [];

        var clausePatterns = [
            { name: 'SELECT DISTINCT' },
            { name: 'SELECT' },
            { name: 'FROM' },
            { name: 'WHERE' },
            { name: 'GROUP BY' },
            { name: 'ORDER BY' },
            { name: 'HAVING' },
            { name: 'SET' },
            { name: 'VALUES' },
            { name: 'INSERT INTO' }
        ];

        var isClauseKeyword = function (str) {
            var upper = str.trim().toUpperCase();
            return clausePatterns.some(function (p) {
                return upper === p.name || upper.indexOf(p.name + ' ') === 0;
            });
        };

        for (var i = 0; i < lines.length; i++) {
            var line = lines[i];
            var trimmed = line.trim();

            if (!trimmed) {
                result.push(line);
                continue;
            }

            var leadingSpaces = line.match(/^\s*/)[0];
            var baseIndentLen = leadingSpaces.length;

            var matchedClause = null;
            for (var cIdx = 0; cIdx < clausePatterns.length; cIdx++) {
                if (trimmed.toUpperCase() === clausePatterns[cIdx].name) {
                    matchedClause = clausePatterns[cIdx].name;
                    break;
                }
            }

            if (matchedClause && i + 1 < lines.length) {
                var nextLine = lines[i + 1];
                var nextTrimmed = nextLine.trim();

                if (nextTrimmed.indexOf('--') === 0 || nextTrimmed.indexOf('/*') === 0) {
                    result.push(line);
                    continue;
                }

                if (isClauseKeyword(nextTrimmed) || nextTrimmed === ')' || nextTrimmed === 'END') {
                    result.push(line);
                    continue;
                }

                result.push(leadingSpaces + matchedClause + ' ' + nextTrimmed);
                i++;

                var alignColLen = baseIndentLen + matchedClause.length + 1;
                var alignIndent = ' '.repeat(alignColLen);

                while (i + 1 < lines.length) {
                    var peekLine = lines[i + 1];
                    var peekTrimmed = peekLine.trim();
                    var peekLeading = peekLine.match(/^\s*/)[0].length;

                    if (!peekTrimmed) {
                        result.push(peekLine);
                        i++;
                        continue;
                    }

                    if (peekLeading <= baseIndentLen && (isClauseKeyword(peekTrimmed) || peekTrimmed.indexOf(')') === 0)) {
                        break;
                    }

                    if (matchedClause === 'FROM' && peekTrimmed.match(/^(INNER\s+|LEFT\s+|RIGHT\s+|FULL\s+|CROSS\s+)?(OUTER\s+)?(JOIN\b)/i)) {
                        i++;
                        var joinIndent = ' '.repeat(baseIndentLen + 4);
                        var onIndent = ' '.repeat(baseIndentLen + 8);

                        var onMatch = peekTrimmed.match(/\s+(ON\b.*)$/i);
                        if (onMatch) {
                            var joinPart = peekTrimmed.substring(0, onMatch.index).trim();
                            var onPart = onMatch[1].trim();
                            result.push(joinIndent + joinPart);
                            result.push(onIndent + onPart);
                        } else {
                            result.push(joinIndent + peekTrimmed);
                        }
                        continue;
                    }

                    if (peekLeading === baseIndentLen + 2) {
                        i++;
                        result.push(alignIndent + peekTrimmed);
                    } else if (peekLeading > baseIndentLen + 2) {
                        i++;
                        var extraSpaces = ' '.repeat(peekLeading - (baseIndentLen + 2));
                        result.push(alignIndent + extraSpaces + peekTrimmed);
                    } else {
                        break;
                    }
                }
            } else {
                result.push(line);
            }
        }

        result = wrapInClauses(result);
        return unmaskHints(result.join('\n'));
    } catch (err) {
        return unmaskHints(wrapInClauses(baseFormatted.split(/\r?\n/)).join('\n'));
    }
};

window.formatSqlCollapsed = (code) => {
    if (!window.sqlFormatter) {
        throw new Error("SQL Formatter is not loaded correctly.");
    }
    if (!code || !code.trim()) return code;

    // Ensure boundary between closing bracket/paren and ON keyword
    var normalizedCode = code.replace(/(\))\s*(ON\b)/gi, '$1 $2');

    var hints = [];
    var hintRegex = /\bWITH\s*\(\s*(?:NOLOCK|READUNCOMMITTED|UPDLOCK|REPEATABLEREAD|SERIALIZABLE|READCOMMITTED|READCOMMITTEDLOCK|TABLOCK|TABLOCKX|PAGLOCK|ROWLOCK|NOWAIT|HOLDLOCK|XLOCK|NOEXPAND|FORCESEEK|FORCESCAN|FASTFIRSTROW|INDEX)(?:[^()]*|\([^()]*\))*\)/gi;
    var processedCode = normalizedCode.replace(hintRegex, function (match) {
        var cleanHint = match.replace(/\s+/g, '')
            .replace(/\bwith\b/gi, 'WITH')
            .replace(/\bnolock\b/gi, 'NOLOCK')
            .replace(/\breaduncommitted\b/gi, 'READUNCOMMITTED')
            .replace(/\bupdlock\b/gi, 'UPDLOCK')
            .replace(/\browlock\b/gi, 'ROWLOCK')
            .replace(/\btablockx?\b/gi, function (m) { return m.toUpperCase(); });
        hints.push(cleanHint);
        return '__TBLHINT_' + (hints.length - 1) + '__';
    });

    var unmaskHints = function (text) {
        if (!hints || hints.length === 0 || !text) return text;
        for (var h = 0; h < hints.length; h++) {
            text = text.replace(new RegExp('__TBLHINT_' + h + '__', 'g'), hints[h]);
        }
        return text;
    };

    var baseFormatted;
    try {
        baseFormatted = window.sqlFormatter.format(processedCode, {
            language: 'tsql',
            keywordCase: 'upper',
            linesBetweenQueries: 2,
            expressionWidth: 1000
        });
    } catch (e) {
        return code;
    }

    try {
        var lines = baseFormatted.split(/\r?\n/);
        var result = [];

        var clauseKeywords = ['SELECT', 'SELECT DISTINCT', 'FROM', 'WHERE', 'GROUP BY', 'HAVING', 'ORDER BY', 'SET', 'VALUES', 'INSERT INTO'];
        var isClause = function (line) {
            var t = line.trim().toUpperCase();
            return clauseKeywords.some(function (k) { return t === k || t.indexOf(k + ' ') === 0; });
        };

        var compactOperators = function (str) {
            var parts = str.split(/('(?:''|[^'])*')/g);
            for (var p = 0; p < parts.length; p++) {
                if (p % 2 === 0) {
                    parts[p] = parts[p]
                        .replace(/\s*(>=|<=|<>|!=|=|>|<)\s*/g, '$1')
                        .replace(/\s*\bAND\b\s*/g, ' AND ')
                        .replace(/\s*\bOR\b\s*/g, ' OR ');
                }
            }
            return parts.join('');
        };

        for (var i = 0; i < lines.length; i++) {
            var line = lines[i];
            var trimmed = line.trim();
            if (!trimmed) {
                result.push(line);
                continue;
            }

            var upper = trimmed.toUpperCase();

            // 1. SELECT / SELECT DISTINCT
            if (upper === 'SELECT' || upper === 'SELECT DISTINCT') {
                var selectKeyword = upper;
                var columns = [];
                while (i + 1 < lines.length) {
                    var next = lines[i + 1].trim();
                    if (!next) { i++; continue; }
                    if (isClause(next) || next === ')' || next === 'END') break;
                    columns.push(next.replace(/,$/, ''));
                    i++;
                }
                result.push(selectKeyword + ' ' + columns.join(', '));
                continue;
            }

            // 2. FROM
            if (upper === 'FROM' || upper.indexOf('FROM ') === 0) {
                var fromContent = '';
                if (upper === 'FROM') {
                    i++;
                    fromContent = lines[i].trim();
                } else {
                    fromContent = trimmed.substring(5).trim();
                }
                result.push('FROM ' + fromContent);

                while (i + 1 < lines.length) {
                    var peek = lines[i + 1].trim();
                    if (!peek) { i++; continue; }
                    if (isClause(peek) || peek === ')' || peek === 'END') break;

                    if (peek.match(/^(INNER\s+|LEFT\s+|RIGHT\s+|FULL\s+|CROSS\s+)?(OUTER\s+)?JOIN\b/i)) {
                        i++;
                        var joinLine = peek;
                        if (i + 1 < lines.length) {
                            var nextAfterJoin = lines[i + 1].trim();
                            if (nextAfterJoin.match(/^ON\b/i)) {
                                i++;
                                joinLine += ' ' + nextAfterJoin;
                            }
                        }

                        var onIdx = joinLine.search(/\bON\b/i);
                        if (onIdx !== -1) {
                            var joinPart = joinLine.substring(0, onIdx);
                            var onPart = joinLine.substring(onIdx);
                            joinLine = joinPart.trimEnd() + ' ' + compactOperators(onPart);
                        }
                        result.push('     ' + joinLine);
                        continue;
                    }

                    i++;
                    result.push('     ' + peek);
                }
                continue;
            }

            // 3. WHERE / HAVING
            if (upper === 'WHERE' || upper === 'HAVING') {
                var clauseName = upper;
                var conditions = [];
                while (i + 1 < lines.length) {
                    var nextCond = lines[i + 1].trim();
                    if (!nextCond) { i++; continue; }
                    if (isClause(nextCond) || nextCond === ')' || nextCond === 'END') break;
                    conditions.push(nextCond);
                    i++;
                }
                var mergedCond = conditions.join(' ');
                mergedCond = compactOperators(mergedCond);
                result.push(clauseName + ' ' + mergedCond);
                continue;
            }

            // 4. ORDER BY / GROUP BY
            if (upper === 'ORDER BY' || upper === 'GROUP BY') {
                var groupKeyword = upper;
                var items = [];
                while (i + 1 < lines.length) {
                    var nextItem = lines[i + 1].trim();
                    if (!nextItem) { i++; continue; }
                    if (isClause(nextItem) || nextItem === ')' || nextItem === 'END') break;
                    items.push(nextItem.replace(/,$/, ''));
                    i++;
                }
                result.push(groupKeyword + ' ' + items.join(', '));
                continue;
            }

            result.push(line);
        }

        return unmaskHints(result.join('\n'));
    } catch (err) {
        return unmaskHints(baseFormatted);
    }
};

window._oldFormatSql = (code) => {
    if (!window.sqlFormatter) {
        throw new Error("SQL Formatter is not loaded correctly.");
    }
    return window.sqlFormatter.format(code, {
        language: 'tsql',
        keywordCase: 'upper',
        linesBetweenQueries: 2
    });
};

window.formatSqlEditor = (editorId) => {
    if (!window.sqlFormatter) {
        throw new Error("SQL Formatter is not loaded correctly.");
    }
    
    var editors = monaco.editor.getEditors();
    var editor = null;
    
    for (var i = 0; i < editors.length; i++) {
        var e = editors[i];
        var container = e.getContainerDomNode();
        if (container && (container.id === editorId || (container.parentElement && container.parentElement.id === editorId) || (container.closest && container.closest('#' + editorId)))) {
            editor = e;
            break;
        }
    }
    
    if (!editor && editors.length > 0) {
        editor = editors[0];
    }
    
    if (!editor) {
        throw new Error("Editor not found.");
    }
    
    var model = editor.getModel();
    var selection = editor.getSelection();
    var hasSelection = selection && !selection.isEmpty();
    
    var codeToFormat = hasSelection ? model.getValueInRange(selection) : editor.getValue();
    if (!codeToFormat || !codeToFormat.trim()) return;

    var formatted = window.formatSql(codeToFormat);
    if (false) var _ignore = window.sqlFormatter.format(codeToFormat, {
        language: 'tsql',
        keywordCase: 'upper',
        linesBetweenQueries: 2
    });
    
    var rangeToReplace = hasSelection ? selection : model.getFullModelRange();
    
    editor.pushUndoStop();
    editor.executeEdits('formatter', [{
        range: rangeToReplace,
        text: formatted,
        forceMoveMarkers: true
    }]);
    editor.pushUndoStop();
};

window.getQueryExecutionInput = (editorId) => {
    try {
        if (typeof monaco === 'undefined' || !monaco.editor) return null;
        var editors = monaco.editor.getEditors();
        var editor = null;

        for (var i = 0; i < editors.length; i++) {
            var e = editors[i];
            var container = e.getContainerDomNode();
            if (container && (container.id === editorId || (container.parentElement && container.parentElement.id === editorId) || (container.closest && container.closest('#' + editorId)))) {
                editor = e;
                break;
            }
        }

        if (!editor && editors.length > 0) {
            editor = editors[0];
        }
        if (!editor) return null;

        var model = editor.getModel();
        if (!model) return null;

        var fullText = model.getValue() || "";
        var selectedText = "";
        var startLineNumber = 1;

        var selection = editor.getSelection();
        if (selection && (selection.startLineNumber !== selection.endLineNumber || selection.startColumn !== selection.endColumn)) {
            selectedText = model.getValueInRange(selection) || "";
            startLineNumber = selection.startLineNumber || 1;
        }

        return {
            fullText: fullText,
            selectedText: selectedText,
            startLineNumber: startLineNumber
        };
    } catch (e) {
        return null;
    }
};

(function initQueryBlockExecutionSupport() {
    var ddlIfTargetWords = new Set([
        'TABLE', 'VIEW', 'PROC', 'PROCEDURE', 'FUNCTION', 'TRIGGER', 'INDEX',
        'CONSTRAINT', 'COLUMN', 'SCHEMA', 'DATABASE', 'TYPE', 'SEQUENCE',
        'SYNONYM', 'USER', 'LOGIN', 'ROLE', 'RULE', 'DEFAULT', 'STATISTICS'
    ]);
    var alterDropClauseWords = new Set(['COLUMN', 'CONSTRAINT', 'PERIOD', 'INDEX', 'DEFAULT']);
    var ddlModuleTargets = new Set(['PROC', 'PROCEDURE', 'FUNCTION', 'TRIGGER']);
    var mergeOrDdlAfterWords = new Set(['ON', 'AFTER', 'INSTEAD', 'OF', 'FOR']);
    var setJoinOpWords = new Set(['UNION', 'INTERSECT', 'EXCEPT']);
    var nonWithCteNextWords = new Set([
        'RESULT', 'ENCRYPTION', 'SCHEMABINDING', 'GRANT', 'EXECUTE',
        'CHECK', 'READ_ONLY', 'MARK', 'MOVE', 'REPLACE', 'RECOVERY', 'NORECOVERY',
        'STATS', 'RECOMPILE', 'VIEW_METADATA'
    ]);
    var topLevelStarters = new Set([
        'SELECT', 'WITH', 'INSERT', 'UPDATE', 'DELETE', 'MERGE',
        'EXEC', 'EXECUTE', 'CREATE', 'ALTER', 'DROP', 'TRUNCATE',
        'DECLARE', 'SET', 'USE', 'PRINT', 'DBCC', 'WAITFOR',
        'RAISERROR', 'THROW', 'RETURN', 'BULK', 'IF', 'WHILE',
        'GRANT', 'REVOKE', 'DENY', 'BACKUP', 'RESTORE',
        'COMMIT', 'ROLLBACK', 'SAVE', 'BEGIN'
    ]);
    var blockingPunct = new Set(['.', ',', '(', '=', '+', '-', '/', '<', '>', '!', '%', '&', '|', '^', '~']);

    function isQueryRunnerEditor(editor) {
        try {
            if (!editor || typeof editor.getContainerDomNode !== 'function') return false;
            var container = editor.getContainerDomNode();
            return !!(container && (
                container.id === 'query-editor' ||
                (container.parentElement && container.parentElement.id === 'query-editor') ||
                (container.closest && container.closest('#query-editor'))
            ));
        } catch (e) {
            return false;
        }
    }

    function resolveQueryEditor(editorOrId) {
        if (editorOrId && typeof editorOrId.getModel === 'function') {
            return editorOrId;
        }
        var targetId = typeof editorOrId === 'string' && editorOrId ? editorOrId : 'query-editor';
        if (typeof monaco === 'undefined' || !monaco.editor) return null;
        var editors = monaco.editor.getEditors();
        for (var i = 0; i < editors.length; i++) {
            var e = editors[i];
            var container = e.getContainerDomNode();
            if (container && (container.id === targetId || (container.parentElement && container.parentElement.id === targetId) || (container.closest && container.closest('#' + targetId)))) {
                return e;
            }
        }
        return editors.length > 0 ? editors[0] : null;
    }

    function isIdentStart(ch) {
        return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch === '_' || ch === '@' || ch === '#';
    }

    function isIdentPart(ch) {
        return isIdentStart(ch) || (ch >= '0' && ch <= '9') || ch === '$';
    }

    function tokenizeSqlModelForExecution(model) {
        var tokens = [];
        var lineCount = model.getLineCount();
        var inSingleQuote = false;
        var inBracketIdent = false;
        var inDoubleQuote = false;
        var openToken = null;
        var blockCommentDepth = 0;

        for (var lineNum = 1; lineNum <= lineCount; lineNum++) {
            var line = model.getLineContent(lineNum) || '';
            var len = line.length;
            if (len === 0) continue;

            if (!inSingleQuote && !inBracketIdent && !inDoubleQuote && blockCommentDepth === 0) {
                var goCheck = line.replace(/--.*$/, '').trim();
                if (/^GO(?:\s+\d+)?$/i.test(goCheck)) {
                    tokens.push({
                        kind: 'go',
                        upper: 'GO',
                        startLine: lineNum,
                        startCol: 1,
                        endLine: lineNum,
                        endCol: len + 1
                    });
                    continue;
                }
            }

            var i = 0;
            while (i < len) {
                var ch = line[i];

                if (blockCommentDepth > 0) {
                    if (ch === '/' && i + 1 < len && line[i + 1] === '*') {
                        blockCommentDepth++;
                        i += 2;
                    } else if (ch === '*' && i + 1 < len && line[i + 1] === '/') {
                        blockCommentDepth--;
                        i += 2;
                    } else {
                        i++;
                    }
                    continue;
                }

                if (inSingleQuote) {
                    if (ch === "'") {
                        if (i + 1 < len && line[i + 1] === "'") {
                            i += 2;
                        } else {
                            inSingleQuote = false;
                            i++;
                            if (openToken) {
                                openToken.endLine = lineNum;
                                openToken.endCol = i + 1;
                                tokens.push(openToken);
                                openToken = null;
                            }
                        }
                    } else {
                        i++;
                    }
                    continue;
                }

                if (inBracketIdent) {
                    if (ch === ']') {
                        if (i + 1 < len && line[i + 1] === ']') {
                            i += 2;
                        } else {
                            inBracketIdent = false;
                            i++;
                            if (openToken) {
                                openToken.endLine = lineNum;
                                openToken.endCol = i + 1;
                                tokens.push(openToken);
                                openToken = null;
                            }
                        }
                    } else {
                        i++;
                    }
                    continue;
                }

                if (inDoubleQuote) {
                    if (ch === '"') {
                        if (i + 1 < len && line[i + 1] === '"') {
                            i += 2;
                        } else {
                            inDoubleQuote = false;
                            i++;
                            if (openToken) {
                                openToken.endLine = lineNum;
                                openToken.endCol = i + 1;
                                tokens.push(openToken);
                                openToken = null;
                            }
                        }
                    } else {
                        i++;
                    }
                    continue;
                }

                if (ch === ' ' || ch === '\t' || ch === '\r') {
                    i++;
                    continue;
                }

                if (ch === '-' && i + 1 < len && line[i + 1] === '-') {
                    break;
                }

                if (ch === '/' && i + 1 < len && line[i + 1] === '*') {
                    blockCommentDepth = 1;
                    i += 2;
                    continue;
                }

                if (ch === "'") {
                    openToken = {
                        kind: 'literal',
                        upper: '',
                        startLine: lineNum,
                        startCol: i + 1,
                        endLine: lineNum,
                        endCol: i + 2
                    };
                    inSingleQuote = true;
                    i++;
                    continue;
                }

                if (ch === '[') {
                    openToken = {
                        kind: 'ident',
                        upper: '',
                        startLine: lineNum,
                        startCol: i + 1,
                        endLine: lineNum,
                        endCol: i + 2
                    };
                    inBracketIdent = true;
                    i++;
                    continue;
                }

                if (ch === '"') {
                    openToken = {
                        kind: 'ident',
                        upper: '',
                        startLine: lineNum,
                        startCol: i + 1,
                        endLine: lineNum,
                        endCol: i + 2
                    };
                    inDoubleQuote = true;
                    i++;
                    continue;
                }

                if ((ch === 'N' || ch === 'n') && i + 1 < len && line[i + 1] === "'") {
                    openToken = {
                        kind: 'literal',
                        upper: '',
                        startLine: lineNum,
                        startCol: i + 1,
                        endLine: lineNum,
                        endCol: i + 3
                    };
                    inSingleQuote = true;
                    i += 2;
                    continue;
                }

                if (isIdentStart(ch)) {
                    var wStart = i;
                    i++;
                    while (i < len && isIdentPart(line[i])) {
                        i++;
                    }
                    var isKeywordCandidate = (ch !== '@' && ch !== '#');
                    var wordSlice = isKeywordCandidate && (i - wStart <= 32)
                        ? line.slice(wStart, i).toUpperCase()
                        : '';
                    tokens.push({
                        kind: isKeywordCandidate ? 'word' : 'ident',
                        upper: wordSlice,
                        startLine: lineNum,
                        startCol: wStart + 1,
                        endLine: lineNum,
                        endCol: i + 1
                    });
                    continue;
                }

                if (ch >= '0' && ch <= '9') {
                    var nStart = i;
                    i++;
                    while (i < len && ((line[i] >= '0' && line[i] <= '9') || line[i] === '.' || line[i] === 'x' || line[i] === 'X' || (line[i] >= 'a' && line[i] <= 'f') || (line[i] >= 'A' && line[i] <= 'F'))) {
                        i++;
                    }
                    tokens.push({
                        kind: 'literal',
                        upper: '',
                        startLine: lineNum,
                        startCol: nStart + 1,
                        endLine: lineNum,
                        endCol: i + 1
                    });
                    continue;
                }

                tokens.push({
                    kind: ch === ';' ? 'semi' : 'punct',
                    upper: ch,
                    startLine: lineNum,
                    startCol: i + 1,
                    endLine: lineNum,
                    endCol: i + 2
                });
                i++;
            }
        }

        if (openToken) {
            var lastLen = (model.getLineContent(lineCount) || '').length;
            openToken.endLine = lineCount;
            openToken.endCol = lastLen + 1;
            tokens.push(openToken);
        }

        return tokens;
    }

    function isCteWithAt(tokens, idx) {
        var t1 = tokens[idx + 1];
        if (!t1 || (t1.kind !== 'word' && t1.kind !== 'ident')) return false;
        if (t1.kind === 'word' && nonWithCteNextWords.has(t1.upper)) return false;
        var t2 = tokens[idx + 2];
        if (!t2) return false;
        if (t2.kind === 'word' && t2.upper === 'AS') return true;
        if (t2.kind === 'punct' && t2.upper === '(') {
            var depth = 0;
            for (var k = idx + 2; k < tokens.length && k < idx + 200; k++) {
                var tk = tokens[k];
                if (tk.kind === 'punct' && tk.upper === '(') depth++;
                else if (tk.kind === 'punct' && tk.upper === ')') {
                    depth--;
                    if (depth === 0) {
                        var afterParen = tokens[k + 1];
                        return !!(afterParen && afterParen.kind === 'word' && afterParen.upper === 'AS');
                    }
                }
            }
        }
        return false;
    }

    function shouldStartNewSqlStatement(stmt, tokens, idx, prevWord, prevPrevWord, prevPunct) {
        if (!stmt) return false;
        var tok = tokens[idx];
        if (tok.kind !== 'word' || !topLevelStarters.has(tok.upper)) return false;
        if (blockingPunct.has(prevPunct)) return false;

        var u = tok.upper;
        var nextTok = tokens[idx + 1];
        var nextUpper = nextTok ? nextTok.upper : '';

        if (stmt.isDdlModule) {
            return false;
        }

        if (stmt.isIfOrWhile && stmt.awaitingBody) {
            return false;
        }

        if (u === 'SELECT') {
            if (stmt.expectingInsertSource || stmt.awaitingCteMain) return false;
            if (stmt.isDdlView && prevWord === 'AS') {
                stmt.seenViewSelect = true;
                return false;
            }
            if (setJoinOpWords.has(prevWord) || prevWord === 'AS') return false;
            if (prevWord === 'ALL' && prevPrevWord === 'UNION') return false;
            return true;
        }

        if (u === 'WITH') {
            if (stmt.isDdlView && prevWord === 'AS') return false;
            if (prevWord === 'AS' || prevWord === 'REVOKE' || prevWord === 'GRANT' || prevWord === 'DENY') return false;
            return isCteWithAt(tokens, idx);
        }

        if (u === 'INSERT' || u === 'UPDATE' || u === 'DELETE' || u === 'MERGE') {
            if (stmt.awaitingCteMain) return false;
            if (stmt.activeDmlKind === 'MERGE' && prevWord === 'THEN') return false;
            if (mergeOrDdlAfterWords.has(prevWord)) return false;
            return true;
        }

        if (u === 'SET') {
            if ((stmt.activeDmlKind === 'UPDATE' || stmt.activeDmlKind === 'MERGE') && !stmt.sawUpdateSet) return false;
            if (stmt.activeDmlKind === 'MERGE' && prevWord === 'UPDATE') return false;
            if (prevWord === 'DELETE' || prevWord === 'UPDATE') return false;
            return true;
        }

        if (u === 'EXEC' || u === 'EXECUTE') {
            if (stmt.expectingInsertSource) return false;
            if (prevWord === 'WITH') return false;
            return true;
        }

        if (u === 'ALTER' && stmt.root === 'CREATE' && prevWord === 'OR') {
            return false;
        }

        if (u === 'DROP' && stmt.root === 'ALTER') {
            if (alterDropClauseWords.has(nextUpper)) return false;
        }

        if (u === 'IF' && (stmt.root === 'DROP' || stmt.root === 'CREATE' || stmt.root === 'ALTER')) {
            if (ddlIfTargetWords.has(prevWord) || (prevWord === 'NOT' && nextUpper === 'EXISTS')) return false;
        }

        if (u === 'RETURN' && stmt.root === 'CREATE' && prevWord === 'AS') {
            return false;
        }

        if (u === 'BEGIN' && (nextUpper === 'TRY' || nextUpper === 'CATCH')) {
            return true;
        }

        return true;
    }

    function parseSqlStatementsForExecution(model) {
        var tokens = tokenizeSqlModelForExecution(model);
        var statements = [];
        var currentStmt = null;
        var parenDepth = 0;
        var controlStack = [];
        var prevWord = '';
        var prevPrevWord = '';
        var prevPunct = '';

        function pushStatement() {
            if (currentStmt) {
                statements.push({
                    startLineNumber: currentStmt.startLineNumber,
                    startColumn: currentStmt.startColumn,
                    endLineNumber: currentStmt.endLineNumber,
                    endColumn: currentStmt.endColumn
                });
                currentStmt = null;
            }
        }

        function beginStatementFromToken(tok, idx) {
            var u = tok.kind === 'word' ? tok.upper : '';
            var nextTok = tokens[idx + 1];
            var nextUpper = nextTok ? nextTok.upper : '';
            var next2 = tokens[idx + 2];
            var next3 = tokens[idx + 3];

            var isDdlModule = false;
            var isDdlView = false;
            if (u === 'CREATE' || u === 'ALTER') {
                var targetTok = (nextUpper === 'OR' && next2 && next2.upper === 'ALTER') ? next3 : nextTok;
                if (targetTok && targetTok.kind === 'word') {
                    if (ddlModuleTargets.has(targetTok.upper)) isDdlModule = true;
                    else if (targetTok.upper === 'VIEW') isDdlView = true;
                }
            }

            currentStmt = {
                root: u,
                activeDmlKind: u,
                startLineNumber: tok.startLine,
                startColumn: tok.startCol,
                endLineNumber: tok.endLine,
                endColumn: tok.endCol,
                awaitingCteMain: (u === 'WITH'),
                expectingInsertSource: (u === 'INSERT'),
                isDdlModule: isDdlModule,
                isDdlView: isDdlView,
                seenViewSelect: false,
                sawUpdateSet: false,
                isIfOrWhile: (u === 'IF' || u === 'WHILE'),
                awaitingBody: (u === 'IF' || u === 'WHILE')
            };
        }

        for (var idx = 0; idx < tokens.length; idx++) {
            var tok = tokens[idx];

            if (tok.kind === 'go') {
                pushStatement();
                parenDepth = 0;
                controlStack.length = 0;
                prevWord = '';
                prevPrevWord = '';
                prevPunct = '';
                continue;
            }

            if (tok.kind === 'semi') {
                if (currentStmt) {
                    currentStmt.endLineNumber = tok.endLine;
                    currentStmt.endColumn = tok.endCol;
                    if (parenDepth === 0 && controlStack.length === 0 && !currentStmt.isDdlModule) {
                        pushStatement();
                        prevWord = '';
                        prevPrevWord = '';
                        prevPunct = ';';
                        continue;
                    }
                }
                prevPunct = ';';
                continue;
            }

            if (tok.kind === 'word' && tok.upper === 'ELSE' && currentStmt && currentStmt.root === 'IF' && parenDepth === 0 && controlStack.length === 0 && !currentStmt.awaitingBody) {
                currentStmt.endLineNumber = tok.endLine;
                currentStmt.endColumn = tok.endCol;
                currentStmt.awaitingBody = true;
                prevPrevWord = prevWord;
                prevWord = 'ELSE';
                prevPunct = '';
                continue;
            }

            if (parenDepth === 0 && controlStack.length === 0 && currentStmt &&
                shouldStartNewSqlStatement(currentStmt, tokens, idx, prevWord, prevPrevWord, prevPunct)) {
                pushStatement();
            }

            if (!currentStmt) {
                beginStatementFromToken(tok, idx);
            } else {
                currentStmt.endLineNumber = tok.endLine;
                currentStmt.endColumn = tok.endCol;
            }

            if (tok.kind === 'punct') {
                if (tok.upper === '(') parenDepth++;
                else if (tok.upper === ')' && parenDepth > 0) parenDepth--;
                prevPunct = tok.upper;
            } else if (tok.kind === 'word') {
                var w = tok.upper;
                var nextW = tokens[idx + 1] ? tokens[idx + 1].upper : '';

                if (w === 'CASE') {
                    controlStack.push('CASE');
                } else if (w === 'BEGIN') {
                    var isBeginTran = (nextW === 'TRAN' || nextW === 'TRANSACTION' || nextW === 'DISTRIBUTED' || nextW === 'DIALOG' || nextW === 'CONVERSATION');
                    if (!isBeginTran) {
                        controlStack.push('BEGIN');
                    }
                    if (currentStmt.isIfOrWhile && currentStmt.awaitingBody && parenDepth === 0) {
                        currentStmt.awaitingBody = false;
                    }
                } else if (w === 'END') {
                    if (controlStack.length > 0) {
                        controlStack.pop();
                    }
                } else if (parenDepth === 0 && controlStack.length === 0) {
                    if (currentStmt.isIfOrWhile && currentStmt.awaitingBody && topLevelStarters.has(w) && w !== currentStmt.root) {
                        currentStmt.awaitingBody = false;
                        currentStmt.activeDmlKind = w;
                        if (w === 'INSERT') currentStmt.expectingInsertSource = true;
                        if (w === 'UPDATE' || w === 'MERGE') currentStmt.sawUpdateSet = false;
                    }
                    if (currentStmt.awaitingCteMain && (w === 'SELECT' || w === 'INSERT' || w === 'UPDATE' || w === 'DELETE' || w === 'MERGE')) {
                        currentStmt.awaitingCteMain = false;
                        currentStmt.activeDmlKind = w;
                        if (w === 'INSERT') currentStmt.expectingInsertSource = true;
                        if (w === 'UPDATE' || w === 'MERGE') currentStmt.sawUpdateSet = false;
                    }
                    if (currentStmt.expectingInsertSource && (w === 'VALUES' || w === 'SELECT' || w === 'EXEC' || w === 'EXECUTE' || w === 'DEFAULT')) {
                        currentStmt.expectingInsertSource = false;
                    }
                    if ((currentStmt.activeDmlKind === 'UPDATE' || currentStmt.activeDmlKind === 'MERGE') && w === 'SET') {
                        currentStmt.sawUpdateSet = true;
                    }
                }

                prevPrevWord = prevWord;
                prevWord = w;
                prevPunct = '';
            } else {
                prevPrevWord = prevWord;
                prevWord = '';
                prevPunct = '';
            }
        }

        pushStatement();
        return statements;
    }

    function comparePositions(aLine, aCol, bLine, bCol) {
        if (aLine !== bLine) return aLine - bLine;
        return aCol - bCol;
    }

    function findStatementAtCursor(statements, cursorLine, cursorCol) {
        if (!statements || statements.length === 0) return null;

        for (var i = 0; i < statements.length; i++) {
            var s = statements[i];
            if (comparePositions(cursorLine, cursorCol, s.startLineNumber, s.startColumn) >= 0 &&
                comparePositions(cursorLine, cursorCol, s.endLineNumber, s.endColumn) <= 0) {
                return s;
            }
        }

        var lineCandidates = [];
        for (var j = 0; j < statements.length; j++) {
            var st = statements[j];
            if (st.startLineNumber <= cursorLine && st.endLineNumber >= cursorLine) {
                lineCandidates.push({ index: j, stmt: st });
            }
        }
        if (lineCandidates.length === 0) return null;

        for (var k = lineCandidates.length - 1; k >= 0; k--) {
            var cand = lineCandidates[k].stmt;
            if (cand.startLineNumber === cursorLine && cursorCol < cand.startColumn) {
                var prevCand = k > 0 ? lineCandidates[k - 1].stmt : null;
                if (!prevCand || comparePositions(cursorLine, cursorCol, prevCand.endLineNumber, prevCand.endColumn) > 0) {
                    return cand;
                }
            }
        }

        for (var m = lineCandidates.length - 1; m >= 0; m--) {
            var tailStmt = lineCandidates[m].stmt;
            if (tailStmt.endLineNumber === cursorLine && cursorCol >= tailStmt.endColumn) {
                var nextCand = (m + 1 < lineCandidates.length) ? lineCandidates[m + 1].stmt : null;
                if (!nextCand || comparePositions(cursorLine, cursorCol, nextCand.startLineNumber, nextCand.startColumn) < 0) {
                    return tailStmt;
                }
            }
        }

        return null;
    }

    function statementSharesLineWithAnother(statements, target) {
        for (var i = 0; i < statements.length; i++) {
            var s = statements[i];
            if (s === target) continue;
            if (s.endLineNumber === target.startLineNumber || s.startLineNumber === target.endLineNumber) {
                return true;
            }
        }
        return false;
    }

    window.parseSqlStatementsForExecution = parseSqlStatementsForExecution;

    window.getQueryBlockExecutionInput = function (editorOrId) {
        try {
            var editor = resolveQueryEditor(editorOrId);
            if (!editor) return null;

            var model = editor.getModel();
            if (!model) return null;

            var fullText = model.getValue() || '';
            var selection = editor.getSelection();
            if (selection && !selection.isEmpty()) {
                var selectedText = model.getValueInRange(selection) || '';
                if (!selectedText.trim()) {
                    return { shouldExecute: false };
                }
                var selSingleLinePartial = selection.startLineNumber === selection.endLineNumber &&
                    selection.startColumn > 1;
                return {
                    shouldExecute: true,
                    isSelection: true,
                    isWholeLine: !selSingleLinePartial,
                    fullText: fullText,
                    queryText: selectedText,
                    startLineNumber: selection.startLineNumber,
                    startColumn: selection.startColumn,
                    endLineNumber: selection.endLineNumber,
                    endColumn: selection.endColumn
                };
            }

            var position = editor.getPosition();
            if (!position) return { shouldExecute: false };

            var curLineText = model.getLineContent(position.lineNumber) || '';
            if (!curLineText.trim()) {
                return { shouldExecute: false };
            }

            var statements = parseSqlStatementsForExecution(model);
            var matched = findStatementAtCursor(statements, position.lineNumber, position.column);
            if (!matched) {
                return { shouldExecute: false };
            }

            var blockText = model.getValueInRange(matched) || '';
            if (!blockText.trim()) {
                return { shouldExecute: false };
            }

            var sharesLine = statementSharesLineWithAnother(statements, matched);
            return {
                shouldExecute: true,
                isSelection: false,
                isWholeLine: !sharesLine,
                fullText: fullText,
                queryText: blockText,
                startLineNumber: matched.startLineNumber,
                startColumn: matched.startColumn,
                endLineNumber: matched.endLineNumber,
                endColumn: matched.endColumn
            };
        } catch (e) {
            return null;
        }
    };

    window.flashExecutedQueryBlock = function (editorOrId, rangeInfo) {
        try {
            var editor = resolveQueryEditor(editorOrId);
            if (!editor || !rangeInfo || typeof monaco === 'undefined') return;

            if (editor.__queryBlockFlashTimer) {
                clearTimeout(editor.__queryBlockFlashTimer);
                editor.__queryBlockFlashTimer = null;
            }

            var prevIds = editor.__queryBlockFlashDecorations || [];
            var range = new monaco.Range(
                rangeInfo.startLineNumber,
                rangeInfo.startColumn || 1,
                rangeInfo.endLineNumber,
                rangeInfo.endColumn || 1
            );

            editor.__queryBlockFlashDecorations = editor.deltaDecorations(prevIds, [{
                range: range,
                options: {
                    isWholeLine: rangeInfo.isWholeLine !== false,
                    className: 'executed-query-block-flash',
                    stickiness: monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges
                }
            }]);

            editor.__queryBlockFlashTimer = setTimeout(function () {
                try {
                    if (editor.__queryBlockFlashDecorations) {
                        editor.__queryBlockFlashDecorations = editor.deltaDecorations(editor.__queryBlockFlashDecorations, []);
                    }
                } catch (err) { }
                editor.__queryBlockFlashTimer = null;
            }, 680);
        } catch (e) { }
    };

    window.attachQueryBlockExecutor = function (editor) {
        if (!editor || !isQueryRunnerEditor(editor)) return;
        if (typeof monaco === 'undefined' || !monaco.KeyMod || !monaco.KeyCode) return;

        if (!editor.getAction('execute-query-block')) {
            try {
                editor.addAction({
                    id: 'execute-query-block',
                    label: 'Execute Query Block (Ctrl+Enter)',
                    keybindings: [
                        monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter
                    ],
                    precondition: 'editorTextFocus',
                    contextMenuGroupId: 'navigation',
                    contextMenuOrder: 1,
                    run: function (ed) {
                        var block = window.getQueryBlockExecutionInput(ed);
                        if (!block || !block.shouldExecute) {
                            return;
                        }
                        if (window.globalShortcuts && window.globalShortcuts.queryRunnerInstance) {
                            window.flashExecutedQueryBlock(ed, block);
                            window.globalShortcuts.queryRunnerInstance
                                .invokeMethodAsync(
                                    'ExecuteQueryBlockFromShortcut',
                                    block.queryText,
                                    block.fullText,
                                    Math.max(0, block.startLineNumber - 1)
                                )
                                .catch(function () { });
                        }
                    }
                });
            } catch (e) { }
        }
    };
})();

window.updateSqlAutocompleteData = (tables, views, columns, sps, functions) => {
    window.sqlTables = tables || window.sqlTables;
    window.sqlViews = views || window.sqlViews;
    window.sqlColumns = columns || window.sqlColumns;
    window.sqlSps = sps || window.sqlSps;
    window.sqlFunctions = functions || window.sqlFunctions;
};

window.updateSqlSnippets = (snippets) => {
    window.sqlSnippets = snippets || [];
};

window.registerSqlAutocomplete = (tables, views, columns, sps, functions, dotNetHelper) => {
    if (functions && typeof functions.invokeMethodAsync === 'function') {
        dotNetHelper = functions;
        functions = null;
    }
    window.sqlDotNetHelper = dotNetHelper || window.sqlDotNetHelper;
    window.updateSqlAutocompleteData(tables, views, columns, sps, functions);
    if (window.sqlAutocompleteRegistered) return;

    if (typeof monaco === 'undefined' || typeof monaco.languages === 'undefined') {
        return;
    }

    window.sqlAutocompleteRegistered = true;

    var systemProcedures = [
        {
            name: 'sp_helptext',
            fullName: 'sp_helptext',
            snippet: "sp_helptext '$1'",
            triggerSuggest: true,
            detail: 'System Stored Procedure',
            doc: 'Displays the definition of a user-defined rule, default, unencrypted stored procedure, user-defined function, trigger, computed column, CHECK constraint, view, or system object.'
        },
        {
            name: 'sp_help',
            fullName: 'sp_help',
            snippet: "sp_help '$1'",
            triggerSuggest: true,
            detail: 'System Stored Procedure',
            doc: 'Reports information about a database object, a user-defined data type, or a data type.'
        },
        {
            name: 'sp_spaceused',
            fullName: 'sp_spaceused',
            snippet: "sp_spaceused '$1'",
            triggerSuggest: true,
            detail: 'System Stored Procedure',
            doc: 'Displays the number of rows, disk space reserved, and disk space used by a table, indexed view, or Service Broker queue.'
        },
        {
            name: 'sp_who2',
            fullName: 'sp_who2',
            snippet: "sp_who2",
            triggerSuggest: false,
            detail: 'System Stored Procedure',
            doc: 'Provides information about current SQL Server users, sessions, and processes.'
        },
        {
            name: 'sp_columns',
            fullName: 'sp_columns',
            snippet: "sp_columns '$1'",
            triggerSuggest: true,
            detail: 'System Stored Procedure',
            doc: 'Returns column information for the specified tables or views.'
        }
    ];

    function getSpHelptextContext(model, position) {
        var lineContent = model.getLineContent(position.lineNumber);
        var col = position.column;
        var textBeforeCursor = lineContent.substring(0, col - 1);

        // Match sp_helptext or sp_help before cursor on current line
        var match = textBeforeCursor.match(/(?:(?:exec|execute)\s+)?(sp_helptext|sp_help)\s+(?:@objname\s*=\s*)?N?(['"]?)/i);
        if (!match) {
            var fullTextUntilPosition = model.getValueInRange({
                startLineNumber: 1,
                startColumn: 1,
                endLineNumber: position.lineNumber,
                endColumn: position.column
            });
            match = fullTextUntilPosition.match(/(?:(?:exec|execute)\s+)?(sp_helptext|sp_help)\s+(?:@objname\s*=\s*)?N?(['"]?)$/i);
            if (!match) return null;
        }

        var matchIndex = match.index;
        var fullMatch = match[0];
        var quoteChar = match[2] || "";
        var argStartCol = matchIndex + fullMatch.length + 1;

        if (col < argStartCol) return null;

        if (quoteChar) {
            var textAfterArgStart = lineContent.substring(argStartCol - 1);
            var closingQuoteRelIndex = textAfterArgStart.indexOf(quoteChar);
            var argEndCol;
            var hasClosingQuote = false;

            if (closingQuoteRelIndex >= 0) {
                hasClosingQuote = true;
                argEndCol = argStartCol + closingQuoteRelIndex;
                if (col > argEndCol + 1) {
                    return null;
                }
            } else {
                argEndCol = lineContent.length + 1;
            }

            var typedPrefix = lineContent.substring(argStartCol - 1, col - 1);

            return {
                quoteChar: quoteChar,
                hasClosingQuote: hasClosingQuote,
                typedPrefix: typedPrefix,
                replaceRange: {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: argStartCol,
                    endColumn: hasClosingQuote ? argEndCol : col
                }
            };
        } else {
            var textFromArg = lineContent.substring(argStartCol - 1);
            var spaceMatch = textFromArg.match(/\s/);
            var argEndCol = spaceMatch ? (argStartCol + spaceMatch.index) : (lineContent.length + 1);
            if (col > argEndCol) return null;

            var typedPrefix = lineContent.substring(argStartCol - 1, col - 1);
            return {
                quoteChar: "",
                hasClosingQuote: false,
                typedPrefix: typedPrefix,
                replaceRange: {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: argStartCol,
                    endColumn: argEndCol
                }
            };
        }
    }

    function getActiveStatementContext(model, position) {
        var lineCount = model.getLineCount();
        var curLine = position.lineNumber;
        var curCol = position.column;

        var stmtStarters = /^\s*(?:--.*)?(?:;?\s*WITH|SELECT|INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|CREATE|ALTER|DROP|TRUNCATE|DECLARE|SET|USE)\b/i;
        var setOps = /^\s*(?:UNION(?:\s+ALL)?|EXCEPT|INTERSECT)\b/i;
        var batchSep = /^\s*GO\s*(?:--.*)?$/i;

        function stripCommentsAndStrings(text) {
            return (text || "").replace(/'(?:''|[^'])*'/g, "''").replace(/--.*$/, "");
        }

        function countParens(str) {
            var clean = stripCommentsAndStrings(str);
            var open = 0, close = 0;
            for (var i = 0; i < clean.length; i++) {
                if (clean[i] === '(') open++;
                else if (clean[i] === ')') close++;
            }
            return { open: open, close: close };
        }

        var curLineText = model.getLineContent(curLine) || "";

        // If current line is completely blank and next non-empty line starts a new statement,
        // treat as independent insertion point ONLY when the line above isn't mid-clause (e.g. after WHERE / AND / ON / comma / open '(').
        if (!curLineText.trim()) {
            var prevNonEmptyIdx = -1;
            for (var pIdx = curLine - 1; pIdx >= 1; pIdx--) {
                var pText = stripCommentsAndStrings(model.getLineContent(pIdx)).trim();
                if (pText) { prevNonEmptyIdx = pIdx; break; }
            }
            var prevCleanTrim = prevNonEmptyIdx > 0
                ? stripCommentsAndStrings(model.getLineContent(prevNonEmptyIdx)).trim()
                : "";
            var openClauseEndRegex = /(?:[,(=<>+\-*/]|\b(?:SELECT|DISTINCT|TOP|FROM|JOIN|INNER\s+JOIN|LEFT\s+JOIN|RIGHT\s+JOIN|FULL\s+JOIN|FULL\s+OUTER\s+JOIN|CROSS\s+JOIN|ON|WHERE|AND|OR|NOT|IN|SET|VALUES|HAVING|ORDER\s+BY|GROUP\s+BY|INTO|UPDATE|DELETE|WHEN|THEN|ELSE|CASE|AS|UNION(?:\s+ALL)?))\s*$/i;
            var isPrevOpenClause = prevCleanTrim.length > 0 && !/;\s*$/.test(prevCleanTrim) && openClauseEndRegex.test(prevCleanTrim);

            if (!isPrevOpenClause) {
                var nextNonEmptyIdx = -1;
                for (var n = curLine + 1; n <= lineCount; n++) {
                    var nText = stripCommentsAndStrings(model.getLineContent(n)).trim();
                    if (nText) { nextNonEmptyIdx = n; break; }
                }
                if (nextNonEmptyIdx > 0 && stmtStarters.test(stripCommentsAndStrings(model.getLineContent(nextNonEmptyIdx)))) {
                    return {
                        startLineNumber: curLine,
                        startColumn: 1,
                        endLineNumber: curLine,
                        endColumn: 1,
                        statementText: "",
                        textUntilCursor: ""
                    };
                }
            }
        }

        // 1. Scan upwards to find start
        var startLine = 1;
        var startCol = 1;

        var curLineBeforeCursor = curLineText.substring(0, curCol - 1);
        var cleanBefore = stripCommentsAndStrings(curLineBeforeCursor);
        var lastSemiOnCurLine = cleanBefore.lastIndexOf(';');
        if (lastSemiOnCurLine >= 0) {
            startLine = curLine;
            startCol = lastSemiOnCurLine + 2;
        } else {
            var parenDepth = 0;
            for (var i = curLine; i >= 1; i--) {
                var line = model.getLineContent(i) || "";
                var clean = stripCommentsAndStrings(line);
                var p = countParens(line);
                
                parenDepth += (p.close - p.open);

                if (i < curLine) {
                    if (batchSep.test(clean)) {
                        startLine = i + 1;
                        startCol = 1;
                        break;
                    }

                    var semiIdx = clean.lastIndexOf(';');
                    if (semiIdx >= 0) {
                        var afterSemi = clean.substring(semiIdx + 1).trim();
                        if (afterSemi.length > 0) {
                            startLine = i;
                            startCol = semiIdx + 2;
                        } else {
                            startLine = i + 1;
                            startCol = 1;
                        }
                        break;
                    }

                    var nextLineClean = stripCommentsAndStrings(model.getLineContent(i + 1) || "");
                    if (parenDepth <= 0 && stmtStarters.test(nextLineClean)) {
                        var isSetOpNext = setOps.test(nextLineClean) || setOps.test(clean.trim());
                        if (!isSetOpNext) {
                            startLine = i + 1;
                            startCol = 1;
                            break;
                        }
                    }

                    if (parenDepth <= 0 && stmtStarters.test(clean)) {
                        var prevClean = i > 1 ? stripCommentsAndStrings(model.getLineContent(i - 1)).trim() : "";
                        var isSetOpPrev = setOps.test(clean) || setOps.test(prevClean);
                        if (!isSetOpPrev) {
                            startLine = i;
                            startCol = 1;
                            break;
                        }
                    }
                } else if (i === curLine) {
                    if (parenDepth <= 0 && stmtStarters.test(clean)) {
                        var prevClean = i > 1 ? stripCommentsAndStrings(model.getLineContent(i - 1)).trim() : "";
                        var isSetOpPrev = setOps.test(clean) || setOps.test(prevClean);
                        if (!isSetOpPrev) {
                            startLine = i;
                            startCol = 1;
                            break;
                        }
                    }
                }
            }
        }

        // If statement starts with SELECT/INSERT/UPDATE/DELETE/MERGE right after a CTE closing ')',
        // extend startLine upward to include the enclosing WITH ... AS (...) block.
        while (startLine > 1 && startCol === 1) {
            var starterClean = stripCommentsAndStrings(model.getLineContent(startLine)).trim();
            if (!/^(?:SELECT|INSERT|UPDATE|DELETE|MERGE)\b/i.test(starterClean)) break;

            var aboveIdx = startLine - 1;
            while (aboveIdx >= 1 && !stripCommentsAndStrings(model.getLineContent(aboveIdx)).trim()) {
                aboveIdx--;
            }
            if (aboveIdx < 1) break;
            var aboveClean = stripCommentsAndStrings(model.getLineContent(aboveIdx)).trim();
            if (!/\)\s*$/.test(aboveClean)) break;

            var cDepth = 0;
            var foundWithStart = -1;
            var foundWithCol = 1;
            for (var k = aboveIdx; k >= 1; k--) {
                var kcRaw = model.getLineContent(k);
                var kc = stripCommentsAndStrings(kcRaw);
                if (k < aboveIdx && batchSep.test(kc)) break;

                if (k < aboveIdx) {
                    var kSemi = kc.lastIndexOf(';');
                    if (kSemi >= 0) {
                        var afterSemiCte = kc.substring(kSemi + 1);
                        if (/^\s*WITH\b/i.test(afterSemiCte)) {
                            foundWithStart = k;
                            foundWithCol = kSemi + 2;
                        }
                        break;
                    }
                }

                var kp = countParens(kcRaw);
                cDepth += (kp.close - kp.open);
                if (cDepth <= 0 && /^\s*;?\s*WITH\b/i.test(kc)) {
                    var semiInWith = kc.lastIndexOf(';');
                    foundWithStart = k;
                    foundWithCol = semiInWith >= 0 ? (semiInWith + 2) : 1;
                    break;
                }
                // Stop if balanced parens close back to 0 inside a non-CTE statement (e.g., WHERE x IN (1, 2))
                if (cDepth <= 0 && !/^\s*(?:,?\s*[a-zA-Z0-9_\[\]]+(?:\s*\([^)]*\))?\s+AS\s*\(|\))/i.test(kc)) {
                    break;
                }
            }
            if (foundWithStart > 0) {
                startLine = foundWithStart;
                startCol = foundWithCol;
            } else {
                break;
            }
        }

        // 2. Scan downwards to find end
        var endLine = lineCount;
        var lastLineText = model.getLineContent(lineCount) || "";
        var endCol = lastLineText.length + 1;

        var curLineAfterCursor = curLineText.substring(curCol - 1);
        var cleanAfter = stripCommentsAndStrings(curLineAfterCursor);
        var firstSemiAfter = cleanAfter.indexOf(';');
        if (firstSemiAfter >= 0) {
            endLine = curLine;
            endCol = curCol + firstSemiAfter + 1;
        } else {
            var downParenDepth = 0;
            for (var j = curLine; j <= lineCount; j++) {
                var line = model.getLineContent(j) || "";
                var clean = stripCommentsAndStrings(line);
                var p = countParens(line);
                downParenDepth += (p.open - p.close);

                if (j > curLine) {
                    if (batchSep.test(clean)) {
                        endLine = j - 1;
                        endCol = (model.getLineContent(j - 1) || "").length + 1;
                        break;
                    }

                    var prevLineRaw = model.getLineContent(j - 1) || "";
                    var isBlankSeparatedTopLevelStarter =
                        !prevLineRaw.trim() &&
                        /^(?:SELECT|WITH|INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|EXEC|EXECUTE)\b/i.test(clean);
                    if ((downParenDepth <= 0 || isBlankSeparatedTopLevelStarter) && stmtStarters.test(clean)) {
                        var prevLineClean = stripCommentsAndStrings(prevLineRaw).trim();
                        if (!setOps.test(clean) && !setOps.test(prevLineClean)) {
                            var actualEndLine = j - 1;
                            while (actualEndLine > curLine && !stripCommentsAndStrings(model.getLineContent(actualEndLine) || "").trim()) {
                                actualEndLine--;
                            }
                            endLine = actualEndLine;
                            endCol = (model.getLineContent(actualEndLine) || "").length + 1;
                            break;
                        }
                    }

                    var semiIdx = clean.indexOf(';');
                    if (semiIdx >= 0) {
                        endLine = j;
                        endCol = semiIdx + 2;
                        break;
                    }
                }
            }
        }

        if (startLine < 1) startLine = 1;
        if (startCol < 1) startCol = 1;
        if (endLine > lineCount) endLine = lineCount;
        if (endLine < startLine) endLine = startLine;

        var statementText = model.getValueInRange({
            startLineNumber: startLine,
            startColumn: startCol,
            endLineNumber: endLine,
            endColumn: endCol
        });

        var textUntilCursor = model.getValueInRange({
            startLineNumber: startLine,
            startColumn: startCol,
            endLineNumber: curLine,
            endColumn: curCol
        });

        return {
            startLineNumber: startLine,
            startColumn: startCol,
            endLineNumber: endLine,
            endColumn: endCol,
            statementText: statementText,
            textUntilCursor: textUntilCursor
        };
    }

    function analyzeClauseContext(textUntilCursor) {
        var clean = (textUntilCursor || "")
            .replace(/'(?:''|[^'])*'/g, "''")
            .replace(/--.*$/gm, "")
            .replace(/\/\*[\s\S]*?\*\//g, "");

        var parenDepth = 0;
        var lastOpenParenIdx = -1;
        for (var i = 0; i < clean.length; i++) {
            if (clean[i] === '(') {
                parenDepth++;
                lastOpenParenIdx = i;
            } else if (clean[i] === ')') {
                parenDepth--;
                if (parenDepth === 0) lastOpenParenIdx = -1;
            }
        }

        var textToScan = (parenDepth > 0 && lastOpenParenIdx >= 0)
            ? clean.substring(lastOpenParenIdx + 1)
            : clean;

        var clauseRegex = /\b(SELECT|FROM|JOIN|INNER\s+JOIN|LEFT\s+JOIN|RIGHT\s+JOIN|FULL\s+JOIN|FULL\s+OUTER\s+JOIN|CROSS\s+JOIN|ON|WHERE|HAVING|GROUP\s+BY|ORDER\s+BY|SET|VALUES|INTO|UPDATE|DELETE)\b/gi;
        var matches = [];
        var cm;
        while ((cm = clauseRegex.exec(textToScan)) !== null) {
            matches.push({
                kw: cm[1].toUpperCase().replace(/\s+/g, ' '),
                index: cm.index,
                length: cm[0].length
            });
        }

        if (matches.length === 0) {
            if (parenDepth > 0 && lastOpenParenIdx >= 0) {
                var beforeOpenParen = clean.substring(0, lastOpenParenIdx);
                if (/insert(?:\s+into)?\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s*$/i.test(beforeOpenParen)) {
                    return {
                        clause: 'INSERT_COLUMNS',
                        inParens: true,
                        hasExpression: false
                    };
                }
                if (/\bvalues\s*$/i.test(beforeOpenParen)) {
                    return {
                        clause: 'VALUES',
                        inParens: true,
                        hasExpression: false
                    };
                }
            }
            return {
                clause: 'SELECT',
                inParens: parenDepth > 0,
                hasExpression: false
            };
        }

        var lastMatch = matches[matches.length - 1];
        var clauseName = lastMatch.kw;
        if (clauseName.indexOf('JOIN') >= 0) clauseName = 'JOIN';

        var rawAfterClauseKw = textToScan.substring(lastMatch.index + lastMatch.length);
        var withoutLastToken = rawAfterClauseKw.replace(/[a-zA-Z0-9_\[\]\.]+$/, '').trim();

        // An expression exists if there's text between the clause keyword and the active token,
        // UNLESS that text ends with an operator/connector (AND, OR, NOT, comma, =, <, >, etc.)
        var endsWithOperator = /(?:\b(?:AND|OR|NOT|IN|LIKE|BETWEEN|ON|WHERE|HAVING|SET)\b|[=<>!,]+)\s*$/i.test(withoutLastToken);
        var hasExpression = withoutLastToken.length > 0 && !endsWithOperator;

        return {
            clause: clauseName,
            inParens: parenDepth > 0,
            hasExpression: hasExpression
        };
    }

    function getContextualKeywords(clauseInfo) {
        var clause = clauseInfo.clause;
        var inParens = clauseInfo.inParens;
        var hasExpression = clauseInfo.hasExpression;
        var exprKeywords = ['CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'CAST', 'CONVERT', 'NULL'];

        switch (clause) {
            case 'WHERE':
                var whereKws = [];
                if (hasExpression) {
                    whereKws.push('AND', 'OR', 'ORDER BY', 'GROUP BY', 'HAVING', 'UNION', 'UNION ALL');
                }
                whereKws.push('NOT', 'IN', 'EXISTS', 'BETWEEN', 'LIKE', 'IS NULL', 'IS NOT NULL');
                whereKws.push.apply(whereKws, exprKeywords);
                if (inParens) {
                    whereKws.push('SELECT', 'DISTINCT', 'TOP');
                }
                return whereKws;

            case 'HAVING':
                var havingKws = [];
                if (hasExpression) {
                    havingKws.push('AND', 'OR', 'ORDER BY', 'UNION', 'UNION ALL');
                }
                havingKws.push('NOT', 'IN', 'EXISTS', 'BETWEEN', 'LIKE', 'IS NULL', 'IS NOT NULL');
                havingKws.push.apply(havingKws, exprKeywords);
                return havingKws;

            case 'INSERT_COLUMNS':
                return [];

            case 'INTO':
                return inParens ? [] : ['VALUES', 'SELECT', 'OUTPUT'];

            case 'VALUES':
                return ['NULL', 'DEFAULT', 'CAST', 'CONVERT', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END'];

            case 'ON':
                var onKws = [];
                if (hasExpression) {
                    onKws.push(
                        'WHERE', 'AND', 'OR',
                        'INNER JOIN', 'LEFT JOIN', 'RIGHT JOIN', 'FULL OUTER JOIN', 'CROSS JOIN',
                        'GROUP BY', 'ORDER BY', 'UNION', 'UNION ALL'
                    );
                }
                onKws.push('NOT', 'IN', 'BETWEEN', 'LIKE', 'IS NULL', 'IS NOT NULL');
                onKws.push.apply(onKws, exprKeywords);
                return onKws;

            case 'ORDER BY':
                return ['ASC', 'DESC'];

            case 'GROUP BY':
                return ['HAVING', 'ORDER BY', 'UNION', 'UNION ALL'];

            case 'SELECT':
                var selectKws = ['DISTINCT', 'TOP', 'AS', 'FROM'];
                selectKws.push.apply(selectKws, exprKeywords);
                if (inParens) {
                    selectKws.push('SELECT');
                }
                return selectKws;

            case 'SET':
                var setKws = ['WHERE', 'FROM', 'NULL'];
                setKws.push.apply(setKws, exprKeywords);
                return setKws;

            case 'UPDATE':
                return ['SET', 'WHERE'];

            case 'DELETE':
                return ['WHERE', 'FROM', 'TOP'];

            case 'FROM':
                if (clauseInfo.isCompletedSingleTableDelete) {
                    return ['WHERE'];
                }
            case 'JOIN':
                return [
                    'AS', 'ON', 'WHERE', 'INNER JOIN', 'LEFT JOIN', 'RIGHT JOIN',
                    'FULL OUTER JOIN', 'CROSS JOIN', 'ORDER BY', 'GROUP BY'
                ];

            default:
                return [
                    'SELECT', 'FROM', 'WHERE', 'INNER JOIN', 'LEFT JOIN', 'RIGHT JOIN',
                    'FULL OUTER JOIN', 'CROSS JOIN', 'ON', 'GROUP BY', 'ORDER BY',
                    'HAVING', 'AND', 'OR', 'NOT', 'IN', 'EXISTS', 'BETWEEN', 'LIKE',
                    'IS NULL', 'IS NOT NULL', 'AS', 'DESC', 'ASC', 'UNION', 'UNION ALL',
                    'TOP', 'DISTINCT', 'INSERT INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE',
                    'EXEC', 'DECLARE', 'CAST', 'CONVERT', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END'
                ];
        }
    }

    var sqlScopeKeywords = new Set([
        'select', 'distinct', 'top', 'from', 'where', 'and', 'or', 'not', 'in', 'is', 'null',
        'like', 'between', 'exists', 'case', 'when', 'then', 'else', 'end', 'as', 'join',
        'inner', 'left', 'right', 'full', 'outer', 'cross', 'on', 'group', 'order', 'by',
        'having', 'asc', 'desc', 'union', 'all', 'set', 'values', 'into', 'update', 'delete',
        'cast', 'convert', 'over', 'partition'
    ]);

    function stripCommentsAndStringsSql(text) {
        return (text || "")
            .replace(/'(?:''|[^'])*'/g, "''")
            .replace(/--.*$/gm, " ")
            .replace(/\/\*[\s\S]*?\*\//g, " ");
    }

    function normalizeTableName(tbl) {
        if (!tbl) return "";
        return tbl.toLowerCase().replace(/[\[\]]/g, "").trim();
    }

    function tablesMatch(t1, t2) {
        var n1 = normalizeTableName(t1);
        var n2 = normalizeTableName(t2);
        if (!n1 || !n2) return false;
        if (n1 === n2) return true;
        var parts1 = n1.split('.');
        var parts2 = n2.split('.');
        var name1 = parts1[parts1.length - 1];
        var name2 = parts2[parts2.length - 1];
        if (name1 === name2) {
            if (parts1.length === 1 || parts2.length === 1) return true;
            if (parts1[0] === parts2[0]) return true;
        }
        return false;
    }

    function maskInnerParens(text, startIdx, endIdx) {
        var out = "";
        var depth = 0;
        var limit = Math.min(text.length, endIdx);
        for (var i = Math.max(0, startIdx); i < limit; i++) {
            var ch = text[i];
            if (ch === '(') {
                if (depth === 0) out += '(';
                depth++;
            } else if (ch === ')') {
                if (depth > 0) depth--;
                if (depth === 0) out += ')';
            } else if (depth === 0) {
                out += ch;
            }
        }
        return out;
    }

    function splitTopLevelCommaItems(text) {
        var items = [];
        var current = "";
        var depth = 0;
        for (var i = 0; i < text.length; i++) {
            var ch = text[i];
            if (ch === '(') depth++;
            else if (ch === ')' && depth > 0) depth--;
            if (ch === ',' && depth === 0) {
                if (current.trim()) items.push(current.trim());
                current = "";
            } else {
                current += ch;
            }
        }
        if (current.trim()) items.push(current.trim());
        return items;
    }

    function findMatchingParenIndex(text, openIdx) {
        var depth = 0;
        for (var i = openIdx; i < text.length; i++) {
            if (text[i] === '(') depth++;
            else if (text[i] === ')') {
                depth--;
                if (depth === 0) return i;
            }
        }
        return -1;
    }

    function resolveTableOrView(rawName, cteMap) {
        if (!rawName) return null;
        if (cteMap) {
            var cleanKey = normalizeTableName(rawName);
            if (cteMap[cleanKey]) return cteMap[cleanKey];
        }
        if (window.sqlTables) {
            var tbl = window.sqlTables.find(function(t) { return tablesMatch(t.fullName, rawName); });
            if (tbl) return tbl;
        }
        if (window.sqlViews) {
            var vw = window.sqlViews.find(function(v) { return tablesMatch(v.fullName, rawName); });
            if (vw) return vw;
        }
        return null;
    }

    function extractDerivedTables(rawScopeSpan, nonAliasKw, cteMap) {
        var derivedMap = {};
        if (!rawScopeSpan || rawScopeSpan.indexOf('(') === -1) return derivedMap;

        var regex = /\b(?:from|join|apply)\s*\(/gi;
        var m;
        while ((m = regex.exec(rawScopeSpan)) !== null) {
            var openIdx = rawScopeSpan.indexOf('(', m.index);
            if (openIdx < 0) continue;

            var depthBefore = 0;
            for (var d = 0; d < m.index; d++) {
                if (rawScopeSpan[d] === '(') depthBefore++;
                else if (rawScopeSpan[d] === ')' && depthBefore > 0) depthBefore--;
            }
            if (depthBefore !== 0) continue;

            var closeIdx = findMatchingParenIndex(rawScopeSpan, openIdx);
            if (closeIdx < 0) continue;

            var innerSql = rawScopeSpan.substring(openIdx + 1, closeIdx);
            var innerMasked = maskInnerParens(innerSql, 0, innerSql.length);
            if (!/^\s*SELECT\b/i.test(innerMasked)) continue;

            var afterClose = rawScopeSpan.substring(closeIdx + 1);
            var aliasMatch = afterClose.match(/^\s*(?:as\s+)?\[?([a-zA-Z_][a-zA-Z0-9_]*)\]?/i);
            if (!aliasMatch) continue;
            var alias = aliasMatch[1];
            if (!alias || nonAliasKw.has(alias.toLowerCase())) continue;

            var cols = extractCteColumns(innerSql, null, nonAliasKw, cteMap);
            derivedMap[alias.toLowerCase()] = {
                name: alias,
                schema: '',
                fullName: alias,
                columns: cols,
                primaryKeys: [],
                relations: [],
                isDerived: true
            };
        }
        return derivedMap;
    }

    function extractTableRefsFromScope(scopeMasked, nonAliasKw, cteMap, derivedMap) {
        var refs = [];
        var aliasMap = {};
        var seenKeys = new Set();

        var fromMatch = scopeMasked.match(/\bfrom\b/i);
        if (fromMatch && fromMatch.index >= 0) {
            var afterFrom = scopeMasked.substring(fromMatch.index);
            var endClauseMatch = afterFrom.match(/\b(?:where|group\s+by|order\s+by|having|set|values|output|union|except|intersect|option|for)\b/i);
            var fromClauseOnly = endClauseMatch ? afterFrom.substring(0, endClauseMatch.index) : afterFrom;

            var tableClauseRegex = /(?:from|join|apply|,)\s+(?:(\(\s*\))|([a-zA-Z0-9_\[\]\.]+))(?:\s+(?:as\s+)?([a-zA-Z0-9_\[\]]+))?/gi;
            var tMatch;
            while ((tMatch = tableClauseRegex.exec(fromClauseOnly)) !== null) {
                var isDerivedSource = !!tMatch[1];
                var rawTableName = (tMatch[2] || "").trim();
                var rawAlias = (tMatch[3] || "").trim();
                var cleanAlias = rawAlias.replace(/[\[\]]/g, '');
                var isRealAlias = cleanAlias && !nonAliasKw.has(cleanAlias.toLowerCase());
                var alias = isRealAlias ? cleanAlias : "";

                var matchedObj = null;
                if (isDerivedSource) {
                    if (alias && derivedMap && derivedMap[alias.toLowerCase()]) {
                        matchedObj = derivedMap[alias.toLowerCase()];
                    }
                } else {
                    matchedObj = resolveTableOrView(rawTableName, cteMap);
                }

                if (matchedObj) {
                    if (alias) {
                        aliasMap[alias] = matchedObj.fullName;
                    }
                    var refKey = (matchedObj.fullName || rawTableName || alias).toLowerCase() + '::' + alias.toLowerCase();
                    if (!seenKeys.has(refKey)) {
                        seenKeys.add(refKey);
                        refs.push({
                            tableObj: matchedObj,
                            alias: alias,
                            qualifier: alias || matchedObj.name || matchedObj.fullName
                        });
                    }
                } else if (alias && rawTableName) {
                    aliasMap[alias] = rawTableName;
                }
            }
        } else {
            var dmlTableRegex = /(?:update|delete(?:\s+top\s*\(\s*[^)]*\s*\))?|insert(?:\s+into)?)\s+([a-zA-Z0-9_\[\]\.]+)/gi;
            var dMatch;
            while ((dMatch = dmlTableRegex.exec(scopeMasked)) !== null) {
                var dmlTableName = (dMatch[1] || "").trim();
                var matchedDml = resolveTableOrView(dmlTableName, cteMap);
                if (matchedDml) {
                    var dmlKey = (matchedDml.fullName || dmlTableName).toLowerCase() + '::';
                    if (!seenKeys.has(dmlKey)) {
                        seenKeys.add(dmlKey);
                        refs.push({
                            tableObj: matchedDml,
                            alias: "",
                            qualifier: matchedDml.name || matchedDml.fullName
                        });
                    }
                }
            }
        }

        return { refs: refs, aliasMap: aliasMap };
    }

    function extractCteColumns(cteBody, explicitCols, nonAliasKw, cteMap) {
        if (explicitCols && explicitCols.length > 0) return explicitCols;

        var masked = maskInnerParens(cteBody, 0, cteBody.length);
        var selectMatch = masked.match(/^\s*SELECT\b(?:\s+(?:ALL|DISTINCT))?(?:\s+TOP\s*\(\s*\)\s*(?:PERCENT\s*)?(?:WITH\s+TIES\s*)?|\s+TOP\s+\d+\s*(?:PERCENT\s*)?(?:WITH\s+TIES\s*)?)?/i);
        if (!selectMatch) return [];

        var bodyAfterSelect = masked.substring(selectMatch[0].length);
        var fromInCte = bodyAfterSelect.search(/\bfrom\b/i);
        var selectListText = fromInCte >= 0 ? bodyAfterSelect.substring(0, fromInCte) : bodyAfterSelect;

        var cteRefs = null;
        var cols = [];
        var seenCols = new Set();
        var items = splitTopLevelCommaItems(selectListText);

        items.forEach(function(item) {
            var trimmed = item.trim();
            if (!trimmed) return;

            if (trimmed === '*' || /^[a-zA-Z0-9_\[\]]+\.\*$/.test(trimmed)) {
                if (!cteRefs) cteRefs = extractTableRefsFromScope(masked, nonAliasKw, cteMap, extractDerivedTables(cteBody, nonAliasKw, cteMap)).refs;
                var wildQualifier = trimmed.indexOf('.') > 0 ? trimmed.split('.')[0].replace(/[\[\]]/g, '').toLowerCase() : null;
                cteRefs.forEach(function(r) {
                    if (wildQualifier && (r.alias || '').toLowerCase() !== wildQualifier && (r.tableObj.name || '').toLowerCase() !== wildQualifier) {
                        return;
                    }
                    (r.tableObj.columns || []).forEach(function(c) {
                        if (!seenCols.has(c.toLowerCase())) {
                            seenCols.add(c.toLowerCase());
                            cols.push(c);
                        }
                    });
                });
                return;
            }

            var colName = null;
            var asMatch = trimmed.match(/\bAS\s+\[?([a-zA-Z0-9_]+)\]?\s*$/i);
            if (asMatch) {
                colName = asMatch[1];
            } else if (/^(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?([a-zA-Z0-9_]+)\]?$/.test(trimmed)) {
                colName = trimmed.match(/\[?([a-zA-Z0-9_]+)\]?$/)[1];
            } else {
                var trailingAlias = trimmed.match(/[\s\)]\[?([a-zA-Z0-9_]+)\]?\s*$/);
                if (trailingAlias && !sqlScopeKeywords.has(trailingAlias[1].toLowerCase())) {
                    colName = trailingAlias[1];
                }
            }

            if (colName && !seenCols.has(colName.toLowerCase())) {
                seenCols.add(colName.toLowerCase());
                cols.push(colName);
            }
        });

        return cols;
    }

    function parseCtesFromStatement(cleanStatement, nonAliasKw) {
        var cteMap = {};
        var cteList = [];
        var withMatch = cleanStatement.match(/^\s*;?\s*WITH\b/i);
        if (!withMatch) {
            return { cteMap: cteMap, cteList: cteList, afterCtesOffset: 0 };
        }

        var pos = withMatch[0].length;
        var afterCtesOffset = 0;

        while (pos < cleanStatement.length) {
            var rest = cleanStatement.substring(pos);
            var cteHeaderMatch = rest.match(/^\s*\[?([a-zA-Z_][a-zA-Z0-9_]*)\]?\s*(?:\(([^)]*)\)\s*)?AS\s*\(/i);
            if (!cteHeaderMatch) break;

            var cteName = cteHeaderMatch[1];
            var explicitCols = null;
            if (cteHeaderMatch[2]) {
                explicitCols = cteHeaderMatch[2]
                    .split(',')
                    .map(function(c) { return c.replace(/[\[\]]/g, '').trim(); })
                    .filter(Boolean);
            }

            var openParenIdx = pos + cteHeaderMatch[0].length - 1;
            var closeParenIdx = findMatchingParenIndex(cleanStatement, openParenIdx);
            if (closeParenIdx < 0) break;

            var cteBody = cleanStatement.substring(openParenIdx + 1, closeParenIdx);
            var cteCols = extractCteColumns(cteBody, explicitCols, nonAliasKw, cteMap);
            var cteObj = {
                name: cteName,
                schema: '',
                fullName: cteName,
                columns: cteCols,
                primaryKeys: [],
                relations: [],
                isCte: true,
                bodyStart: openParenIdx + 1,
                bodyEnd: closeParenIdx
            };
            cteMap[cteName.toLowerCase()] = cteObj;
            cteList.push(cteObj);

            pos = closeParenIdx + 1;
            afterCtesOffset = pos;

            var commaCheck = cleanStatement.substring(pos).match(/^\s*,/);
            if (commaCheck) {
                pos += commaCheck[0].length;
            } else {
                break;
            }
        }

        return { cteMap: cteMap, cteList: cteList, afterCtesOffset: afterCtesOffset };
    }

    function extractReferencedColumns(scopeMasked) {
        var qualified = new Set();
        var names = new Set();
        var selectQualified = new Set();
        var selectNames = new Set();

        var fromMatch = scopeMasked.match(/\bfrom\b/i);
        var selectEnd = fromMatch ? fromMatch.index : -1;
        var selectPart = selectEnd > 0 ? scopeMasked.substring(0, selectEnd) : "";

        // Exclude FROM...JOIN... table & alias declarations from referenced column tokens
        var scanPart = scopeMasked;
        if (fromMatch && fromMatch.index >= 0) {
            var afterFrom = scopeMasked.substring(fromMatch.index);
            var nextClause = afterFrom.match(/\b(?:where|group\s+by|order\s+by|having|set|values|output|on)\b/i);
            var beforeFrom = scopeMasked.substring(0, fromMatch.index);
            var afterTableList = nextClause ? afterFrom.substring(nextClause.index) : "";
            scanPart = beforeFrom + " " + afterTableList;
        }

        function collectFromSlice(slice, qSet, nSet) {
            var identRegex = /(?:\[?([a-zA-Z_][a-zA-Z0-9_]*)\]?\.)?\[?([a-zA-Z_][a-zA-Z0-9_]*)\]?/g;
            var m;
            while ((m = identRegex.exec(slice)) !== null) {
                var colLower = m[2].toLowerCase();
                if (sqlScopeKeywords.has(colLower)) continue;
                if (m[1]) {
                    qSet.add(m[1].toLowerCase() + '.' + colLower);
                } else {
                    nSet.add(colLower);
                }
            }
        }

        if (selectPart) collectFromSlice(selectPart, selectQualified, selectNames);
        collectFromSlice(scanPart, qualified, names);

        return {
            qualified: qualified,
            names: names,
            selectQualified: selectQualified,
            selectNames: selectNames
        };
    }

    function buildKeyColumnSet(tableObj) {
        var keySet = new Set();
        if (!tableObj) return keySet;
        if (Array.isArray(tableObj.primaryKeys)) {
            tableObj.primaryKeys.forEach(function(pk) {
                if (pk) keySet.add(pk.toLowerCase());
            });
        }
        if (Array.isArray(tableObj.relations)) {
            tableObj.relations.forEach(function(r) {
                if (r && r.fromCol) keySet.add(r.fromCol.toLowerCase());
            });
        }
        if (window.sqlTables) {
            window.sqlTables.forEach(function(other) {
                if (Array.isArray(other.relations)) {
                    other.relations.forEach(function(r) {
                        if (r && r.toCol && (tablesMatch(tableObj.fullName, r.toTable) || tablesMatch(tableObj.name, r.toTable))) {
                            keySet.add(r.toCol.toLowerCase());
                        }
                    });
                }
            });
        }
        return keySet;
    }

    function buildFkRelatedTableSet(refs) {
        var relatedSet = new Set();
        if (!refs || refs.length === 0) return relatedSet;

        refs.forEach(function(r) {
            var tbl = r.tableObj;
            if (!tbl) return;
            if (Array.isArray(tbl.relations)) {
                tbl.relations.forEach(function(rel) {
                    if (rel && rel.toTable) relatedSet.add(normalizeTableName(rel.toTable));
                });
            }
            if (window.sqlTables) {
                window.sqlTables.forEach(function(other) {
                    if (Array.isArray(other.relations)) {
                        other.relations.forEach(function(rel) {
                            if (rel && (tablesMatch(tbl.fullName, rel.toTable) || tablesMatch(tbl.name, rel.toTable))) {
                                relatedSet.add(normalizeTableName(other.fullName));
                                if (other.name) relatedSet.add(normalizeTableName(other.name));
                            }
                        });
                    }
                });
            }
        });
        return relatedSet;
    }

    var sqlNonAliasKw = new Set([
        'where', 'on', 'join', 'inner', 'left', 'right', 'full', 'outer', 'cross',
        'group', 'order', 'having', 'select', 'as', 'apply', 'from', 'set',
        'union', 'with', 'limit', 'offset', 'top', 'into', 'values', 'table', 'and', 'or', 'not'
    ]);

    function resolveStatementScope(statementText, textUntilCursor, nonAliasKw) {
        var kwSet = nonAliasKw || sqlNonAliasKw;
        var untilRaw = textUntilCursor || "";
        var afterRaw = (statementText || "").substring(untilRaw.length);
        var cleanUntilCursor = stripCommentsAndStringsSql(untilRaw);
        var cleanAfterCursor = stripCommentsAndStringsSql(afterRaw);
        var cleanFullStmt = cleanUntilCursor + cleanAfterCursor;
        var cursorOffset = cleanUntilCursor.length;

        var cteInfo = parseCtesFromStatement(cleanFullStmt, kwSet);

        var parenStack = [];
        for (var i = 0; i < cursorOffset; i++) {
            if (cleanFullStmt[i] === '(') parenStack.push(i);
            else if (cleanFullStmt[i] === ')' && parenStack.length > 0) parenStack.pop();
        }

        var topScopeStart = (cteInfo.afterCtesOffset > 0 && cursorOffset >= cteInfo.afterCtesOffset)
            ? cteInfo.afterCtesOffset
            : 0;

        function buildVisibleCteMap(offset) {
            var visible = {};
            cteInfo.cteList.forEach(function(cte) {
                if (cte.bodyEnd <= offset || offset >= cteInfo.afterCtesOffset) {
                    visible[cte.name.toLowerCase()] = cte;
                }
            });
            return visible;
        }

        var activeScopeStart = topScopeStart;
        var activeScopeEnd = cleanFullStmt.length;
        var outerScopes = [];
        var foundInnerQueryScope = false;

        for (var p = parenStack.length - 1; p >= 0; p--) {
            var openIdx = parenStack[p];
            var closeIdx = findMatchingParenIndex(cleanFullStmt, openIdx);
            var spanEnd = closeIdx > openIdx ? closeIdx : cleanFullStmt.length;
            var spanMasked = maskInnerParens(cleanFullStmt, openIdx + 1, spanEnd);
            if (/^\s*(?:SELECT|WITH|UPDATE|DELETE|INSERT|MERGE)\b/i.test(spanMasked)) {
                if (!foundInnerQueryScope) {
                    activeScopeStart = openIdx + 1;
                    activeScopeEnd = spanEnd;
                    foundInnerQueryScope = true;
                } else {
                    outerScopes.push({
                        start: openIdx + 1,
                        end: spanEnd,
                        cteMap: buildVisibleCteMap(openIdx + 1)
                    });
                }
            }
        }

        if (foundInnerQueryScope && cursorOffset >= cteInfo.afterCtesOffset && topScopeStart < activeScopeStart) {
            outerScopes.push({
                start: topScopeStart,
                end: cleanFullStmt.length,
                cteMap: cteInfo.cteMap
            });
        }

        var activeCteMap = buildVisibleCteMap(activeScopeStart);
        var rawScopeSpan = cleanFullStmt.substring(activeScopeStart, activeScopeEnd);
        var derivedMap = extractDerivedTables(rawScopeSpan, kwSet, activeCteMap);
        var scopeMasked = maskInnerParens(cleanFullStmt, activeScopeStart, activeScopeEnd);
        var scopeUntilCursorMasked = maskInnerParens(cleanFullStmt, activeScopeStart, cursorOffset);
        var primaryParsed = extractTableRefsFromScope(scopeMasked, kwSet, activeCteMap, derivedMap);
        var referencedCols = extractReferencedColumns(scopeMasked);

        var outerAliasMap = {};
        var outerDerivedMap = {};
        outerScopes.forEach(function(os) {
            var oRawSpan = cleanFullStmt.substring(os.start, os.end);
            var oDerived = extractDerivedTables(oRawSpan, kwSet, os.cteMap);
            Object.keys(oDerived).forEach(function(dk) {
                if (!outerDerivedMap[dk]) outerDerivedMap[dk] = oDerived[dk];
            });
            var oMasked = maskInnerParens(cleanFullStmt, os.start, os.end);
            var oParsed = extractTableRefsFromScope(oMasked, kwSet, os.cteMap, oDerived);
            Object.keys(oParsed.aliasMap).forEach(function(k) {
                if (!outerAliasMap[k]) outerAliasMap[k] = oParsed.aliasMap[k];
            });
        });

        return {
            cleanFullStmt: cleanFullStmt,
            cleanUntilCursor: cleanUntilCursor,
            cursorOffset: cursorOffset,
            cteMap: activeCteMap,
            cteList: cteInfo.cteList,
            derivedMap: derivedMap,
            outerDerivedMap: outerDerivedMap,
            afterCtesOffset: cteInfo.afterCtesOffset,
            scopeStart: activeScopeStart,
            scopeEnd: activeScopeEnd,
            scopeMasked: scopeMasked,
            scopeUntilCursorMasked: scopeUntilCursorMasked,
            activeTableRefs: primaryParsed.refs,
            aliasMap: primaryParsed.aliasMap,
            outerAliasMap: outerAliasMap,
            referencedCols: referencedCols
        };
    }

    function isColumnReferenced(qSet, nSet, qual, alias, colLower) {
        if (qual && qSet && qSet.has(qual.toLowerCase() + '.' + colLower)) return true;
        if (alias && qSet && qSet.has(alias.toLowerCase() + '.' + colLower)) return true;
        if (nSet && nSet.has(colLower)) return true;
        return false;
    }

    function computeColumnSortText(opts) {
        var colLower = (opts.colName || "").toLowerCase();
        var refPad = String(opts.refPriorityIdx || 0).padStart(2, '0');
        var colPad = String(opts.colIdx || 0).padStart(4, '0');
        var tail = refPad + '_' + colPad + '_' + opts.colName;
        var refs = opts.referencedCols;

        var isSelectRef = refs && isColumnReferenced(refs.selectQualified, refs.selectNames, opts.qual, opts.alias, colLower);
        var isScopeRef = refs && isColumnReferenced(refs.qualified, refs.names, opts.qual, opts.alias, colLower);

        if (opts.isOnContext) {
            return (opts.isKeyCol ? '0001_0_' : '0002_') + tail;
        }
        if (opts.clause === 'GROUP BY' || opts.clause === 'ORDER BY') {
            if (isSelectRef) return '0001_0_' + tail;
            if (isScopeRef || opts.isKeyCol) return '0001_1_' + tail;
            return '0002_' + tail;
        }
        if (opts.clause === 'WHERE' || opts.clause === 'HAVING' || opts.clause === 'SET') {
            if (isScopeRef) return '0001_0_' + tail;
            if (opts.isKeyCol) return '0001_1_' + tail;
            return '0002_' + tail;
        }
        if (opts.isKeyCol) return '0001_0_' + tail;
        return '0002_' + tail;
    }

    var postTableClauseKeywords = [
        'where', 'on', 'join', 'inner', 'left', 'right', 'full', 'outer', 'cross',
        'group', 'order', 'having', 'union', 'except', 'intersect', 'option', 'for', 'set'
    ];

    function isTypingTableAlias(textBeforeCursor, isCompletedSingleTableDelete) {
        if (isCompletedSingleTableDelete) return false;
        var m = (textBeforeCursor || "").match(/(?:from|join)\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s+(?:(as)\s+)?([a-zA-Z0-9_]*)$/i);
        if (!m) return false;
        if (m[1]) return true;
        var typedToken = (m[2] || "").toLowerCase();
        if (!typedToken) return true;
        if (typedToken === 'on' || typedToken === 'as') return false;
        if (typedToken.length >= 3 && postTableClauseKeywords.some(function(kw) { return kw.indexOf(typedToken) === 0; })) {
            return false;
        }
        return true;
    }

    var sqlBuiltinFunctions = [
        { name: 'COUNT', snippet: 'COUNT($0)', detail: 'Built-in Function' },
        { name: 'SUM', snippet: 'SUM($0)', detail: 'Built-in Function' },
        { name: 'AVG', snippet: 'AVG($0)', detail: 'Built-in Function' },
        { name: 'MIN', snippet: 'MIN($0)', detail: 'Built-in Function' },
        { name: 'MAX', snippet: 'MAX($0)', detail: 'Built-in Function' },
        { name: 'COUNT_BIG', snippet: 'COUNT_BIG($0)', detail: 'Built-in Function' },
        { name: 'ROW_NUMBER', snippet: 'ROW_NUMBER() OVER ($0)', detail: 'Built-in Function' },
        { name: 'RANK', snippet: 'RANK() OVER ($0)', detail: 'Built-in Function' },
        { name: 'DENSE_RANK', snippet: 'DENSE_RANK() OVER ($0)', detail: 'Built-in Function' },
        { name: 'ISNULL', snippet: 'ISNULL($1, $0)', detail: 'Built-in Function' },
        { name: 'COALESCE', snippet: 'COALESCE($0)', detail: 'Built-in Function' },
        { name: 'NULLIF', snippet: 'NULLIF($1, $0)', detail: 'Built-in Function' },
        { name: 'IIF', snippet: 'IIF($1, $2, $0)', detail: 'Built-in Function' },
        { name: 'TRY_CAST', snippet: 'TRY_CAST($1 AS $0)', detail: 'Built-in Function' },
        { name: 'TRY_CONVERT', snippet: 'TRY_CONVERT($1, $0)', detail: 'Built-in Function' },
        { name: 'GETDATE', snippet: 'GETDATE()', detail: 'Built-in Function' },
        { name: 'GETUTCDATE', snippet: 'GETUTCDATE()', detail: 'Built-in Function' },
        { name: 'SYSDATETIME', snippet: 'SYSDATETIME()', detail: 'Built-in Function' },
        { name: 'DATEADD', snippet: 'DATEADD($1, $2, $0)', detail: 'Built-in Function' },
        { name: 'DATEDIFF', snippet: 'DATEDIFF($1, $2, $0)', detail: 'Built-in Function' },
        { name: 'DATETRUNC', snippet: 'DATETRUNC($1, $0)', detail: 'Built-in Function' },
        { name: 'YEAR', snippet: 'YEAR($0)', detail: 'Built-in Function' },
        { name: 'MONTH', snippet: 'MONTH($0)', detail: 'Built-in Function' },
        { name: 'DAY', snippet: 'DAY($0)', detail: 'Built-in Function' },
        { name: 'EOMONTH', snippet: 'EOMONTH($0)', detail: 'Built-in Function' },
        { name: 'LEN', snippet: 'LEN($0)', detail: 'Built-in Function' },
        { name: 'TRIM', snippet: 'TRIM($0)', detail: 'Built-in Function' },
        { name: 'LTRIM', snippet: 'LTRIM($0)', detail: 'Built-in Function' },
        { name: 'RTRIM', snippet: 'RTRIM($0)', detail: 'Built-in Function' },
        { name: 'LOWER', snippet: 'LOWER($0)', detail: 'Built-in Function' },
        { name: 'UPPER', snippet: 'UPPER($0)', detail: 'Built-in Function' },
        { name: 'SUBSTRING', snippet: 'SUBSTRING($1, $2, $0)', detail: 'Built-in Function' },
        { name: 'REPLACE', snippet: 'REPLACE($1, $2, $0)', detail: 'Built-in Function' },
        { name: 'CHARINDEX', snippet: 'CHARINDEX($1, $0)', detail: 'Built-in Function' },
        { name: 'CONCAT', snippet: 'CONCAT($0)', detail: 'Built-in Function' },
        { name: 'STRING_AGG', snippet: 'STRING_AGG($1, $0)', detail: 'Built-in Function' },
        { name: 'FORMAT', snippet: 'FORMAT($1, $0)', detail: 'Built-in Function' },
        { name: 'ROUND', snippet: 'ROUND($1, $0)', detail: 'Built-in Function' },
        { name: 'ABS', snippet: 'ABS($0)', detail: 'Built-in Function' },
        { name: 'CEILING', snippet: 'CEILING($0)', detail: 'Built-in Function' },
        { name: 'FLOOR', snippet: 'FLOOR($0)', detail: 'Built-in Function' },
        { name: 'NEWID', snippet: 'NEWID()', detail: 'Built-in Function' },
        { name: 'SCOPE_IDENTITY', snippet: 'SCOPE_IDENTITY()', detail: 'Built-in Function' }
    ];

    function findActiveExecProcedure(scanText) {
        if (!scanText) return null;
        var execRegex = /\b(?:exec|execute)\s+(?:@?[a-zA-Z0-9_]+\s*=\s*)?((?:\[?[a-zA-Z_][a-zA-Z0-9_]*\]?\.)*\[?[a-zA-Z_][a-zA-Z0-9_]*\]?)\s+([^;]*)$/i;
        var m = scanText.match(execRegex);
        if (!m) return null;
        var rawProc = m[1];
        var afterProc = m[2] || "";
        var matchedSp = null;
        if (window.sqlSps) {
            matchedSp = window.sqlSps.find(function(sp) {
                return tablesMatch(sp.fullName, rawProc) || tablesMatch(sp.name, rawProc);
            });
        }
        return {
            rawProc: rawProc,
            matchedSp: matchedSp,
            afterProc: afterProc
        };
    }

    monaco.languages.registerCompletionItemProvider('sql', {
        triggerCharacters: ['.', "'", '[', ']', ' '],
        provideCompletionItems: function(model, position, context) {
            var lineContent = model.getLineContent(position.lineNumber);
            var textBeforeCursor = lineContent.substring(0, position.column - 1);

            var ctx = getSpHelptextContext(model, position);
            if (ctx) {
                var getParamInsertText = function(fullName) {
                    if (ctx.quoteChar === "'" || ctx.quoteChar === '"') {
                        return ctx.hasClosingQuote ? fullName : (fullName + ctx.quoteChar);
                    }
                    return "'" + fullName + "'";
                };

                var helpTextSuggestions = [];

                // 1. Stored Procedures
                if (window.sqlSps) {
                    window.sqlSps.forEach(sp => {
                        var unbracketed = sp.fullName.replace(/[\[\]]/g, "");
                        helpTextSuggestions.push({
                            label: sp.fullName,
                            kind: monaco.languages.CompletionItemKind.Method,
                            insertText: getParamInsertText(sp.fullName),
                            filterText: sp.fullName + " " + sp.name + " " + unbracketed,
                            sortText: String(sp.name.length).padStart(4, '0') + '_' + sp.name,
                            range: ctx.replaceRange,
                            detail: 'Stored Procedure'
                        });
                    });
                }

                // 2. Views
                if (window.sqlViews) {
                    window.sqlViews.forEach(v => {
                        var unbracketed = v.fullName.replace(/[\[\]]/g, "");
                        helpTextSuggestions.push({
                            label: v.fullName,
                            kind: monaco.languages.CompletionItemKind.Class,
                            insertText: getParamInsertText(v.fullName),
                            filterText: v.fullName + " " + v.name + " " + unbracketed,
                            sortText: String(v.name.length).padStart(4, '0') + '_' + v.name,
                            range: ctx.replaceRange,
                            detail: 'View'
                        });
                    });
                }

                // 3. Functions
                if (window.sqlFunctions) {
                    window.sqlFunctions.forEach(f => {
                        var unbracketed = f.fullName.replace(/[\[\]]/g, "");
                        helpTextSuggestions.push({
                            label: f.fullName,
                            kind: monaco.languages.CompletionItemKind.Function,
                            insertText: getParamInsertText(f.fullName),
                            filterText: f.fullName + " " + f.name + " " + unbracketed,
                            sortText: String(f.name.length).padStart(4, '0') + '_' + f.name,
                            range: ctx.replaceRange,
                            detail: 'Function'
                        });
                    });
                }

                return { suggestions: helpTextSuggestions };
            }

            // Suppress SQL keyword completions inside normal string literals (not sp_helptext)
            var quoteCount = (textBeforeCursor.match(/'/g) || []).length;
            if (quoteCount % 2 === 1) {
                return { suggestions: [] };
            }
            // Match something like "ou.id" or just "id"
            var match = textBeforeCursor.match(/([a-zA-Z0-9_]+(?:\.[a-zA-Z0-9_]*)?)$/);
            var fullTypedWord = match ? match[1] : "";
            var replaceRange = {
                startLineNumber: position.lineNumber,
                endLineNumber: position.lineNumber,
                startColumn: position.column - fullTypedWord.length,
                endColumn: position.column
            };

            var textUntilPosition = model.getValueInRange({
                startLineNumber: position.lineNumber,
                startColumn: 1,
                endLineNumber: position.lineNumber,
                endColumn: position.column
            });

            var matchWithDot = textUntilPosition.match(/(?:\[?[a-zA-Z0-9_]+\]?\.)\[?[a-zA-Z0-9_]*\]?$/);
            var matchBracketed = textUntilPosition.match(/\[[a-zA-Z0-9_]*\]?$/);
            if (matchWithDot) {
                replaceRange = {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: position.column - matchWithDot[0].length,
                    endColumn: position.column
                };
            } else if (matchBracketed) {
                replaceRange = {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: position.column - matchBracketed[0].length,
                    endColumn: position.column
                };
            }

            // Intelligent context detection scoped to active statement
            var stmtContext = getActiveStatementContext(model, position);
            var fullTextUntilPosition = stmtContext.textUntilCursor;
            var cleanTriggerText = stripCommentsAndStringsSql(fullTextUntilPosition);
            var clauseInfo = analyzeClauseContext(fullTextUntilPosition);

            var isSpaceTrigger = context && context.triggerCharacter === ' ';
            var isOnContext = /\bon\s*$/i.test(cleanTriggerText) || /\bon\s+[a-zA-Z0-9_\[\]\.]*$/i.test(cleanTriggerText);
            if (isSpaceTrigger) {
                var isTriggerKeywordBeforeSpace = /(?:\b(?:on|join|from|into|update|delete|where|having|set|by|select|and|or|values|exec|execute)\b|[,(]|insert(?:\s+into)?\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s*\([^)]*\))\s*$/i.test(cleanTriggerText)
                    || !!findActiveExecProcedure(cleanTriggerText);
                if (!isTriggerKeywordBeforeSpace) {
                    return { suggestions: [] };
                }
            }

            var scopeInfo = resolveStatementScope(stmtContext.statementText, fullTextUntilPosition, sqlNonAliasKw);
            var cleanStmtUntilCursor = scopeInfo.cleanUntilCursor;
            var hasBlockTableRefs = scopeInfo.activeTableRefs.length > 0;
            var hasExplicitDotOnLine = /\.[a-zA-Z0-9_]*$/.test(textBeforeCursor);
            var isSingleTableDeleteTarget = /^\s*DELETE(?:\s+TOP\s*\(\s*[^)]*\s*\))?(?:\s+FROM)?\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]*\]?$/i.test(cleanStmtUntilCursor);
            var isCompletedSingleTableDelete = /^\s*DELETE(?:\s+TOP\s*\(\s*[^)]*\s*\))?\s+(?:FROM\s+)?(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s+(?:WITH\s*\(\s*[^)]*\s*\)\s+)?[a-zA-Z0-9_]*$/i.test(cleanStmtUntilCursor);
            clauseInfo.isCompletedSingleTableDelete = isCompletedSingleTableDelete;

            var isInsertContext = /(?:^|[\s;])INSERT(?:\s+INTO)?\s+[a-zA-Z0-9_\[\]\.]*$/i.test(cleanStmtUntilCursor)
                && cleanStmtUntilCursor.indexOf('(') === -1
                && !/\b(?:VALUES|SELECT)\b/i.test(cleanStmtUntilCursor);

            var suggestions = [];

            var isMidQueryClause = clauseInfo.clause !== 'SELECT' || hasBlockTableRefs || clauseInfo.inParens;
            var hasStartedSelect = /^\s*SELECT\b/i.test(scopeInfo.scopeUntilCursorMasked);
            if (!isInsertContext && !isOnContext && !hasExplicitDotOnLine && !isMidQueryClause && !hasStartedSelect) {
                if (window.sqlSnippets && window.sqlSnippets.length > 0) {
                    window.sqlSnippets.forEach(function(s) {
                        suggestions.push({
                            label: s.shortcut,
                            kind: monaco.languages.CompletionItemKind.Snippet,
                            insertText: s.code,
                            documentation: s.code,
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            sortText: '0000_0_' + s.shortcut
                        });
                    });
                } else {
                    suggestions.push({
                        label: 'ssf',
                        kind: monaco.languages.CompletionItemKind.Snippet,
                        insertText: 'SELECT * FROM $0',
                        documentation: 'SELECT * FROM snippet',
                        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                        sortText: '0000_0_ssf'
                    });
                }
            }

            // PRIORITY CHECK: Suppress suggestions when typing a short/explicit table alias on the same line,
            // while allowing 3+ char clause prefixes (e.g., FROM Orders WHE -> WHERE).
            // Never suppress for single-table DELETE FROM <table>, where T-SQL forbids aliases and WHERE follows.
            var hasDotOnCurrentLine = /\.[a-zA-Z0-9_]*$/.test(textBeforeCursor);
            if (!hasDotOnCurrentLine && isTypingTableAlias(textBeforeCursor, isCompletedSingleTableDelete)) {
                return { suggestions: [] };
            }

            var scanText = scopeInfo.scopeUntilCursorMasked || "";
            var matches = scanText.match(/\b[a-z_][a-z0-9_]*\b/ig);
            var suggestTables = true;
            var suggestColumns = true;
            var suggestSps = false;
            var suggestKeywords = true;

            var lastTableKeyword = null;
            var activeExecProc = (clauseInfo.clause !== 'INSERT_COLUMNS' && clauseInfo.clause !== 'VALUES')
                ? findActiveExecProcedure(scanText)
                : null;

            if (activeExecProc) {
                suggestTables = false;
                suggestColumns = false;
                suggestSps = false;
                suggestKeywords = false;
            } else if (clauseInfo.clause === 'INSERT_COLUMNS') {
                suggestTables = false;
                suggestColumns = true;
                suggestSps = false;
                suggestKeywords = false;
            } else if (clauseInfo.clause === 'VALUES') {
                suggestTables = false;
                suggestColumns = false;
                suggestSps = false;
                suggestKeywords = true;
            } else if (matches && matches.length > 0) {
                var tableKeywords = new Set(['from', 'join', 'into', 'insert', 'update', 'delete', 'table']);
                var columnKeywords = new Set(['select', 'where', 'on', 'by', 'having', 'set', 'and', 'or']);
                var spKeywords = new Set(['exec', 'execute']);

                for (var i = matches.length - 1; i >= 0; i--) {
                    var token = matches[i].toLowerCase();
                    if (spKeywords.has(token)) {
                        suggestTables = false;
                        suggestColumns = false;
                        suggestSps = true;
                        if (i >= matches.length - 2) suggestKeywords = false;
                        break;
                    } else if (tableKeywords.has(token)) {
                        lastTableKeyword = token;
                        var lastTableKwMatch = null;
                        var tableKwRegex = new RegExp('\\b' + token + '\\b', 'gi');
                        var tkwm;
                        while ((tkwm = tableKwRegex.exec(scanText)) !== null) {
                            lastTableKwMatch = tkwm;
                        }
                        var textAfterTableKw = lastTableKwMatch ? scanText.substring(lastTableKwMatch.index) : scanText;

                        var completedTableRegex = /(?:from|join|update|delete|into|table)\s+(?:(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?|\(\s*\))(?:\s+|\s*\([^)]*\)\s+)/i;
                        var tableCompletedMatch = textAfterTableKw.match(completedTableRegex);
                        var hasCommaAfter = /(?:from|join)\s+[^;]*,\s*([a-zA-Z0-9_\[\]\.]*)$/i.test(textAfterTableKw);

                        if (tableCompletedMatch && !hasCommaAfter) {
                            suggestTables = false;
                            suggestColumns = false;
                            suggestSps = false;
                            suggestKeywords = true;
                        } else {
                            suggestTables = true;
                            suggestColumns = false;
                            suggestSps = false;
                            if (i >= matches.length - 2 && token !== 'delete') suggestKeywords = false;
                        }
                        break;
                    } else if (columnKeywords.has(token)) {
                        suggestTables = false;
                        suggestColumns = true;
                        suggestSps = false;
                        break;
                    }
                }
            }

            if (isInsertContext) {
                suggestTables = true;
                suggestColumns = false;
                suggestSps = false;
                suggestKeywords = false;
            }

            if (isOnContext) {
                suggestTables = false;
                suggestColumns = true;
                suggestSps = false;
                suggestKeywords = false;
            }

            var autoAlias = (window.userSettings ? window.userSettings.autoGenerateAliases !== false : true) &&
                            (lastTableKeyword === 'join' || (lastTableKeyword === 'from' && !isSingleTableDeleteTarget));
            var useAs = window.userSettings ? window.userSettings.useAsKeyword !== false : true;

            var existingAliases = new Set();
            if (autoAlias) {
                Object.keys(scopeInfo.aliasMap || {}).forEach(function(al) {
                    if (al) existingAliases.add(al.toUpperCase());
                });
                var aliasRegex = /(?:from|join|update)\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s+(?:as\s+)?([a-zA-Z0-9_]+)/gi;
                var aMatch;
                while ((aMatch = aliasRegex.exec(scopeInfo.scopeMasked)) !== null) {
                    if (aMatch[1]) {
                        var u = aMatch[1].trim().toUpperCase();
                        if (!sqlNonAliasKw.has(u.toLowerCase())) {
                            existingAliases.add(u);
                        }
                    }
                }
            }

            var fkRelatedTables = (suggestTables && lastTableKeyword === 'join')
                ? buildFkRelatedTableSet(scopeInfo.activeTableRefs)
                : new Set();

            if (suggestTables && !isInsertContext && scopeInfo.cteMap) {
                Object.keys(scopeInfo.cteMap).forEach(function(cteKey) {
                    var cte = scopeInfo.cteMap[cteKey];
                    var cteInsText = cte.fullName;
                    var cteAlias = null;
                    if (autoAlias) {
                        cteAlias = window.generateTableAlias(cte.name, existingAliases);
                        cteInsText = cte.fullName + (useAs ? " AS " : " ") + cteAlias;
                    }
                    var cteFilterStr = cte.name + " [" + cte.name + "]" + (autoAlias && cteAlias ? (" " + cteAlias) : "");
                    suggestions.push({
                        label: cte.fullName,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: cteInsText,
                        filterText: cteFilterStr,
                        sortText: '0000_1_' + String(cte.name.length).padStart(4, '0') + '_' + cte.name,
                        range: replaceRange,
                        detail: 'CTE'
                    });
                });
            }

            if (suggestTables && window.sqlTables) {
                window.sqlTables.forEach(t => {
                    var insText = t.fullName;
                    var detailText = 'Table';
                    var alias = null;

                    if (isInsertContext) {
                        var cols = (t.insertColumns && t.insertColumns.length > 0) ? t.insertColumns : (t.columns || []);
                        var colList = cols.length > 0
                            ? cols.map(function(c) { return '    [' + c.replace(/[\[\]]/g, '') + ']'; }).join(',\n')
                            : '    /* ColumnList */';
                        var valList = cols.length > 0
                            ? cols.map(function(c) {
                                var paramName = c.replace(/[\[\]]/g, '').replace(/[^a-zA-Z0-9_]/g, '');
                                return '    @' + paramName;
                            }).join(',\n')
                            : '    /* Values */';

                        insText = t.fullName + '\n(\n' + colList + '\n)\nVALUES\n(\n' + valList + '\n);';
                        detailText = 'Table';
                    } else if (autoAlias) {
                        alias = window.generateTableAlias(t.name || t.fullName, existingAliases);
                        insText = t.fullName + (useAs ? " AS " : " ") + alias;
                        detailText = 'Table';
                    }

                    var filterStr = t.name + " [" + t.name + "] " + t.fullName + (t.schema ? (" " + t.schema + "." + t.name) : "") + (autoAlias && alias ? (" " + alias) : "");
                    var isFkMatch = lastTableKeyword === 'join' && (fkRelatedTables.has(normalizeTableName(t.fullName)) || fkRelatedTables.has(normalizeTableName(t.name)));
                    var tblSortPrefix = isFkMatch ? '0000_0_' : '0001_';

                    suggestions.push({
                        label: t.fullName,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: insText,
                        filterText: filterStr,
                        sortText: isInsertContext ? ('0001_' + t.name + '_' + t.fullName) : (tblSortPrefix + String(t.name.length).padStart(4, '0') + '_' + t.name + '_' + t.fullName),
                        range: replaceRange,
                        detail: detailText,
                        documentation: isInsertContext ? {
                            value: '```sql\nINSERT INTO ' + insText + '\n```'
                        } : undefined
                    });
                });
            }

            if (suggestTables && window.sqlViews) {
                window.sqlViews.forEach(v => {
                    var insText = v.fullName;
                    var detailText = 'View';
                    var alias = null;

                    if (isInsertContext) {
                        var cols = v.columns || [];
                        var colList = cols.length > 0
                            ? cols.map(function(c) { return '    [' + c.replace(/[\[\]]/g, '') + ']'; }).join(',\n')
                            : '    /* ColumnList */';
                        var valList = cols.length > 0
                            ? cols.map(function(c) {
                                var paramName = c.replace(/[\[\]]/g, '').replace(/[^a-zA-Z0-9_]/g, '');
                                return '    @' + paramName;
                            }).join(',\n')
                            : '    /* Values */';

                        insText = v.fullName + '\n(\n' + colList + '\n)\nVALUES\n(\n' + valList + '\n);';
                        detailText = 'View';
                    } else if (autoAlias) {
                        alias = window.generateTableAlias(v.name || v.fullName, existingAliases);
                        insText = v.fullName + (useAs ? " AS " : " ") + alias;
                        detailText = 'View';
                    }

                    var vFilterStr = v.name + " [" + v.name + "] " + v.fullName + (autoAlias && alias ? (" " + alias) : "");

                    suggestions.push({
                        label: v.fullName,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: insText,
                        filterText: vFilterStr,
                        sortText: isInsertContext ? ('0001_' + v.name + '_' + v.fullName) : ('0001_' + String(v.name.length).padStart(4, '0') + '_' + v.name + '_' + v.fullName),
                        range: replaceRange,
                        detail: detailText,
                        documentation: isInsertContext ? {
                            value: '```sql\nINSERT INTO ' + insText + '\n```'
                        } : undefined
                    });
                });
            }

            if (suggestTables && !isInsertContext && window.sqlFunctions) {
                window.sqlFunctions.forEach(function(f) {
                    var fType = (f.functionType || "").toLowerCase();
                    if (fType.indexOf('table') >= 0) {
                        suggestions.push({
                            label: f.fullName,
                            kind: monaco.languages.CompletionItemKind.Function,
                            insertText: f.fullName + '($0)',
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            filterText: f.name + " " + f.fullName,
                            sortText: '0002_' + String(f.name.length).padStart(4, '0') + '_' + f.name,
                            range: replaceRange,
                            detail: f.functionType ? ('Function (' + f.functionType + ')') : 'Function'
                        });
                    }
                });
            }

            if (suggestColumns || suggestTables) {
                var aliasMap = scopeInfo.aliasMap;
                var outerAliasMap = scopeInfo.outerAliasMap || {};
                var activeTableRefs = scopeInfo.activeTableRefs;

                // Check if user is typing an alias or table dot (e.g., "o.")
                var dotMatch = textBeforeCursor.match(/([a-zA-Z0-9_]+)\.[a-zA-Z0-9_]*$/);
                var explicitTableName = null;
                var rawPrefix = "";
                if (dotMatch) {
                    rawPrefix = dotMatch[1];
                    var prefixLower = rawPrefix.toLowerCase();
                    var foundAliasKey = Object.keys(aliasMap).find(k => k.toLowerCase() === prefixLower);
                    if (foundAliasKey) {
                        explicitTableName = aliasMap[foundAliasKey];
                    } else {
                        var foundOuterKey = Object.keys(outerAliasMap).find(k => k.toLowerCase() === prefixLower);
                        explicitTableName = foundOuterKey ? outerAliasMap[foundOuterKey] : rawPrefix;
                    }
                    suggestKeywords = false;
                }

                if (suggestColumns) {
                    if (explicitTableName) {
                        // User explicitly typed "o." -> suggest only columns of that table/view/CTE/derived table
                        var matchedObjs = [];
                        var explicitNorm = normalizeTableName(explicitTableName);
                        var derivedTarget = (scopeInfo.derivedMap && scopeInfo.derivedMap[explicitNorm])
                            || (scopeInfo.outerDerivedMap && scopeInfo.outerDerivedMap[explicitNorm]);
                        var cteTarget = scopeInfo.cteMap ? scopeInfo.cteMap[explicitNorm] : null;
                        if (derivedTarget) {
                            matchedObjs.push(derivedTarget);
                        } else if (cteTarget) {
                            matchedObjs.push(cteTarget);
                        }
                        var collectDotTarget = function(t) {
                            if (tablesMatch(t.fullName, explicitTableName) || tablesMatch(t.name, explicitTableName)) {
                                matchedObjs.push(t);
                            }
                        };
                        if (matchedObjs.length === 0) {
                            if (window.sqlTables) window.sqlTables.forEach(collectDotTarget);
                            if (window.sqlViews) window.sqlViews.forEach(collectDotTarget);
                        }

                        var dotColItems = [];
                        var seenDotCols = new Set();
                        matchedObjs.forEach(function(tblObj) {
                            var keyColSet = buildKeyColumnSet(tblObj);
                            (tblObj.columns || []).forEach(function(c, colIdx) {
                                var cLower = c.toLowerCase();
                                if (seenDotCols.has(cLower)) return;
                                seenDotCols.add(cLower);
                                dotColItems.push({
                                    col: c,
                                    sortText: computeColumnSortText({
                                        clause: clauseInfo.clause,
                                        isOnContext: isOnContext,
                                        refPriorityIdx: 0,
                                        colIdx: colIdx,
                                        colName: c,
                                        qual: rawPrefix,
                                        alias: rawPrefix,
                                        isKeyCol: keyColSet.has(cLower),
                                        referencedCols: scopeInfo.referencedCols
                                    })
                                });
                            });
                        });

                        if (dotColItems.length === 0 && window.sqlColumns) {
                            window.sqlColumns.forEach(function(c, colIdx) {
                                dotColItems.push({
                                    col: c,
                                    sortText: '0050_' + String(colIdx).padStart(4, '0') + '_' + c
                                });
                            });
                        }

                        dotColItems.forEach(function(item) {
                            var c = item.col;
                            var cleanCol = c.replace(/[\[\]]/g, '');
                            var insText = matchBracketed ? (rawPrefix + ".[" + cleanCol + "]") : (rawPrefix + "." + c);
                            suggestions.push({
                                label: c,
                                kind: monaco.languages.CompletionItemKind.Field,
                                insertText: insText,
                                filterText: rawPrefix + "." + c + " " + c,
                                range: replaceRange,
                                detail: 'Column',
                                sortText: item.sortText
                            });
                        });
                    } else if (activeTableRefs.length > 0) {
                        // Smart Tiered Ranking & Alias Disambiguation for Block Tables
                        var hasMultipleTables = activeTableRefs.length > 1;
                        var seenQualifiedCols = new Set();

                        activeTableRefs.forEach(function(ref, refIdx) {
                            var refPriorityIdx = (isOnContext && hasMultipleTables)
                                ? (refIdx === activeTableRefs.length - 1 ? 0 : refIdx + 1)
                                : refIdx;
                            var keyColSet = buildKeyColumnSet(ref.tableObj);
                            var cols = (clauseInfo.clause === 'INSERT_COLUMNS' && ref.tableObj.insertColumns && ref.tableObj.insertColumns.length > 0)
                                ? ref.tableObj.insertColumns
                                : (ref.tableObj.columns || []);

                            cols.forEach(function(col, colIdx) {
                                var qual = ref.qualifier;
                                var shouldQualify = hasMultipleTables || !!ref.alias;
                                var qualifiedName = shouldQualify && qual ? (qual + "." + col) : col;
                                var dedupeKey = qualifiedName.toLowerCase();
                                if (seenQualifiedCols.has(dedupeKey)) return;
                                seenQualifiedCols.add(dedupeKey);

                                var cleanCol = col.replace(/[\[\]]/g, '');
                                var insertColText = matchBracketed
                                    ? (shouldQualify && qual ? (qual + ".[" + cleanCol + "]") : ("[" + cleanCol + "]"))
                                    : qualifiedName;

                                var filterStr = col + " " + qualifiedName + " [" + col + "]" + (ref.alias ? (" " + ref.alias + "." + col) : "");

                                suggestions.push({
                                    label: qualifiedName,
                                    kind: monaco.languages.CompletionItemKind.Field,
                                    insertText: insertColText,
                                    filterText: filterStr,
                                    range: replaceRange,
                                    detail: 'Column',
                                    sortText: computeColumnSortText({
                                        clause: clauseInfo.clause,
                                        isOnContext: isOnContext,
                                        refPriorityIdx: refPriorityIdx,
                                        colIdx: colIdx,
                                        colName: col,
                                        qual: qual,
                                        alias: ref.alias,
                                        isKeyCol: keyColSet.has(col.toLowerCase()),
                                        referencedCols: scopeInfo.referencedCols
                                    })
                                });
                            });
                        });
                    } else {
                        var allTokens = scopeInfo.scopeMasked.match(/[a-zA-Z0-9_\[\]\.]+/g) || [];
                        var tokenSet = new Set(allTokens.map(x => x.toLowerCase()));

                        var activeColumns = new Set();
                        var hasMatchedTables = false;

                        var checkTableMatch = (t) => {
                            var tLower = t.fullName.toLowerCase();
                            var nameLower = t.name.toLowerCase();
                            var bracketName = "[" + nameLower + "]";

                            if (tokenSet.has(tLower) || tokenSet.has(nameLower) || tokenSet.has(bracketName)) {
                                hasMatchedTables = true;
                                if (t.columns && Array.isArray(t.columns)) {
                                    t.columns.forEach(c => activeColumns.add(c));
                                }
                            }
                        };

                        if (window.sqlTables) window.sqlTables.forEach(checkTableMatch);
                        if (window.sqlViews) window.sqlViews.forEach(checkTableMatch);

                        var columnsToSuggest = (hasMatchedTables && activeColumns.size > 0)
                            ? Array.from(activeColumns)
                            : (window.sqlColumns || []);

                        columnsToSuggest.forEach((c, colIdx) => {
                            suggestions.push({
                                label: c,
                                kind: monaco.languages.CompletionItemKind.Field,
                                insertText: rawPrefix ? (rawPrefix + "." + c) : c,
                                filterText: rawPrefix ? (rawPrefix + "." + c) : c,
                                range: replaceRange,
                                detail: 'Column',
                                sortText: '0050_' + String(colIdx).padStart(4, '0') + '_' + c
                            });
                        });
                    }

                    // Suggest aliases if not after a dot
                    if (!explicitTableName) {
                        Object.keys(aliasMap).forEach(alias => {
                            suggestions.push({
                                label: alias,
                                kind: monaco.languages.CompletionItemKind.Variable,
                                insertText: alias,
                                filterText: alias,
                                range: replaceRange,
                                detail: 'Alias',
                                sortText: '0010_' + alias
                            });
                        });
                    }

                    // Provide JOIN ON Relationship Snippets (kept at very top with 0000_00_0_)
                    var scopeJoinText = scopeInfo.scopeUntilCursorMasked || "";
                    var joinWordMatches = [];
                    var joinWordRegex = /\bjoin\b/gi;
                    var jwm;
                    while ((jwm = joinWordRegex.exec(scopeJoinText)) !== null) {
                        joinWordMatches.push(jwm.index);
                    }
                    var lastJoinIndex = joinWordMatches.length > 0 ? joinWordMatches[joinWordMatches.length - 1] : -1;
                    if (lastJoinIndex >= 0 && (window.sqlTables || window.sqlViews || scopeInfo.cteList.length > 0)) {
                        var joinSegment = scopeJoinText.substring(lastJoinIndex);
                        var onMatch = joinSegment.match(/^join\s+([a-zA-Z0-9_\[\]\.]+)(?:\s+(?:as\s+)?([a-zA-Z0-9_\[\]]+))?(?:\s+with\s*\([^)]*\))?\s+on\s*([a-zA-Z0-9_\[\]\.]*)$/i);
                        if (onMatch) {
                            var joinTableRaw = onMatch[1];
                            var rawJoinAlias = (onMatch[2] || "").trim();
                            var cleanJoinAlias = rawJoinAlias.replace(/[\[\]]/g, '');
                            var joinAlias = (cleanJoinAlias && !sqlNonAliasKw.has(cleanJoinAlias.toLowerCase())) ? cleanJoinAlias : "";
                            var typedRel = onMatch[3] || "";

                            var matchedTable = resolveTableOrView(joinTableRaw, scopeInfo.cteMap);
                            if (matchedTable && matchedTable.relations && Array.isArray(matchedTable.relations)) {
                                var textPriorToJoin = scopeJoinText.substring(0, lastJoinIndex);
                                var queryTables = [];
                                var tableDefRegex = /(?:from|join)\s+([a-zA-Z0-9_\[\]\.]+)(?:\s+(?:as\s+)?([a-zA-Z0-9_\[\]]+))?/gi;
                                var tMatch;
                                while ((tMatch = tableDefRegex.exec(textPriorToJoin)) !== null) {
                                    var rawTbl = tMatch[1];
                                    var rawAl = (tMatch[2] || "").trim().replace(/[\[\]]/g, '');
                                    var alias = (rawAl && !sqlNonAliasKw.has(rawAl.toLowerCase())) ? rawAl : null;
                                    var targetObj = resolveTableOrView(rawTbl, scopeInfo.cteMap);
                                    queryTables.push({
                                        rawTable: rawTbl,
                                        alias: alias,
                                        targetObj: targetObj
                                    });
                                }

                                var relReplaceRange = {
                                    startLineNumber: position.lineNumber,
                                    endLineNumber: position.lineNumber,
                                    startColumn: position.column - typedRel.length,
                                    endColumn: position.column
                                };

                                var leftDisplayName = joinAlias || (matchedTable.name || matchedTable.fullName);
                                if (!joinAlias) {
                                    var joinRevAlias = Object.keys(aliasMap).find(k => tablesMatch(aliasMap[k], joinTableRaw));
                                    if (joinRevAlias) leftDisplayName = joinRevAlias;
                                }
                                var seenConds = new Set();

                                queryTables.forEach(targetEntry => {
                                    var rightDisplayName = targetEntry.alias;
                                    if (!rightDisplayName) {
                                        var revAlias = Object.keys(aliasMap).find(k => tablesMatch(aliasMap[k], targetEntry.rawTable));
                                        rightDisplayName = revAlias || targetEntry.rawTable;
                                    }

                                    // 1. Direct relations on matchedTable (newly joined table first)
                                    if (matchedTable.relations && Array.isArray(matchedTable.relations)) {
                                        matchedTable.relations.forEach(r => {
                                            if (tablesMatch(targetEntry.rawTable, r.toTable)) {
                                                var cond1 = leftDisplayName + "." + r.fromCol + " = " + rightDisplayName + "." + r.toCol;
                                                if (!seenConds.has(cond1)) {
                                                    seenConds.add(cond1);
                                                    suggestions.push({
                                                        label: cond1,
                                                        kind: monaco.languages.CompletionItemKind.Snippet,
                                                        insertText: cond1,
                                                        filterText: typedRel ? (typedRel + " " + cond1 + " " + r.fromCol) : (cond1 + " " + r.fromCol),
                                                        sortText: '0000_00_0_' + cond1,
                                                        range: relReplaceRange,
                                                        detail: 'Relation'
                                                    });
                                                }
                                            }
                                        });
                                    }

                                    // 2. Bidirectional check: relations on targetEntry (still keep newly joined table first)
                                    if (targetEntry.targetObj && targetEntry.targetObj.relations && Array.isArray(targetEntry.targetObj.relations)) {
                                        targetEntry.targetObj.relations.forEach(r => {
                                            if (tablesMatch(matchedTable.fullName, r.toTable) || tablesMatch(matchedTable.name, r.toTable)) {
                                                var cond1 = leftDisplayName + "." + r.toCol + " = " + rightDisplayName + "." + r.fromCol;
                                                if (!seenConds.has(cond1)) {
                                                    seenConds.add(cond1);
                                                    suggestions.push({
                                                        label: cond1,
                                                        kind: monaco.languages.CompletionItemKind.Snippet,
                                                        insertText: cond1,
                                                        filterText: typedRel ? (typedRel + " " + cond1 + " " + r.toCol) : (cond1 + " " + r.toCol),
                                                        sortText: '0000_00_0_' + cond1,
                                                        range: relReplaceRange,
                                                        detail: 'Relation'
                                                    });
                                                }
                                            }
                                        });
                                    }
                                });
                            }
                        }
                    }
                }
            }

            if (activeExecProc && activeExecProc.matchedSp && Array.isArray(activeExecProc.matchedSp.parameters)) {
                var afterCommaSeg = activeExecProc.afterProc.split(',').pop() || "";
                var awaitingParamValue = /=/.test(afterCommaSeg);
                if (!awaitingParamValue) {
                    var usedParamSet = new Set();
                    var usedRegex = /(@[a-zA-Z0-9_]+)\s*=/gi;
                    var upm;
                    while ((upm = usedRegex.exec(activeExecProc.afterProc)) !== null) {
                        usedParamSet.add(upm[1].toLowerCase());
                    }

                    var paramMatch = textBeforeCursor.match(/@[a-zA-Z0-9_]*$/);
                    var paramReplaceRange = paramMatch ? {
                        startLineNumber: position.lineNumber,
                        endLineNumber: position.lineNumber,
                        startColumn: position.column - paramMatch[0].length,
                        endColumn: position.column
                    } : replaceRange;

                    activeExecProc.matchedSp.parameters.forEach(function(p, pIdx) {
                        if (!p || !p.name || usedParamSet.has(p.name.toLowerCase())) return;
                        suggestions.push({
                            label: p.name,
                            kind: monaco.languages.CompletionItemKind.Property,
                            insertText: p.name + ' = ',
                            filterText: p.name + ' ' + p.name.replace(/^@/, ''),
                            sortText: '0001_' + String(pIdx).padStart(3, '0') + '_' + p.name,
                            range: paramReplaceRange,
                            detail: p.type ? ('Parameter (' + p.type + ')') : 'Parameter'
                        });
                    });
                }
            }

            if (suggestSps && window.sqlSps) {
                window.sqlSps.forEach(sp => {
                    var paramsText = '';
                    if (sp.parameters && sp.parameters.length > 0) {
                        paramsText = '\n' + sp.parameters.map(p => `    ${p.name} = , /* ${p.type} */`).join('\n');
                    }
                    suggestions.push({
                        label: sp.fullName,
                        kind: monaco.languages.CompletionItemKind.Method,
                        insertText: sp.fullName + paramsText,
                        filterText: sp.name + " " + sp.fullName,
                        sortText: String(sp.name.length).padStart(4, '0') + '_' + sp.name + '_' + sp.fullName,
                        range: replaceRange,
                        detail: 'Stored Procedure'
                    });
                });
            }

            var isExplicitSpPrefix = !activeExecProc && (fullTypedWord.toLowerCase().startsWith('sp_') || fullTypedWord.toLowerCase() === 'sp');
            if (suggestSps || isExplicitSpPrefix) {
                systemProcedures.forEach(sp => {
                    var item = {
                        label: sp.fullName,
                        kind: monaco.languages.CompletionItemKind.Method,
                        insertText: sp.snippet,
                        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                        filterText: sp.name + " " + sp.fullName,
                        sortText: '0_' + sp.name,
                        range: replaceRange,
                        detail: sp.detail,
                        documentation: { value: sp.doc }
                    };
                    if (sp.triggerSuggest) {
                        item.command = {
                            id: 'editor.action.triggerSuggest',
                            title: 'Trigger Suggest'
                        };
                    }
                    suggestions.push(item);
                });
            }

            var isFunctionExprContext = !activeExecProc
                && !isInsertContext
                && !isOnContext
                && !hasExplicitDotOnLine
                && clauseInfo.clause !== 'INSERT_COLUMNS'
                && clauseInfo.clause !== 'GROUP BY'
                && (suggestColumns || clauseInfo.clause === 'VALUES');

            if (isFunctionExprContext) {
                if (window.sqlFunctions) {
                    window.sqlFunctions.forEach(function(f) {
                        var fType = (f.functionType || "").toLowerCase();
                        if (fType.indexOf('table') >= 0) return;
                        suggestions.push({
                            label: f.fullName,
                            kind: monaco.languages.CompletionItemKind.Function,
                            insertText: f.fullName + '($0)',
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            filterText: f.name + " " + f.fullName,
                            sortText: '0080_' + String(f.name.length).padStart(4, '0') + '_' + f.name,
                            range: replaceRange,
                            detail: f.functionType ? ('Function (' + f.functionType + ')') : 'Function'
                        });
                    });
                }

                sqlBuiltinFunctions.forEach(function(fn) {
                    suggestions.push({
                        label: fn.name,
                        kind: monaco.languages.CompletionItemKind.Function,
                        insertText: fn.snippet,
                        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                        filterText: fn.name,
                        sortText: '0085_' + fn.name,
                        range: replaceRange,
                        detail: fn.detail
                    });
                });
            }

            if (suggestKeywords) {
                var sqlKeywordsList = getContextualKeywords(clauseInfo);
                sqlKeywordsList.forEach(function(kw, kwIdx) {
                    suggestions.push({
                        label: kw,
                        kind: monaco.languages.CompletionItemKind.Keyword,
                        insertText: kw,
                        filterText: kw,
                        range: replaceRange,
                        detail: 'Keyword',
                        sortText: '0100_' + String(kwIdx).padStart(3, '0') + '_' + kw
                    });
                });
            }

            return { suggestions: suggestions };
        },
        resolveCompletionItem: async function(item, token) {
            if (window.sqlDotNetHelper && (item.detail === 'Table' || item.detail === 'View' || item.detail === 'Stored Procedure' || (item.detail && item.detail.startsWith('Function')))) {
                try {
                    let doc = await window.sqlDotNetHelper.invokeMethodAsync('GetSchemaDocumentation', item.label);
                    if (doc) {
                        // Pass documentation as a direct markdown string object
                        item.documentation = { value: doc };
                    }
                } catch (e) { 
                    console.error("ResolveCompletionItem error:", e); 
                }
            }
            return item;
        }
    });

    monaco.languages.registerHoverProvider('sql', {
        provideHover: async function(model, position) {
            var lineContent = model.getLineContent(position.lineNumber);
            var col = position.column - 1;
            
            var start = col;
            while(start > 0 && /[\w\.\[\]]/.test(lineContent[start - 1])) {
                start--;
            }
            var end = col;
            while(end < lineContent.length && /[\w\.\[\]]/.test(lineContent[end])) {
                end++;
            }
            var objectName = lineContent.substring(start, end);
            if (!objectName) return null;

            if (objectName.toLowerCase() === 'sp_helptext') {
                return {
                    contents: [
                        { value: '**System Stored Procedure:** `sp_helptext`' },
                        { value: 'Displays the definition of a user-defined rule, default, unencrypted stored procedure, user-defined function, trigger, computed column, CHECK constraint, view, or system object.' }
                    ]
                };
            }

            if (window.isModelOwnedByQueryRunner && window.isModelOwnedByQueryRunner(model)) {
                return null;
            }

            if (!window.sqlDotNetHelper) {
                return null;
            }

            try {
                let doc = await window.sqlDotNetHelper.invokeMethodAsync('GetSchemaDocumentation', objectName);
                if (doc) {
                    return { contents: [ { value: doc } ] };
                }
            } catch (e) { 
                console.error("HoverProvider error:", e); 
            }
            return null;
        }
    });

    // Auto-capitalize SQL keywords on space
    if (!window.sqlEditorAutoCapitalizeAttached) {
        window.sqlEditorAutoCapitalizeAttached = true;
        
        var sqlKeywords = new Set([
            'select', 'from', 'where', 'and', 'or', 'insert', 'into', 'values', 
            'update', 'set', 'delete', 'inner', 'join', 'left', 'right', 'outer', 
            'on', 'as', 'order', 'group', 'by', 'having', 'top', 'distinct', 
            'null', 'is', 'not', 'asc', 'desc', 'like', 'in', 'between', 'exists', 
            'cast', 'convert', 'create', 'alter', 'drop', 'table', 'view', 
            'procedure', 'exec', 'execute', 'begin', 'end', 'declare', 'if', 
            'else', 'while', 'return', 'print', 'count', 'sum', 'min', 'max', 'avg',
            'with', 'over', 'partition', 'union', 'all', 'any', 'some', 'case', 'when', 'then'
        ]);

        function attachAutoCapitalize(editor) {
            editor.onKeyUp(function(e) {
                if (e.browserEvent.key === ' ') {
                    var position = editor.getPosition();
                    var model = editor.getModel();
                    if (!model || model.getLanguageId() !== 'sql') return;
                    
                    var wordInfo = model.getWordAtPosition({
                        lineNumber: position.lineNumber,
                        column: Math.max(1, position.column - 1)
                    });
                    
                    if (wordInfo && wordInfo.word) {
                        var wLower = wordInfo.word.toLowerCase();
                        if (sqlKeywords.has(wLower) && wordInfo.word !== wLower.toUpperCase()) {
                            var range = new monaco.Range(
                                position.lineNumber, 
                                wordInfo.startColumn, 
                                position.lineNumber, 
                                wordInfo.endColumn
                            );
                            editor.executeEdits('auto-capitalize', [{
                                range: range,
                                text: wLower.toUpperCase(),
                                forceMoveMarkers: true
                            }]);
                        }
                    }
                }
            });
        }

        window.setupEditorInstance = function(editor) {
            if (!editor) return;

            if (window.attachQueryBlockExecutor) {
                window.attachQueryBlockExecutor(editor);
            }
            if (window.attachSchemaHoverPopup) {
                window.attachSchemaHoverPopup(editor);
            }

            try {
                editor.updateOptions({
                    quickSuggestions: {
                        other: true,
                        comments: false,
                        strings: true
                    },
                    acceptSuggestionOnEnter: 'on',
                    wordBasedSuggestions: 'off',
                    suggest: {
                        showWords: false,
                        snippetsPreventQuickSuggestions: false
                    }
                });
            } catch (e) { }

            // Working Paste in context menu (supported across WebView2)
            if (!editor.getAction('custom-paste-action')) {
                try {
                    editor.addAction({
                        id: 'custom-paste-action',
                        precondition: 'editorTextFocus',
                        label: 'Paste',
                        keybindings: [
                            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyV
                        ],
                        run: async function(ed) {
                            var activeEl = document.activeElement;
                            if (activeEl && (activeEl.tagName === 'INPUT' || activeEl.tagName === 'TEXTAREA') && !activeEl.classList.contains('inputarea')) {
                                return;
                            }
                            var text = await window.readClipboardText();
                            if (text) {
                                var selection = ed.getSelection();
                                ed.executeEdits('custom-paste', [{
                                    range: selection,
                                    text: text,
                                    forceMoveMarkers: true
                                }]);
                            }
                        }
                    });
                } catch (e) { }
            }

            // Save as Snippet in context menu
            if (!editor.getAction('save-as-snippet')) {
                try {
                    editor.addAction({
                        id: 'save-as-snippet',
                        label: 'Save as Snippet (Alt+S)',
                        keybindings: [
                            monaco.KeyMod.Alt | monaco.KeyCode.KeyS
                        ],
                        contextMenuGroupId: '9_cutcopypaste',
                        contextMenuOrder: 4,
                        run: function(ed) {
                            if (window.globalShortcuts && window.globalShortcuts.queryRunnerInstance) {
                                window.globalShortcuts.queryRunnerInstance.invokeMethodAsync('OpenSaveSnippetModal');
                            }
                        }
                    });
                } catch (e) { }
            }

            // Toggle Results Pane (Ctrl+R)
            if (!editor.getAction('toggle-results-pane')) {
                try {
                    editor.addAction({
                        id: 'toggle-results-pane',
                        label: 'Toggle Results Pane (Ctrl+R)',
                        keybindings: [
                            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyR
                        ],
                        run: function(ed) {
                            if (window.globalShortcuts && window.globalShortcuts.queryRunnerInstance) {
                                window.globalShortcuts.queryRunnerInstance.invokeMethodAsync('ToggleMinimizeFromShortcut');
                            }
                        }
                    });
                } catch (e) { }
            }

            // Toggle Comment in context menu & keyboard chords
            if (!editor.getAction('toggle-comment-action')) {
                try {
                    editor.addAction({
                        id: 'toggle-comment-action',
                        label: 'Toggle Comment (Ctrl+/)',
                        keybindings: [
                            monaco.KeyMod.CtrlCmd | monaco.KeyCode.Slash,
                            monaco.KeyMod.chord(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyK, monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyC)
                        ],
                        contextMenuGroupId: '1_modification',
                        contextMenuOrder: 2,
                        run: function(ed) {
                            ed.trigger('keyboard', 'editor.action.commentLine', null);
                        }
                    });
                } catch (e) { }
            }

            if (!editor.__listenersAttached) {
                editor.__listenersAttached = true;

                editor.onKeyUp(function(e) {
                    try {
                        var pos = editor.getPosition();
                        var model = editor.getModel();
                        if (model && model.getLanguageId() === 'sql') {
                            if (e.browserEvent.key === 'Backspace' || e.browserEvent.key === 'Delete') {
                                var ctx = getSpHelptextContext(model, pos);
                                if (ctx) {
                                    editor.trigger('keyboard', 'editor.action.triggerSuggest', {});
                                }
                            } else if (e.browserEvent.key === ' ' || e.browserEvent.code === 'Space' || /^[a-zA-Z0-9_.]$/.test(e.browserEvent.key)) {
                                var lineContent = model.getLineContent(pos.lineNumber);
                                var textBefore = lineContent.substring(0, pos.column - 1);
                                var checkRange = {
                                    startLineNumber: Math.max(1, pos.lineNumber - 2),
                                    startColumn: 1,
                                    endLineNumber: pos.lineNumber,
                                    endColumn: pos.column
                                };
                                var textBeforeMulti = model.getValueInRange(checkRange);
                                if (/\bon\s+[a-zA-Z0-9_.]*$/i.test(textBefore) || /\bon\s+[a-zA-Z0-9_.]*$/i.test(textBeforeMulti)) {
                                    editor.trigger('keyboard', 'editor.action.triggerSuggest', {});
                                }
                            }
                        }
                    } catch (err) { }
                });

                editor.onDidFocusEditorText(function() {
                    try {
                        var pos = editor.getPosition();
                        var model = editor.getModel();
                        if (model && model.getLanguageId() === 'sql') {
                            var ctx = getSpHelptextContext(model, pos);
                            if (ctx) {
                                editor.trigger('focus', 'editor.action.triggerSuggest', {});
                            }
                        }
                    } catch (err) { }
                });

                attachAutoCapitalize(editor);
            }
        };

        monaco.editor.onDidCreateEditor(window.setupEditorInstance);
        var editors = monaco.editor.getEditors();
        if (editors) {
            editors.forEach(window.setupEditorInstance);
        }
    }
};

window.readClipboardText = async function() {
    if (window.globalShortcuts && window.globalShortcuts.queryRunnerInstance) {
        try {
            var text = await window.globalShortcuts.queryRunnerInstance.invokeMethodAsync('GetClipboardTextAsync');
            if (text) return text;
        } catch (e) { }
    }
    try {
        if (navigator.clipboard && navigator.clipboard.readText) {
            var text = await navigator.clipboard.readText();
            if (text) return text;
        }
    } catch (e) { }
    return '';
};

window.configureQueryEditor = function(editorId) {
    if (typeof monaco === 'undefined' || !monaco.editor) return;
    var editors = monaco.editor.getEditors();
    var editor = null;
    if (editorId) {
        for (var i = 0; i < editors.length; i++) {
            var c = editors[i].getContainerDomNode();
            if (c && (c.id === editorId || (c.closest && c.closest('#' + editorId)))) {
                editor = editors[i];
                break;
            }
        }
    }
    if (!editor && editors.length > 0) {
        editor = editors[0];
    }
    if (editor && window.attachQueryBlockExecutor) {
        window.attachQueryBlockExecutor(editor);
    }
    if (editor && window.attachSchemaHoverPopup) {
        window.attachSchemaHoverPopup(editor);
    }
    if (editor && window.setupEditorInstance) {
        window.setupEditorInstance(editor);
    }
};

(function initSchemaHoverPopup() {
    var skippedHoverWords = new Set([
        'select', 'from', 'where', 'join', 'inner', 'left', 'right', 'full', 'outer', 'cross',
        'on', 'and', 'or', 'not', 'in', 'exists', 'between', 'like', 'is', 'null', 'as',
        'by', 'group', 'order', 'having', 'asc', 'desc', 'top', 'distinct', 'union', 'all',
        'insert', 'into', 'values', 'update', 'set', 'delete', 'merge', 'using', 'when',
        'matched', 'then', 'else', 'case', 'end', 'begin', 'declare', 'exec', 'execute',
        'create', 'alter', 'drop', 'table', 'view', 'procedure', 'proc', 'function', 'trigger',
        'with', 'nolock', 'if', 'while', 'return', 'print', 'go', 'dbo', 'sp_helptext'
    ]);

    var hoverState = {
        cardEl: null,
        headerBadgeEl: null,
        headerNameEl: null,
        copyBtnEl: null,
        tabsEl: null,
        colTabBtnEl: null,
        scriptTabBtnEl: null,
        bodyEl: null,
        dwellTimer: null,
        hideTimer: null,
        copyResetTimer: null,
        requestSeq: 0,
        isVisible: false,
        activeKey: null,
        pendingKey: null,
        currentEditor: null,
        currentAnchor: null,
        currentData: null,
        activeTab: 'columns',
        globalEscAttached: false
    };

    function isMainQueryEditor(editor) {
        try {
            if (!editor || typeof editor.getContainerDomNode !== 'function') return false;
            var container = editor.getContainerDomNode();
            return !!(container && (
                container.id === 'query-editor' ||
                (container.parentElement && container.parentElement.id === 'query-editor') ||
                (container.closest && container.closest('#query-editor'))
            ));
        } catch (e) {
            return false;
        }
    }

    window.isModelOwnedByQueryRunner = function (model) {
        try {
            if (!model || typeof monaco === 'undefined' || !monaco.editor) return false;
            var editors = monaco.editor.getEditors();
            for (var i = 0; i < editors.length; i++) {
                if (isMainQueryEditor(editors[i]) && editors[i].getModel() === model) {
                    return true;
                }
            }
        } catch (e) { }
        return false;
    };

    function getSchemaHoverDotNetHelper() {
        if (window.globalShortcuts && window.globalShortcuts.queryRunnerInstance) {
            return window.globalShortcuts.queryRunnerInstance;
        }
        return window.sqlDotNetHelper || null;
    }

    function escapeHoverHtml(text) {
        return String(text || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function cancelDwellTimer() {
        if (hoverState.dwellTimer) {
            clearTimeout(hoverState.dwellTimer);
            hoverState.dwellTimer = null;
        }
        hoverState.pendingKey = null;
    }

    function cancelHideTimer() {
        if (hoverState.hideTimer) {
            clearTimeout(hoverState.hideTimer);
            hoverState.hideTimer = null;
        }
    }

    function hideHoverCardImmediate() {
        cancelDwellTimer();
        cancelHideTimer();
        hoverState.requestSeq++;
        hoverState.isVisible = false;
        hoverState.activeKey = null;
        hoverState.currentEditor = null;
        hoverState.currentAnchor = null;
        hoverState.currentData = null;
        if (hoverState.cardEl) {
            hoverState.cardEl.style.display = 'none';
        }
    }

    function scheduleHideHoverCard(delayMs) {
        cancelHideTimer();
        hoverState.hideTimer = setTimeout(function () {
            hideHoverCardImmediate();
        }, delayMs || 220);
    }

    function ensureHoverCardDom() {
        if (hoverState.cardEl) return hoverState.cardEl;
        if (typeof document === 'undefined' || !document.body) return null;

        var card = document.createElement('div');
        card.id = 'exploredb-schema-hover-card';
        card.className = 'schema-hover-card';
        card.style.display = 'none';

        var header = document.createElement('div');
        header.className = 'schema-hover-header';

        var titleWrap = document.createElement('div');
        titleWrap.className = 'schema-hover-title-wrap';

        var badge = document.createElement('span');
        badge.className = 'schema-hover-badge';

        var nameSpan = document.createElement('span');
        nameSpan.className = 'schema-hover-name';

        titleWrap.appendChild(badge);
        titleWrap.appendChild(nameSpan);

        var actionsWrap = document.createElement('div');
        actionsWrap.className = 'schema-hover-actions';

        var copyBtn = document.createElement('button');
        copyBtn.type = 'button';
        copyBtn.className = 'schema-hover-copy-btn';
        copyBtn.innerHTML = '<i class="bi bi-copy"></i><span>Copy</span>';

        var expandBtn = document.createElement('button');
        expandBtn.type = 'button';
        expandBtn.className = 'schema-hover-copy-btn schema-hover-expand-btn';
        expandBtn.title = 'Expand to full popup';
        expandBtn.innerHTML = '<i class="bi bi-arrows-fullscreen"></i>';

        actionsWrap.appendChild(copyBtn);
        actionsWrap.appendChild(expandBtn);

        header.appendChild(titleWrap);
        header.appendChild(actionsWrap);

        var tabs = document.createElement('div');
        tabs.className = 'schema-hover-tabs';

        var colTabBtn = document.createElement('button');
        colTabBtn.type = 'button';
        colTabBtn.className = 'schema-hover-tab-btn';
        colTabBtn.textContent = 'Columns';

        var scriptTabBtn = document.createElement('button');
        scriptTabBtn.type = 'button';
        scriptTabBtn.className = 'schema-hover-tab-btn';
        scriptTabBtn.textContent = 'Definition';

        tabs.appendChild(colTabBtn);
        tabs.appendChild(scriptTabBtn);

        var body = document.createElement('div');
        body.className = 'schema-hover-body custom-scrollbar-light';

        card.appendChild(header);
        card.appendChild(tabs);
        card.appendChild(body);
        document.body.appendChild(card);

        card.addEventListener('mouseenter', function () {
            cancelHideTimer();
            cancelDwellTimer();
        });
        card.addEventListener('mouseleave', function () {
            scheduleHideHoverCard(220);
        });
        card.addEventListener('mousedown', function (e) {
            e.stopPropagation();
        });

        colTabBtn.addEventListener('click', function () {
            if (!hoverState.currentData) return;
            hoverState.activeTab = 'columns';
            renderHoverCardContent();
        });

        scriptTabBtn.addEventListener('click', function () {
            if (!hoverState.currentData) return;
            hoverState.activeTab = 'script';
            renderHoverCardContent();
        });

        copyBtn.addEventListener('click', function () {
            copyCurrentHoverDefinition();
        });

        expandBtn.addEventListener('click', function () {
            var data = hoverState.currentData;
            if (!data || !data.fullName) return;
            var fullName = data.fullName;
            var tab = hoverState.activeTab || 'columns';
            var helper = getSchemaHoverDotNetHelper();
            hideHoverCardImmediate();
            if (helper) {
                helper.invokeMethodAsync('OpenExpandedSchemaHoverAsync', fullName, tab)
                    .catch(function () { });
            }
        });

        if (!hoverState.globalEscAttached && document.addEventListener) {
            hoverState.globalEscAttached = true;
            document.addEventListener('keydown', function (ev) {
                if (ev.key === 'Escape' && hoverState.isVisible) {
                    hideHoverCardImmediate();
                }
            });
        }

        hoverState.cardEl = card;
        hoverState.headerBadgeEl = badge;
        hoverState.headerNameEl = nameSpan;
        hoverState.copyBtnEl = copyBtn;
        hoverState.tabsEl = tabs;
        hoverState.colTabBtnEl = colTabBtn;
        hoverState.scriptTabBtnEl = scriptTabBtn;
        hoverState.bodyEl = body;
        return card;
    }

    function setCopyButtonFeedback(state) {
        var btn = hoverState.copyBtnEl;
        if (!btn) return;
        if (hoverState.copyResetTimer) {
            clearTimeout(hoverState.copyResetTimer);
            hoverState.copyResetTimer = null;
        }
        if (state === 'copying') {
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm" style="width:11px;height:11px;"></span><span>Loading...</span>';
        } else if (state === 'copied') {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-check2 text-success"></i><span>Copied</span>';
            hoverState.copyResetTimer = setTimeout(function () {
                if (hoverState.copyBtnEl) {
                    hoverState.copyBtnEl.innerHTML = '<i class="bi bi-copy"></i><span>Copy</span>';
                }
                hoverState.copyResetTimer = null;
            }, 1600);
        } else if (state === 'failed') {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-x-circle text-danger"></i><span>Unavailable</span>';
            hoverState.copyResetTimer = setTimeout(function () {
                if (hoverState.copyBtnEl) {
                    hoverState.copyBtnEl.innerHTML = '<i class="bi bi-copy"></i><span>Copy</span>';
                }
                hoverState.copyResetTimer = null;
            }, 1800);
        } else {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-copy"></i><span>Copy</span>';
        }
    }

    async function writeTextToClipboardSafe(text) {
        var helper = getSchemaHoverDotNetHelper();
        if (helper) {
            try {
                var ok = await helper.invokeMethodAsync('SetClipboardTextAsync', text);
                if (ok) return true;
            } catch (e) { }
        }
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch (e) { }
        return false;
    }

    async function copyCurrentHoverDefinition() {
        var data = hoverState.currentData;
        if (!data) return;

        var defText = data.definition;
        if (!data.isDefinitionLoaded) {
            setCopyButtonFeedback('copying');
            var helper = getSchemaHoverDotNetHelper();
            if (helper) {
                try {
                    defText = await helper.invokeMethodAsync('GetSchemaHoverDefinitionAsync', data.fullName);
                    if (hoverState.currentData === data) {
                        data.definition = defText;
                        data.isDefinitionLoaded = true;
                        if (hoverState.activeTab === 'script') {
                            renderHoverCardContent();
                        }
                    }
                } catch (e) {
                    defText = null;
                }
            }
        }

        if (!defText || !defText.trim()) {
            setCopyButtonFeedback('failed');
            return;
        }

        var copied = await writeTextToClipboardSafe(defText);
        setCopyButtonFeedback(copied ? 'copied' : 'failed');
    }

    function renderScriptElement(defText) {
        var body = hoverState.bodyEl;
        if (!body) return;

        if (!defText || !defText.trim()) {
            body.innerHTML = '<div class="schema-hover-status"><i class="bi bi-lock"></i><span>Definition unavailable (object may be encrypted or lack VIEW DEFINITION permission).</span></div>';
            return;
        }

        var isLight = typeof document !== 'undefined' && document.documentElement && document.documentElement.getAttribute('data-bs-theme') === 'light';
        var themeClass = isLight ? 'vs' : 'vs-dark';

        var pre = document.createElement('pre');
        pre.className = 'schema-hover-code monaco-editor ' + themeClass;
        pre.textContent = defText;
        body.innerHTML = '';
        body.appendChild(pre);

        if (defText.length <= 25000 && typeof monaco !== 'undefined' && monaco.editor && typeof monaco.editor.colorize === 'function') {
            var expectedData = hoverState.currentData;
            var expectedSeq = hoverState.requestSeq;
            monaco.editor.colorize(defText, 'sql', { tabSize: 4 }).then(function (html) {
                if (!hoverState.isVisible || hoverState.currentData !== expectedData || hoverState.activeTab !== 'script' || hoverState.requestSeq !== expectedSeq) {
                    return;
                }
                if (body.contains(pre)) {
                    pre.innerHTML = html;
                }
            }).catch(function () { });
        }
    }

    function loadDefinitionForActiveCard() {
        var data = hoverState.currentData;
        var body = hoverState.bodyEl;
        if (!data || !body) return;

        if (data.isDefinitionLoaded) {
            renderScriptElement(data.definition);
            return;
        }

        var label = (data.kind === 'Table') ? 'Generating DDL script...' : 'Loading definition...';
        body.innerHTML = '<div class="schema-hover-status"><span class="spinner-border spinner-border-sm text-primary"></span><span>' + escapeHoverHtml(label) + '</span></div>';

        var helper = getSchemaHoverDotNetHelper();
        if (!helper) {
            body.innerHTML = '<div class="schema-hover-status"><span>Unable to reach schema service.</span></div>';
            return;
        }

        var expectedData = data;
        var expectedSeq = hoverState.requestSeq;
        helper.invokeMethodAsync('GetSchemaHoverDefinitionAsync', data.fullName)
            .then(function (def) {
                expectedData.definition = def;
                expectedData.isDefinitionLoaded = true;
                if (!hoverState.isVisible || hoverState.currentData !== expectedData || hoverState.activeTab !== 'script' || hoverState.requestSeq !== expectedSeq) {
                    return;
                }
                updateCopyButtonVisibility();
                renderScriptElement(def);
                repositionHoverCard();
            })
            .catch(function () {
                if (!hoverState.isVisible || hoverState.currentData !== expectedData || hoverState.activeTab !== 'script') {
                    return;
                }
                updateCopyButtonVisibility();
                body.innerHTML = '<div class="schema-hover-status"><i class="bi bi-exclamation-triangle text-warning"></i><span>Error loading script.</span></div>';
            });
    }

    function updateCopyButtonVisibility() {
        var btn = hoverState.copyBtnEl;
        var data = hoverState.currentData;
        if (!btn || !data) return;

        var isTableOrView = (data.kind === 'Table' || data.kind === 'View');
        var isViewingDefinition = !isTableOrView || hoverState.activeTab === 'script';
        var hasDefinition = !!(data.isDefinitionLoaded && data.definition && data.definition.trim());
        btn.style.display = (isViewingDefinition && hasDefinition) ? 'inline-flex' : 'none';
    }

    function renderHoverCardContent() {
        var data = hoverState.currentData;
        var body = hoverState.bodyEl;
        if (!data || !body) return;

        var isTableOrView = (data.kind === 'Table' || data.kind === 'View');
        hoverState.tabsEl.style.display = isTableOrView ? 'flex' : 'none';

        if (isTableOrView) {
            hoverState.colTabBtnEl.className = 'schema-hover-tab-btn' + (hoverState.activeTab === 'columns' ? ' active' : '');
            hoverState.scriptTabBtnEl.className = 'schema-hover-tab-btn' + (hoverState.activeTab === 'script' ? ' active' : '');
            hoverState.colTabBtnEl.textContent = 'Columns (' + (data.columns ? data.columns.length : 0) + ')';
            hoverState.scriptTabBtnEl.textContent = 'Definition';
        } else {
            hoverState.activeTab = 'script';
        }
        hoverState.copyBtnEl.title = 'Copy Definition';
        updateCopyButtonVisibility();

        if (isTableOrView && hoverState.activeTab === 'columns') {
            var cols = data.columns || [];
            if (cols.length === 0) {
                body.innerHTML = '<div class="schema-hover-status"><span>No column metadata loaded.</span></div>';
            } else {
                var rowsHtml = '';
                for (var i = 0; i < cols.length; i++) {
                    var c = cols[i];
                    var cName = c.name !== undefined ? c.name : c.Name;
                    var cType = c.dataType !== undefined ? c.dataType : c.DataType;
                    rowsHtml += '<tr><td>' + escapeHoverHtml(cName) + '</td><td>' + escapeHoverHtml(cType) + '</td></tr>';
                }
                body.innerHTML = '<table class="schema-hover-table"><thead><tr><th>Column</th><th>Data Type</th></tr></thead><tbody>' + rowsHtml + '</tbody></table>';
            }
            repositionHoverCard();
            return;
        }

        loadDefinitionForActiveCard();
        repositionHoverCard();
    }

    function repositionHoverCard() {
        var card = hoverState.cardEl;
        var editor = hoverState.currentEditor;
        var anchor = hoverState.currentAnchor;
        if (!card || !editor || !anchor || !hoverState.isVisible) return;

        try {
            var editorDom = editor.getDomNode();
            if (!editorDom) return;

            var vis = editor.getScrolledVisiblePosition({
                lineNumber: anchor.lineNumber,
                column: anchor.startColumn
            });
            if (!vis) return;

            var editorRect = editorDom.getBoundingClientRect();
            var anchorX = editorRect.left + vis.left;
            var anchorBottom = editorRect.top + vis.top + vis.height;
            var anchorTop = editorRect.top + vis.top;

            card.style.display = 'flex';
            var rect = card.getBoundingClientRect();
            var vw = window.innerWidth;
            var vh = window.innerHeight;
            var margin = 12;

            var left = Math.max(margin, Math.min(anchorX, vw - rect.width - margin));
            var top = anchorBottom + 3;
            if (top + rect.height > vh - margin) {
                var aboveTop = anchorTop - rect.height - 3;
                if (aboveTop >= margin) {
                    top = aboveTop;
                } else {
                    top = Math.max(margin, vh - rect.height - margin);
                }
            }

            card.style.left = Math.round(left) + 'px';
            card.style.top = Math.round(top) + 'px';
        } catch (e) { }
    }

    function normalizeHoverPayload(raw) {
        if (!raw) return null;
        return {
            kind: raw.kind || raw.Kind || '',
            fullName: raw.fullName || raw.FullName || '',
            columns: raw.columns || raw.Columns || [],
            isDefinitionLoaded: !!(raw.isDefinitionLoaded !== undefined ? raw.isDefinitionLoaded : raw.IsDefinitionLoaded),
            definition: raw.definition !== undefined ? raw.definition : raw.Definition
        };
    }

    function showHoverCardForToken(editor, anchor, data, key) {
        var card = ensureHoverCardDom();
        if (!card) return;

        cancelHideTimer();
        hoverState.isVisible = true;
        hoverState.activeKey = key;
        hoverState.currentEditor = editor;
        hoverState.currentAnchor = anchor;
        hoverState.currentData = data;
        hoverState.activeTab = (data.kind === 'Table' || data.kind === 'View') ? 'columns' : 'script';

        var badgeText = data.kind === 'Procedure' ? 'SP' : (data.kind === 'Function' ? 'FUNC' : data.kind.toUpperCase());
        hoverState.headerBadgeEl.textContent = badgeText;
        hoverState.headerBadgeEl.className = 'schema-hover-badge kind-' + data.kind.toLowerCase();
        hoverState.headerNameEl.textContent = data.fullName;
        hoverState.headerNameEl.title = data.fullName;
        setCopyButtonFeedback('reset');

        renderHoverCardContent();
    }

    function extractObjectTokenAtPosition(model, position) {
        if (!model || !position) return null;
        var lineNum = position.lineNumber;
        var col = position.column;
        if (col > 10000) return null;

        var winStartCol = Math.max(1, col - 200);
        var slice = model.getValueInRange({
            startLineNumber: lineNum,
            startColumn: winStartCol,
            endLineNumber: lineNum,
            endColumn: col + 200
        });
        if (!slice) return null;

        var rel = col - winStartCol;
        if (rel < 0 || rel > slice.length) return null;

        var isObjChar = function (ch) {
            return (ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9') ||
                ch === '_' || ch === '.' || ch === '[' || ch === ']';
        };

        if (rel === slice.length || !isObjChar(slice[rel])) {
            if (rel > 0 && isObjChar(slice[rel - 1])) {
                rel--;
            } else {
                return null;
            }
        }

        var s = rel;
        while (s > 0 && isObjChar(slice[s - 1])) s--;
        var e = rel + 1;
        while (e < slice.length && isObjChar(slice[e])) e++;

        while (s < e && slice[s] === '.') s++;
        while (e > s && slice[e - 1] === '.') e--;
        if (e <= s) return null;

        var objectName = slice.substring(s, e);
        if (!objectName || /^\d+$/.test(objectName)) return null;

        var lowerClean = objectName.replace(/[\[\]]/g, '').toLowerCase();
        if (skippedHoverWords.has(lowerClean)) return null;

        return {
            objectName: objectName,
            lineNumber: lineNum,
            startColumn: winStartCol + s,
            endColumn: winStartCol + e
        };
    }

    window.attachSchemaHoverPopup = function (editor) {
        if (!editor || !isMainQueryEditor(editor)) return;
        if (editor.__schemaHoverAttached) return;
        editor.__schemaHoverAttached = true;

        editor.onMouseMove(function (e) {
            try {
                if (!e || !e.target || e.target.type !== 6 || !e.target.position) {
                    cancelDwellTimer();
                    if (hoverState.isVisible && hoverState.currentEditor === editor) {
                        scheduleHideHoverCard(220);
                    }
                    return;
                }

                var domNode = editor.getDomNode();
                if (domNode && domNode.querySelector('.suggest-widget.visible')) {
                    cancelDwellTimer();
                    if (hoverState.isVisible) hideHoverCardImmediate();
                    return;
                }

                var model = editor.getModel();
                if (!model || model.getLanguageId() !== 'sql') return;

                var tok = extractObjectTokenAtPosition(model, e.target.position);
                if (!tok) {
                    cancelDwellTimer();
                    if (hoverState.isVisible && hoverState.currentEditor === editor) {
                        scheduleHideHoverCard(220);
                    }
                    return;
                }

                var key = editor.getId() + '::' + tok.lineNumber + '::' + tok.startColumn + '::' + tok.endColumn + '::' + tok.objectName;
                if (hoverState.isVisible && hoverState.activeKey === key) {
                    cancelHideTimer();
                    cancelDwellTimer();
                    return;
                }

                if (hoverState.dwellTimer && hoverState.pendingKey === key) {
                    cancelHideTimer();
                    return;
                }

                cancelHideTimer();
                cancelDwellTimer();
                hoverState.pendingKey = key;
                hoverState.dwellTimer = setTimeout(function () {
                    hoverState.dwellTimer = null;
                    hoverState.pendingKey = null;
                    var helper = getSchemaHoverDotNetHelper();
                    if (!helper) return;

                    var mySeq = ++hoverState.requestSeq;
                    helper.invokeMethodAsync('GetSchemaHoverObject', tok.objectName)
                        .then(function (raw) {
                            if (mySeq !== hoverState.requestSeq) return;
                            var data = normalizeHoverPayload(raw);
                            if (!data || !data.kind) {
                                if (hoverState.isVisible) hideHoverCardImmediate();
                                return;
                            }
                            showHoverCardForToken(editor, tok, data, key);
                        })
                        .catch(function () { });
                }, 380);
            } catch (err) { }
        });

        editor.onMouseLeave(function () {
            cancelDwellTimer();
            if (hoverState.isVisible && hoverState.currentEditor === editor) {
                scheduleHideHoverCard(220);
            }
        });

        editor.onMouseDown(function () {
            hideHoverCardImmediate();
        });

        editor.onDidScrollChange(function () {
            if (hoverState.isVisible && hoverState.currentEditor === editor) {
                hideHoverCardImmediate();
            }
        });

        editor.onDidChangeModelContent(function () {
            if (hoverState.isVisible && hoverState.currentEditor === editor) {
                hideHoverCardImmediate();
            }
        });

        editor.onDidDispose(function () {
            if (hoverState.currentEditor === editor) {
                hideHoverCardImmediate();
            }
        });
    };
})();




















window.createBlobUrlFromStream = async function(contentStreamReference, mimeType) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: mimeType || 'application/pdf' });
    return URL.createObjectURL(blob);
};

window.revokeBlobUrl = function(url) {
    if (url && url.startsWith('blob:')) {
        URL.revokeObjectURL(url);
    }
};

window.sqlDiffNavigator = null;

function getDiffEditorInstance(editorId) {
    editorId = editorId || "sql-diff-editor";

    // 1. Check window.blazorMonaco registry
    if (window.blazorMonaco && typeof window.blazorMonaco.getEditor === 'function') {
        var ed = window.blazorMonaco.getEditor(editorId, true);
        if (ed && typeof ed.getLineChanges === 'function') {
            return ed;
        }
        var edNormal = window.blazorMonaco.getEditor(editorId);
        if (edNormal && typeof edNormal.getLineChanges === 'function') {
            return edNormal;
        }
    }

    // 2. Check window.blazorMonaco.editors array directly
    if (window.blazorMonaco && Array.isArray(window.blazorMonaco.editors)) {
        var holder = window.blazorMonaco.editors.find(function (h) {
            return h.id === editorId;
        });
        if (holder && holder.editor && typeof holder.editor.getLineChanges === 'function') {
            return holder.editor;
        }
    }

    return null;
}

window.initSqlDiffNavigator = function (editorId) {
    return true;
};

window.sqlDiffNavigateBlock = function (editorId, direction) {
    editorId = editorId || "sql-diff-editor";
    var diffEditor = getDiffEditorInstance(editorId);
    if (!diffEditor) return false;

    var changes = diffEditor.getLineChanges();
    if (!changes || changes.length === 0) return false;

    var origEditor = diffEditor.getOriginalEditor();
    var modEditor = diffEditor.getModifiedEditor();
    var isOrigFocused = origEditor && typeof origEditor.hasTextFocus === 'function' && origEditor.hasTextFocus();
    var activeEditor = isOrigFocused ? origEditor : modEditor;
    if (!activeEditor) return false;

    var currentPos = (typeof activeEditor.getPosition === 'function' && activeEditor.getPosition()) || { lineNumber: 1, column: 1 };
    var currentLine = currentPos.lineNumber;

    // Helper to get line bounds for a change on the relevant side (DiffMate pattern)
    function getChangeRange(c) {
        if (isOrigFocused) {
            var start = c.originalStartLineNumber > 0 ? c.originalStartLineNumber : (c.originalEndLineNumber > 0 ? c.originalEndLineNumber : 1);
            var end = c.originalEndLineNumber > 0 ? c.originalEndLineNumber : start;
            return { start: start, end: end };
        } else {
            var start = c.modifiedStartLineNumber > 0 ? c.modifiedStartLineNumber : (c.modifiedEndLineNumber > 0 ? c.modifiedEndLineNumber : 1);
            var end = c.modifiedEndLineNumber > 0 ? c.modifiedEndLineNumber : start;
            return { start: start, end: end };
        }
    }

    // Check if cursor is currently within any diff chunk
    var currentDiffIdx = -1;
    for (var i = 0; i < changes.length; i++) {
        var range = getChangeRange(changes[i]);
        if (currentLine >= range.start && currentLine <= range.end) {
            currentDiffIdx = i;
            break;
        }
    }

    var targetChange = null;

    if (direction === 'next' || direction === 1) {
        if (currentDiffIdx !== -1) {
            targetChange = changes[(currentDiffIdx + 1) % changes.length];
        } else {
            for (var i = 0; i < changes.length; i++) {
                var range = getChangeRange(changes[i]);
                if (range.start > currentLine) {
                    targetChange = changes[i];
                    break;
                }
            }
            if (!targetChange) targetChange = changes[0]; // Wrap around
        }
    } else {
        if (currentDiffIdx !== -1) {
            targetChange = changes[(currentDiffIdx - 1 + changes.length) % changes.length];
        } else {
            for (var i = changes.length - 1; i >= 0; i--) {
                var range = getChangeRange(changes[i]);
                if (range.end < currentLine) {
                    targetChange = changes[i];
                    break;
                }
            }
            if (!targetChange) targetChange = changes[changes.length - 1]; // Wrap around
        }
    }

    if (!targetChange) return false;

    // Prefer jumping to modified side's line. If pure deletion on modified, jump to original side.
    var targetEditor = targetChange.modifiedStartLineNumber > 0 ? modEditor : origEditor;
    var targetLine = targetChange.modifiedStartLineNumber > 0 ? targetChange.modifiedStartLineNumber : targetChange.originalStartLineNumber;

    if (targetEditor) {
        var line = targetLine > 0 ? targetLine : 1;
        targetEditor.revealLineInCenter(line);
        targetEditor.setPosition({ lineNumber: line, column: 1 });
        targetEditor.focus();
        return true;
    }
    return false;
};

window.sqlDiffNext = function (editorId) {
    return window.sqlDiffNavigateBlock(editorId, 'next');
};

window.sqlDiffPrev = function (editorId) {
    return window.sqlDiffNavigateBlock(editorId, 'prev');
};

window.sqlDiffSetSideBySide = function (editorId, sideBySide) {
    var diffEditor = getDiffEditorInstance(editorId);
    if (!diffEditor) return false;
    diffEditor.updateOptions({ renderSideBySide: sideBySide });
    return true;
};

window.initSqlCompareSplitter = function (dotNetRef) {
    // 1. Top Panel Resizer (between Object List and Diff Viewer)
    var topSplitter = document.getElementById('sql-compare-splitter');
    var topPanel = document.getElementById('sql-compare-top-panel');
    
    if (topSplitter && topPanel && !topSplitter._hasSplitterHandler) {
        topSplitter._hasSplitterHandler = true;
        topSplitter.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            try { topSplitter.setPointerCapture(e.pointerId); } catch (err) {}

            var startY = e.clientY;
            var startHeight = topPanel.offsetHeight;
            var minHeight = 120;
            var lastHeight = startHeight;
        var layoutRaf = null;

            topSplitter.classList.add('dragging');
            document.body.style.cursor = 'row-resize';
            document.body.style.userSelect = 'none';

            function onPointerMove(moveEvent) {
                var deltaY = moveEvent.clientY - startY;
                var newHeight = Math.round(Math.max(minHeight, startHeight + deltaY));
                lastHeight = newHeight;
                topPanel.style.height = newHeight + 'px';
                topPanel.style.maxHeight = 'none';
                window.dispatchEvent(new Event('resize'));
            }

            function onPointerUp(upEvent) {
            if (layoutRaf) {
                cancelAnimationFrame(layoutRaf);
                layoutRaf = null;
            }
                try { topSplitter.releasePointerCapture(upEvent.pointerId); } catch (err) {}
                topSplitter.removeEventListener('pointermove', onPointerMove);
                topSplitter.removeEventListener('pointerup', onPointerUp);
                topSplitter.removeEventListener('pointercancel', onPointerUp);
                topSplitter.classList.remove('dragging');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                window.dispatchEvent(new Event('resize'));

                if (dotNetRef && typeof dotNetRef.invokeMethodAsync === 'function') {
                    if (window.layoutMonacoEditors) {
                    window.layoutMonacoEditors();
                }
                dotNetRef.invokeMethodAsync('SetTopPanelHeight', lastHeight);
                }
            }

            topSplitter.addEventListener('pointermove', onPointerMove);
            topSplitter.addEventListener('pointerup', onPointerUp);
            topSplitter.addEventListener('pointercancel', onPointerUp);
        });
    }

    // 2. Diff Panel Resizer (at bottom of Diff Viewer)
    var diffSplitter = document.getElementById('sql-compare-diff-resizer');
    var diffPanel = document.getElementById('sql-compare-diff-panel');

    if (diffSplitter && diffPanel && !diffSplitter._hasSplitterHandler) {
        diffSplitter._hasSplitterHandler = true;
        diffSplitter.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            try { diffSplitter.setPointerCapture(e.pointerId); } catch (err) {}

            var startY = e.clientY;
            var startHeight = diffPanel.offsetHeight;
            var minHeight = 220;
            var lastHeight = startHeight;
        var layoutRaf = null;

            diffSplitter.classList.add('dragging');
            document.body.style.cursor = 'row-resize';
            document.body.style.userSelect = 'none';

            function onPointerMove(moveEvent) {
                var deltaY = moveEvent.clientY - startY;
                var newHeight = Math.round(Math.max(minHeight, startHeight + deltaY));
                lastHeight = newHeight;
                diffPanel.style.height = newHeight + 'px';
                if (!layoutRaf) {
                    layoutRaf = requestAnimationFrame(function () {
                        layoutRaf = null;
                        if (window.layoutMonacoEditors) {
                            window.layoutMonacoEditors();
                        }
                    });
                }
                window.dispatchEvent(new Event('resize'));
            }

            function onPointerUp(upEvent) {
            if (layoutRaf) {
                cancelAnimationFrame(layoutRaf);
                layoutRaf = null;
            }
                try { diffSplitter.releasePointerCapture(upEvent.pointerId); } catch (err) {}
                diffSplitter.removeEventListener('pointermove', onPointerMove);
                diffSplitter.removeEventListener('pointerup', onPointerUp);
                diffSplitter.removeEventListener('pointercancel', onPointerUp);
                diffSplitter.classList.remove('dragging');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                window.dispatchEvent(new Event('resize'));

                if (dotNetRef && typeof dotNetRef.invokeMethodAsync === 'function') {
                    if (window.layoutMonacoEditors) {
                    window.layoutMonacoEditors();
                }
                dotNetRef.invokeMethodAsync('SetDiffPanelHeight', lastHeight);
                }
            }

            diffSplitter.addEventListener('pointermove', onPointerMove);
            diffSplitter.addEventListener('pointerup', onPointerUp);
            diffSplitter.addEventListener('pointercancel', onPointerUp);
        });
    }

    return true;
};

window.layoutMonacoEditors = function () {
    if (typeof monaco !== 'undefined' && monaco.editor) {
        if (typeof monaco.editor.getEditors === 'function') {
            var editors = monaco.editor.getEditors();
            for (var i = 0; i < editors.length; i++) {
                try { editors[i].layout(); } catch (err) {}
            }
        }
        if (typeof monaco.editor.getDiffEditors === 'function') {
            var diffEditors = monaco.editor.getDiffEditors();
            for (var j = 0; j < diffEditors.length; j++) {
                try { diffEditors[j].layout(); } catch (err) {}
            }
        }
    }
};

window.addEventListener('resize', function () {
    if (window.layoutMonacoEditors) {
        window.layoutMonacoEditors();
    }
}, { passive: true });

window.initQueryEditorSplitter = function (dotNetRef, splitterId, wrapperId) {
    splitterId = splitterId || 'query-editor-resizer';
    wrapperId = wrapperId || 'query-editor-wrapper';
    var splitter = document.getElementById(splitterId);
    var wrapper = document.getElementById(wrapperId);
    if (!splitter || !wrapper) return false;

    if (window.ResizeObserver && !wrapper._hasResizeObserver) {
        wrapper._hasResizeObserver = true;
        var ro = new ResizeObserver(function () {
            if (window.layoutMonacoEditors) {
                window.layoutMonacoEditors();
            }
        });
        ro.observe(wrapper);
    }

    if (splitter._hasSplitterHandler) return true;
    splitter._hasSplitterHandler = true;

    splitter.addEventListener('pointerdown', function (e) {
        e.preventDefault();
        try { splitter.setPointerCapture(e.pointerId); } catch (err) {}

        var startY = e.clientY;
        var startHeight = wrapper.offsetHeight;
        var minHeight = 120;
        var lastHeight = startHeight;
        var layoutRaf = null;

        splitter.classList.add('dragging');
        document.body.style.cursor = 'row-resize';
        document.body.style.userSelect = 'none';

        var workspace = wrapper.closest('.query-runner-workspace') || document.querySelector('.query-runner-workspace');
        var maxAllowed = workspace ? Math.max(minHeight, workspace.offsetHeight - 120) : (window.innerHeight - 250);

        function onPointerMove(moveEvent) {
            var deltaY = moveEvent.clientY - startY;
            var newHeight = Math.round(Math.min(maxAllowed, Math.max(minHeight, startHeight + deltaY)));
            lastHeight = newHeight;
            wrapper.style.height = newHeight + 'px';
            wrapper.style.flexGrow = '0';

            if (!layoutRaf) {
                layoutRaf = requestAnimationFrame(function () {
                    layoutRaf = null;
                    if (window.layoutMonacoEditors) {
                        window.layoutMonacoEditors();
                    }
                });
            }
            window.dispatchEvent(new Event('resize'));
        }

        function onPointerUp(upEvent) {
            if (layoutRaf) {
                cancelAnimationFrame(layoutRaf);
                layoutRaf = null;
            }
            try { splitter.releasePointerCapture(upEvent.pointerId); } catch (err) {}
            splitter.removeEventListener('pointermove', onPointerMove);
            splitter.removeEventListener('pointerup', onPointerUp);
            splitter.removeEventListener('pointercancel', onPointerUp);
            splitter.classList.remove('dragging');
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
            window.dispatchEvent(new Event('resize'));

            if (dotNetRef && typeof dotNetRef.invokeMethodAsync === 'function') {
                if (window.layoutMonacoEditors) {
                    window.layoutMonacoEditors();
                }
                dotNetRef.invokeMethodAsync('SetQueryEditorHeight', lastHeight);
            }
        }

        splitter.addEventListener('pointermove', onPointerMove);
        splitter.addEventListener('pointerup', onPointerUp);
        splitter.addEventListener('pointercancel', onPointerUp);
    });

    return true;
};

window.initGridColumnResizers = function (containerId, dotNetRef) {
    var container = document.getElementById(containerId);
    if (!container) return false;

    if (container._hasGridResizer) {
        container._dotNetRef = dotNetRef;
        return true;
    }
    container._hasGridResizer = true;
    container._dotNetRef = dotNetRef;

    var table = container.querySelector('.result-set-table');
    var guide = container.querySelector('.grid-col-guide');
    if (!table) return false;

    var isDragging = false;
    var hasMoved = false;
    var startX = 0;
    var startWidth = 0;
    var currentTh = null;
    var currentColName = null;
    var currentPointerId = null;

    function getColumnFromEvent(e) {
        // 1. Direct handle in <th>
        if (e.target && e.target.classList && e.target.classList.contains('grid-col-resizer')) {
            var th = e.target.closest('th');
            if (!th) return null;
            return {
                th: th,
                colIndex: parseInt(th.getAttribute('data-col-index'), 10),
                colName: th.getAttribute('data-col-name'),
                fromHandle: true
            };
        }

        // 2. Cell edge detection (works on any <th> or <td> across both header and data rows)
        var cell = e.target ? e.target.closest('th, td') : null;
        if (!cell) return null;

        var rect = cell.getBoundingClientRect();
        var edgeThreshold = 6;
        var isRightEdge = (e.clientX >= rect.right - edgeThreshold && e.clientX <= rect.right + 3);
        var isLeftEdge = (e.clientX >= rect.left - 3 && e.clientX <= rect.left + edgeThreshold);

        if (!isRightEdge && !isLeftEdge) return null;

        var targetCell = isRightEdge ? cell : cell.previousElementSibling;
        if (!targetCell) return null;
        if (targetCell.classList.contains('col-index')) return null;

        var cellIdx = targetCell.cellIndex;
        if (cellIdx < 1) return null;

        var thead = table.querySelector('thead');
        if (!thead) return null;
        var allThs = thead.querySelectorAll('th');
        var th = allThs[cellIdx];
        if (!th || th.classList.contains('col-index')) return null;

        return {
            th: th,
            colIndex: parseInt(th.getAttribute('data-col-index'), 10),
            colName: th.getAttribute('data-col-name'),
            fromHandle: false,
            cell: targetCell
        };
    }

    function onPointerMove(e) {
        if (!isDragging) {
            var colInfo = getColumnFromEvent(e);
            if (colInfo) {
                container.classList.add('grid-col-hover');
            } else {
                container.classList.remove('grid-col-hover');
            }
            return;
        }

        // Live dragging
        if (Math.abs(e.clientX - startX) > 2) {
            hasMoved = true;
        }

        var deltaX = e.clientX - startX;
        var minWidth = 50;
        var newWidth = Math.max(minWidth, Math.round(startWidth + deltaX));

        currentTh.style.width = newWidth + 'px';
        currentTh.style.minWidth = newWidth + 'px';

        // Auto-scroll near container edges if table has horizontal overflow
        var cRect = container.getBoundingClientRect();
        if (e.clientX > cRect.right - 35) {
            container.scrollLeft += 8;
        } else if (e.clientX < cRect.left + 35) {
            container.scrollLeft -= 8;
        }

        // Keep vertical guide line aligned with the column boundary
        if (guide) {
            var thRect = currentTh.getBoundingClientRect();
            var guideX = thRect.right - cRect.left + container.scrollLeft;
            guide.style.left = Math.round(guideX) + 'px';
        }
    }

    function onPointerDown(e) {
        if (e.button !== 0) return; // Left mouse button only

        var colInfo = getColumnFromEvent(e);
        if (!colInfo || !colInfo.th) return;

        e.preventDefault();
        e.stopPropagation();

        isDragging = true;
        hasMoved = false;
        startX = e.clientX;
        currentTh = colInfo.th;
        currentColName = colInfo.colName;
        startWidth = currentTh.getBoundingClientRect().width;
        currentPointerId = e.pointerId;

        try { container.setPointerCapture(e.pointerId); } catch (err) {}

        document.body.classList.add('grid-col-resizing');

        if (guide) {
            var cRect = container.getBoundingClientRect();
            var thRect = currentTh.getBoundingClientRect();
            var guideX = thRect.right - cRect.left + container.scrollLeft;
            guide.style.left = Math.round(guideX) + 'px';
            guide.style.top = '0px';
            guide.style.height = table.offsetHeight + 'px';
            guide.style.display = 'block';
        }
    }

    function onPointerUp(e) {
        if (!isDragging) return;

        try { container.releasePointerCapture(e.pointerId); } catch (err) {}

        if (guide) {
            guide.style.display = 'none';
        }

        document.body.classList.remove('grid-col-resizing');
        container.classList.remove('grid-col-hover');
        isDragging = false;

        if (hasMoved) {
            // Block any click that might be fired by the browser on the cell/header
            var blockClick = function (evt) {
                evt.stopPropagation();
                evt.preventDefault();
                window.removeEventListener('click', blockClick, true);
            };
            window.addEventListener('click', blockClick, true);
            setTimeout(function () {
                window.removeEventListener('click', blockClick, true);
            }, 120);

            var finalWidth = Math.round(currentTh.getBoundingClientRect().width);
            var ref = container._dotNetRef;
            if (ref && typeof ref.invokeMethodAsync === 'function' && currentColName) {
                ref.invokeMethodAsync('SetColumnWidth', currentColName, finalWidth);
            }
        }
    }

    function onDblClick(e) {
        var colInfo = getColumnFromEvent(e);
        if (!colInfo || !colInfo.th) return;

        e.preventDefault();
        e.stopPropagation();

        autoFitColumn(colInfo);
    }

    function autoFitColumn(colInfo) {
        var th = colInfo.th;
        var colIdx = th.cellIndex;
        if (colIdx < 1) return;

        var headerSpan = th.querySelector('.text-truncate');
        var maxContentWidth = headerSpan ? (headerSpan.scrollWidth + 52) : (th.scrollWidth + 28);

        var rows = table.querySelectorAll('tbody tr');
        var sampleCount = Math.min(rows.length, 120);
        for (var i = 0; i < sampleCount; i++) {
            var cell = rows[i].cells[colIdx];
            if (cell) {
                var cellWidth = cell.scrollWidth + 24;
                if (cellWidth > maxContentWidth) {
                    maxContentWidth = cellWidth;
                }
            }
        }

        var finalWidth = Math.max(60, Math.min(850, Math.round(maxContentWidth)));
        th.style.width = finalWidth + 'px';
        th.style.minWidth = finalWidth + 'px';

        var ref = container._dotNetRef;
        if (ref && typeof ref.invokeMethodAsync === 'function' && colInfo.colName) {
            ref.invokeMethodAsync('SetColumnWidth', colInfo.colName, finalWidth);
        }
    }

    function onPointerLeave() {
        if (!isDragging) {
            container.classList.remove('grid-col-hover');
        }
    }

    container.addEventListener('pointermove', onPointerMove);
    container.addEventListener('pointerdown', onPointerDown);
    container.addEventListener('pointerup', onPointerUp);
    container.addEventListener('pointercancel', onPointerUp);
    container.addEventListener('pointerleave', onPointerLeave);
    container.addEventListener('dblclick', onDblClick);

    container._cleanupResizers = function () {
        container.removeEventListener('pointermove', onPointerMove);
        container.removeEventListener('pointerdown', onPointerDown);
        container.removeEventListener('pointerup', onPointerUp);
        container.removeEventListener('pointercancel', onPointerUp);
        container.removeEventListener('pointerleave', onPointerLeave);
        container.removeEventListener('dblclick', onDblClick);
        container._hasGridResizer = false;
        container._dotNetRef = null;
    };

    return true;
};

window.destroyGridColumnResizers = function (containerId) {
    var container = document.getElementById(containerId);
    if (container && typeof container._cleanupResizers === 'function') {
        container._cleanupResizers();
    }
};

window.initTabStripWheelScroll = function (containerId) {
    var container = document.getElementById(containerId);
    if (!container || container._wheelInit) return;
    container._wheelInit = true;
    container.addEventListener('wheel', function (e) {
        if (e.deltaY !== 0) {
            e.preventDefault();
            container.scrollLeft += e.deltaY;
        }
    }, { passive: false });
};

window.scrollActiveTabIntoView = function (containerId) {
    var container = document.getElementById(containerId);
    if (!container) return;
    var activeTab = container.querySelector('.ssms-tab.active');
    if (activeTab && typeof activeTab.scrollIntoView === 'function') {
        activeTab.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
    }
};

window.toggleQueryEditorComment = function (editorId) {
    if (typeof monaco === 'undefined' || !monaco.editor) return false;
    var editors = monaco.editor.getEditors();
    var editor = null;
    
    for (var i = 0; i < editors.length; i++) {
        var e = editors[i];
        var container = e.getContainerDomNode();
        if (container && (container.id === editorId || (container.parentElement && container.parentElement.id === editorId) || (container.closest && container.closest('#' + editorId)))) {
            editor = e;
            break;
        }
    }
    
    if (!editor && editors.length > 0) {
        editor = editors[0];
    }
    
    if (!editor) return false;
    
    editor.focus();
    editor.trigger('toolbar', 'editor.action.commentLine', null);
    return true;
};

window.userSettings = {
    autoGenerateAliases: true,
    useAsKeyword: true,
    keepInClauseOnSingleLine: true,
    formattingStyle: 'Default'
};

window.setUserSettings = function (settings) {
    if (settings) {
        window.userSettings = settings;
    }
};

window.generateTableAlias = function (tableName, existingAliases) {
    if (!tableName) return "T";
    var cleanName = tableName.replace(/[\[\]]/g, "").trim();
    if (cleanName.indexOf('.') !== -1) {
        cleanName = cleanName.split('.').pop().trim();
    }
    cleanName = cleanName.replace(/^(?:tbl_|vw_|tbl|vw)(?=[a-zA-Z])/i, "");
    
    var alias = "";
    if (cleanName.indexOf('_') !== -1) {
        var parts = cleanName.split('_').filter(Boolean);
        for (var i = 0; i < parts.length; i++) {
            if (parts[i].length > 0) {
                alias += parts[i][0];
            }
        }
    } else {
        var capitals = cleanName.match(/[A-Z]/g);
        if (capitals && capitals.length > 1) {
            alias = capitals.join('');
        } else {
            alias = cleanName[0];
        }
    }
    
    alias = alias.toUpperCase();
    if (!alias) alias = "T";
    
    var reserved = new Set(['ON', 'IN', 'OR', 'BY', 'AS', 'TO', 'IS', 'AT', 'DO', 'IF', 'GO', 'NO', 'ADD', 'ALL', 'AND', 'SET', 'FOR', 'NOT', 'OUT', 'TOP', 'USE', 'KEY', 'ROW']);
    var finalAlias = reserved.has(alias) ? (alias + "1") : alias;
    var suffix = 2;
    var upperExisting = new Set();
    if (existingAliases) {
        existingAliases.forEach(function (a) {
            if (a) upperExisting.add(String(a).toUpperCase());
        });
    }
    
    while (upperExisting.has(finalAlias)) {
        finalAlias = alias + suffix;
        suffix++;
    }
    
    return finalAlias;
};

// SQL Profiler: Scroll to latest event
window.profilerScrollToLatest = function (containerId, newestFirst) {
    requestAnimationFrame(function () {
        var el = document.getElementById(containerId);
        if (!el) return;
        if (newestFirst) {
            el.scrollTop = 0;
        } else {
            el.scrollTop = el.scrollHeight;
        }
    });
};





// SQL Profiler: Resizable Splitter (resizes top grid panel, matching SQL Compare)
window.initProfilerSplitter = function (dotNetRef) {
    var splitter = document.getElementById('profiler-inspector-splitter');
    var grid = document.getElementById('profiler-grid-container');
    var mainBox = document.querySelector('.profiler-main-box');

    if (splitter && grid && !splitter._hasSplitterHandler) {
        splitter._hasSplitterHandler = true;
        splitter.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            try { splitter.setPointerCapture(e.pointerId); } catch (err) {}

            var startY = e.clientY;
            var startHeight = grid.offsetHeight;
            var minHeight = 80;
            var maxContainerHeight = mainBox ? mainBox.offsetHeight : (window.innerHeight - 200);
            var maxHeight = Math.max(minHeight + 40, maxContainerHeight - 110);
            var lastHeight = startHeight;
        var layoutRaf = null;
            var rafId = null;

            splitter.classList.add('dragging');
            document.body.style.cursor = 'row-resize';
            document.body.style.userSelect = 'none';

            function onPointerMove(moveEvent) {
                var deltaY = moveEvent.clientY - startY;
                var newHeight = Math.round(Math.max(minHeight, Math.min(maxHeight, startHeight + deltaY)));
                lastHeight = newHeight;
                grid.style.height = newHeight + 'px';
                grid.style.flex = '0 0 ' + newHeight + 'px';

                if (!rafId) {
                    rafId = requestAnimationFrame(function () {
                        rafId = null;
                        window.dispatchEvent(new Event('resize'));
                    });
                }
            }

            function onPointerUp(upEvent) {
            if (layoutRaf) {
                cancelAnimationFrame(layoutRaf);
                layoutRaf = null;
            }
                try { splitter.releasePointerCapture(upEvent.pointerId); } catch (err) {}
                splitter.removeEventListener('pointermove', onPointerMove);
                splitter.removeEventListener('pointerup', onPointerUp);
                splitter.removeEventListener('pointercancel', onPointerUp);
                window.removeEventListener('pointerup', onPointerUp);
                window.removeEventListener('pointercancel', onPointerUp);
                if (rafId) {
                    cancelAnimationFrame(rafId);
                    rafId = null;
                }
                splitter.classList.remove('dragging');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                if (rafId) {
                    cancelAnimationFrame(rafId);
                    rafId = null;
                }
                window.dispatchEvent(new Event('resize'));

                if (dotNetRef && typeof dotNetRef.invokeMethodAsync === 'function') {
                    dotNetRef.invokeMethodAsync('SetGridHeight', lastHeight);
                }
            }

            splitter.addEventListener('pointermove', onPointerMove);
            splitter.addEventListener('pointerup', onPointerUp);
            splitter.addEventListener('pointercancel', onPointerUp);
            window.addEventListener('pointerup', onPointerUp);
            window.addEventListener('pointercancel', onPointerUp);
        });
    }

    return true;
};

window.resetGridScrollTop = function (containerId) { var el = document.getElementById(containerId); if (el) el.scrollTop = 0; };
window.triggerWindowResize = function () {
    window.dispatchEvent(new Event('resize'));
};


window.initGridVirtualScroll = function (containerId, dotNetRef, totalRowCount) {
    var container = document.getElementById(containerId);
    if (!container) return false;

    if (totalRowCount !== undefined) {
        container._totalRowCount = totalRowCount;
    }

    if (container._hasVirtualScroll) {
        container._dotNetRef = dotNetRef;
        return true;
    }
    container._hasVirtualScroll = true;
    container._dotNetRef = dotNetRef;

    var ticking = false;

    function handleScroll() {
        if (!ticking) {
            ticking = true;
            window.requestAnimationFrame(function () {
                if (container && container._dotNetRef) {
                    try {
                        container._dotNetRef.invokeMethodAsync('OnVirtualScroll', container.scrollTop, container.clientHeight);
                    } catch (e) {}
                }
                ticking = false;
            });
        }
    }

    function handleWheel(e) {
        if (e.shiftKey) return;
        if (container.scrollHeight > 9000000 || (container._totalRowCount && container._totalRowCount > 400000)) {
            e.preventDefault();
            var maxScroll = Math.max(1, container.scrollHeight - container.clientHeight);
            var pxPerRow = maxScroll / container._totalRowCount;
            var step = Math.max(1, Math.round(Math.abs(e.deltaY) / 33));
            var rowsToScroll = Math.sign(e.deltaY) * Math.min(30, step);
            var newTop = container.scrollTop + (rowsToScroll * pxPerRow);
            container.scrollTop = Math.max(0, Math.min(maxScroll, newTop));
        }
    }

    container.addEventListener('scroll', handleScroll, { passive: true });
    container.addEventListener('wheel', handleWheel, { passive: false });

    var resizeTimer = null;
    function handleResize() {
        if (resizeTimer) clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function () {
            if (container && container._dotNetRef) {
                try {
                    container._dotNetRef.invokeMethodAsync('OnVirtualScroll', container.scrollTop, container.clientHeight);
                } catch (e) {}
            }
        }, 150);
    }
    window.addEventListener('resize', handleResize, { passive: true });

    setTimeout(function () {
        if (container && container._dotNetRef) {
            try {
                container._dotNetRef.invokeMethodAsync('OnVirtualScroll', container.scrollTop, container.clientHeight);
            } catch (e) {}
        }
    }, 20);

    return true;
};

window.initResultSetSplitters = function (containerId) {
    var container = document.getElementById(containerId || 'results-multi-container');
    if (!container) return false;

    var splitters = container.querySelectorAll('.sql-compare-splitter');
    splitters.forEach(function (splitter) {
        if (splitter._hasMultiSplitterHandler) return;
        splitter._hasMultiSplitterHandler = true;

        splitter.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            try { splitter.setPointerCapture(e.pointerId); } catch (err) {}

            var prevPane = splitter.previousElementSibling;
            if (!prevPane) return;

            var startY = e.clientY;
            var startHeight = prevPane.offsetHeight;
            var minHeight = 50;
            var maxHeight = Math.max(minHeight, Math.max(container.offsetHeight * 2, 800));
            var layoutRaf = null;
            var rafId = null;

            splitter.classList.add('dragging');
            document.body.style.cursor = 'row-resize';
            document.body.style.userSelect = 'none';

            function onPointerMove(moveEvent) {
                var deltaY = moveEvent.clientY - startY;
                var newHeight = Math.round(Math.max(minHeight, Math.min(maxHeight, startHeight + deltaY)));
                prevPane.style.flex = '0 0 ' + newHeight + 'px';
                prevPane.style.height = newHeight + 'px';

                if (!rafId) {
                    rafId = requestAnimationFrame(function () {
                        rafId = null;
                        window.dispatchEvent(new Event('resize'));
                    });
                }
            }

            function onPointerUp(upEvent) {
            if (layoutRaf) {
                cancelAnimationFrame(layoutRaf);
                layoutRaf = null;
            }
                try { splitter.releasePointerCapture(upEvent.pointerId); } catch (err) {}
                splitter.removeEventListener('pointermove', onPointerMove);
                splitter.removeEventListener('pointerup', onPointerUp);
                splitter.removeEventListener('pointercancel', onPointerUp);
                window.removeEventListener('pointerup', onPointerUp);
                window.removeEventListener('pointercancel', onPointerUp);
                if (rafId) {
                    cancelAnimationFrame(rafId);
                    rafId = null;
                }
                splitter.classList.remove('dragging');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                window.dispatchEvent(new Event('resize'));
            }

            splitter.addEventListener('pointermove', onPointerMove);
            splitter.addEventListener('pointerup', onPointerUp);
            splitter.addEventListener('pointercancel', onPointerUp);
            window.addEventListener('pointerup', onPointerUp);
            window.addEventListener('pointercancel', onPointerUp);
        });
    });

    return true;
};


window.positionContextMenu = function (elOrId, x, y) {
    var el = (typeof elOrId === 'string') ? document.getElementById(elOrId) : elOrId;
    if (!el) return;

    var rect = el.getBoundingClientRect();
    var vh = window.innerHeight;
    var vw = window.innerWidth;

    var top = y;
    var left = x;

    // Auto-flip UPWARDS if opening downwards would overflow the viewport:
    if (y + rect.height > vh - 10) {
        if (y - rect.height >= 10) {
            top = y - rect.height;
        } else {
            top = Math.max(10, vh - rect.height - 10);
        }
    }

    // Auto-flip LEFTWARDS if opening to the right would overflow the viewport:
    if (x + rect.width > vw - 10) {
        if (x - rect.width >= 10) {
            left = x - rect.width;
        } else {
            left = Math.max(10, vw - rect.width - 10);
        }
    }

    el.style.top = Math.round(top) + 'px';
    el.style.left = Math.round(left) + 'px';
    el.style.visibility = 'visible';
};

window.initSpDebuggerGutter = function (editorId, dotNetHelper) {
    function tryAttach() {
        var editors = (window.blazorMonaco && window.blazorMonaco.editors) ? window.blazorMonaco.editors : [];
        var holder = editors.find(function (h) { return h.id === editorId; });
        if (!holder || !holder.editor) {
            return false;
        }
        var editor = holder.editor;
        if (editor._spGutterAttached) return true;
        editor._spGutterAttached = true;

        editor.onMouseDown(function (e) {
            if (e && e.target && (e.target.type === 2 || e.target.type === 3 || e.target.type === 4)) {
                if (e.target.position && e.target.position.lineNumber) {
                    dotNetHelper.invokeMethodAsync('OnGutterBreakpointClick', e.target.position.lineNumber);
                }
            }
        });
        return true;
    }

    if (!tryAttach()) {
        var attempts = 0;
        var interval = setInterval(function () {
            attempts++;
            if (tryAttach() || attempts > 20) {
                clearInterval(interval);
            }
        }, 150);
    }
};
