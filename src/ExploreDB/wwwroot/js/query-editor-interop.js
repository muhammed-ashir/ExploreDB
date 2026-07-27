window.formatSql = (code) => {
    if (!window.sqlFormatter) {
        console.error("sqlFormatter is not loaded");
        return code;
    }
    try {
        return window.sqlFormatter.format(code, {
            language: 'tsql',
            keywordCase: 'upper',
            linesBetweenQueries: 2
        });
    } catch (e) {
        console.error("Error formatting SQL:", e);
        return code;
    }
};

window.registerSqlAutocomplete = (tables, views) => {
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
        }
    });
};
