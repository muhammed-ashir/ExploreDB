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

window.registerSqlAutocomplete = (tables, views, dotNetHelper) => {
    window.sqlDotNetHelper = dotNetHelper;
    if (window.sqlAutocompleteRegistered) return;
    window.sqlAutocompleteRegistered = true;

    monaco.languages.registerCompletionItemProvider('sql', {
        provideCompletionItems: function(model, position) {
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
                        label: t,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: t,
                        detail: 'Table'
                    });
                });
            }

            if (views) {
                views.forEach(v => {
                    suggestions.push({
                        label: v,
                        kind: monaco.languages.CompletionItemKind.Class,
                        insertText: v,
                        detail: 'View'
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
};
