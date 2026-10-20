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
            
            suggestions.push({
                label: 'ssf',
                kind: monaco.languages.CompletionItemKind.Snippet,
                insertText: 'SELECT * FROM ',
                documentation: 'SELECT * FROM snippet',
                insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet
            });

            // Intelligent context detection
            var fullTextUntilPosition = model.getValueInRange({
                startLineNumber: 1,
                startColumn: 1,
                endLineNumber: position.lineNumber,
                endColumn: position.column
            });

            // 1. Suppress autocomplete when actively typing a table alias after FROM or JOIN
            // e.g. "FROM [tenant].[Orders] ord" or "INNER JOIN [tenant].[OrderItems] or"
            var isAfterTableRegex = /(?:from|join)\s+(?:\[?[a-zA-Z0-9_]+\]?\.)*\[?[a-zA-Z0-9_]+\]?\s+(?:as\s+)?([a-zA-Z0-9_]*)$/i;
            var hasCommaAfter = /(?:from|join)\s+[^;]*,\s*([a-zA-Z0-9_]*)$/i.test(fullTextUntilPosition);
            var afterTableMatch = fullTextUntilPosition.match(isAfterTableRegex);
            var hasDot = textBeforeCursor.match(/\.[a-zA-Z0-9_]*$/);

            if (afterTableMatch && !hasCommaAfter && !hasDot) {
                // User is defining a table alias name: suppress suggestions completely so Enter creates a newline
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
                        suggestTables = true;
                        suggestColumns = false;
                        suggestSps = false;
                        if (i >= matches.length - 2) suggestKeywords = false;
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
                
                // Parse All Tables and Aliases in the Query
                var queryTables = [];
                var aliasMap = {};
                var sqlKw = new Set(['where', 'on', 'join', 'inner', 'left', 'right', 'outer', 'group', 'order', 'having', 'select', 'as', 'cross', 'apply', 'from', 'and', 'or', 'is', 'not', 'set']);
                var tableExtractRegex = /(?:from|join)\s+([a-zA-Z0-9_\[\]\.]+)(?:\s+(?:as\s+)?\[?([a-zA-Z0-9_]+)\]?)?/gi;
                var tMatch;
                while ((tMatch = tableExtractRegex.exec(allText)) !== null) {
                    var rawTable = tMatch[1];
                    var rawAlias = tMatch[2] ? tMatch[2].trim() : null;
                    if (rawAlias && sqlKw.has(rawAlias.toLowerCase())) {
                        rawAlias = null;
                    }
                    var normT = rawTable.toLowerCase().replace(/[\[\]\s]/g, "");
                    queryTables.push({
                        rawTable: rawTable,
                        normTable: normT,
                        baseName: normT.includes('.') ? normT.split('.').pop() : normT,
                        alias: rawAlias
                    });
                    if (rawAlias) {
                        aliasMap[rawAlias.toLowerCase()] = rawTable.toLowerCase().trim();
                    }
                }

                function findTableInQuery(targetTable, currentJoinNormTable) {
                    if (!targetTable) return null;
                    var normTarget = targetTable.toLowerCase().replace(/[\[\]\s]/g, "");
                    var targetBase = normTarget.includes('.') ? normTarget.split('.').pop() : normTarget;

                    for (var i = 0; i < queryTables.length; i++) {
                        var q = queryTables[i];
                        if (currentJoinNormTable && q.normTable === currentJoinNormTable) {
                            continue;
                        }
                        if (q.normTable === normTarget || q.baseName === targetBase) {
                            return q;
                        }
                    }
                    return null;
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
                            if (tLower === explicitTableName || nameLower === explicitTableName || bracketName === explicitTableName) {
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
                    var onMatch = fullTextUntilPosition.match(/(?:join\s+([a-zA-Z0-9_\[\]\.]+)(?:\s+(?:as\s+)?\[?([a-zA-Z0-9_]+)\]?)?[\s\S]*?(?:on|and))\s*([a-zA-Z0-9_\.]*)$/i);
                    if (onMatch && window.sqlTables) {
                        var rawJoinTable = onMatch[1];
                        var normJoinTable = rawJoinTable.toLowerCase().replace(/[\[\]\s]/g, "");
                        var joinTableBase = normJoinTable.includes('.') ? normJoinTable.split('.').pop() : normJoinTable;
                        var joinAlias = (onMatch[2] || "").toLowerCase();
                        var typedRel = onMatch[3] || "";
                        
                        var matchedTable = window.sqlTables.find(x => {
                            var n = x.fullName.toLowerCase().replace(/[\[\]\s]/g, "");
                            return n === normJoinTable || x.name.toLowerCase() === joinTableBase;
                        });

                        if (matchedTable && matchedTable.relations && Array.isArray(matchedTable.relations)) {
                            var addedConditions = new Set();
                            matchedTable.relations.forEach(r => {
                                var targetInfo = findTableInQuery(r.toTable, normJoinTable);
                                // Only suggest relationships between tables that actually exist in the current query!
                                if (!targetInfo) return;

                                var leftSide = joinAlias || matchedTable.name;
                                var rightSide = targetInfo.alias || targetInfo.rawTable;

                                var cond1 = leftSide + "." + r.fromCol + " = " + rightSide + "." + r.toCol;
                                var cond2 = rightSide + "." + r.toCol + " = " + leftSide + "." + r.fromCol;

                                var relReplaceRange = {
                                    startLineNumber: position.lineNumber,
                                    endLineNumber: position.lineNumber,
                                    startColumn: position.column - typedRel.length,
                                    endColumn: position.column
                                };

                                [cond1, cond2].forEach(cond => {
                                    if (addedConditions.has(cond)) return;
                                    addedConditions.add(cond);

                                    suggestions.push({
                                        label: cond,
                                        kind: monaco.languages.CompletionItemKind.Snippet,
                                        insertText: cond,
                                        filterText: cond,
                                        sortText: '0000_' + cond,
                                        range: relReplaceRange,
                                        detail: 'Relationship'
                                    });
                                });
                            });
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

        function configureEditor(editor) {
            try {
                editor.updateOptions({
                    quickSuggestions: {
                        other: true,
                        comments: false,
                        strings: true
                    },
                    acceptSuggestionOnEnter: 'on',
                    wordBasedSuggestions: 'off'
                });
            } catch (e) { }

            editor.onKeyUp(function(e) {
                if (e.browserEvent.key === 'Backspace' || e.browserEvent.key === 'Delete') {
                    try {
                        var pos = editor.getPosition();
                        var model = editor.getModel();
                        if (model && model.getLanguageId() === 'sql') {
                            var ctx = getSpHelptextContext(model, pos);
                            if (ctx) {
                                editor.trigger('keyboard', 'editor.action.triggerSuggest', {});
                            }
                        }
                    } catch (err) { }
                }
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

        monaco.editor.onDidCreateEditor(configureEditor);
        var editors = monaco.editor.getEditors();
        if (editors) {
            editors.forEach(configureEditor);
        }
    }
};


















