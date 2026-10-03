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

window.updateSqlAutocompleteData = (tables, views, columns, sps) => {
    window.sqlTables = tables || window.sqlTables;
    window.sqlViews = views || window.sqlViews;
    window.sqlColumns = columns || window.sqlColumns;
    window.sqlSps = sps || window.sqlSps;
};

window.registerSqlAutocomplete = (tables, views, columns, sps, dotNetHelper) => {
    window.sqlDotNetHelper = dotNetHelper;
    window.updateSqlAutocompleteData(tables, views, columns, sps);
    if (window.sqlAutocompleteRegistered) return;
    window.sqlAutocompleteRegistered = true;

    monaco.languages.registerCompletionItemProvider('sql', {
        triggerCharacters: ['.'],
        provideCompletionItems: function(model, position) {
            var lineContent = model.getLineContent(position.lineNumber);
            var textBeforeCursor = lineContent.substring(0, position.column - 1);
            var match = textBeforeCursor.match(/([a-zA-Z0-9_]*)$/);
            var typedWord = match ? match[1] : "";
            var replaceRange = {
                startLineNumber: position.lineNumber,
                endLineNumber: position.lineNumber,
                startColumn: position.column - typedWord.length,
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

            var matches = fullTextUntilPosition.match(/\b[a-z_][a-z0-9_]*\b/ig);
            var suggestTables = true;
            var suggestColumns = true;
            var suggestSps = false;

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
                        break;
                    } else if (tableKeywords.has(token)) {
                        suggestTables = true;
                        suggestColumns = false;
                        suggestSps = false;
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
                
                // Parse Aliases (e.g., FROM [hr].[Settings] s)
                var aliasMap = {};
                var sqlKw = new Set(['where', 'on', 'join', 'inner', 'left', 'right', 'outer', 'group', 'order', 'having', 'select', 'as', 'cross', 'apply', 'from', 'and', 'or', 'is', 'not', 'set']);
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
                            insertText: c,
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

            return { suggestions: suggestions };
        },
        resolveCompletionItem: async function(item, token) {
            if (item.detail === 'Table' || item.detail === 'View') {
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

        monaco.editor.onDidCreateEditor(attachAutoCapitalize);
        var editors = monaco.editor.getEditors();
        if (editors) {
            editors.forEach(attachAutoCapitalize);
        }
    }
};














