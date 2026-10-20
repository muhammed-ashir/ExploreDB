window.formatSql = (code) => {
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

    var formatted = window.sqlFormatter.format(codeToFormat, {
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

    monaco.languages.registerCompletionItemProvider('sql', {
        triggerCharacters: ['.', "'", '['],
        provideCompletionItems: function(model, position) {
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
                            detail: f.functionType ? ('Function (' + f.functionType + ')') : 'Function'
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

            var match = textUntilPosition.match(/(?:\[?[a-zA-Z0-9_]+\]?\.)\[?[a-zA-Z0-9_]*$/);
            if (match) {
                replaceRange = {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: position.column - match[0].length,
                    endColumn: position.column
                };
            }

            var suggestions = [];
            
            if (window.sqlSnippets && window.sqlSnippets.length > 0) {
                window.sqlSnippets.forEach(function(s) {
                    suggestions.push({
                        label: s.shortcut,
                        kind: monaco.languages.CompletionItemKind.Snippet,
                        insertText: s.code,
                        detail: s.shortcut + ' \u2192 ' + (s.description || 'Snippet'),
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

            // Intelligent context detection
            var fullTextUntilPosition = model.getValueInRange({
                startLineNumber: 1,
                startColumn: 1,
                endLineNumber: position.lineNumber,
                endColumn: position.column
            });

            // PRIORITY CHECK: Suppress suggestions when typing a table alias on the same line.
            // Must run BEFORE the keyword scan because an alias like 'or', 'and', 'on', 'as' etc.
            // would be misidentified as an SQL keyword in the backward token scan below.
            var currentLineUntilCursor = model.getValueInRange({
                startLineNumber: position.lineNumber,
                startColumn: 1,
                endLineNumber: position.lineNumber,
                endColumn: position.column
            });
            var hasDotOnCurrentLine = /\.[a-zA-Z0-9_]*$/.test(currentLineUntilCursor);
            var isTypingAlias = /(?:from|join)\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s+(?:as\s+)?[a-zA-Z0-9_]*$/i.test(currentLineUntilCursor);
            if (isTypingAlias && !hasDotOnCurrentLine) {
                return { suggestions: [] };
            }

            var matches = fullTextUntilPosition.match(/\b[a-z_][a-z0-9_]*\b/ig);
            var suggestTables = true;
            var suggestColumns = true;
            var suggestSps = false;
            var suggestKeywords = true;

            if (matches && matches.length > 0) {
                var tableKeywords = new Set(['from', 'join', 'into', 'update', 'table']);
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
                        // Find the text from this table keyword up to the cursor
                        var lastTableKwIndex = fullTextUntilPosition.toLowerCase().lastIndexOf(token);
                        var textAfterTableKw = lastTableKwIndex >= 0 ? fullTextUntilPosition.substring(lastTableKwIndex) : fullTextUntilPosition;

                        // A table is only considered completed if followed by whitespace (e.g. "FROM [Orders] ")
                        var completedTableRegex = /(?:from|join)\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s+/i;
                        var tableCompletedMatch = textAfterTableKw.match(completedTableRegex);
                        var hasCommaAfter = /(?:from|join)\s+[^;]*,\s*([a-zA-Z0-9_]*)$/i.test(textAfterTableKw);

                        if (tableCompletedMatch && !hasCommaAfter) {
                            // Table is already provided and finished! Do NOT suggest tables.
                            suggestTables = false;
                            
                            // Check if user is on the SAME line actively typing the alias name:
                            // e.g. "FROM [tenant].[Orders] s" or "JOIN [tenant].[OrderItems] oiy"
                            var lineUntilCursor = model.getValueInRange({
                                startLineNumber: position.lineNumber,
                                startColumn: 1,
                                endLineNumber: position.lineNumber,
                                endColumn: position.column
                            });
                            var isTypingAliasOnSameLine = /(?:from|join)\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s+(?:as\s+)?[a-zA-Z0-9_]*$/i.test(lineUntilCursor);
                            var hasDot = textBeforeCursor.match(/\.[a-zA-Z0-9_]*$/);

                            if (isTypingAliasOnSameLine && !hasDot) {
                                // Suppress suggestions while typing the alias name on the table line
                                return { suggestions: [] };
                            }

                            // If on next line or after alias, suggest clause keywords (ON, WHERE, JOIN, etc.)
                            suggestColumns = false;
                            suggestSps = false;
                            suggestKeywords = true;
                        } else {
                            // User is actively typing the table name (e.g. "FROM or"), or after comma in FROM:
                            suggestTables = true;
                            suggestColumns = false;
                            suggestSps = false;
                            if (i >= matches.length - 2) suggestKeywords = false;
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

            if (suggestTables && window.sqlTables) {
                window.sqlTables.forEach(t => {
                    suggestions.push({
                        label: t.fullName,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: t.fullName,
                        filterText: t.name + " " + t.fullName,
                        sortText: String(t.name.length).padStart(4, '0') + '_' + t.name + '_' + t.fullName,
                        range: replaceRange,
                        detail: 'Table'
                    });
                });
            }

            if (suggestTables && window.sqlViews) {
                window.sqlViews.forEach(v => {
                    suggestions.push({
                        label: v.fullName,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: v.fullName,
                        filterText: v.name + " " + v.fullName,
                        sortText: String(v.name.length).padStart(4, '0') + '_' + v.name + '_' + v.fullName,
                        range: replaceRange,
                        detail: 'View'
                    });
                });
            }

            if (suggestColumns || suggestTables) {
                var allText = model.getValue();

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
                
                // Parse Aliases (e.g., FROM [hr].[Settings] s)
                var aliasMap = {};
                var sqlKw = new Set(['where', 'on', 'join', 'inner', 'left', 'right', 'outer', 'group', 'order', 'having', 'select', 'as', 'cross', 'apply', 'from', 'set']);
                var tableRegex = /(?:from|join)\s+([a-zA-Z0-9_\[\]\.]+)(?:\s+as\s+|\s+)([a-zA-Z0-9_]+)/gi;
                var match;
                while ((match = tableRegex.exec(allText)) !== null) {
                    var tableName = match[1];
                    var possibleAlias = match[2].toLowerCase().trim();
                    if (!sqlKw.has(possibleAlias)) {
                        aliasMap[possibleAlias] = tableName.toLowerCase().trim();
                    }
                }
                
                // Check if user is typing an alias dot (e.g., "ou.")
                var lineUntilCursor = model.getValueInRange({
                    startLineNumber: position.lineNumber,
                    startColumn: 1,
                    endLineNumber: position.lineNumber,
                    endColumn: position.column
                });
                var dotMatch = lineUntilCursor.match(/([a-zA-Z0-9_]+)\.[a-zA-Z0-9_]*$/);
                var explicitTableName = null;
                var rawPrefix = "";
                if (dotMatch) {
                    rawPrefix = dotMatch[1];
                    var prefix = rawPrefix.toLowerCase();
                    explicitTableName = aliasMap[prefix] || prefix;
                }

                if (suggestColumns) {
                    var allTokens = allText.match(/[a-zA-Z0-9_\[\]\.]+/g) || [];
                    var tokenSet = new Set(allTokens.map(x => x.toLowerCase()));
                    
                    var activeColumns = new Set();
                    var hasMatchedTables = false;
                    
                    var checkTableMatch = (t) => {
                        var tLower = t.fullName.toLowerCase();
                        var nameLower = t.name.toLowerCase();
                        var bracketName = "[" + nameLower + "]";
                        
                        if (explicitTableName) {
                            if (tablesMatch(t.fullName, explicitTableName) || tLower === explicitTableName || nameLower === explicitTableName || bracketName === explicitTableName) {
                                hasMatchedTables = true;
                                if (t.columns && Array.isArray(t.columns)) {
                                    t.columns.forEach(c => activeColumns.add(c));
                                }
                            }
                        } else {
                            if (tokenSet.has(tLower) || tokenSet.has(nameLower) || tokenSet.has(bracketName)) {
                                hasMatchedTables = true;
                                if (t.columns && Array.isArray(t.columns)) {
                                    t.columns.forEach(c => activeColumns.add(c));
                                }
                            }
                        }
                    };
                    
                    if (window.sqlTables) window.sqlTables.forEach(checkTableMatch);
                    if (window.sqlViews) window.sqlViews.forEach(checkTableMatch);
                    
                    var columnsToSuggest = [];
                    if (explicitTableName) {
                        columnsToSuggest = Array.from(activeColumns);
                        if (columnsToSuggest.length === 0) {
                            // Debug fallback:
                            var availableTables = window.sqlTables ? window.sqlTables.map(x => x.fullName).join(',') : 'none';
                            suggestions.push({
                                label: 'DEBUG_' + explicitTableName,
                                kind: monaco.languages.CompletionItemKind.Text,
                                insertText: 'DEBUG',
                                detail: 'Tables: ' + availableTables.substring(0, 50)
                            });
                            // Fallback to all columns just in case
                            columnsToSuggest = window.sqlColumns || [];
                        }
                    } else {
                        columnsToSuggest = (hasMatchedTables && activeColumns.size > 0) ? Array.from(activeColumns) : (window.sqlColumns || []);
                    }
                        
                    columnsToSuggest.forEach(c => {
                        suggestions.push({
                            label: c,
                            kind: monaco.languages.CompletionItemKind.Field,
                            insertText: rawPrefix ? (rawPrefix + "." + c) : c,
                            filterText: rawPrefix ? (rawPrefix + "." + c) : c,
                            range: replaceRange,
                            detail: explicitTableName ? ('Column of ' + explicitTableName) : 'Column'
                        });
                    });

                    // Suggest aliases if not after a dot
                    if (!explicitTableName) {
                        Object.keys(aliasMap).forEach(alias => {
                            suggestions.push({
                                label: alias,
                                kind: monaco.languages.CompletionItemKind.Variable,
                                insertText: alias,
                                filterText: alias,
                                range: replaceRange,
                                detail: 'Alias for ' + aliasMap[alias]
                            });
                        });
                    }

                    // Provide JOIN ON Relationship Snippets
                    var lastJoinIndex = fullTextUntilPosition.toLowerCase().lastIndexOf('join');
                    if (lastJoinIndex >= 0 && window.sqlTables) {
                        var joinSegment = fullTextUntilPosition.substring(lastJoinIndex);
                        var onMatch = joinSegment.match(/^join\s+([a-zA-Z0-9_\[\]\.]+)(?:\s+as\s+|\s+)?([a-zA-Z0-9_]+)?\s+on\s*([a-zA-Z0-9_\.]*)$/i);
                        if (onMatch) {
                            var joinTableRaw = onMatch[1];
                            var joinAlias = (onMatch[2] || "").toLowerCase();
                            var typedRel = onMatch[3] || "";

                            var matchedTable = window.sqlTables.find(x => tablesMatch(x.fullName, joinTableRaw));
                            if (matchedTable && matchedTable.relations && Array.isArray(matchedTable.relations)) {
                                var textPriorToJoin = fullTextUntilPosition.substring(0, lastJoinIndex);
                                var queryTables = [];
                                var tableDefRegex = /(?:from|join)\s+([a-zA-Z0-9_\[\]\.]+)(?:\s+as\s+|\s+)?([a-zA-Z0-9_]+)?/gi;
                                var tMatch;
                                while ((tMatch = tableDefRegex.exec(textPriorToJoin)) !== null) {
                                    var rawTbl = tMatch[1];
                                    var alias = tMatch[2];
                                    if (alias && sqlKw.has(alias.toLowerCase())) {
                                        alias = null;
                                    }
                                    queryTables.push({
                                        rawTable: rawTbl,
                                        alias: alias
                                    });
                                }

                                var relReplaceRange = {
                                    startLineNumber: position.lineNumber,
                                    endLineNumber: position.lineNumber,
                                    startColumn: position.column - typedRel.length,
                                    endColumn: position.column
                                };

                                var leftDisplayName = joinAlias || (matchedTable.name || matchedTable.fullName);
                                // Also try aliasMap reverse lookup for joinTable in case joinAlias wasn't captured
                                if (!joinAlias) {
                                    var joinRevAlias = Object.keys(aliasMap).find(k => tablesMatch(aliasMap[k], joinTableRaw));
                                    if (joinRevAlias) leftDisplayName = joinRevAlias;
                                }
                                var seenConds = new Set();

                                matchedTable.relations.forEach(r => {
                                    // Only suggest relationships to tables that are ALREADY in the query!
                                    var targetEntry = queryTables.find(qt => tablesMatch(qt.rawTable, r.toTable));
                                    if (!targetEntry) return;

                                    var rightDisplayName = targetEntry.alias;
                                    if (!rightDisplayName) {
                                        // Use aliasMap reverse lookup — handles aliases that are SQL keywords (e.g. 'or', 'and')
                                        var revAlias = Object.keys(aliasMap).find(k => tablesMatch(aliasMap[k], targetEntry.rawTable));
                                        rightDisplayName = revAlias || targetEntry.rawTable;
                                    }

                                    // Direction 1: Joined table first (e.g. ot.OrderId = tof.Id)
                                    var cond1 = leftDisplayName + "." + r.fromCol + " = " + rightDisplayName + "." + r.toCol;
                                    if (!seenConds.has(cond1)) {
                                        seenConds.add(cond1);
                                        suggestions.push({
                                            label: cond1,
                                            kind: monaco.languages.CompletionItemKind.Snippet,
                                            insertText: cond1,
                                            filterText: typedRel ? (typedRel + " " + cond1 + " " + r.fromCol) : (cond1 + " " + r.fromCol),
                                            sortText: '0000_1_' + cond1,
                                            range: relReplaceRange,
                                            detail: 'Relationship (' + (matchedTable.name || matchedTable.fullName) + ' -> ' + targetEntry.rawTable + ')'
                                        });
                                    }

                                    // Direction 2: Other table first (e.g. tof.Id = ot.OrderId)
                                    var cond2 = rightDisplayName + "." + r.toCol + " = " + leftDisplayName + "." + r.fromCol;
                                    if (!seenConds.has(cond2)) {
                                        seenConds.add(cond2);
                                        suggestions.push({
                                            label: cond2,
                                            kind: monaco.languages.CompletionItemKind.Snippet,
                                            insertText: cond2,
                                            filterText: typedRel ? (typedRel + " " + cond2 + " " + r.toCol) : (cond2 + " " + r.toCol),
                                            sortText: '0000_2_' + cond2,
                                            range: relReplaceRange,
                                            detail: 'Relationship (' + targetEntry.rawTable + ' -> ' + (matchedTable.name || matchedTable.fullName) + ')'
                                        });
                                    }
                                });
                            }
                        }
                    }
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

            var isExplicitSpPrefix = fullTypedWord.toLowerCase().startsWith('sp_') || fullTypedWord.toLowerCase() === 'sp';
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

            if (suggestKeywords) {
                var sqlKeywordsList = [
                    'SELECT', 'FROM', 'WHERE', 'INNER JOIN', 'LEFT JOIN', 'RIGHT JOIN',
                    'FULL OUTER JOIN', 'CROSS JOIN', 'ON', 'GROUP BY', 'ORDER BY',
                    'HAVING', 'AND', 'OR', 'NOT', 'IN', 'EXISTS', 'BETWEEN', 'LIKE',
                    'IS NULL', 'IS NOT NULL', 'AS', 'DESC', 'ASC', 'UNION', 'UNION ALL',
                    'TOP', 'DISTINCT', 'INSERT INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE',
                    'EXEC', 'DECLARE', 'CAST', 'CONVERT', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END'
                ];
                sqlKeywordsList.forEach(kw => {
                    suggestions.push({
                        label: kw,
                        kind: monaco.languages.CompletionItemKind.Keyword,
                        insertText: kw,
                        filterText: kw,
                        range: replaceRange,
                        detail: 'Keyword'
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
                        showWords: false
                    }
                });
            } catch (e) { }

            // Working Paste in context menu (supported across WebView2)
            if (!editor.getAction('custom-paste-action')) {
                try {
                    editor.addAction({
                        id: 'custom-paste-action',
                        label: 'Paste',
                        keybindings: [
                            monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyV
                        ],
                        contextMenuGroupId: '9_cutcopypaste',
                        contextMenuOrder: 3,
                        run: async function(ed) {
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
                                if (/\bon\s+[a-zA-Z0-9_.]*$/i.test(textBefore)) {
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
    try {
        if (navigator.clipboard && navigator.clipboard.readText) {
            var text = await navigator.clipboard.readText();
            if (text) return text;
        }
    } catch (e) { }

    if (window.globalShortcuts && window.globalShortcuts.queryRunnerInstance) {
        try {
            var text = await window.globalShortcuts.queryRunnerInstance.invokeMethodAsync('GetClipboardTextAsync');
            if (text) return text;
        } catch (e) { }
    }
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
    if (editor && window.setupEditorInstance) {
        window.setupEditorInstance(editor);
    }
};


















