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

window.registerSqlAutocomplete = (tables, views, columns, dotNetHelper) => {
    window.sqlDotNetHelper = dotNetHelper;
    if (window.sqlAutocompleteRegistered) return;
    window.sqlAutocompleteRegistered = true;

    monaco.languages.registerCompletionItemProvider('sql', {
        provideCompletionItems: function(model, position) {
            var word = model.getWordUntilPosition(position);
            var replaceRange = {
                startLineNumber: position.lineNumber,
                endLineNumber: position.lineNumber,
                startColumn: word.startColumn,
                endColumn: word.endColumn
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

            if (tables) {
                tables.forEach(t => {
                    suggestions.push({
                        label: t.fullName,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: t.fullName,
                        filterText: t.fullName,
                        range: replaceRange,
                        detail: 'Table'
                    });
                });
            }

            if (views) {
                views.forEach(v => {
                    suggestions.push({
                        label: v.fullName,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: v.fullName,
                        filterText: v.fullName,
                        range: replaceRange,
                        detail: 'View'
                    });
                });
            }

            if (columns) {
                columns.forEach(c => {
                    suggestions.push({
                        label: c,
                        kind: monaco.languages.CompletionItemKind.Field,
                        insertText: c,
                        filterText: c,
                        range: replaceRange, // Will just replace the current word
                        detail: 'Column'
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
            var word = model.getWordAtPosition(position);
            if (!word) return null;
            try {
                let doc = await window.sqlDotNetHelper.invokeMethodAsync('GetSchemaDocumentation', word.word);
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
